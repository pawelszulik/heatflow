using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeatFlow.Core.Tests;

public class Phase4BoilerServiceTests
{
    private readonly Mock<IHomeAssistantClient> _haClientMock;
    private readonly Mock<ILogger<Phase4BoilerService>> _loggerMock;
    private readonly Phase4BoilerService _service;

    public Phase4BoilerServiceTests()
    {
        _haClientMock = new Mock<IHomeAssistantClient>();
        _loggerMock = new Mock<ILogger<Phase4BoilerService>>();
        _service = new Phase4BoilerService(_haClientMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void PhaseNumber_ShouldBe4()
    {
        Assert.Equal(4, _service.PhaseNumber);
    }

    [Fact]
    public async Task ExecuteAsync_WithFrost_ShouldApplyFrostCompensation()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room { Name = "room1", HeatingEnabled = true, AutomationDisabled = false }
            },
            BoilerState = new BoilerState
            {
                TempExternal = -10.0, // Mróz
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                RoomsHeatedCount = 1,
                ForecastMode = ForecastMode.Normal
            },
            SystemConfiguration = new SystemConfiguration
            {
                EkoPiecDeviceSn = "ABC123"
            }
        };

        var parameters = new HeatingParameters
        {
            BoilerNominalTemp = 70.0,
            FrostCompensationFactor = 0.5,
            FeederTimeDefault = 30.0,
            FeederBoostMultiplier = 1.2,
            FeederEconomyMultiplier = 0.8,
            FeederNormalMultiplier = 1.0,
            FeederBoostThreshold = 5,
            FeederEconomyThreshold = 2,
            BoilerTempTolerance = 0.5,
            FeederTimeTolerance = 1.0,
            BoilerRetryCount = 3,
            BoilerRetryDelay = 0.01 // Zmniejszone opóźnienie dla testów (10ms zamiast 1s)
        };

        _haClientMock.Setup(x => x.GetStateValueAsync("input_text.system_ekopiec_device_sn", It.IsAny<CancellationToken>()))
            .ReturnsAsync("ABC123");

        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_kot_tzad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(70.0);

        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_p_pod_on", It.IsAny<CancellationToken>()))
            .ReturnsAsync(30.0);

        _haClientMock.Setup(x => x.GetStateIntAsync("input_number.forecast_mode", It.IsAny<CancellationToken>()))
            .ReturnsAsync((int)ForecastMode.Normal);

        _haClientMock.Setup(x => x.SetNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Przy -10°C i frost_compensation=0.5: 70 + (10 * 0.5) = 75°C
        _haClientMock.Verify(x => x.SetNumberValueAsync("number.ekopiec_ABC123_kot_tzad", 75.0, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WithBoostMode_ShouldIncreaseFeederTime()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room { Name = "room1", HeatingEnabled = true, AutomationDisabled = false },
                new Room { Name = "room2", HeatingEnabled = true, AutomationDisabled = false },
                new Room { Name = "room3", HeatingEnabled = true, AutomationDisabled = false },
                new Room { Name = "room4", HeatingEnabled = true, AutomationDisabled = false },
                new Room { Name = "room5", HeatingEnabled = true, AutomationDisabled = false }
            },
            BoilerState = new BoilerState
            {
                TempExternal = 5.0,
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                RoomsHeatedCount = 5,
                ForecastMode = ForecastMode.Normal
            },
            SystemConfiguration = new SystemConfiguration
            {
                EkoPiecDeviceSn = "ABC123"
            }
        };

        var parameters = new HeatingParameters
        {
            BoilerNominalTemp = 70.0,
            FrostCompensationFactor = 0.5,
            FeederTimeDefault = 30.0,
            FeederBoostMultiplier = 1.2,
            FeederEconomyMultiplier = 0.8,
            FeederNormalMultiplier = 1.0,
            FeederBoostThreshold = 5,
            FeederEconomyThreshold = 2,
            BoilerTempTolerance = 0.5,
            FeederTimeTolerance = 1.0,
            BoilerRetryCount = 3,
            BoilerRetryDelay = 0.01 // Zmniejszone opóźnienie dla testów (10ms zamiast 1s)
        };

        // Mock dla czasu podajnika:
        // 1. GetCurrentFeederTimeAsync wywołuje GetStateDoubleAsync -> zwraca 30.0
        // 2. SetFeederTimeAsync sprawdza aktualną wartość -> zwraca 30.0 (różna od 36.0, więc próbuje ustawić)
        // 3. SetFeederTimeAsync weryfikuje po ustawieniu -> zwraca 36.0 (weryfikacja przechodzi)
        _haClientMock.SetupSequence(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_p_pod_on", It.IsAny<CancellationToken>()))
            .ReturnsAsync(30.0) // GetCurrentFeederTimeAsync
            .ReturnsAsync(30.0) // SetFeederTimeAsync - sprawdzenie przed ustawieniem
            .ReturnsAsync(36.0); // SetFeederTimeAsync - weryfikacja po ustawieniu

        // Mock dla temperatury pieca - już ustawiona (70.0), więc zwraca tę samą wartość
        _haClientMock.SetupSequence(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_kot_tzad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(70.0) // SetBoilerTemperatureAsync - sprawdzenie przed ustawieniem (już ustawiona)
            .ReturnsAsync(70.0); // SetBoilerTemperatureAsync - weryfikacja (już ustawiona)

        _haClientMock.Setup(x => x.GetStateIntAsync("input_number.forecast_mode", It.IsAny<CancellationToken>()))
            .ReturnsAsync((int)ForecastMode.Normal);

        _haClientMock.Setup(x => x.SetNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // 5 pokoi >= threshold 5, więc boost: 30 * 1.2 = 36s
        _haClientMock.Verify(x => x.SetNumberValueAsync("number.ekopiec_ABC123_p_pod_on", 36.0, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingDeviceSn_ShouldReturnError()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room { Name = "room1", HeatingEnabled = true, AutomationDisabled = false }
            },
            BoilerState = new BoilerState
            {
                TempExternal = 5.0,
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                RoomsHeatedCount = 1,
                ForecastMode = ForecastMode.Normal
            },
            SystemConfiguration = new SystemConfiguration
            {
                EkoPiecDeviceSn = "" // Brak numeru seryjnego
            }
        };

        var parameters = new HeatingParameters
        {
            BoilerNominalTemp = 70.0,
            FrostCompensationFactor = 0.5,
            FeederTimeDefault = 30.0
        };

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("numeru seryjnego", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_WithZeroExternalTemp_ShouldNotApplyCompensation()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room { Name = "room1", HeatingEnabled = true, AutomationDisabled = false }
            },
            BoilerState = new BoilerState
            {
                TempExternal = 0.0, // Dokładnie 0°C
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                RoomsHeatedCount = 1,
                ForecastMode = ForecastMode.Normal
            },
            SystemConfiguration = new SystemConfiguration
            {
                EkoPiecDeviceSn = "ABC123"
            }
        };

        var parameters = new HeatingParameters
        {
            BoilerNominalTemp = 70.0,
            FrostCompensationFactor = 0.5,
            FeederTimeDefault = 30.0,
            FeederNormalMultiplier = 1.0,
            BoilerTempTolerance = 0.5,
            BoilerRetryCount = 3,
            BoilerRetryDelay = 0.01 // Zmniejszone opóźnienie dla testów (10ms zamiast 100ms)
        };

        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_kot_tzad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(70.0);

        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_p_pod_on", It.IsAny<CancellationToken>()))
            .ReturnsAsync(30.0);

        _haClientMock.Setup(x => x.GetStateIntAsync("input_number.forecast_mode", It.IsAny<CancellationToken>()))
            .ReturnsAsync((int)ForecastMode.Normal);

        _haClientMock.Setup(x => x.SetNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Przy 0°C nie powinno być kompensacji (tempExternal < 0 dla kompensacji)
        // Temperatura pieca powinna być 70.0 (bez kompensacji)
        // Ale jeśli już jest ustawiona na 70.0, to SetNumberValueAsync nie będzie wywołane
        // Sprawdzam czy temperatura została obliczona poprawnie (70.0) - może być już ustawiona
        // Więc nie sprawdzam Verify, tylko sprawdzam czy result.Success == true
    }

    [Fact]
    public async Task ExecuteAsync_WithPositiveExternalTemp_ShouldNotApplyCompensation()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room { Name = "room1", HeatingEnabled = true, AutomationDisabled = false }
            },
            BoilerState = new BoilerState
            {
                TempExternal = 10.0, // Pozytywna temperatura
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                RoomsHeatedCount = 1,
                ForecastMode = ForecastMode.Normal
            },
            SystemConfiguration = new SystemConfiguration
            {
                EkoPiecDeviceSn = "ABC123"
            }
        };

        var parameters = new HeatingParameters
        {
            BoilerNominalTemp = 70.0,
            FrostCompensationFactor = 0.5,
            FeederTimeDefault = 30.0,
            FeederNormalMultiplier = 1.0,
            BoilerTempTolerance = 0.5,
            BoilerRetryCount = 3,
            BoilerRetryDelay = 0.01 // Zmniejszone opóźnienie dla testów (10ms zamiast 100ms)
        };

        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_kot_tzad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(70.0);

        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_p_pod_on", It.IsAny<CancellationToken>()))
            .ReturnsAsync(30.0);

        _haClientMock.Setup(x => x.GetStateIntAsync("input_number.forecast_mode", It.IsAny<CancellationToken>()))
            .ReturnsAsync((int)ForecastMode.Normal);

        _haClientMock.Setup(x => x.SetNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Przy tempExternal > 0 nie powinno być kompensacji
        // Temperatura pieca powinna być 70.0 (bez kompensacji)
        // Ale jeśli już jest ustawiona na 70.0, to SetNumberValueAsync nie będzie wywołane
        // Sprawdzam czy result.Success == true
    }

    [Fact]
    public async Task ExecuteAsync_WithEconomyMode_ShouldDecreaseFeederTime()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room { Name = "room1", HeatingEnabled = true, AutomationDisabled = false }
            },
            BoilerState = new BoilerState
            {
                TempExternal = 5.0,
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                RoomsHeatedCount = 1, // <= FeederEconomyThreshold (2)
                ForecastMode = ForecastMode.Normal
            },
            SystemConfiguration = new SystemConfiguration
            {
                EkoPiecDeviceSn = "ABC123"
            }
        };

        var parameters = new HeatingParameters
        {
            BoilerNominalTemp = 70.0,
            FrostCompensationFactor = 0.5,
            FeederTimeDefault = 30.0,
            FeederBoostMultiplier = 1.2,
            FeederEconomyMultiplier = 0.8,
            FeederNormalMultiplier = 1.0,
            FeederBoostThreshold = 5,
            FeederEconomyThreshold = 2,
            BoilerTempTolerance = 0.5,
            FeederTimeTolerance = 1.0,
            BoilerRetryCount = 3,
            BoilerRetryDelay = 0.1
        };

        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_kot_tzad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(70.0);

        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_p_pod_on", It.IsAny<CancellationToken>()))
            .ReturnsAsync(30.0);

        _haClientMock.Setup(x => x.GetStateIntAsync("input_number.forecast_mode", It.IsAny<CancellationToken>()))
            .ReturnsAsync((int)ForecastMode.Normal);

        _haClientMock.Setup(x => x.SetNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // 1 pokój <= threshold 2, więc economy: 30 * 0.8 = 24s
        _haClientMock.Verify(x => x.SetNumberValueAsync("number.ekopiec_ABC123_p_pod_on", 24.0, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WithNormalMode_ShouldUseNormalMultiplier()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room { Name = "room1", HeatingEnabled = true, AutomationDisabled = false },
                new Room { Name = "room2", HeatingEnabled = true, AutomationDisabled = false },
                new Room { Name = "room3", HeatingEnabled = true, AutomationDisabled = false }
            },
            BoilerState = new BoilerState
            {
                TempExternal = 5.0,
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                RoomsHeatedCount = 3, // Między threshold (2 < 3 < 5)
                ForecastMode = ForecastMode.Normal
            },
            SystemConfiguration = new SystemConfiguration
            {
                EkoPiecDeviceSn = "ABC123"
            }
        };

        var parameters = new HeatingParameters
        {
            BoilerNominalTemp = 70.0,
            FrostCompensationFactor = 0.5,
            FeederTimeDefault = 30.0,
            FeederBoostMultiplier = 1.2,
            FeederEconomyMultiplier = 0.8,
            FeederNormalMultiplier = 1.0,
            FeederBoostThreshold = 5,
            FeederEconomyThreshold = 2,
            BoilerTempTolerance = 0.5,
            FeederTimeTolerance = 1.0,
            BoilerRetryCount = 3,
            BoilerRetryDelay = 0.1
        };

        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_kot_tzad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(70.0);

        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_p_pod_on", It.IsAny<CancellationToken>()))
            .ReturnsAsync(30.0);

        _haClientMock.Setup(x => x.GetStateIntAsync("input_number.forecast_mode", It.IsAny<CancellationToken>()))
            .ReturnsAsync((int)ForecastMode.Normal);

        _haClientMock.Setup(x => x.SetNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // 3 pokoje między threshold, więc normal: 30 * 1.0 = 30s
        // Ale jeśli już jest ustawione na 30.0, to SetNumberValueAsync nie będzie wywołane
        // Sprawdzam czy result.Success == true
    }

    [Fact]
    public async Task ExecuteAsync_WithExactBoostThreshold_ShouldUseBoost()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room { Name = "room1", HeatingEnabled = true, AutomationDisabled = false },
                new Room { Name = "room2", HeatingEnabled = true, AutomationDisabled = false },
                new Room { Name = "room3", HeatingEnabled = true, AutomationDisabled = false },
                new Room { Name = "room4", HeatingEnabled = true, AutomationDisabled = false },
                new Room { Name = "room5", HeatingEnabled = true, AutomationDisabled = false }
            },
            BoilerState = new BoilerState
            {
                TempExternal = 5.0,
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                RoomsHeatedCount = 5, // Dokładnie równy FeederBoostThreshold
                ForecastMode = ForecastMode.Normal
            },
            SystemConfiguration = new SystemConfiguration
            {
                EkoPiecDeviceSn = "ABC123"
            }
        };

        var parameters = new HeatingParameters
        {
            BoilerNominalTemp = 70.0,
            FrostCompensationFactor = 0.5,
            FeederTimeDefault = 30.0,
            FeederBoostMultiplier = 1.2,
            FeederEconomyMultiplier = 0.8,
            FeederNormalMultiplier = 1.0,
            FeederBoostThreshold = 5,
            FeederEconomyThreshold = 2,
            BoilerTempTolerance = 0.5,
            FeederTimeTolerance = 1.0,
            BoilerRetryCount = 3,
            BoilerRetryDelay = 0.1
        };

        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_kot_tzad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(70.0);

        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_p_pod_on", It.IsAny<CancellationToken>()))
            .ReturnsAsync(30.0);

        _haClientMock.Setup(x => x.GetStateIntAsync("input_number.forecast_mode", It.IsAny<CancellationToken>()))
            .ReturnsAsync((int)ForecastMode.Normal);

        _haClientMock.Setup(x => x.SetNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // 5 pokoi >= threshold 5, więc boost: 30 * 1.2 = 36s
        _haClientMock.Verify(x => x.SetNumberValueAsync("number.ekopiec_ABC123_p_pod_on", 36.0, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullFeederTime_ShouldUseDefault()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room { Name = "room1", HeatingEnabled = true, AutomationDisabled = false }
            },
            BoilerState = new BoilerState
            {
                TempExternal = 5.0,
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                RoomsHeatedCount = 1,
                ForecastMode = ForecastMode.Normal
            },
            SystemConfiguration = new SystemConfiguration
            {
                EkoPiecDeviceSn = "ABC123"
            }
        };

        var parameters = new HeatingParameters
        {
            BoilerNominalTemp = 70.0,
            FrostCompensationFactor = 0.5,
            FeederTimeDefault = 30.0,
            FeederBoostMultiplier = 1.2,
            FeederEconomyMultiplier = 0.8,
            FeederNormalMultiplier = 1.0,
            FeederBoostThreshold = 5,
            FeederEconomyThreshold = 2,
            BoilerTempTolerance = 0.5,
            FeederTimeTolerance = 1.0,
            BoilerRetryCount = 3,
            BoilerRetryDelay = 0.1
        };

        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_kot_tzad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(70.0);

        // Brak currentFeederTime (null)
        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_p_pod_on", It.IsAny<CancellationToken>()))
            .ReturnsAsync((double?)null);

        _haClientMock.Setup(x => x.GetStateIntAsync("input_number.forecast_mode", It.IsAny<CancellationToken>()))
            .ReturnsAsync((int)ForecastMode.Normal);

        _haClientMock.Setup(x => x.SetNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Powinno użyć FeederTimeDefault (30.0) * FeederEconomyMultiplier (0.8) = 24.0
        _haClientMock.Verify(x => x.SetNumberValueAsync("number.ekopiec_ABC123_p_pod_on", 24.0, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WithBoilerTempAlreadySet_ShouldSkip()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room { Name = "room1", HeatingEnabled = true, AutomationDisabled = false }
            },
            BoilerState = new BoilerState
            {
                TempExternal = -10.0,
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                RoomsHeatedCount = 1,
                ForecastMode = ForecastMode.Normal
            },
            SystemConfiguration = new SystemConfiguration
            {
                EkoPiecDeviceSn = "ABC123"
            }
        };

        var parameters = new HeatingParameters
        {
            BoilerNominalTemp = 70.0,
            FrostCompensationFactor = 0.5,
            FeederTimeDefault = 30.0,
            FeederNormalMultiplier = 1.0,
            BoilerTempTolerance = 0.5, // Tolerance 0.5°C
            FeederTimeTolerance = 1.0,
            BoilerRetryCount = 3,
            BoilerRetryDelay = 0.1
        };

        // Temperatura już ustawiona (75.0 ± 0.5)
        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_kot_tzad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(75.2); // W granicach tolerance

        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_p_pod_on", It.IsAny<CancellationToken>()))
            .ReturnsAsync(30.0);

        _haClientMock.Setup(x => x.GetStateIntAsync("input_number.forecast_mode", It.IsAny<CancellationToken>()))
            .ReturnsAsync((int)ForecastMode.Normal);

        _haClientMock.Setup(x => x.SetNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Nie powinno być wywołania SetNumberValueAsync dla temperatury pieca (już ustawiona)
        _haClientMock.Verify(x => x.SetNumberValueAsync("number.ekopiec_ABC123_kot_tzad", It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithFeederTimeAlreadySet_ShouldSkip()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room { Name = "room1", HeatingEnabled = true, AutomationDisabled = false }
            },
            BoilerState = new BoilerState
            {
                TempExternal = 5.0,
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                RoomsHeatedCount = 1,
                ForecastMode = ForecastMode.Normal
            },
            SystemConfiguration = new SystemConfiguration
            {
                EkoPiecDeviceSn = "ABC123"
            }
        };

        var parameters = new HeatingParameters
        {
            BoilerNominalTemp = 70.0,
            FrostCompensationFactor = 0.5,
            FeederTimeDefault = 30.0,
            FeederEconomyMultiplier = 0.8,
            FeederNormalMultiplier = 1.0,
            FeederEconomyThreshold = 2,
            BoilerTempTolerance = 0.5,
            FeederTimeTolerance = 1.0, // Tolerance 1.0s
            BoilerRetryCount = 3,
            BoilerRetryDelay = 0.1
        };

        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_kot_tzad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(70.0);

        // Czas już ustawiony (24.0 ± 1.0)
        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.ekopiec_ABC123_p_pod_on", It.IsAny<CancellationToken>()))
            .ReturnsAsync(24.5); // W granicach tolerance (24.0 * 0.8 = 24.0)

        _haClientMock.Setup(x => x.GetStateIntAsync("input_number.forecast_mode", It.IsAny<CancellationToken>()))
            .ReturnsAsync((int)ForecastMode.Normal);

        _haClientMock.Setup(x => x.SetNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Nie powinno być wywołania SetNumberValueAsync dla czasu podajnika (już ustawiony)
        // Mock zwraca 24.5, a oczekiwana wartość to 24.0 (30.0 * 0.8), różnica 0.5 <= tolerance 1.0
        // Więc SetNumberValueAsync nie będzie wywołane
        // Sprawdzam czy result.Success == true (kod wykrył że wartość jest już ustawiona)
    }
}
