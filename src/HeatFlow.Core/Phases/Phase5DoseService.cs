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
/// </summary>
public class Phase5DoseService : IPhaseService
{
    public const string OutdoorEntityId = "sensor.temperatura_ogrod_temperature";
    public const string BoilerTempEntityId = "sensor.kociol_temperatura_kotla";
    public const string BoilerSetpointEntityId = "number.kociol_temperatura_zadana_kotla";
    public const string FlueTempEntityId = "sensor.kociol_temperatura_spalin";
    public const string BlowerActualEntityId = "sensor.kociol_aktualna_moc_dmuchawy";
    public const string FeederCounterEntityId = "sensor.kociol_czas_pracy_podajnika_2";
    public const string SummerSwitchEntityId = "switch.kociol_tryb_zima_lato";
    /// <summary>on = Auto, off = tryb ręczny sterownika (rozpalanie / awaria).</summary>
    public const string ModeSwitchEntityId = "switch.kociol_tryb_pracy";
    /// <summary>Zegar sterownika - zmienia się przy każdym odczycie integracji (co 30 s), stąd świeżość danych kotła.</summary>
    public const string ControllerClockEntityId = "sensor.kociol_aktualna_data";
    /// <summary>Automatyzacja HA #24: spaliny &gt; 250 °C → dmuchawa na minimum.</summary>
    public const string FlueSafetyAutomationEntityId = "automation.kociol_bezpiecznik_spalin_dmuchawa_na_minimum";

    /// <summary>Te same 7 czujników BMx280, z których liczony był model (§ 2, a2_doby.py).</summary>
    public static readonly string[] IndoorEntityIds =
    {
        "sensor.kuchnia_czujnik_bm280_kuchnia_bmx280_temperature",
        "sensor.salon_czujnik_bm280_salon_bmx280_temperature",
        "sensor.spizarnia_czujnik_bm280_spizarnia_bmx280_temperature",
        "sensor.suszarnia_czujnik_bm280_suszarnia_bmx280_temperature",
        "sensor.edyta_czujnik_bm280_edyta_bmx280_temperature",
        "sensor.hol_czujnik_bm280_hol_bmx280_temperature",
        "sensor.przejsciowy_czujnik_bm280_przejsciowy_bmx280_temperature"
    };

    private const int MinIndoorSensors = 5;
    private const double FlueSelfDisableTemp = 200.0;
    private static readonly TimeSpan SampleRetention = TimeSpan.FromDays(4);
    private static readonly TimeSpan CalcInterval = TimeSpan.FromMinutes(55);
    private static readonly TimeSpan UnderpowerWindow = TimeSpan.FromHours(2);
    private static readonly TimeSpan StandbyWindow = TimeSpan.FromHours(6);
    private static readonly TimeSpan BoilerColdWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan FlueSafetyHold = TimeSpan.FromHours(6);
    private const int StandbyEntriesForTrim = 2;

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
            // 1. Migawka (co przebieg) - pamięć do Tz, trymu, bezpieczników i bilansu doby.
            var inputs = await ReadInputsAsync(now, cancellationToken);
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

            // 2. Korekta k raz na dobę (za wczoraj), zanim policzymy dawkę.
            await CorrectKIfDueAsync(ds, parameters, now, notes, cancellationToken);

            var windowStart = now - TimeSpan.FromHours(Math.Max(StandbyWindow.TotalHours, parameters.FeedSmoothingPastHours));
            var samples = await _repository.GetSamplesSinceAsync(windowStart, cancellationToken);

            // 3. Dawka z pogody - co godzinę.
            if (ds.LastCalcUtc == null || ds.ModelPause == null || now - ds.LastCalcUtc.Value >= CalcInterval)
            {
                Calculate(ds, state, parameters, samples, now, notes);
            }

            // 4. Co sterownik dostałby teraz (sugestia przy wyłączonym automacie).
            DoseDecision? decision = null;
            int? blower = null;
            string? blowerNote = null;
            var flueSafetyRecent = inputs.FlueSafetyLastTriggeredUtc.HasValue
                                   && now - inputs.FlueSafetyLastTriggeredUtc.Value < FlueSafetyHold;
            if (ds.ModelPause.HasValue && ds.PlannedWork.HasValue && controller != null)
            {
                var plan = new DosePlan(ds.PlannedWork.Value, ds.ModelPause.Value, ds.Ceiling, ds.ShortfallPct);
                decision = DoseCalculator.Decide(controller.FeederWork, controller.FeederPause, plan,
                    ds.AutomatActive ? ds.LastChangeUtc : null, now, parameters);
                var blowerTarget = DoseCalculator.BlowerFor(decision.Work, decision.Pause, parameters);
                (var limited, blowerNote) = DoseCalculator.LimitBlower(blowerTarget, controller.Blower,
                    inputs.Sample.FlueTemp, flueSafetyRecent, parameters);
                blower = limited;
                ds.PlannedPause = decision.Pause;
                ds.PlannedBlower = blower;
            }

            // 5. Wyłącznik: włączenie / wyłączenie przez Pawła.
            if (automatSwitch.Enabled && !ds.AutomatActive)
            {
                var refuse = controller == null
                    ? "mapper nie odpowiada - nie mogę zapamiętać zastanych nastaw"
                    : SafetyReason(inputs, ds, samples, now, parameters) ?? ParametersReason(parameters);
                if (refuse != null)
                {
                    await _repository.SetAutomatSwitchAsync(false, $"Faza5: {Truncate(refuse, 180)}", cancellationToken);
                    ds.LastSelfDisableReason = $"nie włączono: {refuse}";
                    ds.LastSelfDisableUtc = now;
                    notes.Add($"automat NIE włączony: {refuse}");
                    await _notifier.NotifyAsync("HeatFlow: automatu dawki nie włączam",
                        $"Powód: {refuse}. Przełącznik wrócił na off, nastawy kotła bez zmian.", cancellationToken);
                }
                else
                {
                    ds.AutomatActive = true;
                    ds.SavedWork = controller!.FeederWork;
                    ds.SavedPause = controller.FeederPause;
                    ds.SavedBlower = controller.Blower;
                    ds.LastChangeUtc = null; // pierwsza zmiana bez czekania na odstęp
                    notes.Add($"automat włączony, zapamiętane {controller.FeederWork}/{controller.FeederPause} s, dmuchawa {controller.Blower} %");
                    await _notifier.NotifyAsync("HeatFlow: automat dawki włączony",
                        $"Zapamiętane nastawy: praca {controller.FeederWork} s, postój {controller.FeederPause} s, dmuchawa {controller.Blower} %. " +
                        $"Przy wyłączeniu {(parameters.DoseRestoreOnDisable ? "wrócą" : "NIE wrócą (parametr)")}. Teraz model mówi: {DescribePlan(ds)}.",
                        cancellationToken);
                }
            }
            else if (!automatSwitch.Enabled && ds.AutomatActive)
            {
                await DeactivateAsync(ds, controller, "wyłączony w HA", selfDisable: false, noBlowerIncrease: false,
                    parameters, now, notes, cancellationToken);
            }

            // 6. Automat aktywny: bezpieczniki, potem ewentualna zmiana nastaw.
            if (ds.AutomatActive)
            {
                var reason = SafetyReason(inputs, ds, samples, now, parameters) ?? ParametersReason(parameters);
                if (reason != null)
                {
                    var flueHigh = inputs.Sample.FlueTemp > FlueSelfDisableTemp;
                    if (flueHigh && controller != null && blower.HasValue && blower.Value < controller.Blower)
                    {
                        // „> 200 °C → −2 pkt” robimy przed samowyłączeniem; przywracanie nie podniesie dmuchawy.
                        var cut = await _writer.WriteAsync(DoseRegister.Blower, blower.Value, cancellationToken);
                        notes.Add($"spaliny > 200 °C: {cut.Report}");
                        if (cut.Last != null) { SetController(ds, cut.Last); controller = cut.Last; }
                    }

                    await DeactivateAsync(ds, controller, reason, selfDisable: true, noBlowerIncrease: flueHigh,
                        parameters, now, notes, cancellationToken);
                }
                else if (decision is { Change: true } && controller != null && blower.HasValue)
                {
                    await ApplyAsync(ds, controller, decision, blower.Value, blowerNote, parameters, now, notes, cancellationToken);
                }
                else if (decision != null)
                {
                    notes.Add(decision.Reason);
                }
                else
                {
                    notes.Add(controller == null ? "mapper nie odpowiada - bez zmian" : "brak planu - bez zmian");
                }
            }
            else if (decision != null)
            {
                notes.Add($"automat wyłączony - sugestia {decision.Work}/{decision.Pause} s, dmuchawa {blower} % ({decision.Reason})");
            }

            ds.LastReason = Truncate(string.Join(" | ", notes.Prepend(DescribePlan(ds))), 1000);
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
    private void Calculate(DoseState ds, HeatingState state, HeatingParameters p,
        IReadOnlyList<DoseSample> samples, DateTime now, List<string> notes)
    {
        var measured = samples
            .Where(s => s.OutdoorTemp.HasValue)
            .Select(s => (s.TimestampUtc, s.OutdoorTemp!.Value));
        var tz = DoseCalculator.SmoothedOutdoorTemp(measured, state.Forecast, now,
            p.FeedSmoothingPastHours, p.FeedSmoothingForecastHours);
        if (tz == null)
        {
            notes.Add("brak pomiaru Tz z czujnika ogrodowego - dawki nie liczę");
            return;
        }

        var currentWork = ds.ControllerWork ?? ds.PlannedWork ?? p.FeederWorkMin;
        EvaluateTrim(ds, DoseCalculator.Plan(p, tz.Value, currentWork, 0), samples, now, p, notes);
        var plan = DoseCalculator.Plan(p, tz.Value, currentWork, ds.TrimSec);

        ds.OutdoorSmoothed = Math.Round(tz.Value, 2);
        ds.NeedSecPerDay = Math.Round(DoseCalculator.Need(p, tz.Value));
        ds.PlannedWork = plan.Work;
        ds.ModelPause = Math.Round(plan.ModelPause, 1);
        ds.Ceiling = plan.Ceiling;
        ds.ShortfallPct = Math.Round(plan.ShortfallPct, 1);
        ds.LastCalcUtc = now;
        if (plan.Ceiling)
        {
            notes.Add($"kocioł nie nadąża - sufit {p.FeederWorkMax}/{p.FeederPauseMin} s, brakuje {plan.ShortfallPct:F0} %");
        }
    }

    /// <summary>
    /// Szybki trym od kotła (§ 9): kocioł nie dochodzi do nastawy przy ciągłej dmuchawie przez 2 h
    /// → postój −20 s; ≥ 2 wejścia w podtrzymanie w 6 h → +20 s. Nie częściej niż co
    /// FeedMinIntervalHours. Na suficie nie zbiera (anti-windup). Zerowany o północy.
    /// </summary>
    private static void EvaluateTrim(DoseState ds, DosePlan planWithoutTrim, IReadOnlyList<DoseSample> samples,
        DateTime now, HeatingParameters p, List<string> notes)
    {
        if (ds.LastTrimUtc.HasValue && now - ds.LastTrimUtc.Value < TimeSpan.FromHours(p.FeedMinIntervalHours))
        {
            return;
        }

        var (_, _, pauseMin, pauseMax) = DoseCalculator.Bounds(p);
        var last2h = samples.Where(s => s.TimestampUtc > now - UnderpowerWindow).ToList();
        var last6h = samples.Where(s => s.TimestampUtc > now - StandbyWindow).ToList();
        var effective = planWithoutTrim.ModelPause + ds.TrimSec;

        if (DoseCalculator.BoilerUnderpowered(last2h, UnderpowerWindow))
        {
            if (!planWithoutTrim.Ceiling && effective - DoseCalculator.TrimStepSec >= pauseMin
                && ds.TrimSec - DoseCalculator.TrimStepSec >= -DoseCalculator.TrimLimitSec)
            {
                ds.TrimSec -= DoseCalculator.TrimStepSec;
                ds.LastTrimUtc = now;
                notes.Add($"trym: kocioł 2 h pod nastawą − 3 K przy ciągłej dmuchawie → postój −{DoseCalculator.TrimStepSec} s (trym {ds.TrimSec:+0;-0} s)");
            }
            else
            {
                notes.Add("trym: kocioł pod nastawą, ale postój już na minimum / trym na granicy - nie zbieram");
            }
            return;
        }

        var entries = DoseCalculator.StandbyEntries(last6h);
        if (entries >= StandbyEntriesForTrim)
        {
            if (effective + DoseCalculator.TrimStepSec <= pauseMax
                && ds.TrimSec + DoseCalculator.TrimStepSec <= DoseCalculator.TrimLimitSec)
            {
                ds.TrimSec += DoseCalculator.TrimStepSec;
                ds.LastTrimUtc = now;
                notes.Add($"trym: {entries} wejścia w podtrzymanie w 6 h → postój +{DoseCalculator.TrimStepSec} s (trym {ds.TrimSec:+0;-0} s)");
            }
            else
            {
                notes.Add($"trym: {entries} wejścia w podtrzymanie w 6 h, ale postój na maksimum / trym na granicy");
            }
        }
    }

    private async Task ApplyAsync(DoseState ds, ControllerFeedSettings controller, DoseDecision decision, int blower,
        string? blowerNote, HeatingParameters p, DateTime now, List<string> notes, CancellationToken ct)
    {
        var result = await _writer.WriteSettingsAsync(controller, decision.Work, decision.Pause, blower, ct);
        if (result.Last != null) SetController(ds, result.Last);

        if (!result.Confirmed)
        {
            notes.Add($"zapis niepotwierdzony: {result.Report}");
            await DeactivateAsync(ds, result.Last ?? controller, $"zapis nie potwierdził się w mapperze ({result.Report})",
                selfDisable: true, noBlowerIncrease: false, p, now, notes, ct);
            return;
        }

        ds.LastChangeUtc = now;
        var what = $"{controller.FeederWork}/{controller.FeederPause} s → {decision.Work}/{decision.Pause} s, " +
                   $"dmuchawa {controller.Blower} → {blower} %";
        notes.Add($"ZMIANA {what} ({decision.Reason}{(blowerNote != null ? "; " + blowerNote : "")})");
        await _notifier.NotifyAsync($"Kocioł: dawka {decision.Work}/{decision.Pause} s, dmuchawa {blower} %",
            $"{what}. {DescribePlan(ds)}. Powód: {decision.Reason}{(blowerNote != null ? "; " + blowerNote : "")}. Potwierdzone w mapperze.",
            ct);
    }

    /// <summary>
    /// Wyłączenie automatu - ręczne (przełącznik w HA) albo samowyłączenie. Przywraca nastawy zastane
    /// przy włączeniu (DoseRestoreOnDisable), przy spalinach &gt; 200 °C bez podnoszenia dmuchawy.
    /// </summary>
    private async Task DeactivateAsync(DoseState ds, ControllerFeedSettings? controller, string reason,
        bool selfDisable, bool noBlowerIncrease, HeatingParameters p, DateTime now, List<string> notes, CancellationToken ct)
    {
        var restore = await RestoreAsync(ds, controller, noBlowerIncrease, p, ct);

        if (selfDisable)
        {
            await _repository.SetAutomatSwitchAsync(false, $"Faza5: {Truncate(reason, 180)}", ct);
            ds.LastSelfDisableReason = Truncate(reason, 500);
            ds.LastSelfDisableUtc = now;
        }

        ds.AutomatActive = false;
        ds.SavedWork = null;
        ds.SavedPause = null;
        ds.SavedBlower = null;

        notes.Add($"automat {(selfDisable ? "SAM SIĘ WYŁĄCZYŁ" : "wyłączony")}: {reason}; {restore}");
        await _notifier.NotifyAsync(
            selfDisable ? "HeatFlow: automat dawki SAM SIĘ WYŁĄCZYŁ" : "HeatFlow: automat dawki wyłączony",
            $"Powód: {reason}. {restore}",
            ct);
    }

    private async Task<string> RestoreAsync(DoseState ds, ControllerFeedSettings? controller, bool noBlowerIncrease,
        HeatingParameters p, CancellationToken ct)
    {
        if (!p.DoseRestoreOnDisable)
            return "Nastaw nie przywracam (DoseRestoreOnDisable = wył.) - zostają ostatnie z automatu.";
        if (ds.SavedWork == null || ds.SavedPause == null || ds.SavedBlower == null)
            return "Brak zapamiętanych nastaw - nic nie przywracam.";

        var work = Math.Clamp(ds.SavedWork.Value, DoseLimits.WorkMin, DoseLimits.WorkMax);
        var pause = Math.Clamp(ds.SavedPause.Value, DoseLimits.PauseMin, DoseLimits.PauseMax);
        var blower = Math.Clamp(ds.SavedBlower.Value, DoseLimits.BlowerMin, DoseLimits.BlowerMax);
        var wanted = $"{work}/{pause} s, dmuchawa {blower} %";

        if (controller == null)
            return $"Mapper nie odpowiada - nastaw NIE przywróciłem, sprawdź przy kotle (miało być {wanted}).";

        if (noBlowerIncrease && blower > controller.Blower)
            blower = controller.Blower;

        var result = await _writer.WriteSettingsAsync(controller, work, pause, blower, ct);
        if (result.Last != null) SetController(ds, result.Last);
        return result.Confirmed
            ? $"Przywrócone: praca {work} s, postój {pause} s, dmuchawa {blower} % (potwierdzone w mapperze)."
            : $"PRZYWRÓCENIE NIE DOSZŁO ({result.Report}) - sprawdź przy kotle, miało być {wanted}.";
    }

    /// <summary>Powody samowyłączenia (§ 9 „Wyłącznik i bezpieczniki”). Null = można działać.</summary>
    private static string? SafetyReason(DoseInputs inputs, DoseState ds, IReadOnlyList<DoseSample> samples,
        DateTime now, HeatingParameters p)
    {
        if (inputs.Sample.FlueTemp > FlueSelfDisableTemp)
            return $"spaliny {inputs.Sample.FlueTemp:F0} °C > {FlueSelfDisableTemp:F0} °C";

        var lastHour = samples.Where(s => s.TimestampUtc > now - BoilerColdWindow).ToList();
        if (DoseCalculator.BoilerColdFor(lastHour, BoilerColdWindow))
            return "kocioł poniżej nastawy − 10 K od godziny";

        var boilerStale = TimeSpan.FromMinutes(p.DoseBoilerStaleMinutes);
        if (ds.LastBoilerDataUtc == null || now - ds.LastBoilerDataUtc.Value > boilerStale)
            return $"brak świeżych danych kotła w HA (ostatnie: {Ago(ds.LastBoilerDataUtc, now)})";
        if (ds.LastMapperOkUtc == null || now - ds.LastMapperOkUtc.Value > boilerStale)
            return $"mapper nie odpowiada (ostatnio: {Ago(ds.LastMapperOkUtc, now)})";
        if (ds.LastOutdoorDataUtc == null || now - ds.LastOutdoorDataUtc.Value > TimeSpan.FromMinutes(p.DoseOutdoorStaleMinutes))
            return $"brak świeżej temperatury zewnętrznej (ostatnia: {Ago(ds.LastOutdoorDataUtc, now)})";

        if (inputs.SummerMode == true)
            return "kocioł przełączony na tryb lato";
        if (inputs.ControllerAuto == false)
            return "sterownik w trybie ręcznym";

        return null;
    }

    private static string? ParametersReason(HeatingParameters p)
    {
        var errors = DoseLimits.Validate(p);
        return errors.Count == 0 ? null : $"parametry poza granicami: {string.Join(", ", errors)}";
    }

    /// <summary>Korekta k raz na dobę, za wczorajszą dobę lokalną (§ 9 „Korekta raz na dobę”).</summary>
    private async Task CorrectKIfDueAsync(DoseState ds, HeatingParameters p, DateTime nowUtc,
        List<string> notes, CancellationToken ct)
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
        var (newK, reason) = DoseCalculator.CorrectK(p.FeedCorrectionK, day, before.IndoorMean.HasValue ? before : null, p);

        if (newK.HasValue && Math.Abs(newK.Value - p.FeedCorrectionK) > 0.0005)
        {
            var current = await _configurationService.GetHeatingParametersAsync(ct);
            var updated = Clone(current);
            updated.FeedCorrectionK = newK.Value;
            await _configurationService.SaveHeatingParametersAsync(updated, ct);
            try { await _auditService.LogHeatingParametersChangesAsync(current, updated, "Faza5", ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Faza 5: nie zapisano audytu zmiany k"); }
            // Ten przebieg liczy już z nowym k.
            p.FeedCorrectionK = newK.Value;
        }

        ds.LastKCorrectionDate = yesterday;
        ds.TrimSec = 0;
        notes.Add($"korekta k za {yesterday:yyyy-MM-dd}: {reason}; trym wyzerowany");
    }

    private async Task<DoseInputs> ReadInputsAsync(DateTime now, CancellationToken ct)
    {
        var outdoorState = await _haClient.GetStateAsync(OutdoorEntityId, ct);
        var outdoor = ParseDouble(outdoorState);

        var indoorValues = new List<double>();
        foreach (var id in IndoorEntityIds)
        {
            var v = ParseDouble(await _haClient.GetStateAsync(id, ct));
            if (v.HasValue) indoorValues.Add(v.Value);
        }

        var clock = await _haClient.GetStateAsync(ControllerClockEntityId, ct);
        var counter = await _haClient.GetStateAsync(FeederCounterEntityId, ct);
        var flueSafety = await _haClient.GetStateAsync(FlueSafetyAutomationEntityId, ct);

        var sample = new DoseSample
        {
            TimestampUtc = now,
            OutdoorTemp = outdoor,
            IndoorTemp = indoorValues.Count >= MinIndoorSensors ? Math.Round(indoorValues.Average(), 2) : null,
            BoilerTemp = ParseDouble(await _haClient.GetStateAsync(BoilerTempEntityId, ct)),
            BoilerSetpoint = ParseDouble(await _haClient.GetStateAsync(BoilerSetpointEntityId, ct)),
            FlueTemp = ParseDouble(await _haClient.GetStateAsync(FlueTempEntityId, ct)),
            BlowerActual = ParseDouble(await _haClient.GetStateAsync(BlowerActualEntityId, ct)),
            FeederCounterSec = DoseCalculator.ParseFeederCounter(counter?.State),
            SummerMode = HomeAssistantClient.ParseBoolState((await _haClient.GetStateAsync(SummerSwitchEntityId, ct))?.State)
        };

        var controllerAuto = HomeAssistantClient.ParseBoolState((await _haClient.GetStateAsync(ModeSwitchEntityId, ct))?.State);

        return new DoseInputs(
            sample,
            IsValid(clock) ? UpdatedUtc(clock!, now) : null,
            outdoor.HasValue ? UpdatedUtc(outdoorState!, now) : null,
            sample.SummerMode,
            controllerAuto,
            ParseLastTriggered(flueSafety));
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

    private static void SetController(DoseState ds, ControllerFeedSettings settings)
    {
        ds.ControllerWork = settings.FeederWork;
        ds.ControllerPause = settings.FeederPause;
        ds.ControllerBlower = settings.Blower;
    }

    private static string DescribePlan(DoseState ds)
    {
        if (ds.OutdoorSmoothed == null) return "brak planu";
        return string.Create(CultureInfo.GetCultureInfo("pl-PL"),
            $"Tz {ds.OutdoorSmoothed:F1} °C, potrzeba {ds.NeedSecPerDay:F0} s/dobę → {ds.PlannedWork}/{ds.ModelPause:F0} s{(ds.TrimSec != 0 ? $" (trym {ds.TrimSec:+0;-0} s)" : "")}{(ds.Ceiling ? $", SUFIT - brakuje {ds.ShortfallPct:F0} %" : "")}");
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
