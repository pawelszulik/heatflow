using HeatFlow.Domain;
using HeatFlow.Infrastructure.Database;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Infrastructure.Configuration;

/// <summary>
/// Implementacja serwisu konfiguracji z cache'owaniem w pamięci.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private readonly IHeatFlowRepository _repository;
    private readonly ILogger<ConfigurationService> _logger;
    private HeatingParameters? _cachedParameters;
    private SystemConfiguration? _cachedSystemConfig;
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheTimeout = TimeSpan.FromMinutes(5);

    public ConfigurationService(
        IHeatFlowRepository repository,
        ILogger<ConfigurationService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<HeatingParameters> GetHeatingParametersAsync(CancellationToken cancellationToken = default)
    {
        // Sprawdź cache
        if (_cachedParameters != null && DateTime.UtcNow - _lastCacheUpdate < _cacheTimeout)
        {
            return _cachedParameters;
        }

        try
        {
            var entity = await _repository.GetHeatingParametersAsync(cancellationToken);
            if (entity != null)
            {
                _cachedParameters = entity.ToHeatingParameters();
                _lastCacheUpdate = DateTime.UtcNow;
                return _cachedParameters;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd podczas odczytu parametrów z bazy, używam wartości domyślnych");
        }

        // Zwróć wartości domyślne jeśli baza jest pusta lub wystąpił błąd
        return GetDefaultHeatingParameters();
    }

    public async Task SaveHeatingParametersAsync(HeatingParameters parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await _repository.GetHeatingParametersAsync(cancellationToken);
            if (entity == null)
            {
                entity = new HeatingParametersEntity { Id = 1 };
            }

            entity.UpdateFrom(parameters);
            await _repository.SaveHeatingParametersAsync(entity, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            // Odśwież cache
            _cachedParameters = parameters;
            _lastCacheUpdate = DateTime.UtcNow;

            _logger.LogInformation("Zapisano parametry algorytmu do bazy danych");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas zapisu parametrów do bazy");
            throw;
        }
    }

    public async Task UpdateHeatingParametersAsync(HeatingParameters parameters, CancellationToken cancellationToken = default)
    {
        // To samo co SaveHeatingParametersAsync, ale z logowaniem jako aktualizacja
        await SaveHeatingParametersAsync(parameters, cancellationToken);
        _logger.LogDebug("Zaktualizowano parametry algorytmu w bazie danych (Faza 0)");
    }

    public async Task<List<RoomConfiguration>> GetAllRoomsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _repository.GetRoomConfigurationsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas odczytu konfiguracji pokoi z bazy");
            return new List<RoomConfiguration>();
        }
    }

    public async Task<RoomConfiguration?> GetRoomAsync(string roomName, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _repository.GetRoomConfigurationAsync(roomName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd podczas odczytu konfiguracji pokoju {Room} z bazy", roomName);
            return null;
        }
    }

    public async Task SaveRoomAsync(RoomConfiguration roomConfig, CancellationToken cancellationToken = default)
    {
        try
        {
            await _repository.SaveRoomConfigurationAsync(roomConfig, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Zapisano konfigurację pokoju {Room} do bazy danych", roomConfig.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas zapisu konfiguracji pokoju {Room} do bazy", roomConfig.Name);
            throw;
        }
    }

    public async Task<SystemConfiguration> GetSystemConfigurationAsync(CancellationToken cancellationToken = default)
    {
        // Sprawdź cache
        if (_cachedSystemConfig != null && DateTime.UtcNow - _lastCacheUpdate < _cacheTimeout)
        {
            return _cachedSystemConfig;
        }

        try
        {
            var config = await _repository.GetSystemConfigurationAsync(cancellationToken);
            if (config != null)
            {
                _cachedSystemConfig = config;
                _lastCacheUpdate = DateTime.UtcNow;
                return config;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd podczas odczytu konfiguracji systemowej z bazy, używam wartości domyślnych");
        }

        // Zwróć domyślną konfigurację jeśli baza jest pusta
        var defaultConfig = GetDefaultSystemConfiguration();
        _cachedSystemConfig = defaultConfig;
        return defaultConfig;
    }

    public async Task SaveSystemConfigurationAsync(SystemConfiguration systemConfig, CancellationToken cancellationToken = default)
    {
        try
        {
            await _repository.SaveSystemConfigurationAsync(systemConfig, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            // Odśwież cache
            _cachedSystemConfig = systemConfig;
            _lastCacheUpdate = DateTime.UtcNow;

            _logger.LogInformation("Zapisano konfigurację systemową do bazy danych");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas zapisu konfiguracji systemowej do bazy");
            throw;
        }
    }

    private HeatingParameters GetDefaultHeatingParameters()
    {
        return new HeatingParameters
        {
            // Progi deficytów
            DeficitHighP1 = 1.0,
            DeficitHighP2 = 2.0,
            DeficitHighP3 = 3.0,

            // Wartości bazowe
            DeficitHighP1Base = 1.0,
            DeficitHighP2Base = 2.0,
            DeficitHighP3Base = 3.0,
            BufferPreparationBase = 0.8,

            // Bufor przygotowania
            BufferPreparation = 0.8,
            BufferHeatingTime = 60,

            // Parametry prognozy
            ForecastTempDropThreshold = 5.0,
            ForecastTempRiseThreshold = 3.0,
            ForecastHoursCount = 8,
            ForecastPreHeatingP1Multiplier = 0.8,
            ForecastPreHeatingP2Multiplier = 0.9,
            ForecastPreHeatingP3Multiplier = 0.9,
            ForecastPreHeatingBufferMultiplier = 1.2,
            ForecastReductionP1Multiplier = 1.2,
            ForecastReductionP2Multiplier = 1.2,
            ForecastReductionP3Multiplier = 1.2,
            ForecastReductionBufferMultiplier = 0.8,

            // Parametry arbitrażu
            MaxValvesOpen = 5,
            MinValvesOpen = 1,
            UsageSoonMinutes = 30,
            ScorePriorityMultiplier = 100,
            ScoreDeficitMultiplier = 10,
            ScoreSensitiveBonus = 50,
            ScoreUsageSoonBonus = 20,
            ScoreHeatingScheduleBonus = 50,
            ScoreThresholdMax = 50.0,
            ScoreThresholdDisabled = 0.0,
            MinDwellMinutes = 20,

            // Parametry zaworów
            ValveTolerance = 0.1,
            ValveRetryCount = 3,
            ValveRetryDelay = 1.0,

            // Parametry pieca
            MinReturnTemp = 45.0,
            BoilerNominalTemp = 70.0,
            FrostCompensationFactor = 0.5,
            Mixer4DDefault = 50.0,
            FeederTimeDefault = 30.0,
            FeederBoostMultiplier = 1.2,
            FeederEconomyMultiplier = 0.8,
            FeederNormalMultiplier = 1.0,
            FeederBoostThreshold = 5,
            FeederEconomyThreshold = 2,
            BoilerTempTolerance = 0.5,
            FeederTimeTolerance = 1.0,
            BoilerRetryCount = 3,
            BoilerRetryDelay = 1.0,

            // Parametry bezpieczeństwa
            MinTempDiff = 20.0,
            MinMixer4D = 20.0,
            Hysteresis = 0.5,
            HysteresisSafetyThreshold = 2.0,
            TempValidationMin = 0.0,
            TempValidationMax = 40.0
        };
    }

    private SystemConfiguration GetDefaultSystemConfiguration()
    {
        return new SystemConfiguration
        {
            Id = 1,
            RoomsList = string.Empty,
            EkoPiecDeviceSn = string.Empty,
            TempReturnEntityId = "sensor.temp_return",
            Mixer4DPositionEntityId = "sensor.mixer_4d_position",
            BoilerTempEntityId = null,
            FeederTimeEntityId = null,
            SystemEnabled = true
        };
    }
}
