using System.Globalization;
using System.Text.Json;
using HeatFlow.Core.Dose;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.Database;
using HeatFlow.Infrastructure.EsterownikMapper;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Core.Phases;

/// <summary>
/// Faza 5 - „Dawka”: czas pracy i postoju podajnika oraz dmuchawa w grzaniu z pogody
/// (analiza-kociol-nastawy.md § 9). Co przebieg (5 min) zapisuje migawkę kotła i pilnuje
/// bezpieczników; co godzinę liczy dawkę z Tz wygładzonej 3 h; raz na dobę uczy k.
///
/// Wyłącznik switch.heatflow_dawka_automat (tabela DoseAutomatSwitch, domyślnie off):
/// off = liczy i pokazuje, nic nie zapisuje. Przy włączeniu zapamiętuje nastawy zastane
/// w sterowniku, przy wyłączeniu (ręcznym albo samowyłączeniu) przywraca je
/// (DoseRestoreOnDisable) i pisze na Discord.
///
/// Zapisuje wyłącznie number.kociol_czas_pracy_podajnika, number.kociol_czas_postoju_podajnika
/// i number.kociol_moc_dmuchawy - nastawy kotła, podtrzymania, maks. spalin, regulatora spalin
/// ani min. mocy dmuchawy nie rusza nigdy. Potwierdzenie zapisu z mappera, nie ze stanu HA.
/// Sygnałem zwrotnym jest kocioł, nigdy temperatura w pokojach (§ 7).
///
/// Każda zmiana (i decyzja zablokowana regułą) trafia do dziennika FeedChangeLog: co, z ilu na
/// ile, w którą stronę i dlaczego, po polsku, z danymi wejściowymi (<see cref="DoseLog"/>).
/// </summary>
public class Phase5DoseService : IPhaseService
{
    // Encje HA - z SystemConfiguration (DoseEntities), czujniki w domu - z pokoi z flagą
    // UseForDoseIndoorMean, progi - z HeatingParameters. W kodzie zostają tylko twarde granice
    // (DoseLimits) i stałe techniczne poniżej.
    private static readonly TimeSpan SampleRetention = TimeSpan.FromDays(4);
    private static readonly TimeSpan CalcInterval = TimeSpan.FromMinutes(55);

    private readonly IHomeAssistantClient _haClient;
    private readonly IDoseRepository _repository;
    private readonly IEsterownikMapperClient _mapper;
    private readonly DoseControllerWriter _writer;
    private readonly IDoseNotifier _notifier;
    private readonly IConfigurationService _configurationService;
    private readonly IConfigurationAuditService _auditService;
    private readonly IApplicationErrorLogger _errorLogger;
    private readonly ILogger<Phase5DoseService> _logger;
    private readonly TimeProvider _time;

    public int PhaseNumber => 5;

    public Phase5DoseService(
        IHomeAssistantClient haClient,
        IDoseRepository repository,
        IEsterownikMapperClient mapper,
        DoseControllerWriter writer,
        IDoseNotifier notifier,
        IConfigurationService configurationService,
        IConfigurationAuditService auditService,
        IApplicationErrorLogger errorLogger,
        ILogger<Phase5DoseService> logger,
        TimeProvider? time = null)
    {
        _haClient = haClient;
        _repository = repository;
        _mapper = mapper;
        _writer = writer;
        _notifier = notifier;
        _configurationService = configurationService;
        _auditService = auditService;
        _errorLogger = errorLogger;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    private record DoseInputs(
        DoseSample Sample,
        DateTime? BoilerDataUtc,
        DateTime? OutdoorDataUtc,
        bool? SummerMode,
        bool? ControllerAuto,
        DateTime? FlueSafetyLastTriggeredUtc);

    /// <summary>Powód samowyłączenia: kod do dziennika (SAMOWYLACZENIE_&lt;Code&gt;) i opis po polsku.</summary>
    private record SafetyIssue(string Code, string Text);

    /// <summary>Wszystko, co jeden przebieg dopisuje do dziennika i opisu wyniku.</summary>
    private sealed class RunLog
    {
        public List<string> Notes { get; } = new();
        public List<FeedChangeLog> Entries { get; } = new();
        public required DoseLogContext Ctx { get; init; }
        public required DoseEntities Entities { get; init; }
    }

    public async Task<PhaseResult> ExecuteAsync(
        HeatingState state,
        HeatingParameters parameters,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var now = _time.GetUtcNow().UtcDateTime;
        var notes = new List<string>();

        try
        {
            var entities = state.SystemConfiguration == null ? null : DoseEntities.From(state.SystemConfiguration);
            var configErrors = entities?.Validate() ?? new List<string> { "brak SystemConfiguration" };
            if (configErrors.Count > 0)
            {
                return await ConfigurationErrorAsync(configErrors, now, startTime, cancellationToken);
            }

            var run = new RunLog { Ctx = new DoseLogContext { NowUtc = now, Parameters = parameters }, Entities = entities! };
            notes = run.Notes;

            // 1. Migawka (co przebieg) - pamięć do Tz, trymu, bezpieczników i bilansu doby.
            var rooms = await _configurationService.GetAllRoomsAsync(cancellationToken);
            var inputs = await ReadInputsAsync(entities!, rooms, parameters, now, cancellationToken);
            await _repository.AddSampleAsync(inputs.Sample, cancellationToken);
            await _repository.DeleteSamplesOlderThanAsync(now - SampleRetention, cancellationToken);

            var ds = await _repository.GetStateAsync(cancellationToken) ?? new DoseState();
            var automatSwitch = await _repository.GetAutomatSwitchAsync(cancellationToken);

            if (parameters.DoseTargetFromRooms && parameters.DoseTargetUseActual)
            {
                await UpdateRoomActualsAsync(ds, rooms, parameters, now, cancellationToken);
            }

            if (inputs.BoilerDataUtc.HasValue) ds.LastBoilerDataUtc = inputs.BoilerDataUtc;
            if (inputs.OutdoorDataUtc.HasValue) ds.LastOutdoorDataUtc = inputs.OutdoorDataUtc;

            var controller = await _mapper.GetFeedSettingsAsync(cancellationToken);
            if (controller != null)
            {
                SetController(ds, controller);
                ds.LastMapperOkUtc = now;
            }

            var ctx = run.Ctx;
            ctx.AutomatActive = ds.AutomatActive;
            ctx.BoilerTemp = inputs.Sample.BoilerTemp;
            ctx.BoilerSetpoint = inputs.Sample.BoilerSetpoint;
            ctx.FlueTemp = inputs.Sample.FlueTemp;
            ctx.Controller = Snapshot(controller);
            ctx.OutdoorAtPreviousChange = ds.LastChangeOutdoorSmoothed;
            ctx.ForecastNextHour = NextForecastHour(state.Forecast, now);
            ctx.HoursSinceFlueSafety = inputs.FlueSafetyLastTriggeredUtc.HasValue
                ? Math.Round((now - inputs.FlueSafetyLastTriggeredUtc.Value).TotalHours, 1)
                : null;
            ctx.K = parameters.FeedCorrectionK;

            // 2. Korekta k raz na dobę (za wczoraj), zanim policzymy dawkę.
            await CorrectKIfDueAsync(ds, parameters, now, run, cancellationToken);
            ctx.K = parameters.FeedCorrectionK;

            var windowStart = now - TimeSpan.FromHours(new[]
            {
                parameters.DoseTrimStandbyHours, parameters.DoseTrimUnderpowerHours, parameters.FeedSmoothingPastHours,
                parameters.DoseBoilerColdMinutes / 60.0, parameters.DoseBoilerColdWarnMinutes / 60.0
            }.Max());
            var samples = await _repository.GetSamplesSinceAsync(windowStart, cancellationToken);
            var last2h = samples.Where(s => s.TimestampUtc > now - TimeSpan.FromHours(parameters.DoseTrimUnderpowerHours)
                                            && s.BlowerActual.HasValue).ToList();
            ctx.HeatingShare2hPct = last2h.Count > 0 ? Math.Round(100.0 * last2h.Count(s => s.BlowerActual > 0) / last2h.Count) : null;
            ctx.StandbyEntries6h = DoseCalculator.StandbyEntries(samples
                .Where(s => s.TimestampUtc > now - TimeSpan.FromHours(parameters.DoseTrimStandbyHours)).ToList());

            // 3. Dawka z pogody - co godzinę.
            if (ds.LastCalcUtc == null || ds.ModelPause == null || now - ds.LastCalcUtc.Value >= CalcInterval)
            {
                Calculate(ds, state, rooms, parameters, samples, now, run);
            }
            FillPlan(ctx, ds);

            // 4. Co sterownik dostałby teraz (sugestia przy wyłączonym automacie).
            DoseDecision? decision = null;
            int? blower = null;
            int? blowerWanted = null;
            string? blowerNote = null;
            string? blowerCode = null;
            var flueSafetyRecent = inputs.FlueSafetyLastTriggeredUtc.HasValue
                                   && now - inputs.FlueSafetyLastTriggeredUtc.Value < TimeSpan.FromHours(parameters.DoseFlueSafetyHoldHours);
            if (ds.ModelPause.HasValue && ds.PlannedWork.HasValue && controller != null)
            {
                var plan = new DosePlan(ds.PlannedWork.Value, ds.ModelPause.Value, ds.Ceiling, ds.ShortfallPct);
                decision = DoseCalculator.Decide(controller.FeederWork, controller.FeederPause, plan,
                    ds.AutomatActive ? ds.LastChangeUtc : null, now, parameters);
                blowerWanted = DoseCalculator.BlowerFor(decision.Work, decision.Pause, parameters);
                (var limited, blowerNote, blowerCode) = DoseCalculator.LimitBlower(blowerWanted.Value, controller.Blower,
                    inputs.Sample.FlueTemp, flueSafetyRecent, parameters);
                blower = limited;
                ds.PlannedPause = decision.Pause;
                ds.PlannedBlower = blower;
            }

            // 5. Wyłącznik: włączenie / wyłączenie przez Pawła.
            if (automatSwitch.Enabled && !ds.AutomatActive)
            {
                var refuse = controller == null
                    ? new SafetyIssue("MAPPER", "mapper nie odpowiada - nie mogę zapamiętać zastanych nastaw")
                    : SafetyReason(inputs, ds, samples, now, parameters) ?? ParametersReason(parameters);
                if (refuse != null)
                {
                    await _repository.SetAutomatSwitchAsync(false, $"Faza5: {Truncate(refuse.Text, 180)}", cancellationToken);
                    ds.LastSelfDisableReason = $"nie włączono: {refuse.Text}";
                    ds.LastSelfDisableUtc = now;
                    notes.Add($"automat NIE włączony: {refuse.Text}");
                    run.Entries.Add(DoseLog.Entry(ctx, DoseLog.Automat, 1, 0, $"SAMOWYLACZENIE_{refuse.Code}", DoseLog.StatusInternal,
                        $"Automatu nie włączono: {refuse.Text}. Przełącznik wrócił na off, nastawy kotła bez zmian."));
                    await _notifier.NotifyAsync("HeatFlow: automatu dawki nie włączam",
                        $"Powód: {refuse.Text}. Przełącznik wrócił na off, nastawy kotła bez zmian.", cancellationToken);
                }
                else
                {
                    ds.AutomatActive = true;
                    ctx.AutomatActive = true;
                    ds.SavedWork = controller!.FeederWork;
                    ds.SavedPause = controller.FeederPause;
                    ds.SavedBlower = controller.Blower;
                    ds.LastChangeUtc = null; // pierwsza zmiana bez czekania na odstęp
                    ds.LastChangeOutdoorSmoothed = ds.OutdoorSmoothed;
                    ctx.OutdoorAtPreviousChange = ds.OutdoorSmoothed;
                    ds.LastLoggedBlockKey = null;
                    ds.LastChangeDirection = 0;
                    ResetShadow(ds);
                    var remembered = $"praca {controller.FeederWork} s, postój {controller.FeederPause} s, dmuchawa {controller.Blower} %";
                    notes.Add($"automat włączony, zapamiętane {remembered}");
                    run.Entries.Add(DoseLog.Entry(ctx, DoseLog.Automat, 0, 1, "WLACZENIE_RECZNE", DoseLog.StatusInternal,
                        $"Automat dawki włączony w HA. Zapamiętane nastawy: {remembered}; przy wyłączeniu " +
                        $"{(parameters.DoseRestoreOnDisable ? "wrócą" : "NIE wrócą (parametr)")}."));
                    await _notifier.NotifyAsync("HeatFlow: automat dawki włączony",
                        $"Zapamiętane nastawy: {remembered}. " +
                        $"Przy wyłączeniu {(parameters.DoseRestoreOnDisable ? "wrócą" : "NIE wrócą (parametr)")}. Teraz model mówi: {DescribePlan(ds)}.",
                        cancellationToken);
                }
            }
            else if (!automatSwitch.Enabled && ds.AutomatActive)
            {
                await DeactivateAsync(ds, controller, new SafetyIssue("", "wyłączony w HA"), selfDisable: false,
                    noBlowerIncrease: false, parameters, now, run, cancellationToken);
            }

            // 6. Automat aktywny: bezpieczniki, potem ewentualna zmiana nastaw.
            if (ds.AutomatActive)
            {
                var issue = SafetyReason(inputs, ds, samples, now, parameters) ?? ParametersReason(parameters);
                var hold = await _repository.GetOpenWriteHoldAsync(cancellationToken);
                // Wstrzymanie po niepotwierdzonym zapisie: true = można liczyć i zapisywać dawkę od nowa (limit minął).
                var holdHandled = false;
                if (issue == null && hold != null)
                {
                    holdHandled = !await ResolveWriteHoldAsync(ds, controller, hold, inputs.Sample.FlueTemp, flueSafetyRecent,
                        parameters, now, run, cancellationToken);
                    if (!holdHandled) await ResumeFromSummerPauseAsync(ds, now, run, cancellationToken);
                }
                if (issue is { Code: SummerPauseCode })
                {
                    // Tryb lato to nie awaria: automat czeka z zapisami, nastawy zostają, po lecie wraca sam.
                    await PauseForSummerAsync(ds, issue, now, run, cancellationToken);
                }
                else if (issue != null)
                {
                    var flueHigh = inputs.Sample.FlueTemp > parameters.DoseFlueMaxTemp;
                    if (flueHigh && controller != null && blower.HasValue && blower.Value < controller.Blower)
                    {
                        // „> 200 °C → −2 pkt” robimy przed samowyłączeniem; przywracanie nie podniesie dmuchawy.
                        var cut = await _writer.WriteAsync(run.Entities, DoseRegister.Blower, blower.Value, cancellationToken);
                        notes.Add($"spaliny > {parameters.DoseFlueMaxTemp} °C: {cut.Report}");
                        var w = cut.Writes.FirstOrDefault();
                        run.Entries.Add(DoseLog.Change(ctx, DoseLog.Blower, controller.Blower, blower.Value, "SPALINY_200",
                            cut.Confirmed ? DoseLog.StatusWritten : DoseLog.StatusUnconfirmed,
                            confirmation: w?.Confirmation, seen: w?.Seen));
                        if (cut.Last != null) { SetController(ds, cut.Last); controller = cut.Last; }
                    }

                    await DeactivateAsync(ds, controller, issue, selfDisable: true, noBlowerIncrease: flueHigh,
                        parameters, now, run, cancellationToken);
                }
                else if (holdHandled)
                {
                    // wstrzymanie obsłużone w tym przebiegu (czeka / wznowione / automat wyłączony)
                }
                else if (decision is { Change: true } && controller != null && blower.HasValue)
                {
                    await ResumeFromSummerPauseAsync(ds, now, run, cancellationToken);
                    await WarnBoilerColdAsync(ds, samples, now, parameters, run, cancellationToken);
                    await ApplyAsync(ds, controller, decision, blower.Value, blowerWanted!.Value, blowerNote, blowerCode,
                        parameters, now, run, cancellationToken);
                }
                else if (decision != null)
                {
                    await ResumeFromSummerPauseAsync(ds, now, run, cancellationToken);
                    await WarnBoilerColdAsync(ds, samples, now, parameters, run, cancellationToken);
                    notes.Add(decision.Reason);
                    if (decision.WaitingForInterval && controller != null)
                    {
                        LogWaitingForInterval(ds, controller, decision, parameters, now, run);
                    }
                }
                else
                {
                    notes.Add(controller == null ? "mapper nie odpowiada - bez zmian" : "brak planu - bez zmian");
                }
            }
            else if (controller != null && ds.ModelPause.HasValue && ds.PlannedWork.HasValue)
            {
                ShadowStep(ds, controller, inputs.Sample.FlueTemp, flueSafetyRecent, parameters, now, run);
            }

            // 7. Przycisk „Ustaw sugerowane” (zlecenie z HA): jednorazowy zapis sugerowanych nastaw.
            await HandleApplyRequestAsync(ds, controller, inputs, samples, flueSafetyRecent, parameters, now, run, cancellationToken);

            // 8. Nowa sugestia przy wyłączonym automacie → Discord (tylko w godzinach dnia).
            await NotifyNewSuggestionAsync(ds, controller, now, run, cancellationToken);

            ds.LastReason = Truncate(string.Join(" | ", notes.Prepend(DescribePlan(ds))), 1000);
            await _repository.AddChangeLogAsync(run.Entries, cancellationToken);
            await _repository.SaveStateAsync(ds, cancellationToken);

            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            return PhaseResult.SuccessResult(PhaseNumber, duration, BuildDetails(ds, controller, notes));
        }
        catch (Exception ex)
        {
            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Błąd podczas wykonania Fazy 5 (dawka)");
            await _errorLogger.LogAsync(ex, PhaseNumber, nameof(Phase5DoseService), null, "Error", "Console", cancellationToken);
            return PhaseResult.ErrorResult(PhaseNumber, ex.Message, duration);
        }
    }

    /// <summary>Powiadomienie o nowej sugestii idzie na Discord tylko w godzinach lokalnych [od, do) - nocą nie budzimy.</summary>
    private const int SuggestionNotifyFromHour = 8;
    private const int SuggestionNotifyToHour = 22;

    /// <summary>
    /// Przy WYŁĄCZONYM automacie: gdy sugestia (cień) różni się od tego, co jest w sterowniku, a jeszcze jej nie zgłoszono -
    /// jedna wiadomość na Discord („postój 180 → 200 s, dmuchawa 30 %; w kotle teraz …; powód; ustaw przyciskiem”).
    /// Sugestia z nocy czeka do 8:00 (o ile dalej się różni od kotła). Każdą sugestię zgłaszamy raz - zapamiętuje to wpis
    /// dziennika <see cref="DoseLog.SuggestionParameter"/> (bez migracji). Po „Ustaw sugerowane” sugestia = kocioł, więc cisza.
    /// </summary>
    private async Task NotifyNewSuggestionAsync(DoseState ds, ControllerFeedSettings? readController, DateTime now, RunLog run, CancellationToken ct)
    {
        // Stan sterownika bierzemy z DoseState (aktualizowany po zapisie z przycisku w tym samym przebiegu),
        // nie ze zmiennej sprzed zapisu.
        if (ds.AutomatActive || readController == null || ds.ShadowWork == null || ds.ShadowPause == null || ds.ShadowBlower == null
            || ds.ControllerWork == null || ds.ControllerPause == null || ds.ControllerBlower == null)
            return;
        var controller = new ControllerFeedSettings(ds.ControllerWork.Value, ds.ControllerPause.Value, ds.ControllerBlower.Value);

        var local = TimeZoneInfo.ConvertTimeFromUtc(now, _time.LocalTimeZone);
        if (local.Hour < SuggestionNotifyFromHour || local.Hour >= SuggestionNotifyToHour)
            return;

        var (work, pause, blower) = (ds.ShadowWork.Value, ds.ShadowPause.Value, ds.ShadowBlower.Value);
        if (work == controller.FeederWork && pause == controller.FeederPause && blower == controller.Blower)
            return;

        var last = await _repository.GetLastSuggestionNotificationAsync(ct);
        if (last != null && last.ControllerWork == work && last.ControllerPause == pause && last.ControllerBlower == blower)
            return;

        var heads = new List<string>();
        if (work != controller.FeederWork) heads.Add(DoseLog.Head(DoseLog.Work, controller.FeederWork, work));
        if (pause != controller.FeederPause) heads.Add(DoseLog.Head(DoseLog.Pause, controller.FeederPause, pause));
        if (blower != controller.Blower) heads.Add(DoseLog.Head(DoseLog.Blower, controller.Blower, blower));

        var cause = run.Ctx.Ceiling ? "SUFIT"
            : run.Ctx.TrimSec != 0 && ds.LastTrimCode != null ? ds.LastTrimCode
            : "POGODA";
        var text = $"Nowa sugestia dawki: {string.Join("; ", heads)}. W kotle teraz {controller.FeederWork}/{controller.FeederPause} s, " +
                   $"dmuchawa {controller.Blower} %. Powód: {DoseLog.Why(cause, run.Ctx)}. " +
                   "Automat jest wyłączony - ustaw przyciskiem „Ustaw sugerowane” w HA.";

        var entry = DoseLog.Entry(run.Ctx, DoseLog.SuggestionParameter, controller.FeederPause, pause, DoseLog.SuggestionCode,
            DoseLog.StatusInternal, text);
        entry.ControllerWork = work;       // dla tego wpisu: ZGŁOSZONA sugestia (dedupe), nie stan sterownika
        entry.ControllerPause = pause;
        entry.ControllerBlower = blower;
        run.Entries.Add(entry);
        run.Notes.Add($"sugestia na Discord: {suggestedText(work, pause, blower)}");
        await _notifier.NotifyAsync("HeatFlow: nowa sugestia dawki", text, ct);

        static string suggestedText(int w, int p, int b) => $"{w}/{p} s, dmuchawa {b} %";
    }

    /// <summary>Zlecenie z przycisku, którego Console nie obsłużył w tym czasie, wygasa (nic nie ustawiono).</summary>
    private static readonly TimeSpan ApplyRequestTtl = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Przycisk „Ustaw sugerowane” w HA: API dopisuje zlecenie (wpis dziennika ze statusem ZLECENIE), a tu,
    /// w najbliższym przebiegu, jednorazowo zapisujemy do sterownika to, co automat ustawiłby teraz
    /// (cień: <c>ds.Shadow*</c> = sugestia widoczna w HA). Tylko przy WYŁĄCZONYM automacie. Te same ściany
    /// co przy automacie: twarde granice (zapis), bezpieczniki z <see cref="SafetyReason"/>, ograniczenia dmuchawy
    /// od spalin / #24, potwierdzenie w mapperze. Bez potwierdzenia - próba przywrócenia wartości sprzed kliknięcia.
    /// Każdy wynik (zapisane / zablokowane / niepotwierdzone) dostaje wpis w dzienniku i wiadomość na Discord.
    /// </summary>
    private async Task HandleApplyRequestAsync(DoseState ds, ControllerFeedSettings? controller, DoseInputs inputs,
        IReadOnlyList<DoseSample> samples, bool flueSafetyRecent, HeatingParameters p, DateTime now, RunLog run, CancellationToken ct)
    {
        await _repository.ExpireApplyRequestsAsync(now - ApplyRequestTtl, ct);
        var request = await _repository.GetPendingApplyRequestAsync(now - ApplyRequestTtl, ct);
        if (request == null) return;

        var ctx = run.Ctx;

        async Task BlockAsync(string reason)
        {
            var text = $"Przycisk „Ustaw sugerowane”: nic nie ustawiono - {reason}.";
            run.Notes.Add($"przycisk: {reason}");
            run.Entries.Add(DoseLog.Entry(ctx, DoseLog.ButtonParameter, null, null, DoseLog.ButtonCode, DoseLog.StatusBlocked, text));
            await _repository.SetApplyRequestStatusAsync(request, DoseLog.StatusRequestDone,
                $"Zlecenie z przycisku obsłużone: nic nie ustawiono - {reason}.", ct);
            await _notifier.NotifyAsync("HeatFlow: przycisk „Ustaw sugerowane” - zablokowano", text, ct);
        }

        if (ds.AutomatActive)
        {
            await BlockAsync("automat dawki jest włączony i steruje sam (przycisk działa tylko przy wyłączonym automacie)");
            return;
        }
        if (controller == null)
        {
            await BlockAsync("mapper nie odpowiada, nie znam obecnych nastaw kotła");
            return;
        }
        var issue = SafetyReason(inputs, ds, samples, now, p) ?? ParametersReason(p);
        if (issue != null)
        {
            await BlockAsync(issue.Text);
            return;
        }
        if (!ds.ModelPause.HasValue || !ds.PlannedWork.HasValue || ds.ShadowWork == null || ds.ShadowPause == null || ds.ShadowBlower == null)
        {
            await BlockAsync("brak sugestii - dawka jeszcze nie policzona");
            return;
        }

        // Krok liczony od tego, co JEST w sterowniku (limit FeedMaxStepSec jak przy automacie), nie od cienia,
        // który po kilku sugestiach bez zapisu odjeżdża od sterownika (21.10: 160 → 260 s jednym kliknięciem).
        var plan = new DosePlan(ds.PlannedWork.Value, ds.ModelPause.Value, ds.Ceiling, ds.ShortfallPct);
        var step = DoseCalculator.Decide(controller.FeederWork, controller.FeederPause, plan, null, now, p);
        var work = step.Change ? step.Work : controller.FeederWork;
        var pause = step.Change ? step.Pause : controller.FeederPause;
        var (blower, _, blowerCode) = DoseCalculator.LimitBlower(DoseCalculator.BlowerFor(work, pause, p), controller.Blower,
            inputs.Sample.FlueTemp, flueSafetyRecent, p);
        var suggested = $"{work}/{pause} s, dmuchawa {blower} %";

        if (work == controller.FeederWork && pause == controller.FeederPause && blower == controller.Blower)
        {
            ds.ShadowWork = work;
            ds.ShadowPause = pause;
            ds.ShadowBlower = blower;
            var same = $"Przycisk „Ustaw sugerowane”: sugerowane ({suggested}) jest już w sterowniku - nic do ustawienia.";
            run.Notes.Add("przycisk: sugerowane = obecne");
            run.Entries.Add(DoseLog.Entry(ctx, DoseLog.ButtonParameter, null, null, DoseLog.ButtonCode, DoseLog.StatusInternal, same));
            await _repository.SetApplyRequestStatusAsync(request, DoseLog.StatusRequestDone,
                $"Zlecenie z przycisku obsłużone: sugerowane ({suggested}) już w sterowniku.", ct);
            await _notifier.NotifyAsync("HeatFlow: przycisk „Ustaw sugerowane”", same, ct);
            return;
        }

        var result = await _writer.WriteSettingsAsync(run.Entities, controller, work, pause, blower, ct);
        if (result.Last != null) SetController(ds, result.Last);

        static string ParameterOf(DoseRegister r) => r switch
        {
            DoseRegister.Work => DoseLog.Work,
            DoseRegister.Pause => DoseLog.Pause,
            _ => DoseLog.Blower
        };

        var lines = new List<string>();
        foreach (var w in result.Writes)
        {
            var entry = DoseLog.Change(ctx, ParameterOf(w.Register), w.From, w.To, DoseLog.ButtonCode,
                w.Confirmation == "OK" ? DoseLog.StatusWritten : DoseLog.StatusUnconfirmed,
                w.Register == DoseRegister.Blower && blowerCode != null ? DoseLog.Why(blowerCode, ctx) : null,
                w.Confirmation, w.Seen);
            run.Entries.Add(entry);
            lines.Add(entry.Message);
        }

        if (!result.Confirmed)
        {
            run.Notes.Add($"przycisk: zapis niepotwierdzony: {result.Report}");
            var restore = await _writer.WriteSettingsAsync(run.Entities, result.Last ?? controller,
                controller.FeederWork, controller.FeederPause, controller.Blower, ct);
            if (restore.Last != null) SetController(ds, restore.Last);
            foreach (var w in restore.Writes)
            {
                var entry = DoseLog.Change(ctx, ParameterOf(w.Register), w.From, w.To, "RECZNE_PRZYWROCENIE",
                    w.Confirmation == "OK" ? DoseLog.StatusWritten : DoseLog.StatusUnconfirmed, null, w.Confirmation, w.Seen);
                run.Entries.Add(entry);
                lines.Add(entry.Message);
            }

            var failed = $"zapis nie potwierdził się w mapperze ({result.Report}); " +
                         (restore.Confirmed ? "wartości sprzed kliknięcia przywrócone." : "przywrócenie TEŻ niepotwierdzone - sprawdź nastawy na panelu kotła!");
            await _repository.SetApplyRequestStatusAsync(request, DoseLog.StatusRequestDone,
                $"Zlecenie z przycisku obsłużone: {failed}", ct);
            await _notifier.NotifyAsync("HeatFlow: przycisk „Ustaw sugerowane” - NIEPOTWIERDZONE", string.Join("\n", lines.Append(failed)), ct);
            return;
        }

        ds.ShadowWork = work;
        ds.ShadowPause = pause;
        ds.ShadowBlower = blower;
        var what = $"{controller.FeederWork}/{controller.FeederPause} s, dmuchawa {controller.Blower} % → {suggested}";
        run.Notes.Add($"przycisk: ustawiono sugerowane {what}");
        await _repository.SetApplyRequestStatusAsync(request, DoseLog.StatusRequestDone,
            $"Zlecenie z przycisku obsłużone: ustawiono {what}.", ct);
        await _notifier.NotifyAsync($"Kocioł: ustawiono sugerowane przyciskiem ({suggested})",
            string.Join("\n", lines), ct);
    }

    /// <summary>Krok 1-4, 6 i szybki trym - wynik do DoseState.</summary>
    private void Calculate(DoseState ds, HeatingState state, IReadOnlyList<RoomConfiguration> rooms, HeatingParameters p,
        IReadOnlyList<DoseSample> samples, DateTime now, RunLog run)
    {
        var measured = samples
            .Where(s => s.OutdoorTemp.HasValue)
            .Select(s => (s.TimestampUtc, s.OutdoorTemp!.Value));
        var tz = DoseCalculator.SmoothedOutdoorTemp(measured, state.Forecast, now,
            p.FeedSmoothingPastHours, p.FeedSmoothingForecastHours);
        if (tz == null)
        {
            run.Notes.Add("brak pomiaru Tz z czujnika ogrodowego - dawki nie liczę");
            return;
        }

        var currentWork = ds.ControllerWork ?? ds.PlannedWork ?? p.FeederWorkMin;
        // Cel domu: z pokoi ważonych stratami (pokój = wyższa z: cel, faktyczna wygładzona) albo stała;
        // wzrost od celów ograniczony i z dodatkiem na dogrzanie, spadek od razu.
        var actuals = p.DoseTargetUseActual
            ? DoseCalculator.ParseRoomActuals(ds.RoomActualSmoothed).ToDictionary(x => x.Key, x => x.Value.Value)
            : null;
        var (house, rawTarget, source, inputs) = HouseTargetFor(rooms, state, p, actuals);
        var hours = ds.LastCalcUtc == null ? 0 : (now - ds.LastCalcUtc.Value).TotalHours;
        var previousUsed = ds.HouseTargetUsed;
        if (previousUsed.HasValue && ds.HouseTarget.HasValue && source == ds.HouseTargetSource && source == "pokoje"
            && ds.RoomTargetsLastCalc != null)
        {
            // Zmiana od samych faktycznych (cele jak przy poprzednim liczeniu) przechodzi od razu i bez
            // dodatku na dogrzanie: dom już ma tę temperaturę, nie trzeba go do niej grzać. Ograniczenie
            // wzrostu i dodatek dotyczą tylko wzrostu celów (harmonogram, Paweł podnosi nastawy).
            var lastTargets = ParseTargets(ds.RoomTargetsLastCalc);
            var atLastTargets = DoseCalculator.CombineHouseTarget(
                inputs.Select(r => r with { Target = lastTargets.GetValueOrDefault(r.Name, r.Target) }).ToList(), actuals, p).Target;
            if (atLastTargets.HasValue) previousUsed += atLastTargets.Value - ds.HouseTarget.Value;
        }
        var (usedTarget, bonus) = DoseCalculator.LimitTargetRise(rawTarget, previousUsed, hours, p);
        LogHouseTargetChange(ds, rawTarget, source, house, run);
        ds.HouseTarget = Math.Round(rawTarget, 2);
        ds.HouseTargetBase = house.BaseTarget.HasValue ? Math.Round(house.BaseTarget.Value, 2) : null;
        ds.HouseTargetUsed = Math.Round(usedTarget, 3);
        ds.HouseTargetSource = source;
        ds.StorageBonusSec = Math.Round(bonus);
        ds.RoomTargetsLastCalc = source == "pokoje" ? FormatTargets(house.Rooms) : null;
        ds.HouseTargetActualRooms = Truncate(string.Join(";", house.Rooms.Where(r => r.ByActual).Select(r => r.Name)), 500);

        EvaluateTrim(ds, DoseCalculator.Plan(p, tz.Value, currentWork, 0, usedTarget, bonus), samples, now, p, run);
        var plan = DoseCalculator.Plan(p, tz.Value, currentWork, ds.TrimSec, usedTarget, bonus);

        ds.OutdoorSmoothed = Math.Round(tz.Value, 2);
        ds.NeedSecPerDay = Math.Round(DoseCalculator.Need(p, tz.Value, usedTarget) + bonus);
        ds.PlannedWork = plan.Work;
        ds.ModelPause = Math.Round(plan.ModelPause, 1);
        ds.Ceiling = plan.Ceiling;
        ds.ShortfallPct = Math.Round(plan.ShortfallPct, 1);
        ds.LastCalcUtc = now;
        if (plan.Ceiling)
        {
            run.Notes.Add($"kocioł nie nadąża - sufit {p.FeederWorkMax}/{p.FeederPauseMin} s, brakuje {plan.ShortfallPct:F0} %");
        }
    }

    /// <summary>
    /// Szybki trym od kotła (§ 9): kocioł nie dochodzi do nastawy przy ciągłej dmuchawie przez 2 h
    /// → postój −20 s; ≥ 2 wejścia w podtrzymanie w 6 h → +20 s. Nie częściej niż co
    /// FeedMinIntervalHours. Na suficie nie zbiera (anti-windup). Zerowany o północy.
    /// </summary>
    private static void EvaluateTrim(DoseState ds, DosePlan planWithoutTrim, IReadOnlyList<DoseSample> samples,
        DateTime now, HeatingParameters p, RunLog run)
    {
        if (ds.LastTrimUtc.HasValue && now - ds.LastTrimUtc.Value < TimeSpan.FromHours(p.FeedMinIntervalHours))
        {
            return;
        }

        var (_, _, pauseMin, pauseMax) = DoseCalculator.Bounds(p);
        var underpowerWindow = TimeSpan.FromHours(p.DoseTrimUnderpowerHours);
        var last2h = samples.Where(s => s.TimestampUtc > now - underpowerWindow).ToList();
        var last6h = samples.Where(s => s.TimestampUtc > now - TimeSpan.FromHours(p.DoseTrimStandbyHours)).ToList();
        var trimStep = p.DoseTrimStepSec;
        var trimLimit = p.DoseTrimLimitSec;
        var effective = planWithoutTrim.ModelPause + ds.TrimSec;
        const string followUp = "postój z modelu przesunięty - sterownik dostanie to przy najbliższej zmianie";

        string? code = null;
        int step = 0;
        string? blockedText = null;
        if (DoseCalculator.BoilerUnderpowered(last2h, underpowerWindow, p.DoseTrimUnderpowerK))
        {
            code = "TRYM_KOCIOL_PONIZEJ_NASTAWY";
            if (!planWithoutTrim.Ceiling && effective - trimStep >= pauseMin
                && ds.TrimSec - trimStep >= -trimLimit)
                step = -trimStep;
            else
                blockedText = planWithoutTrim.Ceiling
                    ? "kocioł na suficie dawki - trym nie zbiera"
                    : effective - trimStep < pauseMin
                        ? $"postój z trymem zszedłby poniżej minimum {pauseMin} s - trym nie zbiera"
                        : $"trym już na granicy −{trimLimit} s";
        }
        else if (DoseCalculator.StandbyEntries(last6h) >= p.DoseTrimStandbyEntries)
        {
            code = "TRYM_PODTRZYMANIA";
            if (effective + trimStep <= pauseMax
                && ds.TrimSec + trimStep <= trimLimit)
                step = trimStep;
            else
                blockedText = effective + trimStep > pauseMax
                    ? "postój już na maksimum"
                    : $"trym już na granicy +{trimLimit} s";
        }

        if (code == null) return;

        if (step != 0)
        {
            var old = ds.TrimSec;
            ds.TrimSec += step;
            ds.LastTrimUtc = now;
            ds.LastTrimCode = code;
            run.Notes.Add($"trym {old:+0;-0} → {ds.TrimSec:+0;-0} s ({code})");
            run.Entries.Add(DoseLog.Change(run.Ctx, DoseLog.Trim, old, ds.TrimSec, code, DoseLog.StatusInternal, extra: followUp));
        }
        else
        {
            run.Notes.Add($"trym: {blockedText}");
            var key = $"TRYM|{code}|{ds.TrimSec}";
            if (ds.LastLoggedBlockKey != key)
            {
                ds.LastLoggedBlockKey = key;
                var wanted = ds.TrimSec + (code == "TRYM_PODTRZYMANIA" ? trimStep : -trimStep);
                run.Entries.Add(DoseLog.Entry(run.Ctx, DoseLog.Trim, ds.TrimSec, wanted, code, DoseLog.StatusBlocked,
                    $"Chciałbym: {DoseLog.Head(DoseLog.Trim, ds.TrimSec, wanted)}, ale {blockedText}. Dlaczego: {DoseLog.Why(code, run.Ctx)}."));
            }
        }
    }

    private async Task ApplyAsync(DoseState ds, ControllerFeedSettings controller, DoseDecision decision, int blower,
        int blowerWanted, string? blowerNote, string? blowerCode, HeatingParameters p, DateTime now, RunLog run, CancellationToken ct)
    {
        var cause = DoseCause(ds, controller, decision, p, now);
        var stepNote = StepLimitNote(ds, decision, p);
        var result = await _writer.WriteSettingsAsync(run.Entities, controller, decision.Work, decision.Pause, blower, ct);
        if (result.Last != null) SetController(ds, result.Last);

        var lines = new List<string>();
        foreach (var w in result.Writes)
        {
            var (parameter, code, extra) = w.Register switch
            {
                DoseRegister.Work => (DoseLog.Work, cause, (string?)null),
                DoseRegister.Pause => (DoseLog.Pause, cause, stepNote),
                _ => (DoseLog.Blower, blowerCode ?? cause,
                    blowerCode == null ? $"dmuchawa idzie za dawką ({decision.Work}/{decision.Pause} s = {100.0 * decision.Work / (decision.Work + decision.Pause):0.0} % pracy podajnika)" : null)
            };
            var entry = DoseLog.Change(run.Ctx, parameter, w.From, w.To, code,
                w.Confirmation == "OK" ? DoseLog.StatusWritten : DoseLog.StatusUnconfirmed,
                extra, w.Confirmation, w.Seen);
            run.Entries.Add(entry);
            lines.Add(entry.Message);
        }

        // Dmuchawa chciała w górę, ale reguła spalin / #24 nie pozwoliła - też do dziennika.
        if (blowerCode is "SPALINY_185" or "PO_AUTOMATYZACJI_24" && blowerWanted > controller.Blower)
        {
            var entry = DoseLog.Entry(run.Ctx, DoseLog.Blower, controller.Blower, blowerWanted, blowerCode, DoseLog.StatusBlocked,
                $"Chciałbym: {DoseLog.Head(DoseLog.Blower, controller.Blower, blowerWanted)} razem z dawką, ale {DoseLog.Why(blowerCode, run.Ctx)}.");
            run.Entries.Add(entry);
            lines.Add(entry.Message);
        }

        if (!result.Confirmed)
        {
            run.Notes.Add($"zapis niepotwierdzony: {result.Report}");
            await StartWriteHoldAsync(ds, controller, decision, blower, result, now, run, ct);
            return;
        }

        ds.LastChangeUtc = now;
        ds.LastChangeOutdoorSmoothed = ds.OutdoorSmoothed;
        ds.LastChangeDirection = DoseDirection(controller.FeederWork, controller.FeederPause, decision.Work, decision.Pause);
        ds.LastLoggedBlockKey = null;
        var what = $"{controller.FeederWork}/{controller.FeederPause} s → {decision.Work}/{decision.Pause} s, " +
                   $"dmuchawa {controller.Blower} → {blower} %";
        run.Notes.Add($"ZMIANA {what} ({cause}: {decision.Reason}{(blowerNote != null ? "; " + blowerNote : "")})");
        await _notifier.NotifyAsync($"Kocioł: dawka {decision.Work}/{decision.Pause} s, dmuchawa {blower} %",
            string.Join("\n", lines), ct);
    }

    /// <summary>
    /// Przyczyna zmiany dawki do dziennika: SUFIT (ląduje na maks. pracy / min. postoju przy
    /// sufitowym planie), trym (bez trymu zmiany by nie było albo byłaby inna), inaczej POGODA.
    /// </summary>
    private static string DoseCause(DoseState ds, ControllerFeedSettings controller, DoseDecision decision,
        HeatingParameters p, DateTime now)
    {
        var (_, workMax, pauseMin, pauseMax) = DoseCalculator.Bounds(p);
        if (ds.Ceiling && decision.Work == workMax && decision.Pause == pauseMin)
            return "SUFIT";

        if (ds.TrimSec != 0 && ds.LastTrimCode != null && ds.ModelPause.HasValue && ds.PlannedWork.HasValue)
        {
            var withoutTrim = new DosePlan(ds.PlannedWork.Value,
                Math.Clamp(ds.ModelPause.Value - ds.TrimSec, pauseMin, pauseMax), ds.Ceiling, ds.ShortfallPct);
            var d = DoseCalculator.Decide(controller.FeederWork, controller.FeederPause, withoutTrim, null, now, p);
            if (!d.Change || d.Work != decision.Work || d.Pause != decision.Pause)
                return ds.LastTrimCode;
        }

        return "POGODA";
    }

    /// <summary>„Chciałbym postój 160 → 140 s, ale od poprzedniej zmiany minęło dopiero 70 min” - raz na daną decyzję.</summary>
    private static void LogWaitingForInterval(DoseState ds, ControllerFeedSettings controller, DoseDecision decision,
        HeatingParameters p, DateTime now, RunLog run)
    {
        // Następny krok tej samej rampy (ten sam kierunek, ten sam czas pracy) czeka na odstęp z definicji -
        // to widać we wpisie zmiany („cel z modelu …”), więc nie dublujemy go wpisem ZABLOKOWANE.
        var wanted = DoseDirection(controller.FeederWork, controller.FeederPause, decision.Work, decision.Pause);
        if (decision.Work == controller.FeederWork && wanted == ds.LastChangeDirection) return;

        var key = $"ODSTEP|{decision.Work}|{decision.Pause}";
        if (ds.LastLoggedBlockKey == key) return;
        ds.LastLoggedBlockKey = key;

        var cause = DoseCause(ds, controller, decision with { Change = true }, p, now);
        var (parameter, from, to) = decision.Work != controller.FeederWork
            ? (DoseLog.Work, controller.FeederWork, decision.Work)
            : (DoseLog.Pause, controller.FeederPause, decision.Pause);
        run.Entries.Add(DoseLog.Entry(run.Ctx, parameter, from, to, "ODSTEP", DoseLog.StatusBlocked,
            $"Chciałbym: {DoseLog.Head(parameter, from, to)}, ale od poprzedniej zmiany minęło dopiero " +
            $"{decision.MinutesSinceLastChange:0} min (min. {p.FeedMinIntervalHours} h). Dlaczego: {DoseLog.Why(cause, run.Ctx)}."));
    }

    /// <summary>
    /// Tryb cienia (automat wyłączony): te same reguły co automat (odstęp, martwa strefa, krok,
    /// dmuchawa), ale od wirtualnych nastaw „gdyby automat był włączony”, startujących od tego, co
    /// jest w sterowniku. Wpis SUGESTIA tylko przy wirtualnej zmianie - jak w dzienniku automatu.
    /// </summary>
    private static void ShadowStep(DoseState ds, ControllerFeedSettings controller, double? flue, bool flueSafetyRecent,
        HeatingParameters p, DateTime now, RunLog run)
    {
        if (ds.ShadowWork == null || ds.ShadowPause == null || ds.ShadowBlower == null)
        {
            ds.ShadowWork = controller.FeederWork;
            ds.ShadowPause = controller.FeederPause;
            ds.ShadowBlower = controller.Blower;
            ds.ShadowLastChangeUtc = null;
        }

        var shadow = new ControllerFeedSettings(ds.ShadowWork.Value, ds.ShadowPause.Value, ds.ShadowBlower.Value);
        var plan = new DosePlan(ds.PlannedWork!.Value, ds.ModelPause!.Value, ds.Ceiling, ds.ShortfallPct);
        var decision = DoseCalculator.Decide(shadow.FeederWork, shadow.FeederPause, plan, ds.ShadowLastChangeUtc, now, p);
        var (blower, _, blowerCode) = DoseCalculator.LimitBlower(
            DoseCalculator.BlowerFor(decision.Work, decision.Pause, p), shadow.Blower, flue, flueSafetyRecent, p);

        ds.PlannedPause = decision.Change ? decision.Pause : shadow.FeederPause;
        ds.PlannedBlower = decision.Change ? blower : shadow.Blower;
        if (!decision.Change)
        {
            run.Notes.Add($"automat wyłączony - cień {shadow.FeederWork}/{shadow.FeederPause} s, {shadow.Blower} % ({decision.Reason})");
            return;
        }

        var cause = DoseCause(ds, shadow, decision, p, now);
        var stepNote = StepLimitNote(ds, decision, p);
        foreach (var (parameter, from, to) in new[]
                 {
                     (DoseLog.Work, shadow.FeederWork, decision.Work),
                     (DoseLog.Pause, shadow.FeederPause, decision.Pause),
                     (DoseLog.Blower, shadow.Blower, blower)
                 })
        {
            if (from == to) continue;
            var why = parameter == DoseLog.Blower
                ? DoseLog.Why(blowerCode ?? "", run.Ctx, blowerCode == null ? "dmuchawa idzie za dawką" : null)
                : DoseLog.Why(cause, run.Ctx, parameter == DoseLog.Pause ? stepNote : null);
            run.Entries.Add(DoseLog.Entry(run.Ctx, parameter, from, to, parameter == DoseLog.Blower ? blowerCode ?? cause : cause,
                DoseLog.StatusSuggestion, $"Automat wyłączony - ustawiłbym: {DoseLog.Head(parameter, from, to)}: {why}."));
        }

        run.Notes.Add($"automat wyłączony - cień {shadow.FeederWork}/{shadow.FeederPause} → {decision.Work}/{decision.Pause} s, {blower} %");
        ds.ShadowWork = decision.Work;
        ds.ShadowPause = decision.Pause;
        ds.ShadowBlower = blower;
        ds.ShadowLastChangeUtc = now;
    }

    /// <summary>„cel z modelu 112 s - dalej krokami ≤ 20 s co 2 h”, gdy krok ograniczył zmianę postoju.</summary>
    private static string? StepLimitNote(DoseState ds, DoseDecision decision, HeatingParameters p)
    {
        if (ds.ModelPause == null || ds.PlannedWork == null) return null;
        if (ds.PlannedWork != decision.Work)
            return $"cel z modelu {ds.PlannedWork}/{ds.ModelPause.Value:0} s - dalej krokami co {p.FeedMinIntervalHours} h";
        return Math.Abs(ds.ModelPause.Value - decision.Pause) >= 5
            ? $"cel z modelu {ds.ModelPause.Value:0} s - dalej krokami ≤ {p.FeedMaxStepSec} s co {p.FeedMinIntervalHours} h"
            : null;
    }

    /// <summary>+1 = więcej węgla (większy udział pracy podajnika), −1 = mniej.</summary>
    private static int DoseDirection(int fromWork, int fromPause, int toWork, int toPause)
        => Math.Sign((double)toWork / (toWork + toPause) - (double)fromWork / (fromWork + fromPause));

    /// <summary>Kod z <see cref="SafetyReason"/>, który wstrzymuje automat zamiast go wyłączać.</summary>
    private const string SummerPauseCode = "TRYB_LATO";
    private const string SummerPauseKey = "PAUZA|TRYB_LATO";

    /// <summary>Tyle czeka niepotwierdzony zapis, zanim automat policzy dawkę od nowa i zapisze ją jeszcze raz.</summary>
    private static readonly TimeSpan WriteHoldTimeout = TimeSpan.FromHours(2);

    private const string HoldParameter = "wstrzymanie_zapisu";
    private const string HoldOpen = "WSTRZYMANIE";
    private const string HoldConfirmed = "WSTRZYMANIE_POTWIERDZONE";
    private const string HoldClosed = "WSTRZYMANIE_ZAMKNIETE";
    private const string HoldTimedOut = "WSTRZYMANIE_PRZEKROCZONE";

    /// <summary>Tyle razy z rzędu zapis może nie dojść (każda próba = <see cref="WriteHoldTimeout"/>), zanim automat się wyłączy.</summary>
    private const int MaxHoldAttempts = 3;

    private static string Minutes(TimeSpan span) => span.TotalMinutes >= 120 ? $"{span.TotalHours:0.#} h" : $"{span.TotalMinutes:0} min";

    /// <summary>
    /// Tryb lato: automat zostaje włączony, ale niczego nie zapisuje (kocioł grzeje wtedy wodę, nie dom). Nastawy w
    /// sterowniku zostają takie, jakie są; po lecie dawka rusza od nich. Raz na epizod wpis i Discord (klucz w LastLoggedBlockKey).
    /// </summary>
    private async Task PauseForSummerAsync(DoseState ds, SafetyIssue issue, DateTime now, RunLog run, CancellationToken ct)
    {
        run.Notes.Add($"automat wstrzymany: {issue.Text} - bez zapisów");
        if (ds.LastLoggedBlockKey == SummerPauseKey) return;
        ds.LastLoggedBlockKey = SummerPauseKey;
        const string text = "kocioł na trybie lato - automat czeka (niczego nie zapisuje, nastawy zostają); wróci sam po powrocie na zimę.";
        run.Entries.Add(DoseLog.Entry(run.Ctx, DoseLog.Automat, 1, 1, "PAUZA_TRYB_LATO", DoseLog.StatusInternal, $"Automat dawki wstrzymany: {text}"));
        await _notifier.NotifyAsync("HeatFlow: automat dawki wstrzymany (tryb lato)", text, ct);
    }

    private async Task ResumeFromSummerPauseAsync(DoseState ds, DateTime now, RunLog run, CancellationToken ct)
    {
        if (ds.LastLoggedBlockKey != SummerPauseKey) return;
        ds.LastLoggedBlockKey = null;
        const string text = "koniec trybu lato - automat znów steruje dawką (od nastaw, które są teraz w sterowniku).";
        run.Notes.Add(text);
        run.Entries.Add(DoseLog.Entry(run.Ctx, DoseLog.Automat, 1, 1, "PAUZA_KONIEC", DoseLog.StatusInternal, $"Automat dawki wznowiony: {text}"));
        await _notifier.NotifyAsync("HeatFlow: automat dawki wznowiony", text, ct);
    }

    /// <summary>
    /// Zapis nie potwierdził się w mapperze (także po ponownych odczytach w <see cref="DoseControllerWriter"/>). To jeszcze
    /// nie powód do wyłączenia: noc 2026-10-25 (cofnięcie zegara sterownika przy zmianie czasu) pokazała, że mapper potrafi
    /// przez godzinę pokazywać stare dane, a zapis i tak dochodzi. Stan trzymamy w dzienniku (wpis WSTRZYMANIE z
    /// nastawami, które zapis miał ustawić); <see cref="ResolveWriteHoldAsync"/> sprawdza go co przebieg.
    /// </summary>
    private async Task StartWriteHoldAsync(DoseState ds, ControllerFeedSettings before, DoseDecision decision, int blower,
        DoseWriteResult result, DateTime now, RunLog run, CancellationToken ct)
    {
        var seen = result.Last ?? before;
        var intended = $"{decision.Work}/{decision.Pause} s, dmuchawa {blower} %";
        var text = $"zapis {before.FeederWork}/{before.FeederPause} s, dmuchawa {before.Blower} % → {intended} nie potwierdził się w mapperze " +
                   $"({result.Report}; mapper pokazuje {seen.FeederWork}/{seen.FeederPause} s, dmuchawa {seen.Blower} %). " +
                   $"Automat czeka z kolejnymi zapisami i sprawdza mapper co przebieg; jeśli przez {Minutes(WriteHoldTimeout)} nie potwierdzi, " +
                   "przeliczy dawkę od nowa i zapisze ją jeszcze raz (stare nastawy nie wracają).";
        var entry = DoseLog.Entry(run.Ctx, HoldParameter, before.FeederPause, decision.Pause, "ZAPIS_NIEPOTWIERDZONY", HoldOpen,
            $"Automat dawki wstrzymany: {text}");
        entry.ControllerWork = decision.Work;       // dla tego wpisu: nastawy, które zapis miał ustawić (nie stan sterownika)
        entry.ControllerPause = decision.Pause;
        entry.ControllerBlower = blower;
        run.Entries.Add(entry);
        run.Notes.Add($"automat wstrzymany: {text}");
        await _notifier.NotifyAsync("HeatFlow: zapis dawki niepotwierdzony - automat czeka", text, ct);
    }

    /// <summary>
    /// Wstrzymanie po niepotwierdzonym zapisie: gdy mapper pokazuje już czas pracy i postój, które miał ustawić zapis -
    /// wznawiamy (odstęp 2 h liczy się od zapisu); dmuchawę, której zapis nie ruszył (idzie po postoju), ustawiamy teraz,
    /// z bieżącymi ograniczeniami od spalin. Gdy minął limit - zwraca true: dawka jest liczona i zapisywana od nowa (stare
    /// zapamiętane nastawy są gorsze niż świeże przeliczenie); dopiero <see cref="MaxHoldAttempts"/> nieudanych prób z rzędu
    /// wyłącza automat, BEZ przywracania. W międzyczasie żadnych zapisów. Zwraca false, gdy sprawa załatwiona w tym przebiegu.
    /// </summary>
    private async Task<bool> ResolveWriteHoldAsync(DoseState ds, ControllerFeedSettings? controller, FeedChangeLog hold,
        double? flue, bool flueSafetyRecent, HeatingParameters p, DateTime now, RunLog run, CancellationToken ct)
    {
        var age = now - hold.TimestampUtc;
        var landed = controller != null && hold.ControllerWork == controller.FeederWork && hold.ControllerPause == controller.FeederPause;
        if (landed)
        {
            var (blower, _, blowerCode) = DoseCalculator.LimitBlower(DoseCalculator.BlowerFor(controller!.FeederWork, controller.FeederPause, p),
                controller.Blower, flue, flueSafetyRecent, p);
            if (blower != controller.Blower)
            {
                var result = await _writer.WriteAsync(run.Entities, DoseRegister.Blower, blower, ct);
                if (result.Last != null) SetController(ds, result.Last);
                var w = result.Writes.FirstOrDefault();
                run.Entries.Add(DoseLog.Change(run.Ctx, DoseLog.Blower, controller.Blower, blower, blowerCode ?? "WSTRZYMANIE_DOKONCZENIE",
                    result.Confirmed ? DoseLog.StatusWritten : DoseLog.StatusUnconfirmed,
                    extra: "dmuchawa idzie za dawką, której zapis potwierdził się z opóźnieniem", confirmation: w?.Confirmation, seen: w?.Seen));
                if (!result.Confirmed)
                {
                    run.Notes.Add($"wstrzymanie trwa: czas pracy i postój już w sterowniku, ale dmuchawa {controller.Blower} → {blower} % niepotwierdzona ({result.Report})");
                    if (age < WriteHoldTimeout) return false;
                    landed = false; // limit minął, a zapis nie jest cały
                }
                else
                {
                    controller = result.Last ?? controller with { Blower = blower };
                }
            }
        }

        if (landed)
        {
            var text = $"zapis {controller!.FeederWork}/{controller.FeederPause} s, dmuchawa {controller.Blower} % potwierdził się w mapperze " +
                       $"po {Minutes(age)} - automat działa dalej.";
            await _repository.SetApplyRequestStatusAsync(hold, HoldConfirmed, $"Zapis potwierdzony z opóźnieniem ({Minutes(age)}).", ct);
            ds.LastChangeUtc = hold.TimestampUtc;
            ds.LastChangeOutdoorSmoothed = ds.OutdoorSmoothed;
            ds.LastChangeDirection = hold.OldValue.HasValue && hold.NewValue.HasValue ? Math.Sign(hold.OldValue.Value - hold.NewValue.Value) : 0;
            ds.LastLoggedBlockKey = null;
            run.Notes.Add($"wstrzymanie zakończone: {text}");
            run.Entries.Add(DoseLog.Entry(run.Ctx, HoldParameter, null, null, "ZAPIS_POTWIERDZONY_POZNIEJ", DoseLog.StatusInternal,
                $"Automat dawki wznowiony: {text}"));
            await _notifier.NotifyAsync("HeatFlow: zapis dawki potwierdzony - automat wznowiony", text, ct);
            return false;
        }

        if (age >= WriteHoldTimeout)
        {
            var attempt = 1 + await TimedOutHoldsInARowAsync(ct);
            await _repository.SetApplyRequestStatusAsync(hold, HoldTimedOut,
                $"Zapis nie potwierdził się przez {Minutes(age)} (próba {attempt} z {MaxHoldAttempts}).", ct);
            if (attempt >= MaxHoldAttempts)
            {
                await DeactivateAsync(ds, controller,
                    new SafetyIssue("ZAPIS_NIEPOTWIERDZONY", $"zapis nie potwierdził się w mapperze po {attempt} próbach (łącznie ok. {Minutes(WriteHoldTimeout * attempt)})"),
                    selfDisable: true, noBlowerIncrease: false, p, now, run, ct, restoreSaved: false);
                return false;
            }

            var retry = $"zapis nie potwierdził się przez {Minutes(age)} (próba {attempt} z {MaxHoldAttempts}) - przeliczam dawkę od nowa i zapisuję jeszcze raz. " +
                        "Stare nastawy nie wracają.";
            run.Notes.Add($"wstrzymanie: {retry}");
            run.Entries.Add(DoseLog.Entry(run.Ctx, HoldParameter, null, null, "ZAPIS_PONOWNIE", DoseLog.StatusInternal, $"Automat dawki: {retry}"));
            await _notifier.NotifyAsync("HeatFlow: zapis dawki dalej niepotwierdzony - próbuję jeszcze raz", retry, ct);
            return true;
        }

        run.Notes.Add($"automat wstrzymany od {Minutes(age)}: czekam na potwierdzenie zapisu " +
                      $"{hold.ControllerWork}/{hold.ControllerPause} s, dmuchawa {hold.ControllerBlower} % (limit {Minutes(WriteHoldTimeout)})");
        return false;
    }

    /// <summary>Ile ostatnich wstrzymań z rzędu skończyło się przekroczeniem limitu (do pierwszego potwierdzonego zapisu).</summary>
    private async Task<int> TimedOutHoldsInARowAsync(CancellationToken ct)
    {
        var count = 0;
        foreach (var e in await _repository.GetChangeLogAsync(200, ct))
        {
            if (e.Parameter == HoldParameter && e.Status == HoldTimedOut) count++;
            else if (e.Status == DoseLog.StatusWritten || e.Status == HoldConfirmed || e.ReasonCode.StartsWith("WLACZENIE", StringComparison.Ordinal)) break;
        }
        return count;
    }

    /// <summary>
    /// Wyłączenie automatu - ręczne (przełącznik w HA) albo samowyłączenie. Przywraca nastawy zastane
    /// przy włączeniu (DoseRestoreOnDisable), przy spalinach &gt; 200 °C bez podnoszenia dmuchawy.
    /// </summary>
    private async Task DeactivateAsync(DoseState ds, ControllerFeedSettings? controller, SafetyIssue reason,
        bool selfDisable, bool noBlowerIncrease, HeatingParameters p, DateTime now, RunLog run, CancellationToken ct,
        bool restoreSaved = true)
    {
        var code = selfDisable ? $"SAMOWYLACZENIE_{reason.Code}" : "WYLACZENIE_RECZNE";
        var openHold = await _repository.GetOpenWriteHoldAsync(ct);
        if (openHold != null)
            await _repository.SetApplyRequestStatusAsync(openHold, HoldClosed, $"Wstrzymanie zamknięte: {reason.Text}.", ct);
        run.Entries.Add(DoseLog.Entry(run.Ctx, DoseLog.Automat, 1, 0, code, DoseLog.StatusInternal,
            selfDisable
                ? $"Automat dawki SAM SIĘ WYŁĄCZYŁ: {reason.Text}."
                : "Automat dawki wyłączony w HA."));

        var restore = await (restoreSaved ? RestoreAsync(ds, controller, noBlowerIncrease, p, run.Entities, ct) : Task.FromResult(NoRestore));
        foreach (var w in restore.Writes)
        {
            var parameter = w.Register switch
            {
                DoseRegister.Work => DoseLog.Work,
                DoseRegister.Pause => DoseLog.Pause,
                _ => DoseLog.Blower
            };
            run.Entries.Add(DoseLog.Change(run.Ctx, parameter, w.From, w.To, "PRZYWROCENIE_NASTAW",
                w.Confirmation == "OK" ? DoseLog.StatusWritten : DoseLog.StatusUnconfirmed,
                extra: selfDisable ? $"po samowyłączeniu ({reason.Text})" : "po wyłączeniu w HA",
                confirmation: w.Confirmation, seen: w.Seen));
        }

        if (selfDisable)
        {
            await _repository.SetAutomatSwitchAsync(false, $"Faza5: {Truncate(reason.Text, 180)}", ct);
            ds.LastSelfDisableReason = Truncate(reason.Text, 500);
            ds.LastSelfDisableUtc = now;
        }

        ds.AutomatActive = false;
        run.Ctx.AutomatActive = false;
        ResetShadow(ds);
        ds.SavedWork = null;
        ds.SavedPause = null;
        ds.SavedBlower = null;
        ds.LastLoggedBlockKey = null;

        run.Notes.Add($"automat {(selfDisable ? "SAM SIĘ WYŁĄCZYŁ" : "wyłączony")}: {reason.Text}; {restore.Summary}");
        await _notifier.NotifyAsync(
            selfDisable ? "HeatFlow: automat dawki SAM SIĘ WYŁĄCZYŁ" : "HeatFlow: automat dawki wyłączony",
            $"Powód: {reason.Text}. {restore.Summary}",
            ct);
    }

    private record RestoreOutcome(string Summary, IReadOnlyList<DoseRegisterWrite> Writes);

    private static readonly RestoreOutcome NoRestore = new(
        "Nastaw NIE przywracam (zapamiętane są starsze niż to, co policzył automat) - w sterowniku zostają ostatnie; sprawdź przy kotle.",
        Array.Empty<DoseRegisterWrite>());

    private async Task<RestoreOutcome> RestoreAsync(DoseState ds, ControllerFeedSettings? controller, bool noBlowerIncrease,
        HeatingParameters p, DoseEntities entities, CancellationToken ct)
    {
        var none = Array.Empty<DoseRegisterWrite>();
        if (!p.DoseRestoreOnDisable)
            return new("Nastaw nie przywracam (DoseRestoreOnDisable = wył.) - zostają ostatnie z automatu.", none);
        if (ds.SavedWork == null || ds.SavedPause == null || ds.SavedBlower == null)
            return new("Brak zapamiętanych nastaw - nic nie przywracam.", none);

        var work = Math.Clamp(ds.SavedWork.Value, DoseLimits.WorkMin, DoseLimits.WorkMax);
        var pause = Math.Clamp(ds.SavedPause.Value, DoseLimits.PauseMin, DoseLimits.PauseMax);
        var blower = Math.Clamp(ds.SavedBlower.Value, DoseLimits.BlowerMin, DoseLimits.BlowerMax);
        var wanted = $"{work}/{pause} s, dmuchawa {blower} %";

        if (controller == null)
            return new($"Mapper nie odpowiada - nastaw NIE przywróciłem, sprawdź przy kotle (miało być {wanted}).", none);

        if (noBlowerIncrease && blower > controller.Blower)
            blower = controller.Blower;

        var result = await _writer.WriteSettingsAsync(entities, controller, work, pause, blower, ct);
        if (result.Last != null) SetController(ds, result.Last);
        return new(result.Confirmed
            ? $"Przywrócone: praca {work} s, postój {pause} s, dmuchawa {blower} % (potwierdzone w mapperze)."
            : $"PRZYWRÓCENIE NIE DOSZŁO ({result.Report}) - sprawdź przy kotle, miało być {wanted}.", result.Writes);
    }

    /// <summary>Powody samowyłączenia (§ 9 „Wyłącznik i bezpieczniki”). Null = można działać.</summary>
    private static SafetyIssue? SafetyReason(DoseInputs inputs, DoseState ds, IReadOnlyList<DoseSample> samples,
        DateTime now, HeatingParameters p)
    {
        if (inputs.Sample.FlueTemp > p.DoseFlueMaxTemp)
            return new("SPALINY_200", $"spaliny {inputs.Sample.FlueTemp:F0} °C > {p.DoseFlueMaxTemp} °C");

        if (p.DoseBoilerColdMinutes > 0 && BoilerCold(samples, now, p.DoseBoilerColdMinutes, p.DoseBoilerColdK))
            return new("KOCIOL_ZIMNY", $"kocioł poniżej nastawy − {p.DoseBoilerColdK:0.#} K od {p.DoseBoilerColdMinutes} min");

        var boilerStale = TimeSpan.FromMinutes(p.DoseBoilerStaleMinutes);
        if (ds.LastBoilerDataUtc == null || now - ds.LastBoilerDataUtc.Value > boilerStale)
            return new("BRAK_DANYCH_KOTLA", $"brak świeżych danych kotła w HA (ostatnie: {Ago(ds.LastBoilerDataUtc, now)})");
        if (ds.LastMapperOkUtc == null || now - ds.LastMapperOkUtc.Value > boilerStale)
            return new("BRAK_MAPPERA", $"mapper nie odpowiada (ostatnio: {Ago(ds.LastMapperOkUtc, now)})");
        if (ds.LastOutdoorDataUtc == null || now - ds.LastOutdoorDataUtc.Value > TimeSpan.FromMinutes(p.DoseOutdoorStaleMinutes))
            return new("BRAK_TZ", $"brak świeżej temperatury zewnętrznej (ostatnia: {Ago(ds.LastOutdoorDataUtc, now)})");

        if (inputs.SummerMode == true)
            return new("TRYB_LATO", "kocioł przełączony na tryb lato");
        if (inputs.ControllerAuto == false)
            return new("TRYB_RECZNY", "sterownik w trybie ręcznym");

        return null;
    }

    private static bool BoilerCold(IReadOnlyList<DoseSample> samples, DateTime now, int minutes, double kelvinBelow)
    {
        var window = TimeSpan.FromMinutes(minutes);
        return DoseCalculator.BoilerColdFor(samples.Where(s => s.TimestampUtc > now - window).ToList(), window, kelvinBelow);
    }

    /// <summary>
    /// DoseBoilerColdMinutes = 0: kocioł zimny nie wyłącza automatu, tylko ostrzega - raz na epizod
    /// (wpis OSTRZEZENIE w dzienniku + Discord), po DoseBoilerColdWarnMinutes (60) poniżej nastawy − DoseBoilerColdK (10 K).
    /// </summary>
    private async Task WarnBoilerColdAsync(DoseState ds, IReadOnlyList<DoseSample> samples, DateTime now,
        HeatingParameters p, RunLog run, CancellationToken ct)
    {
        if (p.DoseBoilerColdMinutes != 0) return;
        if (!BoilerCold(samples, now, p.DoseBoilerColdWarnMinutes, p.DoseBoilerColdK))
        {
            ds.BoilerColdWarned = false;
            return;
        }
        if (ds.BoilerColdWarned) return;
        ds.BoilerColdWarned = true;
        var text = $"kocioł {DoseLog.Number(run.Ctx.BoilerTemp, "0.0")} °C, nastawa {DoseLog.Number(run.Ctx.BoilerSetpoint, "0")} °C - od {p.DoseBoilerColdWarnMinutes} min poniżej nastawy − {p.DoseBoilerColdK:0.#} K " +
                   $"(spaliny {DoseLog.Number(run.Ctx.FlueTemp, "0")} °C, dmuchawa {DoseLog.Number(run.Ctx.HeatingShare2hPct, "0")} % czasu). " +
                   "Automat zostaje włączony (DoseBoilerColdMinutes = 0).";
        run.Entries.Add(DoseLog.Entry(run.Ctx, DoseLog.Automat, 1, 1, "KOCIOL_ZIMNY", DoseLog.StatusWarning, $"Ostrzeżenie: {text}"));
        run.Notes.Add($"ostrzeżenie: {text}");
        await _notifier.NotifyAsync("HeatFlow: kocioł zimny", text, ct);
    }

    private static SafetyIssue? ParametersReason(HeatingParameters p)
    {
        var errors = DoseLimits.Validate(p);
        return errors.Count == 0 ? null : new("PARAMETRY", $"parametry poza granicami: {string.Join(", ", errors)}");
    }

    /// <summary>Korekta k raz na dobę, za wczorajszą dobę lokalną (§ 9 „Korekta raz na dobę”).</summary>
    private async Task CorrectKIfDueAsync(DoseState ds, HeatingParameters p, DateTime nowUtc,
        RunLog run, CancellationToken ct)
    {
        var tz = _time.LocalTimeZone;
        var today = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz).Date;
        var yesterday = today.AddDays(-1);
        if (ds.LastKCorrectionDate.HasValue && ds.LastKCorrectionDate.Value.Date >= yesterday)
        {
            return;
        }

        var dayStart = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(yesterday, DateTimeKind.Unspecified), tz);
        var dayEnd = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(today, DateTimeKind.Unspecified), tz);
        var beforeStart = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(yesterday.AddDays(-1), DateTimeKind.Unspecified), tz);

        var samples = await _repository.GetSamplesSinceAsync(beforeStart.AddMinutes(-10), ct);
        var day = DoseCalculator.BalanceForDay(samples, dayStart, dayEnd);
        var before = DoseCalculator.BalanceForDay(samples, beforeStart, dayStart);
        var oldK = p.FeedCorrectionK;
        var (newK, reason) = DoseCalculator.CorrectK(oldK, day, before.IndoorMean.HasValue ? before : null, p);
        var dayText = yesterday.ToString("yyyy-MM-dd");

        if (newK.HasValue && Math.Abs(newK.Value - oldK) > 0.0005)
        {
            var current = await _configurationService.GetHeatingParametersAsync(ct);
            var updated = Clone(current);
            updated.FeedCorrectionK = newK.Value;
            await _configurationService.SaveHeatingParametersAsync(updated, ct);
            try { await _auditService.LogHeatingParametersChangesAsync(current, updated, "Faza5", ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Faza 5: nie zapisano audytu zmiany k"); }
            // Ten przebieg liczy już z nowym k.
            p.FeedCorrectionK = newK.Value;
            run.Entries.Add(DoseLog.Change(run.Ctx, DoseLog.K, oldK, newK.Value, "KOREKTA_K", DoseLog.StatusInternal,
                extra: $"bilans doby {dayText}: {reason}"));
        }
        else
        {
            run.Entries.Add(DoseLog.Entry(run.Ctx, DoseLog.K, oldK, oldK,
                newK.HasValue ? "KOREKTA_K" : "KOREKTA_K_POMINIETA",
                newK.HasValue ? DoseLog.StatusInternal : DoseLog.StatusBlocked,
                $"k {DoseLog.Number(oldK, "0.000")} bez zmian - bilans doby {dayText}: {reason}."));
        }

        ds.LastKCorrectionDate = yesterday;
        // Trym zerujemy tylko wtedy, gdy k tej doby się uczyło i przejmuje stałą część różnicy. Doba pominięta
        // (lato, dziury, sufit) k nie rusza, więc trym zostaje - inaczej sugestie skaczą o północy (23.10: 210 → 150 s w 4 h).
        if (newK.HasValue)
        {
            if (ds.TrimSec != 0)
            {
                run.Entries.Add(DoseLog.Change(run.Ctx, DoseLog.Trim, ds.TrimSec, 0, "KOREKTA_K", DoseLog.StatusInternal,
                    extra: "trym zerowany o północy - stałą część różnicy przejmuje k"));
            }
            ds.TrimSec = 0;
            run.Notes.Add($"korekta k za {dayText}: {reason}; trym wyzerowany");
        }
        else
        {
            run.Notes.Add($"korekta k za {dayText}: {reason}; trym zostaje ({ds.TrimSec:+0;-0} s)");
        }
    }

    private async Task<DoseInputs> ReadInputsAsync(DoseEntities e, IReadOnlyList<RoomConfiguration> rooms, HeatingParameters p,
        DateTime now, CancellationToken ct)
    {
        var outdoorState = await _haClient.GetStateAsync(e.OutdoorTemp, ct);
        var outdoor = ParseDouble(outdoorState);

        // Temperatura w domu do korekty k: domyślnie średnia czujników z flagą UseForDoseIndoorMean
        // (7× BMx280 z kalibracji modelu); przy DoseIndoorMeasureWeighted - wszystkie pokoje ważone
        // HeatLossW, ten sam zestaw co cel domu.
        double? indoor = null;
        if (p.DoseIndoorMeasureWeighted)
        {
            var readings = new List<(double Value, double Weight)>();
            foreach (var room in rooms.Where(r => r.HeatLossW > 0 && !string.IsNullOrWhiteSpace(r.SensorTemperatureEntityId)))
            {
                var v = await ReadRoomTemperatureAsync(room.SensorTemperatureEntityId, ct);
                if (v.HasValue) readings.Add((v.Value, room.HeatLossW));
            }
            if (readings.Count >= p.DoseIndoorMinSensors)
                indoor = Math.Round(readings.Sum(r => r.Value * r.Weight) / readings.Sum(r => r.Weight), 2);
        }
        else
        {
            var values = new List<double>();
            foreach (var room in rooms.Where(r => r.UseForDoseIndoorMean && !string.IsNullOrWhiteSpace(r.SensorTemperatureEntityId)))
            {
                var v = await ReadRoomTemperatureAsync(room.SensorTemperatureEntityId, ct);
                if (v.HasValue) values.Add(v.Value);
            }
            if (values.Count >= p.DoseIndoorMinSensors) indoor = Math.Round(values.Average(), 2);
        }

        var clock = await _haClient.GetStateAsync(e.ControllerClock, ct);
        var counter = await _haClient.GetStateAsync(e.FeederCounter, ct);
        var flueSafety = string.IsNullOrWhiteSpace(e.FlueSafetyAutomation) ? null : await _haClient.GetStateAsync(e.FlueSafetyAutomation, ct);

        var sample = new DoseSample
        {
            TimestampUtc = now,
            OutdoorTemp = outdoor,
            IndoorTemp = indoor,
            BoilerTemp = ParseDouble(await _haClient.GetStateAsync(e.BoilerTemp, ct)),
            BoilerSetpoint = ParseDouble(await _haClient.GetStateAsync(e.BoilerSetpoint, ct)),
            FlueTemp = ParseDouble(await _haClient.GetStateAsync(e.FlueTemp, ct)),
            BlowerActual = ParseDouble(await _haClient.GetStateAsync(e.BlowerActual, ct)),
            FeederCounterSec = DoseCalculator.ParseFeederCounter(counter?.State),
            SummerMode = HomeAssistantClient.ParseBoolState((await _haClient.GetStateAsync(e.SummerSwitch, ct))?.State)
        };

        var controllerAuto = HomeAssistantClient.ParseBoolState((await _haClient.GetStateAsync(e.ControllerMode, ct))?.State);

        return new DoseInputs(
            sample,
            IsValid(clock) ? UpdatedUtc(clock!, now) : null,
            outdoor.HasValue ? UpdatedUtc(outdoorState!, now) : null,
            sample.SummerMode,
            controllerAuto,
            ParseLastTriggered(flueSafety));
    }

    /// <summary>
    /// Błędna konfiguracja encji (SystemConfiguration): faza nic nie czyta ani nie zapisuje i zgłasza
    /// błąd. Gdy automat był włączony - wyłącza go (bez przywracania: nie wiadomo, gdzie pisać) i pisze
    /// do dziennika i na Discord.
    /// </summary>
    private async Task<PhaseResult> ConfigurationErrorAsync(List<string> errors, DateTime now, DateTime startTime, CancellationToken ct)
    {
        var text = $"błędna konfiguracja encji Fazy 5: {string.Join("; ", errors)}";
        _logger.LogError("Faza 5: {Text}", text);
        var ds = await _repository.GetStateAsync(ct) ?? new DoseState();
        var automat = await _repository.GetAutomatSwitchAsync(ct);
        if (automat.Enabled || ds.AutomatActive)
        {
            await _repository.SetAutomatSwitchAsync(false, "Faza5: konfiguracja", ct);
            var ctx = new DoseLogContext { NowUtc = now, AutomatActive = ds.AutomatActive };
            await _repository.AddChangeLogAsync(new[]
            {
                DoseLog.Entry(ctx, DoseLog.Automat, 1, 0, "SAMOWYLACZENIE_KONFIGURACJA", DoseLog.StatusInternal,
                    $"Automat dawki SAM SIĘ WYŁĄCZYŁ: {text}. Nastaw nie przywracam - sprawdź przy kotle " +
                    $"(zapamiętane: {ds.SavedWork}/{ds.SavedPause} s, dmuchawa {ds.SavedBlower} %).")
            }, ct);
            ds.AutomatActive = false;
            ds.LastSelfDisableReason = Truncate(text, 500);
            ds.LastSelfDisableUtc = now;
            ds.LastReason = Truncate(text, 1000);
            await _repository.SaveStateAsync(ds, ct);
            await _notifier.NotifyAsync("HeatFlow: automat dawki SAM SIĘ WYŁĄCZYŁ", $"{text}. Nastaw nie przywróciłem - sprawdź przy kotle.", ct);
        }

        return PhaseResult.ErrorResult(PhaseNumber, text, (long)(DateTime.UtcNow - startTime).TotalMilliseconds);
    }

    /// <summary>Temperatura pokoju: stan sensora albo current_temperature głowicy (climate.*), jak w Fazie 1.</summary>
    private async Task<double?> ReadRoomTemperatureAsync(string entityId, CancellationToken ct)
    {
        var state = await _haClient.GetStateAsync(entityId, ct);
        var v = ParseDouble(state);
        if (v.HasValue || state == null || !entityId.StartsWith("climate.", StringComparison.Ordinal)) return v;
        return state.Attributes.TryGetValue("current_temperature", out var raw)
               && raw is JsonElement { ValueKind: JsonValueKind.Number } el
            ? el.GetDouble()
            : null;
    }

    /// <summary>
    /// Faktyczne temperatury pokoi z wagą (HeatLossW) do celu domu - ten sam odczyt co w Fazie 1
    /// (sensor albo current_temperature głowicy), wygładzone średnią wykładniczą DoseActualSmoothingHours.
    /// Co przebieg, żeby średnia miała gęste próbki.
    /// </summary>
    private async Task UpdateRoomActualsAsync(DoseState ds, IReadOnlyList<RoomConfiguration> rooms, HeatingParameters p,
        DateTime now, CancellationToken ct)
    {
        var readings = new Dictionary<string, double>();
        foreach (var room in rooms.Where(r => r.HeatLossW > 0 && !string.IsNullOrWhiteSpace(r.SensorTemperatureEntityId)))
        {
            var v = await ReadRoomTemperatureAsync(room.SensorTemperatureEntityId, ct);
            if (v is > -30 and < 60) readings[room.Name] = v.Value;
        }
        var smoothed = DoseCalculator.SmoothRoomActuals(DoseCalculator.ParseRoomActuals(ds.RoomActualSmoothed), readings, now,
            p.DoseActualSmoothingHours);
        ds.RoomActualSmoothed = Truncate(DoseCalculator.FormatRoomActuals(smoothed), 1000);
    }

    /// <summary>Cel domu: z pokoi (ważony HeatLossW, z faktycznymi) albo stała IndoorTargetForFeed - też awaryjnie.</summary>
    private static (HouseTargetResult House, double Target, string Source, List<RoomTargetInput> Inputs) HouseTargetFor(
        IReadOnlyList<RoomConfiguration> rooms, HeatingState state, HeatingParameters p, IReadOnlyDictionary<string, double>? actuals)
    {
        var empty = new HouseTargetResult(null, null, new List<RoomTargetPart>(), null, false);
        if (!p.DoseTargetFromRooms)
            return (empty, p.IndoorTargetForFeed, "stała", new());
        var inputs = DoseCalculator.RoomTargets(rooms, state.CurrentTime, state.IsWeekend);
        var house = DoseCalculator.CombineHouseTarget(inputs, actuals, p);
        return house.Target.HasValue
            ? (house, house.Target.Value, "pokoje", inputs)
            : (house, p.IndoorTargetForFeed, "stała - brak wag/celów pokoi", inputs);
    }

    private static Dictionary<string, double> ParseTargets(string? text)
        => (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('='))
            .Where(x => x.Length == 2 && double.TryParse(x[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            .ToDictionary(x => x[0], x => double.Parse(x[1], CultureInfo.InvariantCulture));

    private static string FormatTargets(IEnumerable<RoomTargetPart> rooms)
        => string.Join(";", rooms.OrderBy(r => r.Name).Select(r => $"{r.Name}={r.Target.ToString("0.##", CultureInfo.InvariantCulture)}"));

    /// <summary>
    /// Zmiana celu domu o ≥ 0,3 K (albo zmiana źródła) - osobny wpis CEL_DOMU: które pokoje zmieniły cel,
    /// które weszły po faktycznej, a które po celu, cel z samych celów i czy zadziałało przycięcie.
    /// Pierwsze liczenie zapisuje punkt odniesienia z jednym wpisem (z wagami).
    /// </summary>
    private static void LogHouseTargetChange(DoseState ds, double target, string source, HouseTargetResult house, RunLog run)
    {
        var sourceChanged = ds.HouseTargetSource != null && ds.HouseTargetSource != source;
        if (ds.HouseTargetLogged.HasValue && Math.Abs(target - ds.HouseTargetLogged.Value) < 0.3 && !sourceChanged)
            return;

        var rooms = house.Rooms;
        var previous = ParseTargets(ds.RoomTargetsSnapshot);
        var changes = rooms
            .Where(r => !previous.TryGetValue(r.Name, out var old) || Math.Abs(old - r.Target) > 0.01)
            .Select(r => previous.TryGetValue(r.Name, out var old)
                ? $"{r.Name} {DoseLog.Number(old)} → {DoseLog.Number(r.Target)}"
                : $"{r.Name} {DoseLog.Number(r.Target)}")
            .ToList();
        var weightSum = rooms.Sum(r => r.Weight);
        string why;
        if (source != "pokoje")
            why = $"źródło: {source} (IndoorTargetForFeed)";
        else if (ds.HouseTargetLogged == null)
            why = $"cel z pokoi ważony stratami ({rooms.Count} pokoi, Σ {weightSum:0} W): " + (run.Ctx.Parameters.DoseTargetUseActual
                ? DescribeRooms(house, run.Ctx.Parameters)
                : string.Join(", ", rooms.OrderByDescending(r => r.Weight).Select(r => $"{r.Name} {DoseLog.Number(r.Target)} °C × {r.Weight:0} W")));
        else
            why = (changes.Count > 0 ? $"zmiany celów pokoi: {string.Join(", ", changes)}" : "zmiana faktycznych temperatur albo wag pokoi")
                  + "; " + DescribeRooms(house, run.Ctx.Parameters);
        var tail = ds.HouseTargetLogged.HasValue && target > ds.HouseTargetLogged.Value
            ? changes.Count > 0
                ? $"; wzrost od celów - dawka rośnie stopniowo (≤ {DoseLog.Number(run.Ctx.Parameters.DoseTargetRiseKPerHour, "0.00")} K/h, z dodatkiem na dogrzanie domu)"
                : "; wzrost od faktycznych - od razu, bez dodatku na dogrzanie"
            : ds.HouseTargetLogged.HasValue ? "; niższy cel działa od razu" : "";

        run.Entries.Add(DoseLog.Entry(run.Ctx, DoseLog.HouseTargetParameter, ds.HouseTargetLogged ?? target, target, "CEL_DOMU",
            DoseLog.StatusInternal,
            $"{DoseLog.Head(DoseLog.HouseTargetParameter, ds.HouseTargetLogged ?? target, target)}: {why}{tail}."));
        ds.HouseTargetLogged = Math.Round(target, 2);
        ds.RoomTargetsSnapshot = FormatTargets(rooms);
    }

    /// <summary>„po faktycznej: sien 25,3 (cel 22); po celu: salon 26, …; z samych celów 21,95 °C”.</summary>
    private static string DescribeRooms(HouseTargetResult house, HeatingParameters p)
    {
        if (!p.DoseTargetUseActual) return $"faktyczne wyłączone (DoseTargetUseActual) - same cele";
        var byActual = house.Rooms.Where(r => r.ByActual).OrderByDescending(r => (r.Used - r.Target) * r.Weight)
            .Select(r => $"{r.Name} {DoseLog.Number(r.Used)} (cel {DoseLog.Number(r.Target)}" +
                         (r.Actual > house.RoomCap + 0.05 ? $", fakt. {DoseLog.Number(r.Actual)} przycięta do {DoseLog.Number(house.RoomCap)}" : "") + ")")
            .ToList();
        var byTarget = house.Rooms.Where(r => !r.ByActual).OrderByDescending(r => r.Weight)
            .Select(r => $"{r.Name} {DoseLog.Number(r.Target)}" + (r.Actual.HasValue ? "" : " (brak odczytu)"))
            .ToList();
        var text = $"po faktycznej: {(byActual.Count > 0 ? string.Join(", ", byActual) : "żaden")}; " +
                   $"po celu: {(byTarget.Count > 0 ? string.Join(", ", byTarget) : "żaden")}; " +
                   $"z samych celów {DoseLog.Number(house.BaseTarget, "0.00")} °C";
        if (house.HouseCapped)
            text += $", przycięty do celu z celów + {DoseLog.Number(p.DoseActualMaxAboveTargetsK)} K";
        return text;
    }

    private static bool IsValid(EntityState? state)
        => state != null && !string.IsNullOrWhiteSpace(state.State) && state.State != "unknown" && state.State != "unavailable";

    private static double? ParseDouble(EntityState? state)
    {
        if (!IsValid(state)) return null;
        return double.TryParse(state!.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>last_updated z HA w UTC; brak (default) = „teraz”, bo encja właśnie odpowiedziała.</summary>
    private static DateTime UpdatedUtc(EntityState state, DateTime now)
    {
        var t = state.LastUpdated == default ? state.LastChanged : state.LastUpdated;
        if (t == default) return now;
        return t.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : t.ToUniversalTime();
    }

    private static DateTime? ParseLastTriggered(EntityState? automation)
    {
        if (automation == null || !automation.Attributes.TryGetValue("last_triggered", out var raw) || raw == null)
            return null;
        var text = raw is JsonElement { ValueKind: JsonValueKind.String } el ? el.GetString() : raw as string;
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            ? dto.UtcDateTime
            : null;
    }

    private static double? NextForecastHour(ForecastData? forecast, DateTime now)
        => forecast?.ForecastHours
            .Where(f => f.Temperature.HasValue && f.DateTime.HasValue && f.DateTime.Value > now.AddMinutes(-30))
            .OrderBy(f => f.DateTime)
            .Select(f => f.Temperature)
            .FirstOrDefault();

    private static void FillPlan(DoseLogContext ctx, DoseState ds)
    {
        ctx.HouseTarget = ds.HouseTargetUsed;
        ctx.HouseTargetRaw = ds.HouseTarget;
        ctx.HouseTargetSource = ds.HouseTargetSource;
        ctx.StorageBonusSec = ds.StorageBonusSec;
        ctx.OutdoorSmoothed = ds.OutdoorSmoothed;
        ctx.NeedSecPerDay = ds.NeedSecPerDay;
        ctx.TrimSec = ds.TrimSec;
        ctx.Ceiling = ds.Ceiling;
        ctx.ShortfallPct = ds.ShortfallPct;
    }

    private static void ResetShadow(DoseState ds)
    {
        ds.ShadowWork = null;
        ds.ShadowPause = null;
        ds.ShadowBlower = null;
        ds.ShadowLastChangeUtc = null;
    }

    private static ControllerFeedSettingsSnapshot? Snapshot(ControllerFeedSettings? s)
        => s == null ? null : new ControllerFeedSettingsSnapshot(s.FeederWork, s.FeederPause, s.Blower);

    private static void SetController(DoseState ds, ControllerFeedSettings settings)
    {
        ds.ControllerWork = settings.FeederWork;
        ds.ControllerPause = settings.FeederPause;
        ds.ControllerBlower = settings.Blower;
    }

    private static string DescribePlan(DoseState ds)
    {
        if (ds.OutdoorSmoothed == null) return "brak planu";
        return string.Create(DoseLog.Pl,
            $"Tz {ds.OutdoorSmoothed:F1} °C, cel domu {ds.HouseTargetUsed:F1} °C ({ds.HouseTargetSource}), potrzeba {ds.NeedSecPerDay:F0} s/dobę → {ds.PlannedWork}/{ds.ModelPause:F0} s{(ds.TrimSec != 0 ? $" (trym {ds.TrimSec:+0;-0} s)" : "")}{(ds.Ceiling ? $", SUFIT - brakuje {ds.ShortfallPct:F0} %" : "")}");
    }

    private static string BuildDetails(DoseState ds, ControllerFeedSettings? controller, List<string> notes)
    {
        var ctrl = controller == null ? "sterownik: brak odczytu" : $"sterownik {controller.FeederWork}/{controller.FeederPause} s, {controller.Blower} %";
        var automat = ds.AutomatActive ? "automat: wł." : "automat: wył.";
        return string.Join(" | ", new[] { DescribePlan(ds), ctrl, automat }.Concat(notes));
    }

    private static string Ago(DateTime? t, DateTime now)
        => t == null ? "nigdy" : $"{(now - t.Value).TotalMinutes:F0} min temu";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static HeatingParameters Clone(HeatingParameters p)
        => JsonSerializer.Deserialize<HeatingParameters>(JsonSerializer.Serialize(p))!;
}
