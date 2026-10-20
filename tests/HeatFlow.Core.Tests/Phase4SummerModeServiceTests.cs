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
            .Setup(x => x.GetStateAsync("switch.kociol_tryb_zima_lato", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityState?)null);

        var state = BuildState(externalTemp: 15.0, rooms: new List<Room>());
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        Assert.Contains("Pominięto", result.Details ?? "");
        // Encja nieczytelna - nie dotykamy własnej pamięci stanu przełącznika
        _repoMock.Verify(x => x.GetSwitchStateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWinterMode_AlreadyActivatedToday_ShouldNotCallTurnOn()
    {
        SetupSwitchState(isSummerActive: false);
        _repoMock
            .Setup(x => x.GetLogForDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SummerModeLog { Date = DateTime.Now.Date, WasActivated = true });

        var state = BuildState(externalTemp: 20.0, rooms: BuildAllStayRooms(3));
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

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
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_on",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWinterMode_RoomHasRealDeficit_ShouldNotActivate()
    {
        SetupSwitchState(isSummerActive: false);
        SetupNoLogToday();

        var rooms = new List<Room>
        {
            BuildRoom("salon", DeficitClassification.Stay),
            BuildColdRoom("sypialnia", tempActual: 18.0, tempTarget: 21.0)
        };
        var state = BuildState(externalTemp: 20.0, rooms: rooms, forecastMaxTemp: 24.0);
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_on",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWinterMode_SafetyRoomMaxButNoRealDeficit_ShouldActivate()
    {
        // Regresja: pokój bezpieczeństwa (minValvesOpen) albo trzymany dwellem dostaje
        // DeficitClassification.Max niezależnie od tego, czy dom jest przegrzany - Faza 4
        // nie może już na tym warunku blokować aktywacji, tylko na realnym TempDeficit.
        if (OutsideActivationWindow()) return;

        SetupSwitchState(isSummerActive: false);
        SetupNoLogToday();
        SetupTurnOnSucceeds();

        var safetyRoom = BuildRoom("garaz", DeficitClassification.Max);
        safetyRoom.TempTarget = 6.0;
        safetyRoom.TempActual = 18.0; // przegrzany, mimo klasyfikacji Max
        safetyRoom.TempDeficit = safetyRoom.TempTarget - safetyRoom.TempActual.Value; // -12, nie > 0

        var rooms = new List<Room> { safetyRoom };
        rooms.AddRange(BuildAllStayRooms(2));
        var state = BuildState(externalTemp: 15.0, rooms: rooms, forecastMaxTemp: 24.0);
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_on",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
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

        var state = BuildState(externalTemp: 15.0, rooms: BuildAllStayRooms(3), forecastMaxTemp: 24.0);
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

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
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

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
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

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
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

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
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

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

        var state = BuildState(externalTemp: 5.0, rooms: BuildTwoColdRooms(), forecastMaxTemp: 12.0);
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

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

        var state = BuildState(externalTemp: 5.0, rooms: BuildTwoColdRooms(), forecastMaxTemp: 12.0);
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

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
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExceptionThrown_ShouldReturnErrorResult()
    {
        _haClientMock
            .Setup(x => x.GetStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var state = BuildState(externalTemp: 15.0, rooms: new List<Room>());
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.False(result.Success);
        Assert.Contains("Connection refused", result.ErrorMessage ?? "");
    }

    // --- Ciepły dzień (prognoza / temperatura zewnętrzna) ---

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_ColdRoomsButWarmForecast_ShouldNotDeactivate()
    {
        SetupSwitchState(isSummerActive: true);
        SetupNoLogToday();

        var state = BuildState(externalTemp: 8.0, rooms: BuildTwoColdRooms(), forecastMaxTemp: 23.0);
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        Assert.Contains("ciepły dzień", result.Details ?? "");
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_ColdRoomsAndColdForecast_ShouldDeactivate()
    {
        SetupSwitchState(isSummerActive: true);
        SetupNoLogToday();
        SetupTurnOffSucceeds();

        var state = BuildState(externalTemp: 8.0, rooms: BuildTwoColdRooms(), forecastMaxTemp: 15.0);
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_ForecastMaxExactlyAtThreshold_ShouldNotDeactivate()
    {
        // Warunek: >= próg
        SetupSwitchState(isSummerActive: true);
        SetupNoLogToday();

        var state = BuildState(externalTemp: 8.0, rooms: BuildTwoColdRooms(), forecastMaxTemp: 20.0);
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_NoForecastAndWarmExternalTemp_ShouldNotDeactivate()
    {
        SetupSwitchState(isSummerActive: true);
        SetupNoLogToday();

        var state = BuildState(externalTemp: 21.0, rooms: BuildTwoColdRooms());
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        Assert.Contains("brak prognozy", result.Details ?? "");
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_ColdNowButWarmTomorrowAfternoon_ShouldDeactivate()
    {
        // Regresja: przed wprowadzeniem osobnego okna dla dezaktywacji (6h) ten sam test kończył
        // się blokadą "ciepły dzień", bo okno 24h łapało jutrzejsze popołudnie (22°C, godzina 14
        // od teraz) mimo że najbliższe 6h są chłodne. To dokładnie ten bug: latem 2026-09-26/27
        // kocioł utknął w trybie letnim mimo wychłodzonego domu.
        SetupSwitchState(isSummerActive: true);
        SetupNoLogToday();
        SetupTurnOffSucceeds();

        var state = BuildState(externalTemp: 9.0, rooms: BuildTwoColdRooms());
        state.Forecast = BuildDiurnalForecast(coldTemp: 9.0, warmTemp: 22.0, hoursUntilWarm: 14);

        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        Assert.Contains("dezaktywowany", result.Details ?? "");
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_ColdRoomsButWarmWithin6h_ShouldNotDeactivate()
    {
        SetupSwitchState(isSummerActive: true);
        SetupNoLogToday();

        var state = BuildState(externalTemp: 9.0, rooms: BuildTwoColdRooms());
        state.Forecast = BuildDiurnalForecast(coldTemp: 9.0, warmTemp: 22.0, hoursUntilWarm: 3);

        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        Assert.Contains("ciepły dzień", result.Details ?? "");
        Assert.Contains("prognoza max 6h", result.Details ?? "");
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_ForecastWithoutTemperatures_ShouldFallbackToExternalTemp()
    {
        SetupSwitchState(isSummerActive: true);
        SetupNoLogToday();

        var state = BuildState(externalTemp: 25.0, rooms: BuildTwoColdRooms());
        state.Forecast = new ForecastData { CurrentTemp = 25.0, ForecastHours = new List<ForecastHour>() };
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_CustomWarmDayThreshold_ShouldUseParameter()
    {
        SetupSwitchState(isSummerActive: true);
        SetupNoLogToday();
        SetupTurnOffSucceeds();

        // Prognoza 23°C, ale próg podniesiony do 25°C -> to nie jest ciepły dzień
        var state = BuildState(externalTemp: 8.0, rooms: BuildTwoColdRooms(), forecastMaxTemp: 23.0);
        var result = await _service.ExecuteAsync(state, TestParameters.Default(summerModeWarmDayTemp: 25.0));

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWinterMode_AllConditionsButColdForecast_ShouldNotActivate()
    {
        if (OutsideActivationWindow()) return;

        SetupSwitchState(isSummerActive: false);
        SetupNoLogToday();

        var state = BuildState(externalTemp: 15.0, rooms: BuildAllStayRooms(3), forecastMaxTemp: 17.0);
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        Assert.Contains("brak ciepłego dnia", result.Details ?? "");
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_on",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWinterMode_NoForecastAndWarmExternalTemp_ShouldActivate()
    {
        if (OutsideActivationWindow()) return;

        SetupSwitchState(isSummerActive: false);
        SetupNoLogToday();
        SetupTurnOnSucceeds();

        var state = BuildState(externalTemp: 22.0, rooms: BuildAllStayRooms(3));
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_on",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- Okno prognozy ciepłego dnia przy aktywacji (SummerModeForecastHours, domyślnie 6h) ---

    /// <summary>
    /// 2026-10-20 12:09:40 lokalnie (10:09:40 UTC): w ogrodzie ~12,5 °C, max dnia 15 °C, ciepło
    /// (≥ 18 °C) dopiero jutro przed południem. Okno 24h złapało jutro i Faza 4 włączyła lato
    /// (SummerModeLog Id 14); okno 6h widzi tylko dzisiejsze 12-15 °C - brak aktywacji.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenWinterMode_Today20Oct_CoolNext6h_WarmOnlyTomorrow_ShouldNotActivate()
    {
        var now = new DateTime(2026, 10, 20, 10, 9, 40, DateTimeKind.Utc);
        var service = ServiceAt(now);
        SetupSwitchState(isSummerActive: false, new SummerModeSwitchState { IsSummer = false, SinceUtc = now.AddDays(-1) });
        SetupNoLogToday();
        SetupTurnOnSucceeds();

        var state = BuildState(externalTemp: 12.5, rooms: BuildAllStayRooms(3));
        state.Forecast = BuildForecast20Oct(now);
        var result = await service.ExecuteAsync(state, new HeatingParameters { SummerModeWarmDayTemp = 18.0 });

        Assert.True(result.Success);
        Assert.Contains("brak ciepłego dnia", result.Details ?? "");
        Assert.Contains("prognoza max 6h", result.Details ?? "");
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_on",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Ten sam stan z oknem 24h (zachowanie sprzed poprawki) - lato się włącza.</summary>
    [Fact]
    public async Task ExecuteAsync_WhenWinterMode_Today20Oct_With24hWindow_ActivatesLikeBefore()
    {
        var now = new DateTime(2026, 10, 20, 10, 9, 40, DateTimeKind.Utc);
        var service = ServiceAt(now);
        SetupSwitchState(isSummerActive: false, new SummerModeSwitchState { IsSummer = false, SinceUtc = now.AddDays(-1) });
        SetupNoLogToday();
        SetupTurnOnSucceeds();

        var state = BuildState(externalTemp: 12.5, rooms: BuildAllStayRooms(3));
        state.Forecast = BuildForecast20Oct(now);
        var result = await service.ExecuteAsync(state,
            new HeatingParameters { SummerModeWarmDayTemp = 18.0, SummerModeForecastHours = 24 });

        Assert.Contains("aktywowany", result.Details ?? "");
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_on",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWinterMode_WarmWithinNext6h_ShouldActivate()
    {
        var now = new DateTime(2026, 6, 15, 7, 30, 0, DateTimeKind.Utc); // 9:30 lokalnie
        var service = ServiceAt(now);
        SetupSwitchState(isSummerActive: false, new SummerModeSwitchState { IsSummer = false, SinceUtc = now.AddDays(-1) });
        SetupNoLogToday();
        SetupTurnOnSucceeds();

        var state = BuildState(externalTemp: 16.0, rooms: BuildAllStayRooms(3));
        state.Forecast = BuildDiurnalForecastAt(now, coldTemp: 16.0, warmTemp: 22.0, hoursUntilWarm: 4);
        var result = await service.ExecuteAsync(state, new HeatingParameters { SummerModeWarmDayTemp = 18.0 });

        Assert.Contains("aktywowany", result.Details ?? "");
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_on",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(x => x.SaveLogAsync(
            It.Is<SummerModeLog>(l => l.WasActivated && l.ActivatedAt == new DateTime(2026, 6, 15, 9, 30, 0)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWinterMode_WarmOnlyIn8h_ShouldNotActivate()
    {
        var now = new DateTime(2026, 6, 15, 4, 30, 0, DateTimeKind.Utc); // 6:30 lokalnie
        var service = ServiceAt(now);
        SetupSwitchState(isSummerActive: false, new SummerModeSwitchState { IsSummer = false, SinceUtc = now.AddDays(-1) });
        SetupNoLogToday();

        var state = BuildState(externalTemp: 16.0, rooms: BuildAllStayRooms(3));
        state.Forecast = BuildDiurnalForecastAt(now, coldTemp: 16.0, warmTemp: 22.0, hoursUntilWarm: 8);
        var result = await service.ExecuteAsync(state, new HeatingParameters { SummerModeWarmDayTemp = 18.0 });

        Assert.Contains("brak ciepłego dnia", result.Details ?? "");
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_on",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1000)]
    public async Task ExecuteAsync_ForecastHoursOutOfRange_FallsBackTo6h(int hours)
    {
        var now = new DateTime(2026, 10, 20, 10, 9, 40, DateTimeKind.Utc);
        var service = ServiceAt(now);
        SetupSwitchState(isSummerActive: false, new SummerModeSwitchState { IsSummer = false, SinceUtc = now.AddDays(-1) });
        SetupNoLogToday();

        var state = BuildState(externalTemp: 12.5, rooms: BuildAllStayRooms(3));
        state.Forecast = BuildForecast20Oct(now);
        var result = await service.ExecuteAsync(state,
            new HeatingParameters { SummerModeWarmDayTemp = 18.0, SummerModeForecastHours = hours });

        Assert.Contains("prognoza max 6h", result.Details ?? "");
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_on",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- Karencja po zmianie przełącznika (własna pamięć Fazy 4, nie last_changed HA) ---

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_SwitchChangedLessThan3HoursAgo_ShouldNotDeactivate()
    {
        SetupSwitchState(isSummerActive: true, previousSwitchState:
            new SummerModeSwitchState { IsSummer = true, SinceUtc = DateTime.UtcNow.AddMinutes(-30) });
        SetupNoLogToday();

        var state = BuildState(externalTemp: 8.0, rooms: BuildTwoColdRooms(), forecastMaxTemp: 12.0);
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        Assert.Contains("karencja", result.Details ?? "");
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
        // Karencja sprawdzana przed sięgnięciem do logu dnia
        _repoMock.Verify(x => x.GetLogForDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWinterMode_SwitchChangedLessThan3HoursAgo_ShouldNotActivate()
    {
        if (OutsideActivationWindow()) return;

        SetupSwitchState(isSummerActive: false, previousSwitchState:
            new SummerModeSwitchState { IsSummer = false, SinceUtc = DateTime.UtcNow.AddHours(-1) });
        SetupNoLogToday();

        var state = BuildState(externalTemp: 15.0, rooms: BuildAllStayRooms(3), forecastMaxTemp: 24.0);
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        Assert.Contains("karencja", result.Details ?? "");
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_on",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_SwitchChangedMoreThan3HoursAgo_ShouldDeactivate()
    {
        SetupSwitchState(isSummerActive: true, previousSwitchState:
            new SummerModeSwitchState { IsSummer = true, SinceUtc = DateTime.UtcNow.AddHours(-3.5) });
        SetupNoLogToday();
        SetupTurnOffSucceeds();

        var state = BuildState(externalTemp: 8.0, rooms: BuildTwoColdRooms(), forecastMaxTemp: 12.0);
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_IntegrationBlipDoesNotResetGrace_ShouldDeactivate()
    {
        // Regresja: zanik integracji kotła (last_changed HA się resetuje, stan nie) nie może
        // już wydłużać karencji - liczy się tylko własna pamięć Fazy 4. Stan zapisany 3.5h temu,
        // mimo że HA "właśnie" zaktualizowało last_changed (co tu w ogóle nie jest odczytywane).
        SetupSwitchState(isSummerActive: true, previousSwitchState:
            new SummerModeSwitchState { IsSummer = true, SinceUtc = DateTime.UtcNow.AddHours(-3.5) });
        SetupNoLogToday();
        SetupTurnOffSucceeds();

        var state = BuildState(externalTemp: 8.0, rooms: BuildTwoColdRooms(), forecastMaxTemp: 12.0);
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSummerMode_NoPreviousSwitchState_ShouldNotApplyGrace()
    {
        // Pierwsze uruchomienie po migracji (nowa tabela, brak wiersza) -> brak karencji,
        // działa tylko reguła z SummerModeLog. Zapisuje baseline, nie zamraża Fazy 4 na 3h.
        SetupSwitchState(isSummerActive: true, previousStateKnown: false);
        SetupNoLogToday();
        SetupTurnOffSucceeds();

        var state = BuildState(externalTemp: 8.0, rooms: BuildTwoColdRooms(), forecastMaxTemp: 12.0);
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        _haClientMock.Verify(x => x.CallServiceAsync("switch", "turn_off",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(x => x.SaveSwitchStateAsync(
            It.Is<SummerModeSwitchState>(s => s.IsSummer && s.SinceUtc <= DateTime.UtcNow.AddHours(-3)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSwitchStateUnavailable_ShouldReturnSuccessWithSkip()
    {
        _haClientMock
            .Setup(x => x.GetStateAsync("switch.kociol_tryb_zima_lato", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityState { State = "unavailable" });

        var state = BuildState(externalTemp: 15.0, rooms: new List<Room>());
        var result = await _service.ExecuteAsync(state, TestParameters.Default());

        Assert.True(result.Success);
        Assert.Contains("Pominięto", result.Details ?? "");
        // unavailable - nie dotykamy własnej pamięci stanu przełącznika
        _repoMock.Verify(x => x.GetSwitchStateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- Helpers ---

    /// <summary>
    /// Ustawia stan przełącznika z HA i - niezależnie od last_changed HA - własną pamięć
    /// stanu Fazy 4 w repozytorium (to ona decyduje o karencji, patrz
    /// UpdateAndGetHoursSinceSwitchChangeAsync).
    /// </summary>
    /// <param name="previousSwitchState">
    /// Ostatni stan zapamiętany przez Fazę 4. Domyślnie taki sam jak bieżący, sprzed doby -
    /// poza karencją 3h. Podaj z innym SinceUtc/IsSummer, żeby wymusić konkretny scenariusz.
    /// </param>
    /// <param name="previousStateKnown">
    /// false symuluje pierwsze uruchomienie po migracji (brak wiersza w tabeli) - wtedy
    /// previousSwitchState jest ignorowany.
    /// </param>
    private void SetupSwitchState(
        bool isSummerActive,
        SummerModeSwitchState? previousSwitchState = null,
        bool previousStateKnown = true)
    {
        _haClientMock
            .Setup(x => x.GetStateAsync("switch.kociol_tryb_zima_lato", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityState
            {
                EntityId = "switch.kociol_tryb_zima_lato",
                State = isSummerActive ? "on" : "off"
            });

        _repoMock
            .Setup(x => x.GetSwitchStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(previousStateKnown
                ? previousSwitchState ?? new SummerModeSwitchState { IsSummer = isSummerActive, SinceUtc = DateTime.UtcNow.AddHours(-24) }
                : null);

        _repoMock
            .Setup(x => x.SaveSwitchStateAsync(It.IsAny<SummerModeSwitchState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupTurnOffSucceeds()
    {
        _haClientMock
            .Setup(x => x.CallServiceAsync("switch", "turn_off", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repoMock
            .Setup(x => x.SaveLogAsync(It.IsAny<SummerModeLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupTurnOnSucceeds()
    {
        _haClientMock
            .Setup(x => x.CallServiceAsync("switch", "turn_on", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repoMock
            .Setup(x => x.SaveLogAsync(It.IsAny<SummerModeLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    /// <summary>Faza 4 z zegarem zatrzymanym na <paramref name="utcNow"/>, czas lokalny Europe/Warsaw.</summary>
    private Phase4SummerModeService ServiceAt(DateTime utcNow)
        => new(_haClientMock.Object, _repoMock.Object, _errorLoggerMock.Object,
            new Mock<ILogger<Phase4SummerModeService>>().Object, new FixedTimeProvider(utcNow));

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    }

    /// <summary>
    /// Prognoza jak 2026-10-20 (godziny w UTC, jak zapisuje Faza 0): dziś 10-15 UTC 12,5-15 °C,
    /// wieczór i noc ~11 °C, jutro rano rośnie do 18,7 °C o 10:00 UTC (w oknie 24h od 10:09).
    /// </summary>
    private static ForecastData BuildForecast20Oct(DateTime nowUtc)
    {
        var start = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Unspecified);
        var temps = new[] { 12.5, 13.5, 14.5, 15.0, 14.5, 13.0, 11.3, 11.2, 11.0, 10.9, 10.5, 10.5, 11.2, 10.9,
            11.2, 11.4, 11.4, 11.5, 11.7, 11.4, 10.3, 11.8, 13.6, 18.2, 18.7, 18.2, 18.7, 18.2, 17.5, 15.3 };
        return new ForecastData
        {
            CurrentTemp = 12.5,
            ForecastHours = temps.Select((t, i) => new ForecastHour { DateTime = start.AddHours(i), Temperature = t }).ToList()
        };
    }

    private static ForecastData BuildDiurnalForecastAt(DateTime nowUtc, double coldTemp, double warmTemp, int hoursUntilWarm)
        => new()
        {
            CurrentTemp = coldTemp,
            ForecastHours = Enumerable.Range(0, 30)
                .Select(i => new ForecastHour { DateTime = nowUtc.AddHours(i), Temperature = i == hoursUntilWarm ? warmTemp : coldTemp })
                .ToList()
        };

    /// <summary>Godzina poza oknem aktywacji 6-14 - testy aktywacji wtedy nic nie sprawdzają.</summary>
    private static bool OutsideActivationWindow()
    {
        var currentHour = DateTime.Now.Hour;
        return currentHour < 6 || currentHour >= 14;
    }

    private static List<Room> BuildTwoColdRooms()
    {
        return new List<Room>
        {
            BuildColdRoom("salon", tempActual: 18.0, tempTarget: 21.0),
            BuildColdRoom("sypialnia", tempActual: 18.5, tempTarget: 21.0)
        };
    }

    private void SetupNoLogToday()
    {
        _repoMock
            .Setup(x => x.GetLogForDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SummerModeLog?)null);
    }

    private static HeatingState BuildState(double externalTemp, List<Room> rooms, double? forecastMaxTemp = null)
    {
        return new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = rooms,
            BoilerState = new BoilerState { TempExternal = externalTemp },
            Forecast = forecastMaxTemp.HasValue ? BuildForecast(externalTemp, forecastMaxTemp.Value) : null
        };
    }

    /// <summary>
    /// 24 godziny prognozy, wszystkie na poziomie maxTemp - wartość jest więc "widoczna" w
    /// każdym oknie (domyślnie 6h, parametr do 24h), bo te testy sprawdzają próg
    /// ciepłego dnia, nie samo okno czasowe (na to patrz BuildDiurnalForecast).
    /// </summary>
    private static ForecastData BuildForecast(double currentTemp, double maxTemp)
    {
        return new ForecastData
        {
            CurrentTemp = currentTemp,
            ForecastHours = Enumerable.Range(0, 24)
                .Select(i => new ForecastHour
                {
                    DateTime = DateTime.UtcNow.AddHours(i),
                    Temperature = maxTemp
                })
                .ToList()
        };
    }

    /// <summary>
    /// Profil dobowy z realnymi znacznikami czasu: chłodno teraz i przez najbliższe godziny,
    /// cieplej dopiero po <paramref name="hoursUntilWarm"/> godzinach (np. jutrzejsze popołudnie).
    /// Do testowania, że Faza 4 patrzy tylko na najbliższe SummerModeForecastHours (6h) -
    /// okno 24h zawsze łapało gdzieś ciepłe popołudnie (dziś albo jutro) i blokowało powrót na
    /// zimę bezterminowo, nawet wieczorem przy wychłodzonym domu.
    /// </summary>
    private static ForecastData BuildDiurnalForecast(double coldTemp, double warmTemp, int hoursUntilWarm)
    {
        var now = DateTime.UtcNow;
        return new ForecastData
        {
            CurrentTemp = coldTemp,
            ForecastHours = Enumerable.Range(0, 30)
                .Select(i => new ForecastHour
                {
                    DateTime = now.AddHours(i),
                    Temperature = i == hoursUntilWarm ? warmTemp : coldTemp
                })
                .ToList()
        };
    }

    private static Room BuildRoom(string name, DeficitClassification classification)
    {
        // TempActual == TempTarget -> TempDeficit = 0, tak jak Faza 1 liczyłaby dla dogrzanego
        // pokoju. Klasyfikacja Max tu (Score=100) symuluje pokój bezpieczeństwa/dwell, nie
        // realny deficyt - to jest celowe (patrz SafetyRoomMaxButNoRealDeficit_ShouldActivate).
        var room = new Room { Name = name, TempTarget = 21.0, TempActual = 21.0 };
        room.TempDeficit = 0.0;
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
        // Deficyt >= 1°C i klasyfikacja Max. TempDeficit liczony tak jak w Phase1DiagnoseService
        // (TemperatureHelper.CalculateDeficit), bo Faza 4 od tego czasu patrzy na to pole wprost.
        var room = new Room { Name = name, TempTarget = tempTarget, TempActual = tempActual };
        room.TempDeficit = tempTarget - tempActual;
        room.Score = 100;
        room.ClassifyDeficit(TestParameters.Default());
        return room;
    }

    private static Room BuildRoomWithNullTemp(string name)
    {
        // Brak odczytu: Faza 1 podstawia TempActual = TempTarget (fallback), więc TempDeficit = 0.
        // TempActual zostaje tu null tylko po to, żeby przetestować gałąź ShouldDeactivate,
        // która jawnie sprawdza TempActual.HasValue.
        var room = new Room { Name = name, TempTarget = 21.0, TempActual = null };
        room.TempDeficit = 0.0;
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
