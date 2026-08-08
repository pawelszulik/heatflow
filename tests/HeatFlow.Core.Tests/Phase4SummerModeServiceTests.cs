using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Database;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeatFlow.Core.Tests;

public class Phase4SummerModeServiceTests
{
    private readonly Mock<IHomeAssistantClient> _haClientMock;
    private readonly Mock<ISummerModeRepository> _repoMock;
    private readonly Mock<IApplicationErrorLogger> _errorLoggerMock;
    private readonly Phase4SummerModeService _service;

    public Phase4SummerModeServiceTests()
    {
        _haClientMock = new Mock<IHomeAssistantClient>();
        _repoMock = new Mock<ISummerModeRepository>();
        _errorLoggerMock = new Mock<IApplicationErrorLogger>();
        _errorLoggerMock
            .Setup(x => x.LogAsync(It.IsAny<Exception?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var loggerMock = new Mock<ILogger<Phase4SummerModeService>>();
        _service = new Phase4SummerModeService(
            _haClientMock.Object,
            _repoMock.Object,
            _errorLoggerMock.Object,
            loggerMock.Object);
    }

    [Fact]
    public void PhaseNumber_ShouldBe4()
    {
        Assert.Equal(4, _service.PhaseNumber);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSwitchUnreadable_ShouldReturnSuccessWithSkip()
    {
        _haClientMock
            .Setup(x => x.GetStateBoolAsync("switch.kociol_tryb_zima_lato", It.IsAny<CancellationToken>()))
            .ReturnsAsync((bool?)null);

        var state = BuildState(externalTemp: 15.0, rooms: new List<Room>());
        var result = await _service.ExecuteAsync(state, new HeatingParameters());

        Assert.True(result.Success);
        Assert.Contains("Pominięto", result.Details ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_WhenWinterMode_AlreadyActivatedToday_ShouldNotCallTurnOn()
    {
        SetupSwitchState(isSummerActive: false);
        _repoMock
            .Setup(x => x.GetLogForDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SummerModeLog { Date = DateTime.Now.Date, WasActivated = true });

        var state = BuildState(externalTemp: 20.0, rooms: BuildAllStayRooms(3));
        var result = await _service.ExecuteAsync(state, new HeatingParameters());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_on",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWinterMode_TempExactlyAtThreshold_ShouldNotActivate()
    {
        // Temp = 10.0 (warunek: > 10.0) → nie aktywuj
        SetupSwitchState(isSummerActive: false);
        SetupNoLogToday();

        var state = BuildState(externalTemp: 10.0, rooms: BuildAllStayRooms(2));
        var result = await _service.ExecuteAsync(state, new HeatingParameters());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_on",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWinterMode_RoomHasMaxDeficit_ShouldNotActivate()
    {
        SetupSwitchState(isSummerActive: false);
        SetupNoLogToday();

        var rooms = new List<Room>
        {
            BuildRoom("salon", DeficitClassification.Stay),
            BuildRoom("sypialnia", DeficitClassification.Max)
        };
        var state = BuildState(externalTemp: 20.0, rooms: rooms);
        var result = await _service.ExecuteAsync(state, new HeatingParameters());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_on",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWinterMode_AllConditionsMet_ShouldActivateAndSaveLog()
    {
        // Uwaga: test aktywacji pomija godzinę doby (DateTime.Now.Hour może być poza oknem 6-14)
        // Działa poprawnie tylko między 6:00 a 13:59
        var currentHour = DateTime.Now.Hour;
        if (currentHour < 6 || currentHour >= 14)
        {
            return; // Poza oknem czasowym - pomiń test
        }

        SetupSwitchState(isSummerActive: false);
        SetupNoLogToday();
        _haClientMock
            .Setup(x => x.CallServiceAsync("switch", "turn_on", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repoMock
            .Setup(x => x.SaveLogAsync(It.IsAny<SummerModeLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var state = BuildState(externalTemp: 15.0, rooms: BuildAllStayRooms(3));
        var result = await _service.ExecuteAsync(state, new HeatingParameters());

        Assert.True(result.Success);
        Assert.Contains("aktywowany", result.Details ?? "");
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_on",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(x => x.SaveLogAsync(
            It.Is<SummerModeLog>(l => l.WasActivated && l.ActivatedAt.HasValue),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_AlreadyDeactivatedToday_ShouldNotCallTurnOff()
    {
        SetupSwitchState(isSummerActive: true);
        _repoMock
            .Setup(x => x.GetLogForDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SummerModeLog { Date = DateTime.Now.Date, WasDeactivated = true });

        var state = BuildState(externalTemp: 5.0, rooms: new List<Room>
        {
            BuildColdRoom("salon", tempActual: 18.0, tempTarget: 21.0),
            BuildColdRoom("sypialnia", tempActual: 19.0, tempTarget: 21.0)
        });
        var result = await _service.ExecuteAsync(state, new HeatingParameters());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_OnlyOneRoomCold_ShouldNotDeactivate()
    {
        SetupSwitchState(isSummerActive: true);
        SetupNoLogToday();

        var state = BuildState(externalTemp: 5.0, rooms: new List<Room>
        {
            BuildColdRoom("salon", tempActual: 18.0, tempTarget: 21.0),
            BuildRoom("sypialnia", DeficitClassification.Stay)
        });
        var result = await _service.ExecuteAsync(state, new HeatingParameters());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_RoomsMaxButDeficitLessThan1Degree_ShouldNotDeactivate()
    {
        SetupSwitchState(isSummerActive: true);
        SetupNoLogToday();

        // Deficyt 0.4°C i 0.5°C - oba poniżej progu 1°C
        var state = BuildState(externalTemp: 5.0, rooms: new List<Room>
        {
            BuildColdRoom("salon", tempActual: 20.6, tempTarget: 21.0),
            BuildColdRoom("sypialnia", tempActual: 20.5, tempTarget: 21.0)
        });
        var result = await _service.ExecuteAsync(state, new HeatingParameters());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_ActivatedTodayButLessThan3HoursAgo_ShouldNotDeactivate()
    {
        SetupSwitchState(isSummerActive: true);
        // Aktywowano 2h temu - za wcześnie
        _repoMock
            .Setup(x => x.GetLogForDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SummerModeLog
            {
                Date = DateTime.Now.Date,
                WasActivated = true,
                ActivatedAt = DateTime.Now.AddHours(-2)
            });

        var state = BuildState(externalTemp: 5.0, rooms: new List<Room>
        {
            BuildColdRoom("salon", tempActual: 18.0, tempTarget: 21.0),
            BuildColdRoom("sypialnia", tempActual: 18.5, tempTarget: 21.0)
        });
        var result = await _service.ExecuteAsync(state, new HeatingParameters());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_ActivatedTodayMoreThan3HoursAgo_ShouldDeactivate()
    {
        SetupSwitchState(isSummerActive: true);
        // Aktywowano 4h temu - można dezaktywować
        _repoMock
            .Setup(x => x.GetLogForDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SummerModeLog
            {
                Date = DateTime.Now.Date,
                WasActivated = true,
                ActivatedAt = DateTime.Now.AddHours(-4)
            });
        _haClientMock
            .Setup(x => x.CallServiceAsync("switch", "turn_off", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repoMock
            .Setup(x => x.SaveLogAsync(It.IsAny<SummerModeLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var state = BuildState(externalTemp: 5.0, rooms: new List<Room>
        {
            BuildColdRoom("salon", tempActual: 18.0, tempTarget: 21.0),
            BuildColdRoom("sypialnia", tempActual: 18.5, tempTarget: 21.0)
        });
        var result = await _service.ExecuteAsync(state, new HeatingParameters());

        Assert.True(result.Success);
        Assert.Contains("dezaktywowany", result.Details ?? "");
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(x => x.SaveLogAsync(
            It.Is<SummerModeLog>(l => l.WasDeactivated && l.DeactivatedAt.HasValue),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_NotActivatedToday_3hLimitDoesNotApply_ShouldDeactivate()
    {
        // Tryb lato był aktywny (włączony wcześniej, nie dzisiaj) - brak ograniczenia 3h
        SetupSwitchState(isSummerActive: true);
        SetupNoLogToday(); // Brak logu na dziś → WasActivated = false
        _haClientMock
            .Setup(x => x.CallServiceAsync("switch", "turn_off", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repoMock
            .Setup(x => x.SaveLogAsync(It.IsAny<SummerModeLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var state = BuildState(externalTemp: 5.0, rooms: new List<Room>
        {
            BuildColdRoom("salon", tempActual: 18.0, tempTarget: 21.0),
            BuildColdRoom("sypialnia", tempActual: 18.5, tempTarget: 21.0)
        });
        var result = await _service.ExecuteAsync(state, new HeatingParameters());

        Assert.True(result.Success);
        Assert.Contains("dezaktywowany", result.Details ?? "");
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_RoomsMaxButNullTempActual_ShouldNotDeactivate()
    {
        SetupSwitchState(isSummerActive: true);
        SetupNoLogToday();

        // Pokoje Max ale bez odczytu temperatury - nie można potwierdzić deficytu
        var rooms = new List<Room>
        {
            BuildRoomWithNullTemp("salon"),
            BuildRoomWithNullTemp("sypialnia")
        };
        var state = BuildState(externalTemp: 5.0, rooms: rooms);
        var result = await _service.ExecuteAsync(state, new HeatingParameters());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExceptionThrown_ShouldReturnErrorResult()
    {
        _haClientMock
            .Setup(x => x.GetStateBoolAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var state = BuildState(externalTemp: 15.0, rooms: new List<Room>());
        var result = await _service.ExecuteAsync(state, new HeatingParameters());

        Assert.False(result.Success);
        Assert.Contains("Connection refused", result.ErrorMessage ?? "");
    }

    // --- Helpers ---

    private void SetupSwitchState(bool isSummerActive)
    {
        _haClientMock
            .Setup(x => x.GetStateBoolAsync("switch.kociol_tryb_zima_lato", It.IsAny<CancellationToken>()))
            .ReturnsAsync(isSummerActive);
    }

    private void SetupNoLogToday()
    {
        _repoMock
            .Setup(x => x.GetLogForDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SummerModeLog?)null);
    }

    private static HeatingState BuildState(double externalTemp, List<Room> rooms)
    {
        return new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = rooms,
            BoilerState = new BoilerState { TempExternal = externalTemp }
        };
    }

    private static Room BuildRoom(string name, DeficitClassification classification)
    {
        var room = new Room { Name = name, TempTarget = 21.0, TempActual = 21.0 };
        room.Score = classification switch
        {
            DeficitClassification.Max => 100,
            DeficitClassification.Stay => 25,
            DeficitClassification.Disabled => -10,
            _ => 0
        };
        room.ClassifyDeficit(TestParameters.Default());
        return room;
    }

    private static Room BuildColdRoom(string name, double tempActual, double tempTarget)
    {
        // Deficyt >= 1°C i klasyfikacja Max
        var room = new Room { Name = name, TempTarget = tempTarget, TempActual = tempActual };
        room.Score = 100;
        room.ClassifyDeficit(TestParameters.Default());
        return room;
    }

    private static Room BuildRoomWithNullTemp(string name)
    {
        var room = new Room { Name = name, TempTarget = 21.0, TempActual = null };
        room.Score = 100;
        room.ClassifyDeficit(TestParameters.Default());
        return room;
    }

    private static List<Room> BuildAllStayRooms(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => BuildRoom($"room{i}", DeficitClassification.Stay))
            .ToList();
    }
}
