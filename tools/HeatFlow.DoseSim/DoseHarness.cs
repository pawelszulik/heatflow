using HeatFlow.Core.Dose;
using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.EsterownikMapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeatFlow.DoseSim;

/// <summary>Odczyty, które Faza 5 bierze z HA w jednym przebiegu. Null = encja niedostępna.</summary>
public record DoseInputsSnapshot(
    double? Outdoor,
    double? Indoor,
    double? Boiler,
    double? Setpoint,
    double? Flue,
    double? BlowerActual,
    int? FeederCounterSec,
    bool? Summer,
    bool? ControllerAuto,
    bool BoilerDataFresh = true);

/// <summary>
/// Prawdziwy <see cref="Phase5DoseService"/> (i <see cref="DoseControllerWriter"/>) podpięty do atrap:
/// HA, sterownika z mapperem, bazy i konfiguracji w pamięci. Zapis do sterownika = zmiana pola
/// w <see cref="SimController"/>; nic nie wychodzi poza proces.
/// </summary>
public sealed class DoseHarness
{
    public SimClock Clock { get; } = new();
    public SimController Controller { get; }
    public SimHomeAssistant Ha { get; }
    public InMemoryDoseRepository Repo { get; } = new();
    public SimConfigurationService Config { get; }
    public CapturingNotifier Notifier { get; } = new();
    public CollectingErrorLogger Errors { get; } = new();
    public Phase5DoseService Phase { get; }

    public DoseHarness(ControllerFeedSettings initial, HeatingParameters? parameters = null)
    {
        Controller = new SimController(initial) { Now = () => Clock.UtcNow };
        Ha = new SimHomeAssistant(Controller);
        Config = new SimConfigurationService(parameters ?? new HeatingParameters()) { Now = () => Clock.UtcNow };
        Notifier.Now = () => Clock.UtcNow;
        var writer = new DoseControllerWriter(Ha, Controller, new DoseWriteOptions
        {
            ConfirmFirstCheck = TimeSpan.Zero,
            ConfirmPollInterval = TimeSpan.Zero,
            ConfirmTimeout = TimeSpan.Zero,
            RetryDelay = TimeSpan.Zero
        }, NullLogger<DoseControllerWriter>.Instance);
        Phase = new Phase5DoseService(Ha, Repo, Controller, writer, Notifier, Config, new NullAuditService(),
            Errors, NullLogger<Phase5DoseService>.Instance, Clock);
    }

    public HeatingParameters Parameters => Config.Parameters;

    /// <summary>Wystawia odczyty w HA z last_updated = <paramref name="updatedUtc"/> (dla brakujących - niedostępne).</summary>
    public void Publish(DoseInputsSnapshot s, DateTime updatedUtc)
    {
        Ha.Set(Phase5DoseService.OutdoorEntityId, s.Outdoor, updatedUtc);
        foreach (var id in Phase5DoseService.IndoorEntityIds) Ha.Set(id, s.Indoor, updatedUtc);
        Ha.Set(Phase5DoseService.BoilerTempEntityId, s.Boiler, updatedUtc);
        Ha.Set(Phase5DoseService.BoilerSetpointEntityId, s.Setpoint, updatedUtc);
        Ha.Set(Phase5DoseService.FlueTempEntityId, s.Flue, updatedUtc);
        Ha.Set(Phase5DoseService.BlowerActualEntityId, s.BlowerActual, updatedUtc);
        Ha.Set(Phase5DoseService.FeederCounterEntityId,
            s.FeederCounterSec is { } c ? $"{c / 60}m,{c % 60:000}s" : null, updatedUtc);
        Ha.Set(Phase5DoseService.SummerSwitchEntityId, s.Summer is { } su ? (su ? "on" : "off") : null, updatedUtc);
        Ha.Set(Phase5DoseService.ModeSwitchEntityId, s.ControllerAuto is { } a ? (a ? "on" : "off") : null, updatedUtc);
        if (s.BoilerDataFresh)
            Ha.Set(Phase5DoseService.ControllerClockEntityId, updatedUtc.ToString("O"), updatedUtc);
        else
            Ha.Set(Phase5DoseService.ControllerClockEntityId, (string?)null, updatedUtc);
    }

    public Task<Core.Phases.PhaseResult> StepAsync(DateTime utc, ForecastData? forecast)
    {
        Clock.UtcNow = utc;
        return Phase.ExecuteAsync(new HeatingState { CurrentTime = utc, Forecast = forecast }, Parameters);
    }

    /// <summary>„Idealna prognoza”: faktyczna Tz z kolejnych godzin.</summary>
    public static ForecastData IdealForecast(Func<DateTime, double?> tzAt, DateTime nowUtc, int hours = 12)
    {
        var start = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);
        return new ForecastData
        {
            CurrentTemp = tzAt(nowUtc) ?? 0,
            ForecastHours = Enumerable.Range(0, hours)
                .Select(i => new ForecastHour { DateTime = start.AddHours(i), Temperature = tzAt(start.AddHours(i).AddMinutes(30)) })
                .ToList()
        };
    }
}
