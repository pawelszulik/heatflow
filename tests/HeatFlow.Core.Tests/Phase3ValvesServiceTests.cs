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
        var hotRoom = new Room
        {
            Name = "salon",
            ValveEntityId = "climate.salon",
            MaximalSetTemperature = 26.0
        };
        hotRoom.Score = 100;
        hotRoom.ClassifyDeficit();
        hotRoom.ChangeTemperatureToSet(); // TemperatureToSet = 26

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
            Rooms = new List<Room> { hotRoom, room },
            RoomsToHot = new List<Room> { hotRoom },
            RoomsToDisable = new List<Room> { room }
        };

        var parameters = new HeatingParameters
        {
            ValveTolerance = 0.1,
            ValveRetryCount = 3,
            ValveRetryDelay = 0.01,
            MinValvesOpen = 1
        };

        // hotRoom - sukces z pierwszego wywołania
        var salon26Json = System.Text.Json.JsonSerializer.SerializeToElement(26.0);
        _haClientMock.Setup(x => x.GetStateAsync("climate.salon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityState
            {
                State = "heat",
                Attributes = new Dictionary<string, object> { { "temperature", salon26Json } }
            });
        _haClientMock.Setup(x => x.SetClimateTemperatureAsync("climate.salon", 26, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // sypialnia (disabled) - ustawiana na temp minimalną (0)
        var temp21Json = System.Text.Json.JsonSerializer.SerializeToElement(21.0);
        var temp0Json = System.Text.Json.JsonSerializer.SerializeToElement(0.0);
        var callCount = 0;
        _haClientMock.Setup(x => x.GetStateAsync("climate.sypialnia", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                var temp = callCount <= 1 ? temp21Json : temp0Json;
                return new EntityState
                {
                    State = "heat",
                    Attributes = new Dictionary<string, object> { { "temperature", temp } }
                };
            });
        _haClientMock.Setup(x => x.SetClimateTemperatureAsync("climate.sypialnia", 0.0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success, $"Success={result.Success} Error={result.ErrorMessage} Details={result.Details}");
        _haClientMock.Verify(x => x.SetClimateTemperatureAsync("climate.sypialnia", 0.0, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
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
        var hotRoom = new Room
        {
            Name = "salon",
            ValveEntityId = "climate.salon",
            MaximalSetTemperature = 26.0
        };
        hotRoom.Score = 100;
        hotRoom.ClassifyDeficit();
        hotRoom.ChangeTemperatureToSet(); // TemperatureToSet = 26

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
            Rooms = new List<Room> { hotRoom, room },
            RoomsToHot = new List<Room> { hotRoom },
            RoomsToStay = new List<Room> { room }
        };

        var parameters = new HeatingParameters
        {
            ValveTolerance = 0.1,
            ValveRetryCount = 3,
            ValveRetryDelay = 0.01
        };

        // hotRoom - sukces z pierwszego wywołania (temp już ustawiona)
        var salon26Json = System.Text.Json.JsonSerializer.SerializeToElement(26.0);
        _haClientMock.Setup(x => x.GetStateAsync("climate.salon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityState
            {
                State = "heat",
                Attributes = new Dictionary<string, object> { { "temperature", salon26Json } }
            });
        _haClientMock.Setup(x => x.SetClimateTemperatureAsync("climate.salon", 26, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // sypialnia - aktualna temp 18 (różna od docelowej 20), po Set zwraca 20
        var temp18Json = System.Text.Json.JsonSerializer.SerializeToElement(18.0);
        var temp20Json = System.Text.Json.JsonSerializer.SerializeToElement(20.0);
        var sypialniaCallCount = 0;
        _haClientMock.Setup(x => x.GetStateAsync("climate.sypialnia", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                sypialniaCallCount++;
                var temp = sypialniaCallCount <= 1 ? temp18Json : temp20Json;
                return new EntityState
                {
                    State = "heat",
                    Attributes = new Dictionary<string, object> { { "temperature", temp } }
                };
            });
        _haClientMock.Setup(x => x.SetClimateTemperatureAsync("climate.sypialnia", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success, $"Success={result.Success} Error={result.ErrorMessage} Details={result.Details}");
        // Powinno ustawić temperaturę dla RoomsToStay
        _haClientMock.Verify(x => x.SetClimateTemperatureAsync("climate.sypialnia", 20, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAllRoomsToHotFail_ShouldPromoteBestStayRoomToFullHeat()
    {
        // Arrange
        var hotRoom = new Room
        {
            Name = "salon",
            ValveEntityId = "climate.salon",
            MaximalSetTemperature = 26.0
        };
        hotRoom.Score = 100;
        hotRoom.ClassifyDeficit();
        hotRoom.ChangeTemperatureToSet(); // TemperatureToSet = 26

        var stayRoom = new Room
        {
            Name = "kuchnia",
            TempActual = 19.0,
            TempTarget = 20.0,
            ValveEntityId = "climate.kuchnia",
            MaximalSetTemperature = 26.0
        };
        stayRoom.Score = 20;
        stayRoom.ClassifyDeficit();
        stayRoom.ChangeTemperatureToSet(); // TemperatureToSet = 19 (stay)

        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            RoomsToHot = new List<Room> { hotRoom },
            RoomsToStay = new List<Room> { stayRoom }
        };

        var parameters = new HeatingParameters
        {
            ValveTolerance = 0.1,
            ValveRetryCount = 3,
            ValveRetryDelay = 0.01
        };

        // hotRoom - aktualna temp daleka od celu, wszystkie retry nieudane
        _haClientMock.Setup(x => x.GetStateAsync("climate.salon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityState
            {
                State = "heat",
                Attributes = new Dictionary<string, object> { { "temperature", 21.0 } }
            });
        _haClientMock.Setup(x => x.SetClimateTemperatureAsync("climate.salon", 26, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // stayRoom jako fallback - przed ustawieniem temp=19 (różna od 26), po ustawieniu temp=26
        var kuchniaTemp19Json = System.Text.Json.JsonSerializer.SerializeToElement(19.0);
        var kuchniaTemp26Json = System.Text.Json.JsonSerializer.SerializeToElement(26.0);
        var kuchniaCallCount = 0;
        _haClientMock.Setup(x => x.GetStateAsync("climate.kuchnia", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                kuchniaCallCount++;
                var temp = kuchniaCallCount <= 1 ? kuchniaTemp19Json : kuchniaTemp26Json;
                return new EntityState
                {
                    State = "heat",
                    Attributes = new Dictionary<string, object> { { "temperature", temp } }
                };
            });
        _haClientMock.Setup(x => x.SetClimateTemperatureAsync("climate.kuchnia", 26, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains("kuchnia", result.Warnings[0]);
        // Fallback room ustawiony na pełne grzanie (26), NIE na temperaturę stay (19)
        _haClientMock.Verify(x => x.SetClimateTemperatureAsync("climate.kuchnia", 26, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _haClientMock.Verify(x => x.SetClimateTemperatureAsync("climate.kuchnia", 19, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAllRoomsToHotFailAndNoStayRooms_ShouldPromoteDisableRoomToFullHeat()
    {
        // Arrange
        var hotRoom = new Room
        {
            Name = "salon",
            ValveEntityId = "climate.salon",
            MaximalSetTemperature = 26.0
        };
        hotRoom.Score = 100;
        hotRoom.ClassifyDeficit();
        hotRoom.ChangeTemperatureToSet();

        var disableRoom = new Room
        {
            Name = "przedpokoj",
            ValveEntityId = "climate.przedpokoj",
            MinimalSetTemperature = 5.0,
            MaximalSetTemperature = 26.0
        };
        disableRoom.Score = -10;
        disableRoom.ClassifyDeficit();
        disableRoom.ChangeTemperatureToSet(); // TemperatureToSet = 5 (disabled)

        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            RoomsToHot = new List<Room> { hotRoom },
            RoomsToStay = new List<Room>(),
            RoomsToDisable = new List<Room> { disableRoom }
        };

        var parameters = new HeatingParameters
        {
            ValveTolerance = 0.1,
            ValveRetryCount = 3,
            ValveRetryDelay = 0.01
        };

        // hotRoom - wszystkie retry nieudane
        _haClientMock.Setup(x => x.GetStateAsync("climate.salon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityState
            {
                State = "heat",
                Attributes = new Dictionary<string, object> { { "temperature", 21.0 } }
            });
        _haClientMock.Setup(x => x.SetClimateTemperatureAsync("climate.salon", 26, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // disableRoom jako fallback - przed ustawieniem temp=5, po ustawieniu temp=26
        var przedpokojTemp5Json = System.Text.Json.JsonSerializer.SerializeToElement(5.0);
        var przedpokojTemp26Json = System.Text.Json.JsonSerializer.SerializeToElement(26.0);
        var przedpokojCallCount = 0;
        _haClientMock.Setup(x => x.GetStateAsync("climate.przedpokoj", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                przedpokojCallCount++;
                var temp = przedpokojCallCount <= 1 ? przedpokojTemp5Json : przedpokojTemp26Json;
                return new EntityState
                {
                    State = "heat",
                    Attributes = new Dictionary<string, object> { { "temperature", temp } }
                };
            });
        _haClientMock.Setup(x => x.SetClimateTemperatureAsync("climate.przedpokoj", 26, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.Warnings);
        // Fallback room ustawiony na 26, NIE na minimalną (5)
        _haClientMock.Verify(x => x.SetClimateTemperatureAsync("climate.przedpokoj", 26, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _haClientMock.Verify(x => x.SetClimateTemperatureAsync("climate.przedpokoj", 5, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSomeRoomsToHotSucceed_ShouldNotActivateSafetyFallback()
    {
        // Arrange
        var hotRoomFail = new Room
        {
            Name = "salon",
            ValveEntityId = "climate.salon",
            MaximalSetTemperature = 26.0
        };
        hotRoomFail.Score = 100;
        hotRoomFail.ClassifyDeficit();
        hotRoomFail.ChangeTemperatureToSet();

        var hotRoomSuccess = new Room
        {
            Name = "sypialnia",
            ValveEntityId = "climate.sypialnia",
            MaximalSetTemperature = 26.0
        };
        hotRoomSuccess.Score = 80;
        hotRoomSuccess.ClassifyDeficit();
        hotRoomSuccess.ChangeTemperatureToSet();

        var stayRoom = new Room
        {
            Name = "kuchnia",
            TempActual = 19.0,
            TempTarget = 20.0,
            ValveEntityId = "climate.kuchnia",
            MaximalSetTemperature = 26.0
        };
        stayRoom.Score = 20;
        stayRoom.ClassifyDeficit();
        stayRoom.ChangeTemperatureToSet(); // TemperatureToSet = 19

        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            RoomsToHot = new List<Room> { hotRoomFail, hotRoomSuccess },
            RoomsToStay = new List<Room> { stayRoom }
        };

        var parameters = new HeatingParameters
        {
            ValveTolerance = 0.1,
            ValveRetryCount = 3,
            ValveRetryDelay = 0.01
        };

        // salon - nieudane
        _haClientMock.Setup(x => x.GetStateAsync("climate.salon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityState
            {
                State = "heat",
                Attributes = new Dictionary<string, object> { { "temperature", 21.0 } }
            });
        _haClientMock.Setup(x => x.SetClimateTemperatureAsync("climate.salon", 26, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // sypialnia - udane z weryfikacją
        var temp26Json = System.Text.Json.JsonSerializer.SerializeToElement(26.0);
        _haClientMock.Setup(x => x.GetStateAsync("climate.sypialnia", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityState
            {
                State = "heat",
                Attributes = new Dictionary<string, object> { { "temperature", temp26Json } }
            });
        _haClientMock.Setup(x => x.SetClimateTemperatureAsync("climate.sypialnia", 26, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // kuchnia - stay, udane
        _haClientMock.Setup(x => x.GetStateAsync("climate.kuchnia", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityState
            {
                State = "heat",
                Attributes = new Dictionary<string, object> { { "temperature", 20.0 } }
            });
        _haClientMock.Setup(x => x.SetClimateTemperatureAsync("climate.kuchnia", 19, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Warnings); // Brak ostrzeżeń - zabezpieczenie nie aktywowane
        // kuchnia ustawiona na temperaturę stay (19), NIE na full heat (26)
        _haClientMock.Verify(x => x.SetClimateTemperatureAsync("climate.kuchnia", 19, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _haClientMock.Verify(x => x.SetClimateTemperatureAsync("climate.kuchnia", 26, It.IsAny<CancellationToken>()), Times.Never);
    }
}
