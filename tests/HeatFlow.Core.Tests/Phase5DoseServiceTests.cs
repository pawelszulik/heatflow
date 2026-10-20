using System.Text.Json;
using HeatFlow.Core.Dose;
using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.DoseSim;
using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.Database;
using HeatFlow.Infrastructure.EsterownikMapper;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeatFlow.Core.Tests;

/// <summary>
/// Faza 5 „Dawka”: wyłącznik, zapamiętywanie i przywracanie nastaw, samowyłączenie, zapis
/// z potwierdzeniem w mapperze. Sterownik udaje <see cref="FakeController"/>: zapis przez
/// number.set_value w HA zmienia rejestr widziany przez mapper (albo nie - gdy zapis „ginie”).
/// </summary>
public class Phase5DoseServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Encje i pokoje jak w bazie u Pawła (seed / migracja) - Faza 5 bierze je z konfiguracji.</summary>
    private static readonly DoseEntities E = SimDefaults.Entities;
    private static readonly List<RoomConfiguration> Rooms = SimDefaults.IndoorRooms();
    private static readonly string[] IndoorSensors = Rooms.Select(r => r.SensorTemperatureEntityId).ToArray();

    private readonly Mock<IHomeAssistantClient> _ha = new();
    private readonly FakeDoseRepository _repo = new();
    private readonly FakeController _controller = new();
    private readonly Mock<IDoseNotifier> _notifier = new();
    private readonly Mock<IConfigurationService> _config = new();
    private readonly Mock<IConfigurationAuditService> _audit = new();
    private readonly Dictionary<string, EntityState> _entities = new();
    private readonly List<(string Title, string Message)> _messages = new();
    private readonly Phase5DoseService _service;

    public Phase5DoseServiceTests()
    {
        _ha.Setup(x => x.GetStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => _entities.GetValueOrDefault(id));
        _ha.Setup(x => x.SetNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, double value, CancellationToken _) => _controller.Write(id, (int)value));
        _notifier.Setup(x => x.NotifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string t, string m, CancellationToken _) => _messages.Add((t, m)))
            .Returns(Task.CompletedTask);
        _config.Setup(x => x.GetAllRoomsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Rooms);
        _config.Setup(x => x.GetHeatingParametersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new HeatingParameters());

        var errorLogger = new Mock<IApplicationErrorLogger>();
        var writer = new DoseControllerWriter(_ha.Object, _controller, new DoseWriteOptions
        {
            ConfirmFirstCheck = TimeSpan.Zero, ConfirmPollInterval = TimeSpan.Zero,
            ConfirmTimeout = TimeSpan.Zero, RetryDelay = TimeSpan.Zero
        }, new Mock<ILogger<DoseControllerWriter>>().Object);

        _service = new Phase5DoseService(_ha.Object, _repo, _controller, writer, _notifier.Object,
            _config.Object, _audit.Object, errorLogger.Object, new Mock<ILogger<Phase5DoseService>>().Object,
            new FixedTime(Now));

        // Domyślnie: zima, auto, kocioł grzeje przy nastawie, -5 °C na dworze od 2 h.
        SetEnvironment(outdoor: -5.0);
        _controller.Settings = new ControllerFeedSettings(7, 160, 35);
        _repo.State = new DoseState { LastKCorrectionDate = Now.Date.AddDays(-1) };
        AddHistory(hours: 3, outdoor: -5.0);
    }

    [Fact]
    public void PhaseNumber_ShouldBe5() => Assert.Equal(5, _service.PhaseNumber);

    [Fact]
    public async Task AutomatOff_ComputesSuggestion_ButNeverWrites()
    {
        var result = await Run();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("cień 7/160 → 7/140 s", result.Details);
        _ha.Verify(x => x.SetNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(9, _repo.State!.PlannedWork);
        Assert.False(_repo.State.AutomatActive);
        Assert.Empty(_messages);
    }

    [Fact]
    public async Task SwitchOn_RemembersSettings_AndWritesFirstChangeConfirmedByMapper()
    {
        _repo.Switch.Enabled = true;

        var result = await Run();

        Assert.True(result.Success, result.ErrorMessage);
        var s = _repo.State!;
        Assert.True(s.AutomatActive);
        Assert.Equal((7, 160, 35), (s.SavedWork!.Value, s.SavedPause!.Value, s.SavedBlower!.Value));
        // -5 °C: model 9 s / 111 s, ale dawka najwyżej o krok 20 s postoju → 7/140; dmuchawa
        // z udziału pracy 7/147 = 4,8 % → 30 + 0,24 · 5 ≈ 31 %
        Assert.Equal(new ControllerFeedSettings(7, 140, 31), _controller.Settings);
        Assert.Equal(Now, s.LastChangeUtc);
        Assert.Contains(_messages, m => m.Title.Contains("włączony"));
        Assert.Contains(_messages, m => m.Message.Contains("zapis potwierdzony w mapperze"));
    }

    [Fact]
    public async Task OnlyTheThreeAllowedEntities_AreEverWritten()
    {
        _repo.Switch.Enabled = true;
        await Run();
        _repo.Switch.Enabled = false;
        await Run();

        var allowed = new[] { E.FeederWork, E.FeederPause, E.Blower };
        Assert.NotEmpty(_controller.Writes);
        Assert.All(_controller.Writes, w => Assert.Contains(w.Entity, allowed));
        _ha.Verify(x => x.CallServiceAsync("number", It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
        _ha.Verify(x => x.CallServiceAsync("switch", It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeWithin2hOfLastChange_IsNotWritten()
    {
        _repo.Switch.Enabled = true;
        _repo.State = new DoseState
        {
            AutomatActive = true, SavedWork = 7, SavedPause = 200, SavedBlower = 35,
            LastChangeUtc = Now.AddMinutes(-60), LastKCorrectionDate = Now.Date.AddDays(-1)
        };

        var result = await Run();

        Assert.Contains("odstęp", result.Details);
        Assert.Empty(_controller.Writes);
    }

    [Fact]
    public async Task SwitchOff_RestoresRememberedSettings_AndReports()
    {
        _repo.Switch.Enabled = false;
        _controller.Settings = new ControllerFeedSettings(9, 110, 33);
        _repo.State = ActiveState(saved: (7, 160, 35));

        await Run();

        Assert.Equal(new ControllerFeedSettings(7, 160, 35), _controller.Settings);
        Assert.False(_repo.State!.AutomatActive);
        Assert.Null(_repo.State.SavedWork);
        Assert.Contains(_messages, m => m.Title.Contains("wyłączony") && m.Message.Contains("Przywrócone"));
    }

    [Fact]
    public async Task SwitchOff_WithRestoreDisabled_LeavesSettings()
    {
        _repo.Switch.Enabled = false;
        _controller.Settings = new ControllerFeedSettings(9, 110, 33);
        _repo.State = ActiveState(saved: (7, 160, 35));

        await Run(new HeatingParameters { DoseRestoreOnDisable = false });

        Assert.Equal(new ControllerFeedSettings(9, 110, 33), _controller.Settings);
        Assert.Empty(_controller.Writes);
        Assert.Contains(_messages, m => m.Message.Contains("nie przywracam"));
    }

    [Fact]
    public async Task FlueAbove200_SelfDisables_CutsBlower_AndRestoreNeverRaisesBlower()
    {
        _repo.Switch.Enabled = true;
        _controller.Settings = new ControllerFeedSettings(9, 110, 33);
        _repo.State = ActiveState(saved: (7, 160, 35));
        SetEnvironment(outdoor: -5.0, flue: 205);

        await Run();

        // −2 pkt (33 → 31), potem przywrócenie pracy/postoju bez podnoszenia dmuchawy do 35.
        Assert.Equal(new ControllerFeedSettings(7, 160, 31), _controller.Settings);
        Assert.False(_repo.Switch.Enabled);
        Assert.Contains("spaliny", _repo.Switch.ChangedBy);
        Assert.Contains(_messages, m => m.Title.Contains("SAM SIĘ WYŁĄCZYŁ") && m.Message.Contains("spaliny 205"));
    }

    [Theory]
    [InlineData("summer", "tryb lato")]
    [InlineData("manual", "trybie ręcznym")]
    public async Task SummerOrManualMode_SelfDisables_AndRestores(string condition, string expectedReason)
    {
        _repo.Switch.Enabled = true;
        _controller.Settings = new ControllerFeedSettings(9, 110, 33);
        _repo.State = ActiveState(saved: (7, 160, 35));
        SetEnvironment(outdoor: -5.0, summer: condition == "summer", auto: condition != "manual");

        await Run();

        Assert.False(_repo.Switch.Enabled);
        Assert.False(_repo.State!.AutomatActive);
        Assert.Equal(new ControllerFeedSettings(7, 160, 35), _controller.Settings);
        Assert.Contains(_messages, m => m.Message.Contains(expectedReason));
    }

    [Fact]
    public async Task WriteNotConfirmedByMapper_SelfDisables()
    {
        _repo.Switch.Enabled = true;
        _controller.DropWritesTo = E.FeederPause;

        await Run();

        Assert.False(_repo.Switch.Enabled);
        Assert.False(_repo.State!.AutomatActive);
        Assert.Contains(_messages, m => m.Title.Contains("SAM SIĘ WYŁĄCZYŁ") && m.Message.Contains("nie potwierdził"));
        // Dwie próby zapisu postoju (limiter ekoPieca), dmuchawa już nie wysłana, sterownik bez zmian.
        Assert.Equal(2, _controller.Writes.Count(w => w.Entity == E.FeederPause && w.Value == 140));
        Assert.DoesNotContain(_controller.Writes, w => w.Entity == E.Blower);
        Assert.Equal(new ControllerFeedSettings(7, 160, 35), _controller.Settings);
    }

    [Fact]
    public async Task StaleBoilerData_SelfDisables()
    {
        _repo.Switch.Enabled = true;
        _repo.State = ActiveState(saved: (7, 160, 35));
        _repo.State.LastBoilerDataUtc = Now.AddMinutes(-45);
        _entities[E.ControllerClock] = new EntityState { State = "unavailable" };

        await Run();

        Assert.False(_repo.Switch.Enabled);
        Assert.Contains(_messages, m => m.Message.Contains("brak świeżych danych kotła"));
    }

    [Fact]
    public async Task OutdoorSensorQuietFor58Minutes_DoesNotSelfDisable()
    {
        // Czujnik ogrodowy przy stałej temperaturze zmienia stan co ok. 58 min - to nie awaria.
        _repo.Switch.Enabled = true;
        _repo.State = ActiveState(saved: (7, 160, 35));
        _entities[E.OutdoorTemp].LastUpdated = Now.AddMinutes(-58);

        await Run();

        Assert.True(_repo.Switch.Enabled);
        Assert.True(_repo.State!.AutomatActive);
    }

    [Fact]
    public async Task BoilerBelowSetpointMinus10ForAnHour_Default_OnlyWarns_AutomatStaysOn()
    {
        // Decyzja Pawła 2026-10-20: domyślnie (DoseBoilerColdMinutes = 0) tylko ostrzeżenie.
        _repo.Switch.Enabled = true;
        _repo.State = ActiveState(saved: (7, 160, 35));
        _controller.Settings = new ControllerFeedSettings(9, 110, 33);
        MakeBoilerColdForLastHour();

        await Run();
        await Run();

        Assert.True(_repo.Switch.Enabled);
        Assert.True(_repo.State!.AutomatActive);
        Assert.DoesNotContain(_repo.Log, e => e.ReasonCode == "PRZYWROCENIE_NASTAW" || e.ReasonCode.StartsWith("SAMOWYLACZENIE_"));
        var warning = Assert.Single(_repo.Log, e => e.Status == DoseLog.StatusWarning);
        Assert.Equal("KOCIOL_ZIMNY", warning.ReasonCode);
        Assert.Contains("Automat zostaje włączony", warning.Message);
        Assert.Single(_messages, m => m.Title == "HeatFlow: kocioł zimny");
    }

    [Fact]
    public async Task BoilerBelowSetpointMinus10_WithColdMinutesSet_SelfDisables()
    {
        _repo.Switch.Enabled = true;
        _repo.State = ActiveState(saved: (7, 160, 35));
        MakeBoilerColdForLastHour();

        await Run(new HeatingParameters { DoseBoilerColdMinutes = 60 });

        Assert.False(_repo.Switch.Enabled);
        Assert.Contains(_messages, m => m.Message.Contains("poniżej nastawy − 10 K"));
        Assert.Contains(_repo.Log, e => e.ReasonCode == "SAMOWYLACZENIE_KOCIOL_ZIMNY");
    }

    private void MakeBoilerColdForLastHour()
    {
        foreach (var s in _repo.Samples.Where(s => s.TimestampUtc > Now.AddHours(-1))) s.BoilerTemp = 55;
        SetEnvironment(outdoor: -5.0, boiler: 55);
    }

    [Fact]
    public async Task SwitchOn_MapperDown_RefusesAndTurnsSwitchBackOff()
    {
        _repo.Switch.Enabled = true;
        _controller.Down = true;

        await Run();

        Assert.False(_repo.Switch.Enabled);
        Assert.False(_repo.State!.AutomatActive);
        Assert.Empty(_controller.Writes);
        Assert.Contains(_messages, m => m.Title.Contains("nie włączam"));
    }

    [Fact]
    public async Task InvalidParameters_SelfDisable_WithoutWriting()
    {
        _repo.Switch.Enabled = true;
        _repo.State = ActiveState(saved: (7, 160, 35));

        await Run(new HeatingParameters { FeederWorkMax = 12 });

        Assert.False(_repo.Switch.Enabled);
        Assert.Contains(_messages, m => m.Message.Contains("parametry poza granicami"));
        Assert.DoesNotContain(_controller.Writes, w => w.Value > 10 && w.Entity == E.FeederWork);
    }

    [Fact]
    public async Task MidnightKCorrection_LearnsFromYesterdayBalance_AndResetsTrim()
    {
        // Wczoraj: 0 °C, 24 °C w domu, podajnik 0,8 × model → k 1,0 → 0,95.
        _repo.State = new DoseState { LastKCorrectionDate = Now.Date.AddDays(-2), TrimSec = -20 };
        _repo.Samples.Clear();
        var dayStart = Now.Date.AddDays(-1);
        var counter = 0;
        for (var t = dayStart.AddDays(-1); t < Now; t = t.AddMinutes(5))
        {
            counter += 14; // 14 s / 5 min = 4032 s/dobę ≈ 0,78 × 5186
            _repo.Samples.Add(new DoseSample
            {
                TimestampUtc = t, OutdoorTemp = 0, IndoorTemp = 24, BoilerTemp = 66, BoilerSetpoint = 67,
                BlowerActual = 35, FeederCounterSec = counter, SummerMode = false
            });
        }
        // Grzanie 18 h na dobę - poniżej sufitu 22,5 h
        foreach (var s in _repo.Samples.Where((_, i) => i % 4 == 0)) s.BlowerActual = 0;
        HeatingParameters? saved = null;
        _config.Setup(x => x.SaveHeatingParametersAsync(It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .Callback((HeatingParameters p, CancellationToken _) => saved = p)
            .Returns(Task.CompletedTask);

        var parameters = new HeatingParameters();
        var result = await Run(parameters);

        Assert.NotNull(saved);
        Assert.Equal(0.95, saved!.FeedCorrectionK, 3);
        Assert.Equal(0.95, parameters.FeedCorrectionK, 3);
        Assert.Equal(0, _repo.State!.TrimSec);
        Assert.Equal(dayStart, _repo.State.LastKCorrectionDate);
        Assert.Contains("korekta k", result.Details);
    }

    [Fact]
    public async Task BoilerUnderpoweredFor2h_TrimsPauseDown20s()
    {
        foreach (var s in _repo.Samples) { s.BoilerTemp = 60; s.BlowerActual = 35; }
        SetEnvironment(outdoor: 5.0, boiler: 60);
        foreach (var s in _repo.Samples) s.OutdoorTemp = 5.0;

        await Run();

        Assert.Equal(-20, _repo.State!.TrimSec);
        var model = DoseCalculator.PauseFor(7, DoseCalculator.Need(new HeatingParameters(), 5.0));
        Assert.Equal(model - 20, _repo.State.ModelPause!.Value, 0);
    }

    // --- Dziennik decyzji (FeedChangeLog): co, z ilu na ile, w którą stronę, dlaczego ---

    [Fact]
    public async Task Log_WeatherChange_EachRegisterWithDirectionCauseAndMapperConfirmation()
    {
        _repo.Switch.Enabled = true;

        await Run();

        var on = Assert.Single(_repo.Log, e => e.Parameter == DoseLog.Automat);
        Assert.Equal("WLACZENIE_RECZNE", on.ReasonCode);
        Assert.Contains("Zapamiętane nastawy: praca 7 s, postój 160 s, dmuchawa 35 %", on.Message);

        // Model chce 9/111, krok ogranicza do 7/140 - czas pracy bez zmian, więc bez wpisu.
        Assert.DoesNotContain(_repo.Log, e => e.Parameter == DoseLog.Work);

        var pause = Assert.Single(_repo.Log, e => e.Parameter == DoseLog.Pause);
        Assert.Equal((160.0, 140.0, "w dół", "POGODA", DoseLog.StatusWritten, "OK"),
            (pause.OldValue!.Value, pause.NewValue!.Value, pause.Direction, pause.ReasonCode, pause.Status, pause.MapperConfirmation));
        Assert.StartsWith("Postój 160 → 140 s (w dół = więcej węgla): średnia Tz z 3 h -5,0 °C, potrzeba", pause.Message);
        Assert.Contains("przy k 1", pause.Message);
        Assert.Contains("cel z modelu 9/111 s", pause.Message);
        Assert.EndsWith("zapis potwierdzony w mapperze.", pause.Message);

        var blower = Assert.Single(_repo.Log, e => e.Parameter == DoseLog.Blower);
        Assert.Equal((35.0, 31.0, "w dół", "POGODA"), (blower.OldValue!.Value, blower.NewValue!.Value, blower.Direction, blower.ReasonCode));
        Assert.Contains("dmuchawa idzie za dawką", blower.Message);

        // Dane wejściowe w chwili decyzji
        Assert.Equal(-5.0, pause.OutdoorSmoothed);
        Assert.Equal(66, pause.BoilerTemp);
        Assert.Equal(67, pause.BoilerSetpoint);
        Assert.Equal(140, pause.FlueTemp);
        Assert.Equal(100, pause.HeatingShare2hPct);
        Assert.Equal(1.0, pause.K);
        Assert.True(pause.AutomatActive);

        // Discord: jedna linia na każdą zmienioną nastawę - te same zdania co w dzienniku
        var discord = Assert.Single(_messages, m => m.Title.StartsWith("Kocioł: dawka"));
        Assert.Equal(new[] { pause.Message, blower.Message }, discord.Message.Split('\n'));
    }

    [Fact]
    public async Task Log_PreviousTzIsQuoted_WhenWeatherMovedSinceLastChange()
    {
        _repo.Switch.Enabled = true;
        _repo.State = ActiveState(saved: (7, 160, 35));
        _repo.State.LastChangeOutdoorSmoothed = 4.1;

        await Run();

        var pause = Assert.Single(_repo.Log, e => e.Parameter == DoseLog.Pause);
        Assert.Contains("średnia Tz z 3 h spadła z 4,1 do -5,0 °C", pause.Message);
        Assert.Equal(4.1, pause.OutdoorAtPreviousChange);
    }

    [Fact]
    public async Task Log_Flue205_BlowerDownThenSelfDisableThenRestore()
    {
        _repo.Switch.Enabled = true;
        _controller.Settings = new ControllerFeedSettings(9, 110, 33);
        _repo.State = ActiveState(saved: (7, 160, 35));
        SetEnvironment(outdoor: -5.0, flue: 205);

        await Run();

        var cut = _repo.Log.First(e => e.ReasonCode == "SPALINY_200");
        Assert.Equal((DoseLog.Blower, 33.0, 31.0, "w dół", "SPALINY_200", DoseLog.StatusWritten),
            (cut.Parameter, cut.OldValue!.Value, cut.NewValue!.Value, cut.Direction, cut.ReasonCode, cut.Status));
        Assert.Equal("Dmuchawa 33 → 31 % (w dół = mniej powietrza): spaliny 205 °C > 200; zapis potwierdzony w mapperze.", cut.Message);

        var off = Assert.Single(_repo.Log, e => e.Parameter == DoseLog.Automat);
        Assert.Equal("SAMOWYLACZENIE_SPALINY_200", off.ReasonCode);

        var restored = _repo.Log.Where(e => e.ReasonCode == "PRZYWROCENIE_NASTAW").ToList();
        Assert.Equal(new[] { (DoseLog.Work, 9.0, 7.0, "w dół"), (DoseLog.Pause, 110.0, 160.0, "w górę") },
            restored.Select(e => (e.Parameter, e.OldValue!.Value, e.NewValue!.Value, e.Direction)));
        Assert.All(restored, e => Assert.Equal("OK", e.MapperConfirmation));
    }

    [Fact]
    public async Task Log_ManualOff_LoggedAsWylaczenieReczne_WithRestore()
    {
        _repo.Switch.Enabled = false;
        _controller.Settings = new ControllerFeedSettings(9, 110, 33);
        _repo.State = ActiveState(saved: (7, 160, 35));

        await Run();

        Assert.Contains(_repo.Log, e => e.Parameter == DoseLog.Automat && e.ReasonCode == "WYLACZENIE_RECZNE" && e.NewValue == 0);
        Assert.Equal(3, _repo.Log.Count(e => e.ReasonCode == "PRZYWROCENIE_NASTAW" && e.Status == DoseLog.StatusWritten));
        Assert.Contains(_repo.Log, e => e.Message.StartsWith("Dmuchawa 33 → 35 % (w górę = więcej powietrza): przywrócenie nastaw"));
    }

    [Fact]
    public async Task Log_UnconfirmedWrite_HasRozjazdAndSelfDisableCode()
    {
        _repo.Switch.Enabled = true;
        _controller.DropWritesTo = E.FeederPause;

        await Run();

        var pause = Assert.Single(_repo.Log, e => e.Parameter == DoseLog.Pause && e.ReasonCode == "POGODA");
        Assert.Equal((DoseLog.StatusUnconfirmed, "ROZJAZD"), (pause.Status, pause.MapperConfirmation));
        Assert.Contains("zapisu NIE potwierdzono - sterownik ma 160", pause.Message);
        Assert.Contains(_repo.Log, e => e.ReasonCode == "SAMOWYLACZENIE_ZAPIS_NIEPOTWIERDZONY");
    }

    [Fact]
    public async Task Log_BlockedByInterval_LoggedOnce_NotEveryRun()
    {
        _repo.Switch.Enabled = true;
        _repo.State = ActiveState(saved: (7, 160, 35));
        _repo.State.LastChangeUtc = Now.AddMinutes(-70);

        await Run();
        await Run();

        // Pierwsze liczenie zapisuje też punkt odniesienia celu domu (CEL_DOMU) - raz.
        Assert.Single(_repo.Log, e => e.ReasonCode == "CEL_DOMU");
        var blocked = Assert.Single(_repo.Log, e => e.ReasonCode != "CEL_DOMU");
        Assert.Equal((DoseLog.Pause, "ODSTEP", DoseLog.StatusBlocked, 160.0, 140.0),
            (blocked.Parameter, blocked.ReasonCode, blocked.Status, blocked.OldValue!.Value, blocked.NewValue!.Value));
        Assert.StartsWith("Chciałbym: Postój 160 → 140 s (w dół = więcej węgla), ale od poprzedniej zmiany minęło dopiero 70 min (min. 2 h).", blocked.Message);
        Assert.Empty(_controller.Writes);
    }

    [Fact]
    public async Task Log_AutomatOff_SuggestionLoggedOnce()
    {
        await Run();
        await Run();

        var suggestions = _repo.Log.Where(e => e.Status == DoseLog.StatusSuggestion).ToList();
        Assert.Equal(new[] { DoseLog.Pause, DoseLog.Blower }, suggestions.Select(e => e.Parameter));
        Assert.All(suggestions, e => Assert.StartsWith("Automat wyłączony - ustawiłbym:", e.Message));
        Assert.Empty(_controller.Writes);
    }

    [Fact]
    public async Task Log_BlowerRaiseBlockedByFlue185()
    {
        _repo.Switch.Enabled = true;
        _controller.Settings = new ControllerFeedSettings(7, 150, 30);
        _repo.State = ActiveState(saved: (7, 150, 30));
        SetEnvironment(outdoor: -5.0, flue: 190);

        await Run();

        Assert.Equal(30, _controller.Settings.Blower);
        var blocked = Assert.Single(_repo.Log, e => e.Status == DoseLog.StatusBlocked);
        // 7/150 → 7/130 (krok), dmuchawa za dawką chciałaby 30 → 32 %
        Assert.Equal((DoseLog.Blower, "SPALINY_185", 30.0, 32.0), (blocked.Parameter, blocked.ReasonCode, blocked.OldValue!.Value, blocked.NewValue!.Value));
        Assert.Contains("ale spaliny 190 °C ≥ 185", blocked.Message);
    }

    [Fact]
    public async Task Log_ChangeCausedByTrim_HasTrimCode()
    {
        // +5 °C: model 7 s / 148 s, sterownik 7/150 - bez trymu nic by się nie zmieniło.
        // Kocioł 2 h pod nastawą − 3 K przy ciągłej dmuchawie → trym −20 s → postój 150 → 130 s.
        _repo.Switch.Enabled = true;
        _controller.Settings = new ControllerFeedSettings(7, 150, 30);
        _repo.State = ActiveState(saved: (7, 150, 30));
        foreach (var s in _repo.Samples) { s.BoilerTemp = 60; s.BlowerActual = 35; s.OutdoorTemp = 5.0; }
        SetEnvironment(outdoor: 5.0, boiler: 60);

        await Run();

        var trim = Assert.Single(_repo.Log, e => e.Parameter == DoseLog.Trim);
        Assert.Equal(("TRYM_KOCIOL_PONIZEJ_NASTAWY", 0.0, -20.0, "w dół"), (trim.ReasonCode, trim.OldValue!.Value, trim.NewValue!.Value, trim.Direction));
        Assert.Contains("od 2 h dmuchawa chodzi 100 % czasu, a kocioł 60,0 °C przy nastawie 67 °C", trim.Message);

        var pause = Assert.Single(_repo.Log, e => e.Parameter == DoseLog.Pause);
        Assert.Equal(("TRYM_KOCIOL_PONIZEJ_NASTAWY", 150.0, 130.0), (pause.ReasonCode, pause.OldValue!.Value, pause.NewValue!.Value));
        Assert.StartsWith("Postój 150 → 130 s (w dół = więcej węgla): od 2 h dmuchawa chodzi", pause.Message);
    }

    [Fact]
    public async Task Log_StandbyTrim_PauseUp()
    {
        // Dwa wejścia w podtrzymanie w 6 h → trym +20 s.
        _repo.Samples.Clear();
        var temps = new[] { 64.0, 67.5, 65.0, 67.2, 66.0 };
        for (var i = 0; i < 36; i++)
        {
            _repo.Samples.Add(new DoseSample
            {
                TimestampUtc = Now.AddMinutes(-5 * (36 - i)), OutdoorTemp = 5.0, IndoorTemp = 24,
                BoilerTemp = temps[Math.Min(i / 7, 4)], BoilerSetpoint = 67, BlowerActual = 35, FeederCounterSec = 1000
            });
        }
        SetEnvironment(outdoor: 5.0);

        await Run();

        var trim = Assert.Single(_repo.Log, e => e.Parameter == DoseLog.Trim);
        Assert.Equal(("TRYM_PODTRZYMANIA", 20.0, "w górę"), (trim.ReasonCode, trim.NewValue!.Value, trim.Direction));
        Assert.StartsWith("Trym postoju 0 → 20 s (w górę = mniej węgla): 2 wejścia w podtrzymanie w ostatnich 6 h", trim.Message);
    }

    [Fact]
    public async Task Log_Ceiling_ChangeToMaxDoseHasSufitCode()
    {
        _repo.Switch.Enabled = true;
        _controller.Settings = new ControllerFeedSettings(10, 120, 35);
        _repo.State = ActiveState(saved: (10, 120, 35));
        foreach (var s in _repo.Samples) s.OutdoorTemp = -20.0;
        SetEnvironment(outdoor: -20.0);

        await Run();

        var pause = Assert.Single(_repo.Log, e => e.Parameter == DoseLog.Pause);
        Assert.Equal(("SUFIT", 120.0, 100.0), (pause.ReasonCode, pause.OldValue!.Value, pause.NewValue!.Value));
        Assert.Contains("kocioł nie nadąża - przy maks. pracy i min. postoju brakuje 24 %", pause.Message);
    }

    [Fact]
    public async Task Log_KCorrection_EntryWithDirectionAndBalance()
    {
        _repo.State = new DoseState { LastKCorrectionDate = Now.Date.AddDays(-2) };
        _repo.Samples.Clear();
        var counter = 0;
        var i = 0;
        for (var t = Now.Date.AddDays(-2); t < Now; t = t.AddMinutes(5), i++)
        {
            counter += 14;
            _repo.Samples.Add(new DoseSample
            {
                TimestampUtc = t, OutdoorTemp = 0, IndoorTemp = 24, BoilerTemp = 66, BoilerSetpoint = 67,
                BlowerActual = i % 4 == 0 ? 0 : 35, FeederCounterSec = counter, SummerMode = false
            });
        }
        _config.Setup(x => x.SaveHeatingParametersAsync(It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await Run();

        var k = Assert.Single(_repo.Log, e => e.Parameter == DoseLog.K);
        Assert.Equal(("KOREKTA_K", 1.0, 0.95, "w dół"), (k.ReasonCode, k.OldValue!.Value, k.NewValue!.Value, k.Direction));
        Assert.StartsWith("k 1,000 → 0,950 (w dół = mniej węgla): bilans doby", k.Message);
        Assert.Contains("podajnik 4032 s", k.Message);
    }

    [Fact]
    public async Task Log_ActivationRefused_HasSelfDisableCode()
    {
        _repo.Switch.Enabled = true;
        SetEnvironment(outdoor: -5.0, summer: true);

        await Run();

        var refused = Assert.Single(_repo.Log, e => e.Parameter == DoseLog.Automat);
        Assert.Equal("SAMOWYLACZENIE_TRYB_LATO", refused.ReasonCode);
        Assert.StartsWith("Automatu nie włączono: kocioł przełączony na tryb lato", refused.Message);
    }

    // --- Helpers ---

    private Task<PhaseResult> Run(HeatingParameters? p = null)
        => _service.ExecuteAsync(new HeatingState { CurrentTime = Now, SystemConfiguration = SimDefaults.SystemConfiguration() },
            p ?? new HeatingParameters());

    private DoseState ActiveState((int W, int P, int B) saved) => new()
    {
        AutomatActive = true, SavedWork = saved.W, SavedPause = saved.P, SavedBlower = saved.B,
        LastKCorrectionDate = Now.Date.AddDays(-1), LastChangeUtc = Now.AddHours(-3),
        LastMapperOkUtc = Now.AddMinutes(-5), LastBoilerDataUtc = Now.AddMinutes(-5), LastOutdoorDataUtc = Now.AddMinutes(-5)
    };

    private void SetEnvironment(double outdoor, double flue = 140, bool summer = false, bool auto = true, double boiler = 66)
    {
        EntityState S(string v, DateTime? updated = null) => new() { State = v, LastUpdated = updated ?? Now.AddSeconds(-20) };
        _entities[E.OutdoorTemp] = S(outdoor.ToString(System.Globalization.CultureInfo.InvariantCulture), Now.AddMinutes(-10));
        foreach (var id in IndoorSensors) _entities[id] = S("24.0");
        _entities[E.BoilerTemp] = S(boiler.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _entities[E.BoilerSetpoint] = S("67.0");
        _entities[E.FlueTemp] = S(flue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _entities[E.BlowerActual] = S("35.0");
        _entities[E.FeederCounter] = S("30m,034s");
        _entities[E.SummerSwitch] = S(summer ? "on" : "off");
        _entities[E.ControllerMode] = S(auto ? "on" : "off");
        _entities[E.ControllerClock] = S("2026-11-20T11:59:30+00:00");
        _entities[E.FlueSafetyAutomation] = new EntityState
        {
            State = "on",
            Attributes = new Dictionary<string, object> { ["last_triggered"] = JsonDocument.Parse("null").RootElement }
        };
    }

    private void AddHistory(int hours, double outdoor)
    {
        for (var t = Now.AddHours(-hours); t < Now; t = t.AddMinutes(5))
        {
            _repo.Samples.Add(new DoseSample
            {
                TimestampUtc = t, OutdoorTemp = outdoor, IndoorTemp = 24, BoilerTemp = 66, BoilerSetpoint = 67,
                FlueTemp = 140, BlowerActual = 35, FeederCounterSec = 1000, SummerMode = false
            });
        }
    }

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTime(DateTime utc) => _now = new DateTimeOffset(utc);
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    /// <summary>Sterownik + mapper: zapis z HA zmienia rejestr, chyba że „ginie” albo mapper leży.</summary>
    private sealed class FakeController : IEsterownikMapperClient
    {
        public ControllerFeedSettings Settings { get; set; } = new(7, 160, 35);
        public bool Down { get; set; }
        public string? DropWritesTo { get; set; }
        public List<(string Entity, int Value)> Writes { get; } = new();

        public Task<ControllerFeedSettings?> GetFeedSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Down ? null : Settings);

        public bool Write(string entity, int value)
        {
            Writes.Add((entity, value));
            if (entity == DropWritesTo) return true; // HA mówi ok, sterownik nie przyjął
            if (entity == E.FeederWork) Settings = Settings with { FeederWork = value };
            else if (entity == E.FeederPause) Settings = Settings with { FeederPause = value };
            else if (entity == E.Blower) Settings = Settings with { Blower = value };
            else throw new InvalidOperationException($"Zapis do niedozwolonej encji {entity}");
            return true;
        }
    }

    private sealed class FakeDoseRepository : IDoseRepository
    {
        public DoseAutomatSwitch Switch { get; } = new() { Id = 1 };
        public DoseState? State { get; set; }
        public List<DoseSample> Samples { get; } = new();

        public Task<DoseAutomatSwitch> GetAutomatSwitchAsync(CancellationToken ct = default)
            => Task.FromResult(new DoseAutomatSwitch { Id = 1, Enabled = Switch.Enabled, ChangedBy = Switch.ChangedBy });

        public Task SetAutomatSwitchAsync(bool enabled, string changedBy, CancellationToken ct = default)
        {
            Switch.Enabled = enabled;
            Switch.ChangedBy = changedBy;
            return Task.CompletedTask;
        }

        public Task<DoseState?> GetStateAsync(CancellationToken ct = default) => Task.FromResult(State);

        public Task SaveStateAsync(DoseState state, CancellationToken ct = default)
        {
            State = state;
            return Task.CompletedTask;
        }

        public Task AddSampleAsync(DoseSample sample, CancellationToken ct = default)
        {
            Samples.Add(sample);
            return Task.CompletedTask;
        }

        public Task<List<DoseSample>> GetSamplesSinceAsync(DateTime sinceUtc, CancellationToken ct = default)
            => Task.FromResult(Samples.Where(s => s.TimestampUtc >= sinceUtc).OrderBy(s => s.TimestampUtc).ToList());

        public Task DeleteSamplesOlderThanAsync(DateTime olderThanUtc, CancellationToken ct = default)
        {
            Samples.RemoveAll(s => s.TimestampUtc < olderThanUtc);
            return Task.CompletedTask;
        }

        public List<FeedChangeLog> Log { get; } = new();

        public Task AddChangeLogAsync(IEnumerable<FeedChangeLog> entries, CancellationToken ct = default)
        {
            Log.AddRange(entries);
            return Task.CompletedTask;
        }

        public Task<List<FeedChangeLog>> GetChangeLogAsync(int limit, CancellationToken ct = default)
            => Task.FromResult(Log.AsEnumerable().Reverse().Take(limit).ToList());
    }
}
