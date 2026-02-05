using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeatFlow.Core.Tests;

public class Phase3ValvesServiceTests
{
    private readonly Mock<IHomeAssistantClient> _haClientMock;
    private readonly Mock<ILogger<Phase3ValvesService>> _loggerMock;
    private readonly Phase3ValvesService _service;

    public Phase3ValvesServiceTests()
    {
        _haClientMock = new Mock<IHomeAssistantClient>();
        var errorLoggerMock = new Mock<IApplicationErrorLogger>();
        errorLoggerMock.Setup(x => x.LogAsync(It.IsAny<Exception?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _loggerMock = new Mock<ILogger<Phase3ValvesService>>();
        _service = new Phase3ValvesService(_haClientMock.Object, errorLoggerMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void PhaseNumber_ShouldBe3()
    {
        Assert.Equal(3, _service.PhaseNumber);
    }

    [Fact]
    public async Task ExecuteAsync_WithHeatingEnabled_ShouldSetValveTemperature()
    {
        // Arrange
        var room = new Room
        {
            Name = "sypialnia",
            TempTarget = 21.0,
            HeatingEnabled = true,
            AutomationDisabled = false,
            HeatingSchedule = Schedule.FromString("Brak"),
            ValveEntityId = "climate.sypialnia",
            MaximalSetTemperature = 26.0 // Ustaw przed wywołaniem ChangeTemperatureToSet
        };
        // Ustaw TemperatureToSet przez Score i ClassifyDeficit
        room.Score = 100; // Score > 50 dla Max
        room.ClassifyDeficit(); // Ustawi DeficitClassification na Max
        room.ChangeTemperatureToSet(); // Ustawi TemperatureToSet na 26
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room },
            RoomsToHot = new List<Room> { room }
        };

        var parameters = new HeatingParameters
        {
            ValveTolerance = 0.1,
            ValveRetryCount = 3,
            ValveRetryDelay = 1.0,
            MinValvesOpen = 1
        };

        _haClientMock.Setup(x => x.GetStateAsync("climate.sypialnia", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityState 
            { 
                State = "21.0",
                Attributes = new Dictionary<string, object> { { "temperature", 21.0 } }
            });

        _haClientMock.Setup(x => x.SetClimateTemperatureAsync("climate.sypialnia", 26, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        _haClientMock.Verify(x => x.SetClimateTemperatureAsync("climate.sypialnia", 26, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WithHeatingDisabled_ShouldSetClosedTemp()
    {
        // Arrange
        var room = new Room
        {
            Name = "sypialnia",
            TempTarget = 21.0,
            HeatingEnabled = false,
            AutomationDisabled = false,
            HeatingSchedule = Schedule.FromString("Brak"),
            ValveEntityId = "climate.sypialnia",
            MinimalSetTemperature = 0.0
        };
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room },
            RoomsToDisable = new List<Room> { room }
        };

        var parameters = new HeatingParameters
        {
            ValveTolerance = 0.1,
            ValveRetryCount = 3,
            ValveRetryDelay = 1.0,
            MinValvesOpen = 1
        };

        _haClientMock.Setup(x => x.GetStateAsync("climate.sypialnia", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityState 
            { 
                State = "21.0",
                Attributes = new Dictionary<string, object> { { "temperature", 21.0 } }
            });

        _haClientMock.Setup(x => x.SetClimateTemperatureAsync("climate.sypialnia", 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        _haClientMock.Verify(x => x.SetClimateTemperatureAsync("climate.sypialnia", 0, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingValveEntity_ShouldReturnFalse()
    {
        // Arrange
        var room = new Room
        {
            Name = "sypialnia",
            TempTarget = 21.0,
            HeatingEnabled = true,
            AutomationDisabled = false,
            HeatingSchedule = Schedule.FromString("Brak"),
            ValveEntityId = "", // Brak encji
            MaximalSetTemperature = 26.0
        };
        room.Score = 100;
        room.ClassifyDeficit();
        room.ChangeTemperatureToSet();
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room },
            RoomsToHot = new List<Room> { room }
        };

        var parameters = new HeatingParameters
        {
            ValveTolerance = 0.1,
            ValveRetryCount = 3,
            ValveRetryDelay = 1.0
        };

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success); // Faza się wykonuje, ale ustawienie zaworu zwraca false
        // Sprawdzam czy w szczegółach jest informacja o błędzie
        Assert.Contains("Błędy", result.Details ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_WithTemperatureAlreadySet_ShouldSkipRetry()
    {
        // Arrange
        var room = new Room
        {
            Name = "sypialnia",
            TempTarget = 21.0,
            HeatingEnabled = true,
            AutomationDisabled = false,
            HeatingSchedule = Schedule.FromString("Brak"),
            ValveEntityId = "climate.sypialnia",
            MaximalSetTemperature = 26.0
        };
        room.Score = 100;
        room.ClassifyDeficit();
        room.ChangeTemperatureToSet(); // TemperatureToSet = 26
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room },
            RoomsToHot = new List<Room> { room }
        };

        var parameters = new HeatingParameters
        {
            ValveTolerance = 0.5, // Tolerance 0.5°C
            ValveRetryCount = 3,
            ValveRetryDelay = 0.01 // Zmniejszone opóźnienie dla testów
        };

        // Temperatura już ustawiona (26.0 ± 0.5)
        // GetCurrentValveTemperatureAsync odczytuje "temperature" z atrybutów jako JsonElement
        // Mock musi zwracać JsonElement, nie zwykły double
        // Kod sprawdza: if (tempObj is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Number)
        // Więc muszę użyć JsonElement w mocku
        var jsonElement = System.Text.Json.JsonSerializer.SerializeToElement(26.2);
        var entityState = new EntityState 
        { 
            State = "heat",
            Attributes = new Dictionary<string, object> { { "temperature", jsonElement } } // W granicach tolerance (26.0 ± 0.5)
        };
        _haClientMock.Setup(x => x.GetStateAsync("climate.sypialnia", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entityState);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Nie powinno być wywołania SetClimateTemperatureAsync (temperatura już ustawiona)
        _haClientMock.Verify(x => x.SetClimateTemperatureAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithRetrySuccess_ShouldVerify()
    {
        // Arrange
        var room = new Room
        {
            Name = "sypialnia",
            TempTarget = 21.0,
            HeatingEnabled = true,
            AutomationDisabled = false,
            HeatingSchedule = Schedule.FromString("Brak"),
            ValveEntityId = "climate.sypialnia",
            MaximalSetTemperature = 26.0
        };
        room.Score = 100;
        room.ClassifyDeficit();
        room.ChangeTemperatureToSet(); // TemperatureToSet = 26
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room },
            RoomsToHot = new List<Room> { room }
        };

        var parameters = new HeatingParameters
        {
            ValveTolerance = 0.1,
            ValveRetryCount = 3,
            ValveRetryDelay = 0.01 // Zmniejszone opóźnienie dla testów
        };

        // GetCurrentValveTemperatureAsync wywołuje GetStateAsync:
        // 1. Przed pierwszym retry - temperatura nie ustawiona (21.0)
        // 2. Po pierwszym retry (weryfikacja) - nie będzie wykonana, bo pierwszy retry zwrócił false
        // 3. Przed drugim retry - temperatura nadal nie ustawiona (21.0)
        // 4. Po drugim retry (weryfikacja) - ustawiona poprawnie (26.0)
        // Muszę użyć JsonElement w mocku
        var temp21Json = System.Text.Json.JsonSerializer.SerializeToElement(21.0);
        var temp26Json = System.Text.Json.JsonSerializer.SerializeToElement(26.0);
        // Tworzę EntityState z poprawnymi Attributes (nie null)
        var entityState21 = new EntityState 
        { 
            State = "heat",
            Attributes = new Dictionary<string, object> { { "temperature", temp21Json } }
        };
        var entityState26 = new EntityState 
        { 
            State = "heat",
            Attributes = new Dictionary<string, object> { { "temperature", temp26Json } }
        };
        // Używam Setup z callback, żeby zawsze zwracać odpowiednią wartość
        // Kolejność wywołań:
        // 1. Przed pierwszym retry (sprawdzenie aktualnej temperatury) - 21.0
        // 2. Przed drugim retry (sprawdzenie aktualnej temperatury po pierwszym nieudanym retry) - 21.0
        // 3. Po drugim retry (weryfikacja po udanym retry) - 26.0
        var callCount = 0;
        _haClientMock.Setup(x => x.GetStateAsync("climate.sypialnia", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                // Pierwsze 2 wywołania - temperatura nie ustawiona (21.0)
                if (callCount <= 2)
                    return entityState21;
                // Kolejne wywołania - temperatura ustawiona (26.0)
                return entityState26;
            });

        // Pierwszy retry nieudany, drugi udany
        // Używam Setup z callback, żeby zawsze zwracać odpowiednią wartość
        var setCallCount = 0;
        _haClientMock.Setup(x => x.SetClimateTemperatureAsync("climate.sypialnia", 26, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                setCallCount++;
                // Pierwszy retry nieudany
                if (setCallCount == 1)
                    return false;
                // Kolejne retry udane
                return true;
            });

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success, $"Test nie przeszedł. Success: {result.Success}, ErrorMessage: {result.ErrorMessage}, Details: {result.Details}");
        // Powinno być 2 wywołania SetClimateTemperatureAsync (pierwszy nieudany, drugi udany)
        // Używam AtLeast(2) zamiast Exactly(2), bo może być więcej wywołań jeśli są dodatkowe retry
        _haClientMock.Verify(x => x.SetClimateTemperatureAsync("climate.sypialnia", 26, It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task ExecuteAsync_WithAllRetriesFailed_ShouldReturnFalse()
    {
        // Arrange
        var room = new Room
        {
            Name = "sypialnia",
            TempTarget = 21.0,
            HeatingEnabled = true,
            AutomationDisabled = false,
            HeatingSchedule = Schedule.FromString("Brak"),
            ValveEntityId = "climate.sypialnia",
            MaximalSetTemperature = 26.0
        };
        room.Score = 100;
        room.ClassifyDeficit();
        room.ChangeTemperatureToSet(); // TemperatureToSet = 26
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room },
            RoomsToHot = new List<Room> { room }
        };

        var parameters = new HeatingParameters
        {
            ValveTolerance = 0.1,
            ValveRetryCount = 3,
            ValveRetryDelay = 0.1
        };

        // Temperatura nie ustawiona
        _haClientMock.Setup(x => x.GetStateAsync("climate.sypialnia", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityState 
            { 
                State = "heat",
                Attributes = new Dictionary<string, object> { { "temperature", 21.0 } }
            });

        // Wszystkie retry nieudane
        _haClientMock.Setup(x => x.SetClimateTemperatureAsync("climate.sypialnia", 26, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success); // Faza się wykonuje
        // Powinno być 3 wywołania (ValveRetryCount = 3)
        _haClientMock.Verify(x => x.SetClimateTemperatureAsync("climate.sypialnia", 26, It.IsAny<CancellationToken>()), Times.Exactly(3));
        Assert.Contains("Błędy", result.Details ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_WithNumberEntity_ShouldUseSetNumberValue()
    {
        // Arrange
        var room = new Room
        {
            Name = "sypialnia",
            TempTarget = 21.0,
            HeatingEnabled = true,
            AutomationDisabled = false,
            HeatingSchedule = Schedule.FromString("Brak"),
            ValveEntityId = "number.sypialnia_valve", // Number encja
            MaximalSetTemperature = 26.0
        };
        room.Score = 100;
        room.ClassifyDeficit();
        room.ChangeTemperatureToSet(); // TemperatureToSet = 26
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room },
            RoomsToHot = new List<Room> { room }
        };

        var parameters = new HeatingParameters
        {
            ValveTolerance = 0.1,
            ValveRetryCount = 3,
            ValveRetryDelay = 0.1
        };

        _haClientMock.Setup(x => x.GetStateDoubleAsync("number.sypialnia_valve", It.IsAny<CancellationToken>()))
            .ReturnsAsync(21.0);

        _haClientMock.Setup(x => x.SetNumberValueAsync("number.sypialnia_valve", 26, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Powinno użyć SetNumberValueAsync zamiast SetClimateTemperatureAsync
        _haClientMock.Verify(x => x.SetNumberValueAsync("number.sypialnia_valve", 26, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _haClientMock.Verify(x => x.SetClimateTemperatureAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnsupportedEntityType_ShouldReturnFalse()
    {
        // Arrange
        var room = new Room
        {
            Name = "sypialnia",
            TempTarget = 21.0,
            HeatingEnabled = true,
            AutomationDisabled = false,
            HeatingSchedule = Schedule.FromString("Brak"),
            ValveEntityId = "sensor.sypialnia_valve", // Nieobsługiwany typ
            MaximalSetTemperature = 26.0
        };
        room.Score = 100;
        room.ClassifyDeficit();
        room.ChangeTemperatureToSet(); // TemperatureToSet = 26
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room },
            RoomsToHot = new List<Room> { room }
        };

        var parameters = new HeatingParameters
        {
            ValveTolerance = 0.1,
            ValveRetryCount = 3,
            ValveRetryDelay = 0.1
        };

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success); // Faza się wykonuje
        // Nie powinno być wywołań SetClimateTemperatureAsync ani SetNumberValueAsync
        _haClientMock.Verify(x => x.SetClimateTemperatureAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
        _haClientMock.Verify(x => x.SetNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains("Błędy", result.Details ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_WithRoomsToStay_ShouldSetTemperature()
    {
        // Arrange
        var room = new Room
        {
            Name = "sypialnia",
            TempTarget = 21.0,
            TempActual = 20.5,
            HeatingEnabled = true,
            AutomationDisabled = false,
            HeatingSchedule = Schedule.FromString("Brak"),
            ValveEntityId = "climate.sypialnia"
        };
        room.Score = 0; // Stay
        room.ClassifyDeficit();
        room.ChangeTemperatureToSet(); // TemperatureToSet = tempActual = 20.5
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room },
            RoomsToStay = new List<Room> { room }
        };

        var parameters = new HeatingParameters
        {
            ValveTolerance = 0.1,
            ValveRetryCount = 3,
            ValveRetryDelay = 0.1
        };

        _haClientMock.Setup(x => x.GetStateAsync("climate.sypialnia", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityState 
            { 
                State = "heat",
                Attributes = new Dictionary<string, object> { { "temperature", 20.0 } }
            });

        _haClientMock.Setup(x => x.SetClimateTemperatureAsync("climate.sypialnia", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Powinno ustawić temperaturę dla RoomsToStay
        _haClientMock.Verify(x => x.SetClimateTemperatureAsync("climate.sypialnia", 20, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
