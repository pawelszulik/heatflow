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
                if (issue != null)
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
                else if (decision is { Change: true } && controller != null && blower.HasValue)
                {
                    await WarnBoilerColdAsync(ds, samples, now, parameters, run, cancellationToken);
                    await ApplyAsync(ds, controller, decision, blower.Value, blowerWanted!.Value, blowerNote, blowerCode,
                        parameters, now, run, cancellationToken);
                }
                else if (decision != null)
                {
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
        // Cel domu: z celów pokoi ważonych stratami (albo stała), wzrost ograniczony, spadek od razu.
        var (rawTarget, source, roomTargets) = HouseTargetFor(rooms, state, p);
        var hours = ds.LastCalcUtc == null ? 0 : (now - ds.LastCalcUtc.Value).TotalHours;
        var (usedTarget, bonus) = DoseCalculator.LimitTargetRise(rawTarget, ds.HouseTargetUsed, hours, p);
        LogHouseTargetChange(ds, rawTarget, source, roomTargets, run);
        ds.HouseTarget = Math.Round(rawTarget, 2);
        ds.HouseTargetUsed = Math.Round(usedTarget, 3);
        ds.HouseTargetSource = source;
        ds.StorageBonusSec = Math.Round(bonus);

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
            await DeactivateAsync(ds, result.Last ?? controller,
                new SafetyIssue("ZAPIS_NIEPOTWIERDZONY", $"zapis nie potwierdził się w mapperze ({result.Report})"),
                selfDisable: true, noBlowerIncrease: false, p, now, run, ct);
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

    /// <summary>
    /// Wyłączenie automatu - ręczne (przełącznik w HA) albo samowyłączenie. Przywraca nastawy zastane
    /// przy włączeniu (DoseRestoreOnDisable), przy spalinach &gt; 200 °C bez podnoszenia dmuchawy.
    /// </summary>
    private async Task DeactivateAsync(DoseState ds, ControllerFeedSettings? controller, SafetyIssue reason,
        bool selfDisable, bool noBlowerIncrease, HeatingParameters p, DateTime now, RunLog run, CancellationToken ct)
    {
        var code = selfDisable ? $"SAMOWYLACZENIE_{reason.Code}" : "WYLACZENIE_RECZNE";
        run.Entries.Add(DoseLog.Entry(run.Ctx, DoseLog.Automat, 1, 0, code, DoseLog.StatusInternal,
            selfDisable
                ? $"Automat dawki SAM SIĘ WYŁĄCZYŁ: {reason.Text}."
                : "Automat dawki wyłączony w HA."));

        var restore = await RestoreAsync(ds, controller, noBlowerIncrease, p, run.Entities, ct);
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
        if (ds.TrimSec != 0)
        {
            run.Entries.Add(DoseLog.Change(run.Ctx, DoseLog.Trim, ds.TrimSec, 0, "KOREKTA_K", DoseLog.StatusInternal,
                extra: "trym zerowany o północy - stałą część różnicy przejmuje k"));
        }
        ds.TrimSec = 0;
        run.Notes.Add($"korekta k za {dayText}: {reason}; trym wyzerowany");
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

    /// <summary>Cel domu: z pokoi (ważony HeatLossW) albo stała IndoorTargetForFeed - też awaryjnie.</summary>
    private static (double Target, string Source, List<(string Name, double Target, double Weight)> Rooms) HouseTargetFor(
        IReadOnlyList<RoomConfiguration> rooms, HeatingState state, HeatingParameters p)
    {
        if (!p.DoseTargetFromRooms)
            return (p.IndoorTargetForFeed, "stała", new());
        var (target, list) = DoseCalculator.HouseTarget(rooms, state.CurrentTime, state.IsWeekend);
        return target.HasValue
            ? (target.Value, "pokoje", list)
            : (p.IndoorTargetForFeed, "stała - brak wag/celów pokoi", list);
    }

    /// <summary>
    /// Zmiana celu domu o ≥ 0,3 K (albo zmiana źródła) - osobny wpis CEL_DOMU z rozbiciem, które pokoje
    /// zmieniły cel. Pierwsze liczenie zapisuje punkt odniesienia z jednym wpisem.
    /// </summary>
    private static void LogHouseTargetChange(DoseState ds, double target, string source,
        List<(string Name, double Target, double Weight)> rooms, RunLog run)
    {
        string Snap() => string.Join(";", rooms.OrderBy(r => r.Name)
            .Select(r => $"{r.Name}={r.Target.ToString("0.##", CultureInfo.InvariantCulture)}"));
        var sourceChanged = ds.HouseTargetSource != null && ds.HouseTargetSource != source;
        if (ds.HouseTargetLogged.HasValue && Math.Abs(target - ds.HouseTargetLogged.Value) < 0.3 && !sourceChanged)
            return;

        var previous = (ds.RoomTargetsSnapshot ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('='))
            .Where(x => x.Length == 2)
            .ToDictionary(x => x[0], x => double.Parse(x[1], CultureInfo.InvariantCulture));
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
            why = $"cel z pokoi ważony stratami ({rooms.Count} pokoi, Σ {weightSum:0} W): " +
                  string.Join(", ", rooms.OrderByDescending(r => r.Weight).Select(r => $"{r.Name} {DoseLog.Number(r.Target)} °C × {r.Weight:0} W"));
        else
            why = changes.Count > 0 ? $"zmiany celów pokoi: {string.Join(", ", changes)}" : "zmiana wag pokoi";
        var tail = ds.HouseTargetLogged.HasValue && target > ds.HouseTargetLogged.Value
            ? $"; dawka rośnie stopniowo (≤ {DoseLog.Number(run.Ctx.Parameters.DoseTargetRiseKPerHour, "0.00")} K/h, z dodatkiem na dogrzanie domu)"
            : ds.HouseTargetLogged.HasValue ? "; niższy cel działa od razu" : "";

        run.Entries.Add(DoseLog.Entry(run.Ctx, DoseLog.HouseTargetParameter, ds.HouseTargetLogged ?? target, target, "CEL_DOMU",
            DoseLog.StatusInternal,
            $"{DoseLog.Head(DoseLog.HouseTargetParameter, ds.HouseTargetLogged ?? target, target)}: {why}{tail}."));
        ds.HouseTargetLogged = Math.Round(target, 2);
        ds.RoomTargetsSnapshot = Snap();
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
