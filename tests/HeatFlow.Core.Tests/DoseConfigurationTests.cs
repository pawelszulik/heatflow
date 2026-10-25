using HeatFlow.Core.Dose;
using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.DoseSim;
using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.EsterownikMapper;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HeatFlow.Core.Tests;

/// <summary>
/// Faza 5 bierze encje HA, czujniki w domu, kanał powiadomień i progi z bazy (SystemConfiguration,
/// RoomConfiguration, HeatingParameters), nie z kodu - w kodzie zostają tylko twarde granice DoseLimits.
/// </summary>
public class DoseConfigurationTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    // --- Encje (SystemConfiguration) ---

    [Fact]
    public void Entities_SeedValues_AreValid()
    {
        Assert.Empty(SimDefaults.Entities.Validate());
        Assert.Equal("number.kociol_czas_postoju_podajnika", SimDefaults.Entities.FeederPause);
        Assert.Equal("sensor.kociol_temperatura_kotla", SimDefaults.Entities.BoilerTemp);
    }

    [Fact]
    public void Entities_EmptyConfiguration_ListsWhatIsMissing()
    {
        var errors = DoseEntities.From(new SystemConfiguration()).Validate();
        Assert.Contains(errors, e => e.Contains(nameof(SystemConfiguration.DoseFeederPauseEntityId)));
        Assert.Contains(errors, e => e.Contains(nameof(SystemConfiguration.BoilerTempEntityId)));
        // Automatyzacja #24 jest opcjonalna
        Assert.DoesNotContain(errors, e => e.Contains(nameof(SystemConfiguration.DoseFlueSafetyAutomationEntityId)));
    }

    [Fact]
    public void Entities_WriteToSetpointOrNonNumberOrDuplicate_IsRejected()
    {
        var c = SimDefaults.SystemConfiguration();
        c.DoseFeederPauseEntityId = c.DoseBoilerSetpointEntityId;
        Assert.Contains(DoseEntities.From(c).Validate(), e => e.Contains("nastawa kotła"));

        c = SimDefaults.SystemConfiguration();
        c.DoseBlowerEntityId = "sensor.kociol_aktualna_moc_dmuchawy";
        Assert.Contains(DoseEntities.From(c).Validate(), e => e.Contains("number.*"));

        c = SimDefaults.SystemConfiguration();
        c.DoseBlowerEntityId = c.DoseFeederWorkEntityId;
        Assert.Contains(DoseEntities.From(c).Validate(), e => e.Contains("różne"));
    }

    [Fact]
    public async Task Writer_MisconfiguredEntities_RefusesWithoutCallingHa()
    {
        var ha = new Mock<IHomeAssistantClient>();
        var writer = new DoseControllerWriter(ha.Object, new Mock<IEsterownikMapperClient>().Object, new DoseWriteOptions(),
            NullLogger<DoseControllerWriter>.Instance);
        var c = SimDefaults.SystemConfiguration();
        c.DoseFeederPauseEntityId = c.DoseBoilerSetpointEntityId;

        var result = await writer.WriteAsync(DoseEntities.From(c), DoseRegister.Pause, 150);

        Assert.False(result.Confirmed);
        Assert.Equal("ODMOWA", Assert.Single(result.Writes).Confirmation);
        ha.Verify(x => x.SetNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Phase_BrokenConfiguration_ErrorAndAutomatOff_NothingReadOrWritten()
    {
        var h = new DoseHarness(new ControllerFeedSettings(7, 160, 35));
        h.Repo.Switch.Enabled = true;
        h.Repo.State = new DoseState { AutomatActive = true, SavedWork = 7, SavedPause = 160, SavedBlower = 35 };
        h.Config.System.DoseFeederPauseEntityId = "";

        var result = await h.StepAsync(Now, null);

        Assert.False(result.Success);
        Assert.Contains(nameof(SystemConfiguration.DoseFeederPauseEntityId), result.ErrorMessage);
        Assert.False(h.Repo.Switch.Enabled);
        Assert.False(h.Repo.State!.AutomatActive);
        Assert.Equal("SAMOWYLACZENIE_KONFIGURACJA", Assert.Single(h.Repo.Log).ReasonCode);
        Assert.Empty(h.Controller.History);
        Assert.Empty(h.Repo.Samples);
    }

    // --- Czujniki w domu (RoomConfiguration.UseForDoseIndoorMean) ---

    [Fact]
    public async Task Indoor_FromFlaggedRoomsOnly_WithMinSensorsParameter()
    {
        var h = new DoseHarness(new ControllerFeedSettings(7, 160, 35));
        // Pokój bez flagi z bardzo zimnym czujnikiem nie może wejść do średniej.
        h.Config.Rooms.Add(new RoomConfiguration { Name = "garaz", SensorTemperatureEntityId = "sensor.garaz", UseForDoseIndoorMean = false });
        h.Publish(new DoseInputsSnapshot(0, 24.0, 66, 67, 140, 35, 1000, false, true), Now);
        h.Ha.Set("sensor.garaz", 5.0, Now);

        await h.StepAsync(Now, null);
        Assert.Equal(24.0, h.Repo.Samples.Last().IndoorTemp);

        // Tylko 4 z 7 czujników odpowiada: przy progu 5 - brak średniej, przy progu 4 - jest.
        foreach (var room in h.Config.Rooms.Where(r => r.UseForDoseIndoorMean).Take(3))
            h.Ha.Set(room.SensorTemperatureEntityId, (string?)null, Now);
        await h.StepAsync(Now.AddMinutes(5), null);
        Assert.Null(h.Repo.Samples.Last().IndoorTemp);

        h.Config.Parameters.DoseIndoorMinSensors = 4;
        await h.StepAsync(Now.AddMinutes(10), null);
        Assert.Equal(24.0, h.Repo.Samples.Last().IndoorTemp);
    }

    [Fact]
    public void RoomSnapshot_KeepsDoseFlag_ForAudit()
    {
        var snap = RoomConfiguration.CreateSnapshot(new RoomConfiguration { Name = "salon", UseForDoseIndoorMean = true });
        Assert.True(snap!.UseForDoseIndoorMean);
    }

    // --- Powiadomienia (SystemConfiguration.DoseNotifyService / DoseNotifyTarget) ---

    [Fact]
    public async Task Notifier_UsesServiceAndTargetFromConfiguration()
    {
        var ha = new Mock<IHomeAssistantClient>();
        ha.Setup(x => x.CallServiceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var config = new SimConfigurationService(new HeatingParameters());
        config.System.DoseNotifyService = "inny_kanal";
        var notifier = new HomeAssistantDiscordNotifier(ha.Object, config, NullLogger<HomeAssistantDiscordNotifier>.Instance);

        await notifier.NotifyAsync("t", "m");

        ha.Verify(x => x.CallServiceAsync("notify", "inny_kanal", It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Notifier_NoTargetConfigured_SendsNothing()
    {
        var ha = new Mock<IHomeAssistantClient>();
        var config = new SimConfigurationService(new HeatingParameters());
        config.System.DoseNotifyTarget = "";
        var notifier = new HomeAssistantDiscordNotifier(ha.Object, config, NullLogger<HomeAssistantDiscordNotifier>.Instance);

        await notifier.NotifyAsync("t", "m");

        ha.Verify(x => x.CallServiceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- Progi z HeatingParameters ---

    [Fact]
    public void Thresholds_FlueLimitsFromParameters()
    {
        var p = new HeatingParameters { DoseFlueNoRaiseTemp = 170, DoseFlueMaxTemp = 190, DoseFlueBlowerCutPct = 3 };
        Assert.Equal(36, DoseCalculator.LimitBlower(40, 36, 175, false, p).Blower);        // 175 ≥ 170 - nie podnosi
        Assert.Equal(37, DoseCalculator.LimitBlower(40, 40, 195, false, p).Blower);        // 195 > 190 - −3 pkt
        Assert.Equal("SPALINY_200", DoseCalculator.LimitBlower(40, 40, 195, false, p).Code);
    }

    [Fact]
    public void Thresholds_KCorrectionGainStepAndBoundsFromParameters()
    {
        var day = new DailyBalance(0.0, 24.0, 2593, 18, true, null); // 0,5 × model
        var p = new HeatingParameters { FeedKGain = 1.0, FeedKMaxStep = 0.2, FeedKMin = 0.85 };
        var (k, _) = DoseCalculator.CorrectK(1.0, day, null, p);
        Assert.Equal(0.85, k!.Value, 3); // krok 0,2 → 0,8, ale minimum 0,85
    }

    [Fact]
    public void Thresholds_CeilingDayAndUnderpowerKFromParameters()
    {
        var day = new DailyBalance(-5.0, 24.0, 7200, 21, true, null); // podajnik ≥ 90 % możliwych 7855 s
        Assert.Null(DoseCalculator.CorrectK(1.0, day, null, new HeatingParameters { DoseCeilingDayHeatingHours = 20 }).K);
        Assert.NotNull(DoseCalculator.CorrectK(1.0, day, null, new HeatingParameters()).K);

        var window = Enumerable.Range(0, 24)
            .Select(i => new DoseSample { TimestampUtc = Now.AddMinutes(5 * i), BoilerTemp = 62, BoilerSetpoint = 67, BlowerActual = 35 })
            .ToList();
        Assert.True(DoseCalculator.BoilerUnderpowered(window, TimeSpan.FromHours(2), 3));
        Assert.False(DoseCalculator.BoilerUnderpowered(window, TimeSpan.FromHours(2), 6));
    }

    [Fact]
    public void Validation_NewThresholds()
    {
        Assert.Empty(DoseLimits.Validate(new HeatingParameters()));
        Assert.NotEmpty(DoseLimits.Validate(new HeatingParameters { DoseFlueNoRaiseTemp = 210, DoseFlueMaxTemp = 200 }));
        Assert.NotEmpty(DoseLimits.Validate(new HeatingParameters { FeedKMin = 0.9, FeedCorrectionK = 0.8 }));
        Assert.NotEmpty(DoseLimits.Validate(new HeatingParameters { DoseIndoorMinSensors = 0 }));
        Assert.NotEmpty(DoseLimits.Validate(new HeatingParameters { DoseBlowerLowDuty = 0.2 }));
    }

    [Fact]
    public async Task Trim_StepFromParameters()
    {
        var h = new DoseHarness(new ControllerFeedSettings(7, 150, 30), new HeatingParameters { DoseTrimStepSec = 10 });
        for (var t = Now.AddHours(-3); t <= Now; t = t.AddMinutes(5))
        {
            h.Publish(new DoseInputsSnapshot(5.0, 24, 60, 67, 140, 35, 1000, false, true), t);
            await h.StepAsync(t, null);
        }

        Assert.Equal(-10, h.Repo.State!.TrimSec);
        Assert.Contains(h.Repo.Log, e => e.Parameter == DoseLog.Trim && e.NewValue == -10);
    }
}
