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
            RetryDelay = TimeSpan.Zero,
            GraceRecheckDelay = TimeSpan.Zero
        }, NullLogger<DoseControllerWriter>.Instance);
        Phase = new Phase5DoseService(Ha, Repo, Controller, writer, Notifier, Config, new NullAuditService(),
            Errors, NullLogger<Phase5DoseService>.Instance, Clock);
    }

    public HeatingParameters Parameters => Config.Parameters;
    public DoseEntities Entities => DoseEntities.From(Config.System);

    /// <summary>Wystawia odczyty w HA z last_updated = <paramref name="updatedUtc"/> (dla brakujących - niedostępne).</summary>
    public void Publish(DoseInputsSnapshot s, DateTime updatedUtc)
    {
        var e = Entities;
        Ha.Set(e.OutdoorTemp, s.Outdoor, updatedUtc);
        foreach (var room in Config.Rooms.Where(r => r.UseForDoseIndoorMean)) Ha.Set(room.SensorTemperatureEntityId, s.Indoor, updatedUtc);
        Ha.Set(e.BoilerTemp, s.Boiler, updatedUtc);
        Ha.Set(e.BoilerSetpoint, s.Setpoint, updatedUtc);
        Ha.Set(e.FlueTemp, s.Flue, updatedUtc);
        Ha.Set(e.BlowerActual, s.BlowerActual, updatedUtc);
        Ha.Set(e.FeederCounter, s.FeederCounterSec is { } c ? $"{c / 60}m,{c % 60:000}s" : null, updatedUtc);
        Ha.Set(e.SummerSwitch, s.Summer is { } su ? (su ? "on" : "off") : null, updatedUtc);
        Ha.Set(e.ControllerMode, s.ControllerAuto is { } a ? (a ? "on" : "off") : null, updatedUtc);
        if (s.BoilerDataFresh)
            Ha.Set(e.ControllerClock, updatedUtc.ToString("O"), updatedUtc);
        else
            Ha.Set(e.ControllerClock, (string?)null, updatedUtc);
    }

    /// <summary>Czujniki wszystkich pokoi: temperatura domu + przesunięcie pokoju (brak przesunięcia = 0).</summary>
    public void PublishRoomTemperatures(double houseTemp, IReadOnlyDictionary<string, double>? offsets, DateTime updatedUtc)
    {
        foreach (var room in Config.Rooms)
            Ha.Set(room.SensorTemperatureEntityId, Math.Round(houseTemp + (offsets?.GetValueOrDefault(room.Name) ?? 0), 2), updatedUtc);
    }

    public Task<Core.Phases.PhaseResult> StepAsync(DateTime utc, ForecastData? forecast)
    {
        Clock.UtcNow = utc;
        return Phase.ExecuteAsync(new HeatingState { CurrentTime = utc, Forecast = forecast, SystemConfiguration = Config.System }, Parameters);
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
