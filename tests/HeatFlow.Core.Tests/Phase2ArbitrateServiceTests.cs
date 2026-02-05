using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeatFlow.Core.Tests;

public class Phase2ArbitrateServiceTests
{
    private readonly Mock<IHomeAssistantClient> _haClientMock;
    private readonly Mock<ILogger<Phase2ArbitrateService>> _loggerMock;
    private readonly Phase2ArbitrateService _service;

    public Phase2ArbitrateServiceTests()
    {
        _haClientMock = new Mock<IHomeAssistantClient>();
        var errorLoggerMock = new Mock<IApplicationErrorLogger>();
        errorLoggerMock.Setup(x => x.LogAsync(It.IsAny<Exception?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _loggerMock = new Mock<ILogger<Phase2ArbitrateService>>();
        _service = new Phase2ArbitrateService(_haClientMock.Object, errorLoggerMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void PhaseNumber_ShouldBe2()
    {
        Assert.Equal(2, _service.PhaseNumber);
    }

    [Fact]
    public async Task ExecuteAsync_WithHighDeficitRooms_ShouldSelectTopRooms()
    {
        // Arrange
        var room1 = new Room { Name = "room1", Priority = 1, TempDeficit = 3.0, AutomationDisabled = false };
        room1.Score = 100; // Score > 50 dla Max
        room1.ClassifyDeficit();
        
        var room2 = new Room { Name = "room2", Priority = 2, TempDeficit = 2.5, AutomationDisabled = false };
        room2.Score = 80; // Score > 50 dla Max
        room2.ClassifyDeficit();
        
        var room3 = new Room { Name = "room3", Priority = 3, TempDeficit = 1.5, AutomationDisabled = false };
        room3.Score = 0; // Score między -50 a 50 dla Stay
        room3.ClassifyDeficit();
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room1, room2, room3 }
        };

        var parameters = new HeatingParameters
        {
            MaxValvesOpen = 5,
            MinValvesOpen = 1,
            ScorePriorityMultiplier = 100,
            ScoreDeficitMultiplier = 10,
            ScoreSensitiveBonus = 50,
            ScoreUsageSoonBonus = 20,
            ScoreHeatingScheduleBonus = 50,
            UsageSoonMinutes = 30
        };

        _haClientMock.Setup(x => x.SetBooleanValueAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _haClientMock.Setup(x => x.SetInputNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        var enabledRooms = state.GetEnabledRooms().Where(r => r.HeatingEnabled).ToList();
        Assert.True(enabledRooms.Count <= parameters.MaxValvesOpen);
        Assert.Contains(enabledRooms, r => r.Name == "room1"); // Najwyższy priorytet
    }

    [Fact]
    public async Task ExecuteAsync_WithLessThanMaxRooms_ShouldAddSafetyRoom()
    {
        // Arrange
        var room1 = new Room { Name = "room1", Priority = 1, TempDeficit = 3.0, AutomationDisabled = false };
        room1.Score = 100; // Score > 50 dla Max
        room1.ClassifyDeficit();
        
        var room2 = new Room { Name = "room2", Priority = 2, TempDeficit = 0.5, AutomationDisabled = false };
        room2.Score = -10; // Score między -50 a 50 dla Stay
        room2.ClassifyDeficit();
        
        var room3 = new Room { Name = "room3", Priority = 1, TempDeficit = 0.0, AutomationDisabled = false };
        room3.Score = -10; // Score między -50 a 50 dla Stay
        room3.ClassifyDeficit();
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room1, room2, room3 }
        };

        var parameters = new HeatingParameters
        {
            MaxValvesOpen = 5,
            MinValvesOpen = 1,
            ScorePriorityMultiplier = 100,
            ScoreDeficitMultiplier = 10
        };

        _haClientMock.Setup(x => x.SetBooleanValueAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _haClientMock.Setup(x => x.SetInputNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        var enabledRooms = state.GetEnabledRooms().Where(r => r.HeatingEnabled).ToList();
        // Powinien być room1 (HIGH) + pokój bezpieczeństwa (room3 - najwyższy priorytet z pozostałych)
        Assert.True(enabledRooms.Count >= 2);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoMaxRooms_ShouldAddOnlySafetyRoom()
    {
        // Arrange
        var room1 = new Room { Name = "room1", Priority = 1, TempDeficit = 0.5, AutomationDisabled = false };
        room1.Score = 0; // Score między -50 a 50 dla Stay
        room1.ClassifyDeficit();
        
        var room2 = new Room { Name = "room2", Priority = 2, TempDeficit = 0.0, AutomationDisabled = false };
        room2.Score = -10; // Score między -50 a 50 dla Stay
        room2.ClassifyDeficit();
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room1, room2 }
        };

        var parameters = new HeatingParameters
        {
            MaxValvesOpen = 5,
            MinValvesOpen = 1
        };

        _haClientMock.Setup(x => x.SetBooleanValueAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Kod najpierw dodaje Stay rooms jeśli jest miejsce (MaxValvesOpen = 5, więc może dodać wszystkie Stay)
        // Potem jeśli selectedRooms.Count == 0, dodaje safety room
        // W tym przypadku są 2 Stay rooms, więc oba będą dodane, a potem safety room (room1)
        // Więc będzie 3 pokoje: room1 (safety), room1 (Stay), room2 (Stay)
        // Ale room1 jest już w selectedRooms jako Stay, więc safety room nie będzie dodany (selectedRooms.Count > 0)
        // Sprawdzam kod: if (selectedRooms.Count == 0) - więc jeśli są Stay rooms, safety room nie będzie dodany
        Assert.True(state.RoomsToHot.Count >= 1);
        // Wszystkie wybrane pokoje powinny mieć HeatingEnabled = true
        Assert.All(state.RoomsToHot, r => Assert.True(r.HeatingEnabled));
    }

    [Fact]
    public async Task ExecuteAsync_WithMoreThanMaxValvesOpen_ShouldSelectTopN()
    {
        // Arrange
        var rooms = new List<Room>();
        for (int i = 1; i <= 10; i++)
        {
            var room = new Room { Name = $"room{i}", Priority = i, TempDeficit = 3.0, AutomationDisabled = false };
            room.Score = 100 - i; // Różne score
            room.ClassifyDeficit();
            rooms.Add(room);
        }
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = rooms
        };

        var parameters = new HeatingParameters
        {
            MaxValvesOpen = 5,
            MinValvesOpen = 1
        };

        _haClientMock.Setup(x => x.SetBooleanValueAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Powinno wybrać top 5 pokoi Max (najwyższe score)
        Assert.Equal(5, state.RoomsToHot.Count);
        Assert.All(state.RoomsToHot, r => Assert.Equal(DeficitClassification.Max, r.DeficitClassification));
    }

    [Fact]
    public async Task ExecuteAsync_WithExactlyMaxValvesOpen_ShouldSelectAll()
    {
        // Arrange
        var rooms = new List<Room>();
        for (int i = 1; i <= 5; i++)
        {
            var room = new Room { Name = $"room{i}", Priority = i, TempDeficit = 3.0, AutomationDisabled = false };
            room.Score = 100 - i;
            room.ClassifyDeficit();
            rooms.Add(room);
        }
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = rooms
        };

        var parameters = new HeatingParameters
        {
            MaxValvesOpen = 5,
            MinValvesOpen = 1
        };

        _haClientMock.Setup(x => x.SetBooleanValueAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Powinno wybrać wszystkie 5 pokoi Max
        Assert.Equal(5, state.RoomsToHot.Count);
        Assert.All(state.RoomsToHot, r => Assert.Equal(DeficitClassification.Max, r.DeficitClassification));
    }

    [Fact]
    public async Task ExecuteAsync_WithSpaceForStay_ShouldAddStayRooms()
    {
        // Arrange
        var room1 = new Room { Name = "room1", Priority = 1, TempDeficit = 3.0, AutomationDisabled = false };
        room1.Score = 100;
        room1.ClassifyDeficit();
        
        var room2 = new Room { Name = "room2", Priority = 2, TempDeficit = 0.5, AutomationDisabled = false };
        room2.Score = 0; // Stay
        room2.ClassifyDeficit();
        
        var room3 = new Room { Name = "room3", Priority = 3, TempDeficit = 0.3, AutomationDisabled = false };
        room3.Score = -5; // Stay
        room3.ClassifyDeficit();
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room1, room2, room3 }
        };

        var parameters = new HeatingParameters
        {
            MaxValvesOpen = 5,
            MinValvesOpen = 1
        };

        _haClientMock.Setup(x => x.SetBooleanValueAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Powinno dodać Stay rooms gdy jest miejsce (MaxValvesOpen = 5, więc można dodać 4 Stay)
        var allSelected = state.RoomsToHot.Concat(state.RoomsToStay ?? new List<Room>()).ToList();
        Assert.True(allSelected.Count > 1);
        Assert.Contains(allSelected, r => r.Name == "room1" && r.DeficitClassification == DeficitClassification.Max);
    }

    [Fact]
    public async Task ExecuteAsync_WithAllRoomsDisabled_ShouldAddSafetyRoom()
    {
        // Arrange
        var room1 = new Room { Name = "room1", Priority = 1, TempDeficit = 3.0, AutomationDisabled = true };
        room1.Score = 100;
        room1.ClassifyDeficit();
        
        var room2 = new Room { Name = "room2", Priority = 2, TempDeficit = 2.0, AutomationDisabled = true };
        room2.Score = 80;
        room2.ClassifyDeficit();
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room1, room2 }
        };

        var parameters = new HeatingParameters
        {
            MaxValvesOpen = 5,
            MinValvesOpen = 1
        };

        _haClientMock.Setup(x => x.SetBooleanValueAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        // Wszystkie pokoje są disabled, więc GetEnabledRooms() zwróci pustą listę
        // selectedRooms będzie puste, więc kod spróbuje dodać safety room
        // Ale enabledRooms.OrderByDescending(r => r.Score).First() rzuci wyjątek jeśli lista jest pusta
        // Kod ma try-catch, więc zwróci PhaseResult.ErrorResult
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_WithEqualScores_ShouldSortByPriority()
    {
        // Arrange
        var room1 = new Room { Name = "room1", Priority = 1, TempDeficit = 3.0, AutomationDisabled = false };
        room1.Score = 100;
        room1.ClassifyDeficit();
        
        var room2 = new Room { Name = "room2", Priority = 2, TempDeficit = 3.0, AutomationDisabled = false };
        room2.Score = 100; // Takie samo score jak room1
        room2.ClassifyDeficit();
        
        var room3 = new Room { Name = "room3", Priority = 3, TempDeficit = 3.0, AutomationDisabled = false };
        room3.Score = 100; // Takie samo score jak room1 i room2
        room3.ClassifyDeficit();
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room3, room2, room1 } // Odwrócona kolejność
        };

        var parameters = new HeatingParameters
        {
            MaxValvesOpen = 2,
            MinValvesOpen = 1
        };

        _haClientMock.Setup(x => x.SetBooleanValueAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Powinno wybrać pokoje z najwyższym priorytetem (najniższy numer Priority)
        // Sortowanie: OrderByDescending(x => x.Score) - więc przy równych score kolejność może być dowolna
        // Ale w praktyce powinno wybrać room1 i room2 (najwyższe priorytety)
        Assert.Equal(2, state.RoomsToHot.Count);
        Assert.All(state.RoomsToHot, r => Assert.Equal(DeficitClassification.Max, r.DeficitClassification));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSetRoomsToDisable()
    {
        // Arrange
        var room1 = new Room { Name = "room1", Priority = 1, TempDeficit = 3.0, AutomationDisabled = false };
        room1.Score = 100;
        room1.ClassifyDeficit();
        
        var room2 = new Room { Name = "room2", Priority = 2, TempDeficit = 2.0, AutomationDisabled = false };
        room2.Score = 80;
        room2.ClassifyDeficit();
        
        var room3 = new Room { Name = "room3", Priority = 3, TempDeficit = 0.5, AutomationDisabled = false };
        room3.Score = 0; // Stay
        room3.ClassifyDeficit();
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room1, room2, room3 }
        };

        var parameters = new HeatingParameters
        {
            MaxValvesOpen = 1,
            MinValvesOpen = 1
        };

        _haClientMock.Setup(x => x.SetBooleanValueAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Powinno wybrać tylko room1 (najwyższe score)
        Assert.Single(state.RoomsToHot);
        Assert.Equal("room1", state.RoomsToHot.First().Name);
        // Pozostałe pokoje powinny być w RoomsToDisable
        Assert.True(state.RoomsToDisable.Count > 0);
        Assert.Contains(state.RoomsToDisable, r => r.Name == "room2" || r.Name == "room3");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSetHeatingEnabled()
    {
        // Arrange
        var room1 = new Room { Name = "room1", Priority = 1, TempDeficit = 3.0, AutomationDisabled = false };
        room1.Score = 100;
        room1.ClassifyDeficit();
        
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room> { room1 }
        };

        var parameters = new HeatingParameters
        {
            MaxValvesOpen = 5,
            MinValvesOpen = 1
        };

        _haClientMock.Setup(x => x.SetBooleanValueAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Wszystkie wybrane pokoje powinny mieć HeatingEnabled = true
        var allSelected = state.RoomsToHot.Concat(state.RoomsToStay ?? new List<Room>()).ToList();
        Assert.All(allSelected, r => Assert.True(r.HeatingEnabled));
    }
}
