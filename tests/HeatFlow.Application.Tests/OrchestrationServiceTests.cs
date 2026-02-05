using HeatFlow.Application;
using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeatFlow.Application.Tests;

public class OrchestrationServiceTests
{
    private readonly Mock<IHomeAssistantClient> _haClientMock;
    private readonly Mock<IConfigurationService> _configurationServiceMock;
    private readonly Mock<ILogger<OrchestrationService>> _loggerMock;
    private readonly List<IPhaseService> _phaseServices;
    private OrchestrationService _service;

    public OrchestrationServiceTests()
    {
        _haClientMock = new Mock<IHomeAssistantClient>();
        _configurationServiceMock = new Mock<IConfigurationService>();
        _loggerMock = new Mock<ILogger<OrchestrationService>>();

        var phase0Mock = new Mock<IPhaseService>();
        phase0Mock.Setup(x => x.PhaseNumber).Returns(0);
        phase0Mock.Setup(x => x.ExecuteAsync(It.IsAny<HeatingState>(), It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PhaseResult.SuccessResult(0, 100));

        var phase1Mock = new Mock<IPhaseService>();
        phase1Mock.Setup(x => x.PhaseNumber).Returns(1);
        phase1Mock.Setup(x => x.ExecuteAsync(It.IsAny<HeatingState>(), It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PhaseResult.SuccessResult(1, 200));

        var phase2Mock = new Mock<IPhaseService>();
        phase2Mock.Setup(x => x.PhaseNumber).Returns(2);
        phase2Mock.Setup(x => x.ExecuteAsync(It.IsAny<HeatingState>(), It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PhaseResult.SuccessResult(2, 150));

        var phase3Mock = new Mock<IPhaseService>();
        phase3Mock.Setup(x => x.PhaseNumber).Returns(3);
        phase3Mock.Setup(x => x.ExecuteAsync(It.IsAny<HeatingState>(), It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PhaseResult.SuccessResult(3, 300));

        var phase4Mock = new Mock<IPhaseService>();
        phase4Mock.Setup(x => x.PhaseNumber).Returns(4);
        phase4Mock.Setup(x => x.ExecuteAsync(It.IsAny<HeatingState>(), It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PhaseResult.SuccessResult(4, 250));

        var phase5Mock = new Mock<IPhaseService>();
        phase5Mock.Setup(x => x.PhaseNumber).Returns(5);
        phase5Mock.Setup(x => x.ExecuteAsync(It.IsAny<HeatingState>(), It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PhaseResult.SuccessResult(5, 100));

        _phaseServices = new List<IPhaseService>
        {
            phase0Mock.Object,
            phase1Mock.Object,
            phase2Mock.Object,
            phase3Mock.Object,
            phase4Mock.Object,
            phase5Mock.Object
        };

        var errorLoggerMock = new Mock<IApplicationErrorLogger>();
        errorLoggerMock.Setup(x => x.LogAsync(It.IsAny<Exception?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        errorLoggerMock.Setup(x => x.LogAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _service = new OrchestrationService(_haClientMock.Object, _configurationServiceMock.Object, _phaseServices, _loggerMock.Object, errorLoggerMock.Object);
    }

    [Fact]
    public async Task ExecuteMainLoopAsync_WithSystemDisabled_ShouldSkip()
    {
        // Arrange
        var systemConfig = new SystemConfiguration
        {
            SystemEnabled = false
        };
        _configurationServiceMock.Setup(x => x.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemConfig);

        // Act
        var result = await _service.ExecuteMainLoopAsync();

        // Assert
        Assert.True(result.IsSkipped);
        Assert.Equal("System wyłączony", result.SkipReason);
    }

    [Fact]
    public async Task ExecuteMainLoopAsync_WithSystemEnabled_ShouldExecuteAllPhases()
    {
        // Arrange
        var systemConfig = new SystemConfiguration
        {
            SystemEnabled = true,
            RoomsList = "sypialnia",
            TempReturnEntityId = "sensor.temp_return",
            Mixer4DPositionEntityId = "sensor.mixer_4d_position",
            EkoPiecDeviceSn = "ABC123"
        };
        _configurationServiceMock.Setup(x => x.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemConfig);

        var roomConfig = new RoomConfiguration
        {
            Name = "sypialnia",
            TempTarget = 21.0,
            TempTargetActive = 21.0,
            TempTargetInactive = 20.0,
            Priority = 1,
            Sensitive = false,
            AutomationDisabled = false,
            UsageSchedule = "Brak",
            HeatingSchedule = "Brak",
            SensorTemperatureEntityId = "sensor.sypialnia_temperature",
            ValveEntityId = "climate.sypialnia"
        };
        _configurationServiceMock.Setup(x => x.GetRoomAsync("sypialnia", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roomConfig);

        var parameters = new HeatingParameters();
        _configurationServiceMock.Setup(x => x.GetHeatingParametersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(parameters);

        // Mock dla LoadBoilerStateAsync
        _haClientMock.Setup(x => x.GetStateDoubleAsync("sensor.temp_return", It.IsAny<CancellationToken>()))
            .ReturnsAsync(50.0);

        _haClientMock.Setup(x => x.GetStateDoubleAsync("sensor.mixer_4d_position", It.IsAny<CancellationToken>()))
            .ReturnsAsync(50.0);

        _haClientMock.Setup(x => x.GetStateAsync("weather.home", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityState 
            { 
                State = "sunny",
                Attributes = new Dictionary<string, object> { { "temperature", 5.0 } }
            });

        // Mock dla GetRoomTemperatureAsync
        _haClientMock.Setup(x => x.GetStateDoubleAsync("sensor.sypialnia_temperature", It.IsAny<CancellationToken>()))
            .ReturnsAsync(20.0);

        // Act
        var result = await _service.ExecuteMainLoopAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.PhaseResults.Count); // Faza 0 + fazy 1-3 (fazy 4-5 są zakomentowane)
    }
}
