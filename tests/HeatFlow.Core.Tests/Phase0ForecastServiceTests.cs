using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.Database;
using HeatFlow.Infrastructure.HomeAssistant;
using HeatFlow.Infrastructure.OpenWeatherMap;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeatFlow.Core.Tests;

public class Phase0ForecastServiceTests
{
    private readonly Mock<IHomeAssistantClient> _haClientMock;
    private readonly Mock<IOpenWeatherMapClient> _openWeatherMapClientMock;
    private readonly Mock<IConfigurationService> _configurationServiceMock;
    private readonly Mock<IHeatFlowRepository> _repositoryMock;
    private readonly Mock<ILogger<Phase0ForecastService>> _loggerMock;
    private readonly Phase0ForecastService _service;

    public Phase0ForecastServiceTests()
    {
        _haClientMock = new Mock<IHomeAssistantClient>();
        _openWeatherMapClientMock = new Mock<IOpenWeatherMapClient>();
        _configurationServiceMock = new Mock<IConfigurationService>();
        _repositoryMock = new Mock<IHeatFlowRepository>();
        _loggerMock = new Mock<ILogger<Phase0ForecastService>>();
        _service = new Phase0ForecastService(
            _haClientMock.Object,
            _openWeatherMapClientMock.Object,
            _configurationServiceMock.Object,
            _repositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public void PhaseNumber_ShouldBe0()
    {
        Assert.Equal(0, _service.PhaseNumber);
    }

    [Fact]
    public async Task ExecuteAsync_WithTempDrop_ShouldSetPreHeatingMode()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>()
        };

        var parameters = new HeatingParameters
        {
            ForecastTempDropThreshold = 5.0,
            ForecastTempRiseThreshold = 3.0,
            ForecastHoursCount = 8,
            DeficitHighP1Base = 1.0,
            DeficitHighP2Base = 2.0,
            DeficitHighP3Base = 3.0,
            BufferPreparationBase = 0.8
        };

        var systemConfig = new SystemConfiguration
        {
            Id = 1,
            Latitude = 52.2297,
            Longitude = 21.0122
        };

        // Mock OpenWeatherMap response - aktualna 5°C, min -10°C (spadek 15°C)
        var weatherResponse = CreateOpenWeatherMapResponse(5.0, -10.0);

        _configurationServiceMock.Setup(x => x.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemConfig);

        // Mock repozytorium - brak cache (zwraca null)
        _repositoryMock.Setup(x => x.GetForecastDataCacheAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ForecastDataEntity?)null);

        _openWeatherMapClientMock.Setup(x => x.GetWeatherDataAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(weatherResponse);

        _repositoryMock.Setup(x => x.SaveForecastDataCacheAsync(It.IsAny<ForecastDataEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _haClientMock.Setup(x => x.SetInputNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _configurationServiceMock.Setup(x => x.UpdateHeatingParametersAsync(It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        _haClientMock.Verify(x => x.SetInputNumberValueAsync("input_number.forecast_mode", (int)ForecastMode.PreHeating, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoWeatherData_ShouldReturnError()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>()
        };

        var parameters = new HeatingParameters
        {
            ForecastTempDropThreshold = 5.0,
            ForecastTempRiseThreshold = 3.0,
            ForecastHoursCount = 8
        };

        var systemConfig = new SystemConfiguration
        {
            Id = 1,
            Latitude = 52.2297,
            Longitude = 21.0122
        };

        _configurationServiceMock.Setup(x => x.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemConfig);

        // Mock repozytorium - brak cache (zwraca null)
        _repositoryMock.Setup(x => x.GetForecastDataCacheAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ForecastDataEntity?)null);

        _openWeatherMapClientMock.Setup(x => x.GetWeatherDataAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((OpenWeatherMapResponse?)null);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("prognozy", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingCoordinates_ShouldReturnError()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>()
        };

        var parameters = new HeatingParameters
        {
            ForecastTempDropThreshold = 5.0,
            ForecastTempRiseThreshold = 3.0,
            ForecastHoursCount = 8
        };

        var systemConfig = new SystemConfiguration
        {
            Id = 1,
            Latitude = 0.0, // Nie skonfigurowane
            Longitude = 0.0  // Nie skonfigurowane
        };

        _configurationServiceMock.Setup(x => x.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemConfig);

        // Mock repozytorium - brak cache (zwraca null)
        _repositoryMock.Setup(x => x.GetForecastDataCacheAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ForecastDataEntity?)null);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("prognozy", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_WithValidCache_ShouldUseCache()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>()
        };

        var parameters = new HeatingParameters
        {
            ForecastTempDropThreshold = 5.0,
            ForecastTempRiseThreshold = 3.0,
            ForecastHoursCount = 8,
            DeficitHighP1Base = 1.0,
            DeficitHighP2Base = 2.0,
            DeficitHighP3Base = 3.0,
            BufferPreparationBase = 0.8
        };

        var systemConfig = new SystemConfiguration
        {
            Id = 1,
            Latitude = 52.2297,
            Longitude = 21.0122
        };

        // Cache ważny (< 1h)
        var cachedEntity = new ForecastDataEntity
        {
            Id = 1,
            Latitude = (decimal)systemConfig.Latitude,
            Longitude = (decimal)systemConfig.Longitude,
            CurrentTemp = 5.0m,
            ForecastHoursJson = System.Text.Json.JsonSerializer.Serialize(new List<ForecastHour>
            {
                new ForecastHour { DateTime = DateTime.UtcNow.AddHours(1), Temperature = 0.0 }
            }),
            TempDropThreshold = 5.0m,
            TempRiseThreshold = 3.0m,
            UpdatedAt = DateTime.UtcNow.AddMinutes(-30) // 30 minut temu
        };

        _configurationServiceMock.Setup(x => x.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemConfig);

        _repositoryMock.Setup(x => x.GetForecastDataCacheAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedEntity);

        _haClientMock.Setup(x => x.SetInputNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _configurationServiceMock.Setup(x => x.UpdateHeatingParametersAsync(It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Nie powinno być wywołania API
        _openWeatherMapClientMock.Verify(x => x.GetWeatherDataAsync(
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithStaleCache_ShouldUseAPI()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>()
        };

        var parameters = new HeatingParameters
        {
            ForecastTempDropThreshold = 5.0,
            ForecastTempRiseThreshold = 3.0,
            ForecastHoursCount = 8,
            DeficitHighP1Base = 1.0,
            DeficitHighP2Base = 2.0,
            DeficitHighP3Base = 3.0,
            BufferPreparationBase = 0.8
        };

        var systemConfig = new SystemConfiguration
        {
            Id = 1,
            Latitude = 52.2297,
            Longitude = 21.0122
        };

        // Cache przestarzały (> 1h)
        var cachedEntity = new ForecastDataEntity
        {
            Id = 1,
            Latitude = (decimal)systemConfig.Latitude,
            Longitude = (decimal)systemConfig.Longitude,
            CurrentTemp = 5.0m,
            ForecastHoursJson = "[]",
            TempDropThreshold = 5.0m,
            TempRiseThreshold = 3.0m,
            UpdatedAt = DateTime.UtcNow.AddHours(-2) // 2 godziny temu
        };

        var weatherResponse = CreateOpenWeatherMapResponse(5.0, 0.0);

        _configurationServiceMock.Setup(x => x.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemConfig);

        _repositoryMock.Setup(x => x.GetForecastDataCacheAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedEntity);

        _openWeatherMapClientMock.Setup(x => x.GetWeatherDataAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(weatherResponse);

        _repositoryMock.Setup(x => x.SaveForecastDataCacheAsync(It.IsAny<ForecastDataEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _haClientMock.Setup(x => x.SetInputNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _configurationServiceMock.Setup(x => x.UpdateHeatingParametersAsync(It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Powinno być wywołanie API
        _openWeatherMapClientMock.Verify(x => x.GetWeatherDataAsync(
            systemConfig.Latitude,
            systemConfig.Longitude,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithCacheSaveError_ShouldContinue()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>()
        };

        var parameters = new HeatingParameters
        {
            ForecastTempDropThreshold = 5.0,
            ForecastTempRiseThreshold = 3.0,
            ForecastHoursCount = 8,
            DeficitHighP1Base = 1.0,
            DeficitHighP2Base = 2.0,
            DeficitHighP3Base = 3.0,
            BufferPreparationBase = 0.8
        };

        var systemConfig = new SystemConfiguration
        {
            Id = 1,
            Latitude = 52.2297,
            Longitude = 21.0122
        };

        var weatherResponse = CreateOpenWeatherMapResponse(5.0, 0.0);

        _configurationServiceMock.Setup(x => x.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemConfig);

        _repositoryMock.Setup(x => x.GetForecastDataCacheAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ForecastDataEntity?)null);

        _openWeatherMapClientMock.Setup(x => x.GetWeatherDataAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(weatherResponse);

        // Błąd podczas zapisu cache
        _repositoryMock.Setup(x => x.SaveForecastDataCacheAsync(It.IsAny<ForecastDataEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database error"));

        _haClientMock.Setup(x => x.SetInputNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _configurationServiceMock.Setup(x => x.UpdateHeatingParametersAsync(It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success); // Nie powinien przerywać działania
    }

    [Fact]
    public async Task ExecuteAsync_WithNormalMode_ShouldRestoreBaseValues()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>()
        };

        var parameters = new HeatingParameters
        {
            ForecastTempDropThreshold = 5.0,
            ForecastTempRiseThreshold = 3.0,
            ForecastHoursCount = 8,
            DeficitHighP1Base = 1.0,
            DeficitHighP2Base = 2.0,
            DeficitHighP3Base = 3.0,
            BufferPreparationBase = 0.8
        };

        var systemConfig = new SystemConfiguration
        {
            Id = 1,
            Latitude = 52.2297,
            Longitude = 21.0122
        };

        // tempDiff powinien być w zakresie Normal (między -tempDropThreshold a tempRiseThreshold)
        // currentTemp = 5.0, minTemp w prognozie powinien być taki, żeby tempDiff był między -5 a 3
        // tempDiff = minTemp - currentTemp, więc dla tempDiff = -2: minTemp = 3.0
        // Ale CreateOpenWeatherMapResponse(5.0, 3.0) tworzy prognozę gdzie pierwsza temperatura to currentTemp - 5 = 0.0
        // Więc minTemp będzie 0.0 (najmniejsza w prognozie), a tempDiff = 0.0 - 5.0 = -5.0
        // To jest dokładnie równy -tempDropThreshold, więc PreHeating
        // Muszę stworzyć prognozę gdzie minTemp jest większy, np. 3.0
        // tempDiff powinien być w zakresie Normal (między -tempDropThreshold a tempRiseThreshold)
        // currentTemp = 5.0, minTemp w prognozie powinien być taki, żeby tempDiff był między -5 a 3
        // tempDiff = minTemp - currentTemp, więc dla tempDiff = -2: minTemp = 3.0
        // CreateOpenWeatherMapResponse(5.0, 3.0) tworzy prognozę gdzie pierwsza temperatura to currentTemp - 5 = 0.0
        // Więc minTemp będzie 0.0 (najmniejsza w prognozie), a tempDiff = 0.0 - 5.0 = -5.0 (PreHeating)
        // Muszę stworzyć prognozę gdzie wszystkie temperatury są >= 3.0, żeby minTemp był 3.0
        var weatherResponse = CreateOpenWeatherMapResponseForNormalMode(5.0, 3.0);

        _configurationServiceMock.Setup(x => x.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemConfig);

        _repositoryMock.Setup(x => x.GetForecastDataCacheAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ForecastDataEntity?)null);

        _openWeatherMapClientMock.Setup(x => x.GetWeatherDataAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(weatherResponse);

        _repositoryMock.Setup(x => x.SaveForecastDataCacheAsync(It.IsAny<ForecastDataEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _haClientMock.Setup(x => x.SetInputNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _configurationServiceMock.Setup(x => x.UpdateHeatingParametersAsync(It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // SetInputNumberValueAsync jest wywoływane dla wszystkich trybów, w tym Normal (wartość 0)
        _haClientMock.Verify(x => x.SetInputNumberValueAsync("input_number.forecast_mode", (double)(int)ForecastMode.Normal, It.IsAny<CancellationToken>()), Times.Once);
        // Powinno przywrócić wartości bazowe - sprawdzam czy zostało wywołane UpdateHeatingParametersAsync
        _configurationServiceMock.Verify(x => x.UpdateHeatingParametersAsync(
            It.IsAny<HeatingParameters>(),
            It.IsAny<CancellationToken>()), Times.Once);
        // Sprawdzam wartości bezpośrednio w obiekcie parameters (są modyfikowane w miejscu)
        Assert.Equal(parameters.DeficitHighP1Base, parameters.DeficitHighP1);
        Assert.Equal(parameters.DeficitHighP2Base, parameters.DeficitHighP2);
        Assert.Equal(parameters.DeficitHighP3Base, parameters.DeficitHighP3);
        Assert.Equal(parameters.BufferPreparationBase, parameters.BufferPreparation);
    }

    [Fact]
    public async Task ExecuteAsync_WithReductionMode_ShouldApplyMultipliers()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>()
        };

        var parameters = new HeatingParameters
        {
            ForecastTempDropThreshold = 5.0,
            ForecastTempRiseThreshold = 3.0,
            ForecastHoursCount = 8,
            DeficitHighP1Base = 1.0,
            DeficitHighP2Base = 2.0,
            DeficitHighP3Base = 3.0,
            BufferPreparationBase = 0.8,
            ForecastReductionP1Multiplier = 0.8,
            ForecastReductionP2Multiplier = 0.8,
            ForecastReductionP3Multiplier = 0.8,
            ForecastReductionBufferMultiplier = 0.8
        };

        var systemConfig = new SystemConfiguration
        {
            Id = 1,
            Latitude = 52.2297,
            Longitude = 21.0122
        };

        // tempDiff powinien być >= tempRiseThreshold (3°C) dla Reduction mode
        // currentTemp = 5.0, minTemp w prognozie powinien być taki, żeby tempDiff >= 3.0
        // tempDiff = minTemp - currentTemp, więc dla tempDiff = 5: minTemp = 10.0
        // CreateOpenWeatherMapResponse(5.0, 10.0) tworzy prognozę gdzie pierwsza temperatura to currentTemp - 5 = 0.0
        // Więc minTemp będzie 0.0 (najmniejsza w prognozie), a tempDiff = 0.0 - 5.0 = -5.0 (PreHeating)
        // Muszę stworzyć prognozę gdzie wszystkie temperatury są >= 10.0, żeby minTemp był 10.0
        var weatherResponse = CreateOpenWeatherMapResponseForReductionMode(5.0, 10.0);

        _configurationServiceMock.Setup(x => x.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemConfig);

        _repositoryMock.Setup(x => x.GetForecastDataCacheAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ForecastDataEntity?)null);

        _openWeatherMapClientMock.Setup(x => x.GetWeatherDataAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(weatherResponse);

        _repositoryMock.Setup(x => x.SaveForecastDataCacheAsync(It.IsAny<ForecastDataEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _haClientMock.Setup(x => x.SetInputNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _configurationServiceMock.Setup(x => x.UpdateHeatingParametersAsync(It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        _haClientMock.Verify(x => x.SetInputNumberValueAsync("input_number.forecast_mode", (int)ForecastMode.Reduction, It.IsAny<CancellationToken>()), Times.Once);
        // Powinno zastosować mnożniki - sprawdzam czy zostało wywołane UpdateHeatingParametersAsync
        _configurationServiceMock.Verify(x => x.UpdateHeatingParametersAsync(
            It.IsAny<HeatingParameters>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithExactThresholdDrop_ShouldSetPreHeating()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>()
        };

        var parameters = new HeatingParameters
        {
            ForecastTempDropThreshold = 5.0,
            ForecastTempRiseThreshold = 3.0,
            ForecastHoursCount = 8,
            DeficitHighP1Base = 1.0,
            DeficitHighP2Base = 2.0,
            DeficitHighP3Base = 3.0,
            BufferPreparationBase = 0.8
        };

        var systemConfig = new SystemConfiguration
        {
            Id = 1,
            Latitude = 52.2297,
            Longitude = 21.0122
        };

        // tempDiff dokładnie równy -tempDropThreshold (-5.0)
        var weatherResponse = CreateOpenWeatherMapResponse(5.0, 0.0);

        _configurationServiceMock.Setup(x => x.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemConfig);

        _repositoryMock.Setup(x => x.GetForecastDataCacheAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ForecastDataEntity?)null);

        _openWeatherMapClientMock.Setup(x => x.GetWeatherDataAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(weatherResponse);

        _repositoryMock.Setup(x => x.SaveForecastDataCacheAsync(It.IsAny<ForecastDataEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _haClientMock.Setup(x => x.SetInputNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _configurationServiceMock.Setup(x => x.UpdateHeatingParametersAsync(It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        _haClientMock.Verify(x => x.SetInputNumberValueAsync("input_number.forecast_mode", (int)ForecastMode.PreHeating, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithExactThresholdRise_ShouldSetReduction()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>()
        };

        var parameters = new HeatingParameters
        {
            ForecastTempDropThreshold = 5.0,
            ForecastTempRiseThreshold = 3.0,
            ForecastHoursCount = 8,
            DeficitHighP1Base = 1.0,
            DeficitHighP2Base = 2.0,
            DeficitHighP3Base = 3.0,
            BufferPreparationBase = 0.8
        };

        var systemConfig = new SystemConfiguration
        {
            Id = 1,
            Latitude = 52.2297,
            Longitude = 21.0122
        };

        // tempDiff dokładnie równy tempRiseThreshold (3.0)
        // currentTemp = 5.0, minTemp w prognozie powinien być 8.0 (tempDiff = 8.0 - 5.0 = 3.0)
        // CreateOpenWeatherMapResponse(5.0, 8.0) tworzy prognozę gdzie pierwsza temperatura to currentTemp - 5 = 0.0
        // Więc minTemp będzie 0.0 (najmniejsza w prognozie), a tempDiff = 0.0 - 5.0 = -5.0 (PreHeating)
        // Muszę stworzyć prognozę gdzie wszystkie temperatury są >= 8.0, żeby minTemp był 8.0
        var weatherResponse = CreateOpenWeatherMapResponseForReductionMode(5.0, 8.0);

        _configurationServiceMock.Setup(x => x.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemConfig);

        _repositoryMock.Setup(x => x.GetForecastDataCacheAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ForecastDataEntity?)null);

        _openWeatherMapClientMock.Setup(x => x.GetWeatherDataAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(weatherResponse);

        _repositoryMock.Setup(x => x.SaveForecastDataCacheAsync(It.IsAny<ForecastDataEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _haClientMock.Setup(x => x.SetInputNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _configurationServiceMock.Setup(x => x.UpdateHeatingParametersAsync(It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        _haClientMock.Verify(x => x.SetInputNumberValueAsync("input_number.forecast_mode", (int)ForecastMode.Reduction, It.IsAny<CancellationToken>()), Times.Once);
        // Powinno zastosować mnożniki - sprawdzam czy zostało wywołane UpdateHeatingParametersAsync
        _configurationServiceMock.Verify(x => x.UpdateHeatingParametersAsync(
            It.IsAny<HeatingParameters>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyForecastHours_ShouldUseCurrentTemp()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>()
        };

        var parameters = new HeatingParameters
        {
            ForecastTempDropThreshold = 5.0,
            ForecastTempRiseThreshold = 3.0,
            ForecastHoursCount = 8,
            DeficitHighP1Base = 1.0,
            DeficitHighP2Base = 2.0,
            DeficitHighP3Base = 3.0,
            BufferPreparationBase = 0.8
        };

        var systemConfig = new SystemConfiguration
        {
            Id = 1,
            Latitude = 52.2297,
            Longitude = 21.0122
        };

        // Pusta lista prognoz godzinowych
        var weatherResponse = new OpenWeatherMapResponse
        {
            Latitude = 52.2297,
            Longitude = 21.0122,
            Current = new CurrentWeather
            {
                DateTimeUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Temperature = 5.0
            },
            Hourly = new List<HourlyForecast>() // Pusta lista
        };

        _configurationServiceMock.Setup(x => x.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemConfig);

        _repositoryMock.Setup(x => x.GetForecastDataCacheAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ForecastDataEntity?)null);

        _openWeatherMapClientMock.Setup(x => x.GetWeatherDataAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(weatherResponse);

        _repositoryMock.Setup(x => x.SaveForecastDataCacheAsync(It.IsAny<ForecastDataEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _haClientMock.Setup(x => x.SetInputNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _configurationServiceMock.Setup(x => x.UpdateHeatingParametersAsync(It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Powinno użyć CurrentTemp jako min temp (tempDiff = 0)
        _haClientMock.Verify(x => x.SetInputNumberValueAsync("input_number.forecast_mode", (int)ForecastMode.Normal, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithCacheReadError_ShouldContinueWithAPI()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>()
        };

        var parameters = new HeatingParameters
        {
            ForecastTempDropThreshold = 5.0,
            ForecastTempRiseThreshold = 3.0,
            ForecastHoursCount = 8,
            DeficitHighP1Base = 1.0,
            DeficitHighP2Base = 2.0,
            DeficitHighP3Base = 3.0,
            BufferPreparationBase = 0.8
        };

        var systemConfig = new SystemConfiguration
        {
            Id = 1,
            Latitude = 52.2297,
            Longitude = 21.0122
        };

        var weatherResponse = CreateOpenWeatherMapResponse(5.0, 0.0);

        _configurationServiceMock.Setup(x => x.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemConfig);

        // Błąd podczas pobierania cache
        _repositoryMock.Setup(x => x.GetForecastDataCacheAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database error"));

        _openWeatherMapClientMock.Setup(x => x.GetWeatherDataAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(weatherResponse);

        _repositoryMock.Setup(x => x.SaveForecastDataCacheAsync(It.IsAny<ForecastDataEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _haClientMock.Setup(x => x.SetInputNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _configurationServiceMock.Setup(x => x.UpdateHeatingParametersAsync(It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Powinno kontynuować z API
        _openWeatherMapClientMock.Verify(x => x.GetWeatherDataAsync(
            systemConfig.Latitude,
            systemConfig.Longitude,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullCurrentWeather_ShouldReturnError()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>()
        };

        var parameters = new HeatingParameters
        {
            ForecastTempDropThreshold = 5.0,
            ForecastTempRiseThreshold = 3.0,
            ForecastHoursCount = 8
        };

        var systemConfig = new SystemConfiguration
        {
            Id = 1,
            Latitude = 52.2297,
            Longitude = 21.0122
        };

        // WeatherData.Current == null
        var weatherResponse = new OpenWeatherMapResponse
        {
            Latitude = 52.2297,
            Longitude = 21.0122,
            Current = null,
            Hourly = new List<HourlyForecast>()
        };

        _configurationServiceMock.Setup(x => x.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemConfig);

        _repositoryMock.Setup(x => x.GetForecastDataCacheAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ForecastDataEntity?)null);

        _openWeatherMapClientMock.Setup(x => x.GetWeatherDataAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(weatherResponse);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("prognozy", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_WithNullHourly_ShouldUseCurrentTemp()
    {
        // Arrange
        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = false,
            Rooms = new List<Room>()
        };

        var parameters = new HeatingParameters
        {
            ForecastTempDropThreshold = 5.0,
            ForecastTempRiseThreshold = 3.0,
            ForecastHoursCount = 8,
            DeficitHighP1Base = 1.0,
            DeficitHighP2Base = 2.0,
            DeficitHighP3Base = 3.0,
            BufferPreparationBase = 0.8
        };

        var systemConfig = new SystemConfiguration
        {
            Id = 1,
            Latitude = 52.2297,
            Longitude = 21.0122
        };

        // WeatherData.Hourly == null
        var weatherResponse = new OpenWeatherMapResponse
        {
            Latitude = 52.2297,
            Longitude = 21.0122,
            Current = new CurrentWeather
            {
                DateTimeUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Temperature = 5.0
            },
            Hourly = null!
        };

        _configurationServiceMock.Setup(x => x.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemConfig);

        _repositoryMock.Setup(x => x.GetForecastDataCacheAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ForecastDataEntity?)null);

        _openWeatherMapClientMock.Setup(x => x.GetWeatherDataAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(weatherResponse);

        _repositoryMock.Setup(x => x.SaveForecastDataCacheAsync(It.IsAny<ForecastDataEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _haClientMock.Setup(x => x.SetInputNumberValueAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _configurationServiceMock.Setup(x => x.UpdateHeatingParametersAsync(It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(state, parameters);

        // Assert
        Assert.True(result.Success);
        // Powinno użyć CurrentTemp jako min temp (tempDiff = 0)
        _haClientMock.Verify(x => x.SetInputNumberValueAsync("input_number.forecast_mode", (int)ForecastMode.Normal, It.IsAny<CancellationToken>()), Times.Once);
    }

    private OpenWeatherMapResponse CreateOpenWeatherMapResponse(double currentTemp, double minTemp)
    {
        var now = DateTimeOffset.UtcNow;
        var hourlyForecasts = new List<HourlyForecast>
        {
            new HourlyForecast
            {
                DateTimeUnix = now.AddHours(1).ToUnixTimeSeconds(),
                Temperature = currentTemp - 5
            },
            new HourlyForecast
            {
                DateTimeUnix = now.AddHours(2).ToUnixTimeSeconds(),
                Temperature = minTemp
            },
            new HourlyForecast
            {
                DateTimeUnix = now.AddHours(3).ToUnixTimeSeconds(),
                Temperature = minTemp + 2
            },
            new HourlyForecast
            {
                DateTimeUnix = now.AddHours(4).ToUnixTimeSeconds(),
                Temperature = minTemp + 5
            }
        };

        return new OpenWeatherMapResponse
        {
            Latitude = 52.2297,
            Longitude = 21.0122,
            Current = new CurrentWeather
            {
                DateTimeUnix = now.ToUnixTimeSeconds(),
                Temperature = currentTemp
            },
            Hourly = hourlyForecasts
        };
    }

    private OpenWeatherMapResponse CreateOpenWeatherMapResponseForNormalMode(double currentTemp, double minTempInForecast)
    {
        var now = DateTimeOffset.UtcNow;
        // Tworzę prognozę gdzie wszystkie temperatury są >= minTempInForecast, żeby minTemp był minTempInForecast
        var hourlyForecasts = new List<HourlyForecast>
        {
            new HourlyForecast
            {
                DateTimeUnix = now.AddHours(1).ToUnixTimeSeconds(),
                Temperature = minTempInForecast // Pierwsza temperatura = minTemp
            },
            new HourlyForecast
            {
                DateTimeUnix = now.AddHours(2).ToUnixTimeSeconds(),
                Temperature = minTempInForecast + 1
            },
            new HourlyForecast
            {
                DateTimeUnix = now.AddHours(3).ToUnixTimeSeconds(),
                Temperature = minTempInForecast + 2
            },
            new HourlyForecast
            {
                DateTimeUnix = now.AddHours(4).ToUnixTimeSeconds(),
                Temperature = minTempInForecast + 3
            }
        };

        return new OpenWeatherMapResponse
        {
            Latitude = 52.2297,
            Longitude = 21.0122,
            Current = new CurrentWeather
            {
                DateTimeUnix = now.ToUnixTimeSeconds(),
                Temperature = currentTemp
            },
            Hourly = hourlyForecasts
        };
    }

    private OpenWeatherMapResponse CreateOpenWeatherMapResponseForReductionMode(double currentTemp, double minTempInForecast)
    {
        var now = DateTimeOffset.UtcNow;
        // Tworzę prognozę gdzie wszystkie temperatury są >= minTempInForecast, żeby minTemp był minTempInForecast
        // Dla Reduction mode, minTemp powinien być większy niż currentTemp
        var hourlyForecasts = new List<HourlyForecast>
        {
            new HourlyForecast
            {
                DateTimeUnix = now.AddHours(1).ToUnixTimeSeconds(),
                Temperature = minTempInForecast // Pierwsza temperatura = minTemp
            },
            new HourlyForecast
            {
                DateTimeUnix = now.AddHours(2).ToUnixTimeSeconds(),
                Temperature = minTempInForecast + 1
            },
            new HourlyForecast
            {
                DateTimeUnix = now.AddHours(3).ToUnixTimeSeconds(),
                Temperature = minTempInForecast + 2
            },
            new HourlyForecast
            {
                DateTimeUnix = now.AddHours(4).ToUnixTimeSeconds(),
                Temperature = minTempInForecast + 3
            }
        };

        return new OpenWeatherMapResponse
        {
            Latitude = 52.2297,
            Longitude = 21.0122,
            Current = new CurrentWeather
            {
                DateTimeUnix = now.ToUnixTimeSeconds(),
                Temperature = currentTemp
            },
            Hourly = hourlyForecasts
        };
    }
}
