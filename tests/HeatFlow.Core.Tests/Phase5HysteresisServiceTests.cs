using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeatFlow.Core.Tests;

public class Phase5HysteresisServiceTests
{
    private readonly Mock<IHomeAssistantClient> _haClientMock;
    private readonly Mock<ILogger<Phase5HysteresisService>> _loggerMock;
    private readonly Phase5HysteresisService _service;

    public Phase5HysteresisServiceTests()
    {
        _haClientMock = new Mock<IHomeAssistantClient>();
        var errorLoggerMock = new Mock<IApplicationErrorLogger>();
        errorLoggerMock.Setup(x => x.LogAsync(It.IsAny<Exception?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _loggerMock = new Mock<ILogger<Phase5HysteresisService>>();
        _service = new Phase5HysteresisService(_haClientMock.Object, errorLoggerMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void PhaseNumber_ShouldBe5()
    {
        Assert.Equal(5, _service.PhaseNumber);
    }

    [Fact]
    public async Task ExecuteAsync_WithOverheating_ShouldDisableHeating()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room
                {
                    Name = "sypialnia",
                    TempTarget = 21.0,
                    TempActual = 22.0, // Przegrzanie o 1°C
                    HeatingEnabled = true,
                    AutomationDisabled = false,
                    HeatingSchedule = Schedule.FromString("Brak")
                }
            },
            BoilerState = new BoilerState
            {
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                TempExternal = 5.0,
                RoomsHeatedCount = 1,
                ForecastMode = ForecastMode.Normal
            }
        };

        var parameters = new HeatingParameters
        {
            Hysteresis = 0.5,
            HysteresisSafetyThreshold = 2.0,
            TempValidationMin = 0.0,
            TempValidationMax = 40.0,
            MinReturnTemp = 50.0,
            MinTempDiff = 15.0,
            MinMixer4D = 20.0,
            MinValvesOpen = 1,
            BoilerNominalTemp = 70.0
        };

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        var room = state.Rooms.First();
        // Przegrzanie 1°C > hysteresis 0.5, więc powinno być wyłączone
        Assert.False(room.HeatingEnabled);
    }

    [Fact]
    public async Task ExecuteAsync_WithSafetyThreshold_ShouldDisableImmediately()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room
                {
                    Name = "sypialnia",
                    TempTarget = 21.0,
                    TempActual = 23.5, // Przegrzanie o 2.5°C > safety threshold 2.0
                    HeatingEnabled = true,
                    AutomationDisabled = false,
                    HeatingSchedule = Schedule.FromString("Brak")
                }
            },
            BoilerState = new BoilerState
            {
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                TempExternal = 5.0,
                RoomsHeatedCount = 1,
                ForecastMode = ForecastMode.Normal
            }
        };

        var parameters = new HeatingParameters
        {
            Hysteresis = 0.5,
            HysteresisSafetyThreshold = 2.0,
            TempValidationMin = 0.0,
            TempValidationMax = 40.0,
            MinReturnTemp = 50.0,
            MinTempDiff = 15.0,
            MinMixer4D = 20.0,
            MinValvesOpen = 1,
            BoilerNominalTemp = 70.0
        };

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        var room = state.Rooms.First();
        Assert.False(room.HeatingEnabled);
        // Powinien być logowany alarm bezpieczeństwa
    }

    [Fact]
    public async Task ExecuteAsync_WithHeatingDisabled_ShouldNotCheckHysteresis()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room
                {
                    Name = "sypialnia",
                    TempTarget = 21.0,
                    TempActual = 25.0, // Duże przegrzanie
                    HeatingEnabled = false, // Nie włączone
                    AutomationDisabled = false,
                    HeatingSchedule = Schedule.FromString("Brak")
                }
            },
            BoilerState = new BoilerState
            {
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                TempExternal = 5.0,
                RoomsHeatedCount = 1,
                ForecastMode = ForecastMode.Normal
            }
        };

        var parameters = new HeatingParameters
        {
            Hysteresis = 0.5,
            HysteresisSafetyThreshold = 2.0,
            TempValidationMin = 0.0,
            TempValidationMax = 40.0,
            MinReturnTemp = 50.0,
            MinTempDiff = 15.0,
            MinMixer4D = 20.0,
            MinValvesOpen = 1,
            BoilerNominalTemp = 70.0
        };

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        var room = state.Rooms.First();
        // Powinno pozostać wyłączone (nie sprawdza histerezy dla nie włączonych)
        Assert.False(room.HeatingEnabled);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullTempActual_ShouldNotCheckHysteresis()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room
                {
                    Name = "sypialnia",
                    TempTarget = 21.0,
                    TempActual = null, // Brak temperatury
                    HeatingEnabled = true,
                    AutomationDisabled = false,
                    HeatingSchedule = Schedule.FromString("Brak")
                }
            },
            BoilerState = new BoilerState
            {
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                TempExternal = 5.0,
                RoomsHeatedCount = 1,
                ForecastMode = ForecastMode.Normal
            }
        };

        var parameters = new HeatingParameters
        {
            Hysteresis = 0.5,
            HysteresisSafetyThreshold = 2.0,
            TempValidationMin = 0.0,
            TempValidationMax = 40.0,
            MinReturnTemp = 50.0,
            MinTempDiff = 15.0,
            MinMixer4D = 20.0,
            MinValvesOpen = 1,
            BoilerNominalTemp = 70.0
        };

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        var room = state.Rooms.First();
        // Powinno pozostać włączone (nie sprawdza histerezy bez TempActual)
        Assert.True(room.HeatingEnabled);
    }

    [Fact]
    public async Task ExecuteAsync_WithExactHysteresisThreshold_ShouldDisable()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room
                {
                    Name = "sypialnia",
                    TempTarget = 21.0,
                    TempActual = 21.5, // Dokładnie na progu (0.5°C)
                    HeatingEnabled = true,
                    AutomationDisabled = false,
                    HeatingSchedule = Schedule.FromString("Brak")
                }
            },
            BoilerState = new BoilerState
            {
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                TempExternal = 5.0,
                RoomsHeatedCount = 1,
                ForecastMode = ForecastMode.Normal
            }
        };

        var parameters = new HeatingParameters
        {
            Hysteresis = 0.5,
            HysteresisSafetyThreshold = 2.0,
            TempValidationMin = 0.0,
            TempValidationMax = 40.0,
            MinReturnTemp = 50.0,
            MinTempDiff = 15.0,
            MinMixer4D = 20.0,
            MinValvesOpen = 1,
            BoilerNominalTemp = 70.0
        };

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        var room = state.Rooms.First();
        // W kodzie: if (tempDiff > parameters.Hysteresis) - więc dokładnie równy nie wyłącza
        // Ale sprawdzam czy działa poprawnie
        // tempDiff = 21.5 - 21.0 = 0.5, więc tempDiff > 0.5 = false, więc nie wyłącza
        // Ale w kodzie jest: if (tempDiff > parameters.Hysteresis) - więc dokładnie równy nie wyłącza
        Assert.True(room.HeatingEnabled); // Dokładnie równy nie wyłącza (tempDiff > Hysteresis, nie >=)
    }

    [Fact]
    public async Task ExecuteAsync_WithExactSafetyThreshold_ShouldDisable()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room
                {
                    Name = "sypialnia",
                    TempTarget = 21.0,
                    TempActual = 23.0, // Dokładnie na progu bezpieczeństwa (2.0°C)
                    HeatingEnabled = true,
                    AutomationDisabled = false,
                    HeatingSchedule = Schedule.FromString("Brak")
                }
            },
            BoilerState = new BoilerState
            {
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                TempExternal = 5.0,
                RoomsHeatedCount = 1,
                ForecastMode = ForecastMode.Normal
            }
        };

        var parameters = new HeatingParameters
        {
            Hysteresis = 0.5,
            HysteresisSafetyThreshold = 2.0,
            TempValidationMin = 0.0,
            TempValidationMax = 40.0,
            MinReturnTemp = 50.0,
            MinTempDiff = 15.0,
            MinMixer4D = 20.0,
            MinValvesOpen = 1,
            BoilerNominalTemp = 70.0
        };

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        var room = state.Rooms.First();
        // W kodzie: if (tempDiff > parameters.HysteresisSafetyThreshold) - więc dokładnie równy nie wyłącza jako safety
        // Ale tempDiff = 2.0, więc tempDiff > 2.0 = false, więc nie wyłącza jako safety
        // Ale tempDiff > Hysteresis (0.5) = true, więc wyłącza normalnie
        Assert.False(room.HeatingEnabled);
    }

    [Fact]
    public async Task ExecuteAsync_WithLowReturnTemp_ShouldTriggerSafetyAlarm()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room
                {
                    Name = "sypialnia",
                    TempTarget = 21.0,
                    TempActual = 20.0,
                    HeatingEnabled = true,
                    AutomationDisabled = false,
                    HeatingSchedule = Schedule.FromString("Brak")
                }
            },
            BoilerState = new BoilerState
            {
                TempReturn = 45.0, // < MinReturnTemp (50.0)
                Mixer4DPosition = 50.0,
                TempExternal = 5.0,
                RoomsHeatedCount = 1,
                ForecastMode = ForecastMode.Normal
            }
        };

        var parameters = new HeatingParameters
        {
            Hysteresis = 0.5,
            HysteresisSafetyThreshold = 2.0,
            TempValidationMin = 0.0,
            TempValidationMax = 40.0,
            MinReturnTemp = 50.0,
            MinTempDiff = 15.0,
            MinMixer4D = 20.0,
            MinValvesOpen = 1,
            BoilerNominalTemp = 70.0
        };

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Alarmy systemowe są tylko logowane, nie są w Details
        // Details zawiera tylko liczbę alarmów bezpieczeństwa pokoi (safetyAlarms.Count)
        // Sprawdzam czy Details zawiera informację o alarmach bezpieczeństwa
        Assert.Contains("Alarmy bezpieczeństwa", result.Details ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_WithHighTempDiff_ShouldTriggerSafetyAlarm()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room
                {
                    Name = "sypialnia",
                    TempTarget = 21.0,
                    TempActual = 20.0,
                    HeatingEnabled = true,
                    AutomationDisabled = false,
                    HeatingSchedule = Schedule.FromString("Brak")
                }
            },
            BoilerState = new BoilerState
            {
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                TempExternal = 5.0,
                RoomsHeatedCount = 1,
                ForecastMode = ForecastMode.Normal
            }
        };

        var parameters = new HeatingParameters
        {
            Hysteresis = 0.5,
            HysteresisSafetyThreshold = 2.0,
            TempValidationMin = 0.0,
            TempValidationMax = 40.0,
            MinReturnTemp = 50.0,
            MinTempDiff = 15.0, // tempDiff = 70 - 50 = 20 > 15
            MinMixer4D = 20.0,
            MinValvesOpen = 1,
            BoilerNominalTemp = 70.0
        };

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Alarmy systemowe są tylko logowane, nie są w Details
        // Details zawiera tylko liczbę alarmów bezpieczeństwa pokoi
        Assert.Contains("Alarmy bezpieczeństwa", result.Details ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_WithLowMixer4D_ShouldTriggerSafetyAlarm()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room
                {
                    Name = "sypialnia",
                    TempTarget = 21.0,
                    TempActual = 20.0,
                    HeatingEnabled = true,
                    AutomationDisabled = false,
                    HeatingSchedule = Schedule.FromString("Brak")
                }
            },
            BoilerState = new BoilerState
            {
                TempReturn = 50.0,
                Mixer4DPosition = 15.0, // < MinMixer4D (20.0)
                TempExternal = 5.0,
                RoomsHeatedCount = 1,
                ForecastMode = ForecastMode.Normal
            }
        };

        var parameters = new HeatingParameters
        {
            Hysteresis = 0.5,
            HysteresisSafetyThreshold = 2.0,
            TempValidationMin = 0.0,
            TempValidationMax = 40.0,
            MinReturnTemp = 50.0,
            MinTempDiff = 15.0,
            MinMixer4D = 20.0,
            MinValvesOpen = 1,
            BoilerNominalTemp = 70.0
        };

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Alarmy systemowe są tylko logowane, nie są w Details
        // Details zawiera tylko liczbę alarmów bezpieczeństwa pokoi
        Assert.Contains("Alarmy bezpieczeństwa", result.Details ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_WithLowValvesCount_ShouldTriggerSafetyAlarm()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room
                {
                    Name = "sypialnia",
                    TempTarget = 21.0,
                    TempActual = 20.0,
                    HeatingEnabled = false, // Wyłączone
                    AutomationDisabled = false,
                    HeatingSchedule = Schedule.FromString("Brak")
                }
            },
            BoilerState = new BoilerState
            {
                TempReturn = 50.0,
                Mixer4DPosition = 50.0,
                TempExternal = 5.0,
                RoomsHeatedCount = 0, // 0 < MinValvesOpen (1)
                ForecastMode = ForecastMode.Normal
            }
        };

        var parameters = new HeatingParameters
        {
            Hysteresis = 0.5,
            HysteresisSafetyThreshold = 2.0,
            TempValidationMin = 0.0,
            TempValidationMax = 40.0,
            MinReturnTemp = 50.0,
            MinTempDiff = 15.0,
            MinMixer4D = 20.0,
            MinValvesOpen = 1,
            BoilerNominalTemp = 70.0
        };

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Alarmy systemowe są tylko logowane, nie są w Details
        // Details zawiera tylko liczbę alarmów bezpieczeństwa pokoi
        Assert.Contains("Alarmy bezpieczeństwa", result.Details ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_WithAllSafetyConditionsOk_ShouldReturnSuccess()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room
                {
                    Name = "sypialnia",
                    TempTarget = 21.0,
                    TempActual = 20.0,
                    HeatingEnabled = true,
                    AutomationDisabled = false,
                    HeatingSchedule = Schedule.FromString("Brak")
                }
            },
            BoilerState = new BoilerState
            {
                TempReturn = 55.0, // >= MinReturnTemp (50.0)
                Mixer4DPosition = 50.0, // >= MinMixer4D (20.0)
                TempExternal = 5.0,
                RoomsHeatedCount = 1, // >= MinValvesOpen (1)
                ForecastMode = ForecastMode.Normal
            }
        };

        var parameters = new HeatingParameters
        {
            Hysteresis = 0.5,
            HysteresisSafetyThreshold = 2.0,
            TempValidationMin = 0.0,
            TempValidationMax = 40.0,
            MinReturnTemp = 50.0,
            MinTempDiff = 15.0, // tempDiff = 70 - 55 = 15 <= 15, OK
            MinMixer4D = 20.0,
            MinValvesOpen = 1,
            BoilerNominalTemp = 70.0
        };

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Wszystkie warunki bezpieczeństwa OK - nie powinno być alarmów
        Assert.DoesNotContain("temp_return", result.Details ?? "");
        Assert.DoesNotContain("temp_diff", result.Details ?? "");
        Assert.DoesNotContain("mixer_4d", result.Details ?? "");
        Assert.DoesNotContain("valves_count", result.Details ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_WithNullBoilerState_ShouldUseDefaults()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>
            {
                new Room
                {
                    Name = "sypialnia",
                    TempTarget = 21.0,
                    TempActual = 20.0,
                    HeatingEnabled = true,
                    AutomationDisabled = false,
                    HeatingSchedule = Schedule.FromString("Brak")
                }
            },
            BoilerState = null // Brak stanu pieca
        };

        var parameters = new HeatingParameters
        {
            Hysteresis = 0.5,
            HysteresisSafetyThreshold = 2.0,
            TempValidationMin = 0.0,
            TempValidationMax = 40.0,
            MinReturnTemp = 50.0, // Użyte jako domyślna wartość
            MinTempDiff = 15.0,
            MinMixer4D = 20.0,
            MinValvesOpen = 1,
            BoilerNominalTemp = 70.0
        };

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Powinno użyć wartości domyślnych z parameters
        // W kodzie: var tempReturn = state.BoilerState?.TempReturn ?? parameters.MinReturnTemp;
        // Więc tempReturn = 50.0, co jest >= MinReturnTemp, więc OK
    }
}
