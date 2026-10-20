using System.Text.Json;
using HeatFlow.Core.Dose;
using HeatFlow.Core.Phases;
using HeatFlow.Domain;
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
        Assert.Contains("sugestia 9/", result.Details);
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
        // -5 °C: model 9 s / 111 s → zmiana czasu pracy, postój od razu z modelu; dmuchawa
        // z udziału pracy 9/119 = 7,6 % → 30 + 0,73 · 5 ≈ 34 %
        Assert.Equal(new ControllerFeedSettings(9, 110, 34), _controller.Settings);
        Assert.Equal(Now, s.LastChangeUtc);
        Assert.Contains(_messages, m => m.Title.Contains("włączony"));
        Assert.Contains(_messages, m => m.Message.Contains("Potwierdzone w mapperze"));
    }

    [Fact]
    public async Task OnlyTheThreeAllowedEntities_AreEverWritten()
    {
        _repo.Switch.Enabled = true;
        await Run();
        _repo.Switch.Enabled = false;
        await Run();

        var allowed = new[] { DoseControllerWriter.WorkEntityId, DoseControllerWriter.PauseEntityId, DoseControllerWriter.BlowerEntityId };
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
        _controller.DropWritesTo = DoseControllerWriter.PauseEntityId;

        await Run();

        Assert.False(_repo.Switch.Enabled);
        Assert.False(_repo.State!.AutomatActive);
        Assert.Contains(_messages, m => m.Title.Contains("SAM SIĘ WYŁĄCZYŁ") && m.Message.Contains("nie potwierdził"));
        // Dwie próby zapisu postoju (limiter ekoPieca), potem przywrócenie pracy na 7 s.
        Assert.Equal(2, _controller.Writes.Count(w => w.Entity == DoseControllerWriter.PauseEntityId && w.Value == 110));
        Assert.Equal(7, _controller.Settings.FeederWork);
    }

    [Fact]
    public async Task StaleBoilerData_SelfDisables()
    {
        _repo.Switch.Enabled = true;
        _repo.State = ActiveState(saved: (7, 160, 35));
        _repo.State.LastBoilerDataUtc = Now.AddMinutes(-45);
        _entities[Phase5DoseService.ControllerClockEntityId] = new EntityState { State = "unavailable" };

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
        _entities[Phase5DoseService.OutdoorEntityId].LastUpdated = Now.AddMinutes(-58);

        await Run();

        Assert.True(_repo.Switch.Enabled);
        Assert.True(_repo.State!.AutomatActive);
    }

    [Fact]
    public async Task BoilerBelowSetpointMinus10ForAnHour_SelfDisables()
    {
        _repo.Switch.Enabled = true;
        _repo.State = ActiveState(saved: (7, 160, 35));
        foreach (var s in _repo.Samples.Where(s => s.TimestampUtc > Now.AddHours(-1))) s.BoilerTemp = 55;
        SetEnvironment(outdoor: -5.0, boiler: 55);

        await Run();

        Assert.False(_repo.Switch.Enabled);
        Assert.Contains(_messages, m => m.Message.Contains("poniżej nastawy − 10 K"));
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
        Assert.DoesNotContain(_controller.Writes, w => w.Value > 10 && w.Entity == DoseControllerWriter.WorkEntityId);
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

    // --- Helpers ---

    private Task<PhaseResult> Run(HeatingParameters? p = null)
        => _service.ExecuteAsync(new HeatingState { CurrentTime = Now }, p ?? new HeatingParameters());

    private DoseState ActiveState((int W, int P, int B) saved) => new()
    {
        AutomatActive = true, SavedWork = saved.W, SavedPause = saved.P, SavedBlower = saved.B,
        LastKCorrectionDate = Now.Date.AddDays(-1), LastChangeUtc = Now.AddHours(-3),
        LastMapperOkUtc = Now.AddMinutes(-5), LastBoilerDataUtc = Now.AddMinutes(-5), LastOutdoorDataUtc = Now.AddMinutes(-5)
    };

    private void SetEnvironment(double outdoor, double flue = 140, bool summer = false, bool auto = true, double boiler = 66)
    {
        EntityState S(string v, DateTime? updated = null) => new() { State = v, LastUpdated = updated ?? Now.AddSeconds(-20) };
        _entities[Phase5DoseService.OutdoorEntityId] = S(outdoor.ToString(System.Globalization.CultureInfo.InvariantCulture), Now.AddMinutes(-10));
        foreach (var id in Phase5DoseService.IndoorEntityIds) _entities[id] = S("24.0");
        _entities[Phase5DoseService.BoilerTempEntityId] = S(boiler.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _entities[Phase5DoseService.BoilerSetpointEntityId] = S("67.0");
        _entities[Phase5DoseService.FlueTempEntityId] = S(flue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _entities[Phase5DoseService.BlowerActualEntityId] = S("35.0");
        _entities[Phase5DoseService.FeederCounterEntityId] = S("30m,034s");
        _entities[Phase5DoseService.SummerSwitchEntityId] = S(summer ? "on" : "off");
        _entities[Phase5DoseService.ModeSwitchEntityId] = S(auto ? "on" : "off");
        _entities[Phase5DoseService.ControllerClockEntityId] = S("2026-11-20T11:59:30+00:00");
        _entities[Phase5DoseService.FlueSafetyAutomationEntityId] = new EntityState
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
            Settings = entity switch
            {
                DoseControllerWriter.WorkEntityId => Settings with { FeederWork = value },
                DoseControllerWriter.PauseEntityId => Settings with { FeederPause = value },
                DoseControllerWriter.BlowerEntityId => Settings with { Blower = value },
                _ => throw new InvalidOperationException($"Zapis do niedozwolonej encji {entity}")
            };
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
    }
}
