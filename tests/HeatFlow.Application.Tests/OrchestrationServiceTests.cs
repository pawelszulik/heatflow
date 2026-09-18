using HeatFlow.Application;
using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.Database;
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
    private readonly Mock<IHeatFlowRepository> _repositoryMock;
    private readonly Mock<IPhaseService> _phase4Mock;
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

        _phase4Mock = new Mock<IPhaseService>();
        _phase4Mock.Setup(x => x.PhaseNumber).Returns(4);
        _phase4Mock.Setup(x => x.ExecuteAsync(It.IsAny<HeatingState>(), It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PhaseResult.SuccessResult(4, 250));

        _phaseServices = new List<IPhaseService>
        {
            phase0Mock.Object,
            phase1Mock.Object,
            phase2Mock.Object,
            phase3Mock.Object,
            _phase4Mock.Object
        };

        var errorLoggerMock = new Mock<IApplicationErrorLogger>();
        errorLoggerMock.Setup(x => x.LogAsync(It.IsAny<Exception?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        errorLoggerMock.Setup(x => x.LogAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Nieskonfigurowany mock zwraca null z GetForecastDataCacheAsync -> brak prognozy w stanie
        _repositoryMock = new Mock<IHeatFlowRepository>();

        _service = new OrchestrationService(_haClientMock.Object, _configurationServiceMock.Object, _phaseServices, _loggerMock.Object, errorLoggerMock.Object, _repositoryMock.Object);
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
        SetupEnabledSystem();

        // Act
        var result = await _service.ExecuteMainLoopAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.PhaseResults.Count); // Faza 0 + fazy 1-4
        _phase4Mock.Verify(x => x.ExecuteAsync(It.Is<HeatingState>(st => st.Forecast == null),
            It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteMainLoopAsync_WithFreshForecastCache_ShouldPopulateStateForecast()
    {
        // Arrange
        var systemConfig = SetupEnabledSystem(latitude: 52.2297, longitude: 21.0122);
        _repositoryMock
            .Setup(x => x.GetForecastDataCacheAsync(systemConfig.Latitude, systemConfig.Longitude, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildForecastEntity(systemConfig, updatedAt: DateTime.UtcNow.AddHours(-1), maxTemp: 23.5));

        // Act
        var result = await _service.ExecuteMainLoopAsync();

        // Assert
        Assert.True(result.IsSuccess);
        _phase4Mock.Verify(x => x.ExecuteAsync(
            It.Is<HeatingState>(st => st.Forecast != null && st.Forecast.GetMaxTemp(24) == 23.5),
            It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteMainLoopAsync_WithStaleForecastCache_ShouldLeaveForecastNull()
    {
        // Arrange - cache starszy niż 6h
        var systemConfig = SetupEnabledSystem(latitude: 52.2297, longitude: 21.0122);
        _repositoryMock
            .Setup(x => x.GetForecastDataCacheAsync(systemConfig.Latitude, systemConfig.Longitude, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildForecastEntity(systemConfig, updatedAt: DateTime.UtcNow.AddHours(-7), maxTemp: 23.5));

        // Act
        var result = await _service.ExecuteMainLoopAsync();

        // Assert
        Assert.True(result.IsSuccess);
        _phase4Mock.Verify(x => x.ExecuteAsync(It.Is<HeatingState>(st => st.Forecast == null),
            It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteMainLoopAsync_WhenForecastRepositoryThrows_ShouldStillExecutePhases()
    {
        // Arrange
        SetupEnabledSystem(latitude: 52.2297, longitude: 21.0122);
        _repositoryMock
            .Setup(x => x.GetForecastDataCacheAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB down"));

        // Act
        var result = await _service.ExecuteMainLoopAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.PhaseResults.Count);
        _phase4Mock.Verify(x => x.ExecuteAsync(It.Is<HeatingState>(st => st.Forecast == null),
            It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- Helpers ---

    private static ForecastDataEntity BuildForecastEntity(SystemConfiguration systemConfig, DateTime updatedAt, double maxTemp)
    {
        return new ForecastDataEntity
        {
            Id = 1,
            Latitude = (decimal)systemConfig.Latitude,
            Longitude = (decimal)systemConfig.Longitude,
            CurrentTemp = 10.0m,
            ForecastHoursJson = System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(0, 24)
                .Select(i => new ForecastHour
                {
                    DateTime = DateTime.UtcNow.AddHours(i),
                    Temperature = i == 12 ? maxTemp : maxTemp - 5.0
                })
                .ToList()),
            TempDropThreshold = 5.0m,
            TempRiseThreshold = 3.0m,
            UpdatedAt = updatedAt
        };
    }

    /// <summary>Konfiguracja włączonego systemu z jednym pokojem i mockami HA dla stanu kotła.</summary>
    private SystemConfiguration SetupEnabledSystem(double latitude = 0.0, double longitude = 0.0)
    {
        var systemConfig = new SystemConfiguration
        {
            SystemEnabled = true,
            RoomsList = "sypialnia",
            TempReturnEntityId = "sensor.temp_return",
            Mixer4DPositionEntityId = "sensor.mixer_4d_position",
            EkoPiecDeviceSn = "ABC123",
            Latitude = latitude,
            Longitude = longitude
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

        return systemConfig;
    }
}
