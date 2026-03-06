using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Application;

/// <summary>
/// Serwis orkiestracji wykonania faz algorytmu sterowania grzaniem.
/// </summary>
public class OrchestrationService
{
    private readonly IHomeAssistantClient _haClient;
    private readonly IConfigurationService _configurationService;
    private readonly IPhaseService _phase0;
    private readonly IPhaseService _phase1;
    private readonly IPhaseService _phase2;
    private readonly IPhaseService _phase3;
    private readonly IPhaseService _phase4;
    private readonly DataPersistenceService? _dataPersistenceService;
    private readonly IApplicationErrorLogger _errorLogger;
    private readonly ILogger<OrchestrationService> _logger;
    private DateTime _lastPhase0Execution = DateTime.MinValue;

    public OrchestrationService(
        IHomeAssistantClient haClient,
        IConfigurationService configurationService,
        IEnumerable<IPhaseService> phaseServices,
        ILogger<OrchestrationService> logger,
        IApplicationErrorLogger errorLogger,
        DataPersistenceService? dataPersistenceService = null)
    {
        _haClient = haClient;
        _configurationService = configurationService;
        _logger = logger;
        _errorLogger = errorLogger;
        _dataPersistenceService = dataPersistenceService;

        var phases = phaseServices.ToDictionary(p => p.PhaseNumber);
        _phase0 = phases[0];
        _phase1 = phases[1];
        _phase2 = phases[2];
        _phase3 = phases[3];
        _phase4 = phases[4];
    }

    /// <summary>
    /// Wykonuje główną pętlę (fazy 1-5).
    /// </summary>
    public async Task<ExecutionResult> ExecuteMainLoopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Sprawdź czy system jest włączony
            SystemConfiguration systemConfig = await _configurationService.GetSystemConfigurationAsync(cancellationToken);
            if (!systemConfig.SystemEnabled)
            {
                _logger.LogInformation("System grzania jest wyłączony");
                return ExecutionResult.Skipped("System wyłączony");
            }

            // Załaduj stan i parametry
            HeatingState state = await LoadHeatingStateAsync(cancellationToken);
            var parameters = await LoadHeatingParametersAsync(cancellationToken);

            // Wykonaj fazy
            List<PhaseResult> results = new List<PhaseResult>();

            var result0 = await ExecutePhase0IfNeededAsync(cancellationToken);
            if (result0 != null)
                results.Add(result0);

            var result1 = await _phase1.ExecuteAsync(state, parameters, cancellationToken);
            results.Add(result1);
            await Task.Delay(2000, cancellationToken); // Opóźnienie 2s między fazami

            var result2 = await _phase2.ExecuteAsync(state, parameters, cancellationToken);
            results.Add(result2);
            await Task.Delay(2000, cancellationToken);

            var result3 = await _phase3.ExecuteAsync(state, parameters, cancellationToken);
            results.Add(result3);
            await Task.Delay(2000, cancellationToken);

            var result4 = await _phase4.ExecuteAsync(state, parameters, cancellationToken);
            results.Add(result4);

            // Zapisz wyniki do bazy danych (jeśli serwis jest dostępny)
            if (_dataPersistenceService != null)
            {
                await _dataPersistenceService.SaveExecutionResultsAsync(results, state, cancellationToken);
            }

            var allSuccess = results.All(r => r.Success);
            return ExecutionResult.Success(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas wykonania głównej pętli");
            await _errorLogger.LogAsync(ex, null, nameof(OrchestrationService), null, "Error", "Console", cancellationToken);
            return ExecutionResult.Error(ex.Message);
        }
    }

    /// <summary>
    /// Wykonuje Fazę 0 (prognoza pogody) jeśli minęła godzina od ostatniego wykonania.
    /// </summary>
    public async Task<PhaseResult?> ExecutePhase0IfNeededAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastPhase0Execution).TotalHours < 1)
        {
            return null; // Nie minęła jeszcze godzina
        }

        try
        {
            var systemConfig = await _configurationService.GetSystemConfigurationAsync(cancellationToken);
            if (!systemConfig.SystemEnabled)
            {
                return null;
            }

            var state = await LoadHeatingStateAsync(cancellationToken);
            var parameters = await LoadHeatingParametersAsync(cancellationToken);

            var result = await _phase0.ExecuteAsync(state, parameters, cancellationToken);
            _lastPhase0Execution = now;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas wykonania Fazę 0");
            await _errorLogger.LogAsync(ex, 0, nameof(OrchestrationService), null, "Error", "Console", cancellationToken);
            return PhaseResult.ErrorResult(0, ex.Message);
        }
    }

    private async Task<HeatingState> LoadHeatingStateAsync(CancellationToken cancellationToken)
    {
        // Pobierz konfigurację systemową
        var systemConfig = await _configurationService.GetSystemConfigurationAsync(cancellationToken);
        
        var rooms = new List<Room>();
        if (!string.IsNullOrWhiteSpace(systemConfig.RoomsList))
        {
            var roomNames = systemConfig.RoomsList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            
            foreach (var roomName in roomNames)
            {
                var room = await LoadRoomAsync(roomName, cancellationToken);
                if (room != null)
                {
                    rooms.Add(room);
                }
            }
        }

        var boilerState = await LoadBoilerStateAsync(systemConfig, cancellationToken);

        return new HeatingState
        {
            CurrentTime = DateTime.Now,
            IsWeekend = DateTime.Now.DayOfWeek == DayOfWeek.Saturday || DateTime.Now.DayOfWeek == DayOfWeek.Sunday,
            Rooms = rooms,
            BoilerState = boilerState,
            SystemConfiguration = systemConfig
        };
    }

    private async Task<Room?> LoadRoomAsync(string roomName, CancellationToken cancellationToken)
    {
        try
        {
            // Pobierz konfigurację pokoju z bazy danych
            var roomConfig = await _configurationService.GetRoomAsync(roomName, cancellationToken);
            if (roomConfig == null)
            {
                _logger.LogWarning("Nie znaleziono konfiguracji pokoju {Room} w bazie danych", roomName);
                return null;
            }

            if (roomConfig.AutomationDisabled)
            {
                return null; // Pomijamy wyłączone pokoje
            }

            // Konwertuj RoomConfiguration na Room
            var room = roomConfig.ToRoom();
            
            // Ustaw encje HA z konfiguracji
            room.SensorTemperatureEntityId = roomConfig.SensorTemperatureEntityId;
            room.ValveEntityId = roomConfig.ValveEntityId;

            // Pobierz aktualną temperaturę z HA (używając encji z konfiguracji)
            room.TempActual = await GetRoomTemperatureAsync(roomConfig.SensorTemperatureEntityId, cancellationToken);

            return room;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd podczas ładowania pokoju {Room}", roomName);
            return null;
        }
    }

    private async Task<double?> GetRoomTemperatureAsync(string sensorEntityId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sensorEntityId))
        {
            return null;
        }

        // Spróbuj odczytać temperaturę z podanej encji
        var temp = await _haClient.GetStateDoubleAsync(sensorEntityId, cancellationToken);
        if (temp.HasValue)
        {
            return temp.Value;
        }

        // Jeśli encja to climate, spróbuj odczytać current_temperature
        if (sensorEntityId.StartsWith("climate."))
        {
            var climateState = await _haClient.GetStateAsync(sensorEntityId, cancellationToken);
            if (climateState != null && climateState.Attributes.TryGetValue("current_temperature", out var tempObj))
            {
                if (tempObj is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    return jsonElement.GetDouble();
                }
            }
        }

        return null;
    }

    private async Task<BoilerState?> LoadBoilerStateAsync(SystemConfiguration systemConfig, CancellationToken cancellationToken)
    {
        try
        {
            var tempReturn = await _haClient.GetStateDoubleAsync(
                systemConfig.TempReturnEntityId,
                cancellationToken) ?? 50.0;

            var mixer4DPosition = await _haClient.GetStateDoubleAsync(
                systemConfig.Mixer4DPositionEntityId,
                cancellationToken) ?? 50.0;

            var tempExternal = await GetExternalTemperatureAsync(cancellationToken);

            return new BoilerState
            {
                TempReturn = tempReturn,
                Mixer4DPosition = mixer4DPosition,
                TempExternal = tempExternal,
                ForecastMode = ForecastMode.Normal
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd podczas ładowania stanu pieca");
            return null;
        }
    }

    private async Task<double> GetExternalTemperatureAsync(CancellationToken cancellationToken)
    {
        var weatherEntities = new[] { "weather.home", "weather.openweathermap" };
        foreach (var entityId in weatherEntities)
        {
            var state = await _haClient.GetStateAsync(entityId, cancellationToken);
            if (state != null && state.Attributes.TryGetValue("temperature", out var tempObj))
            {
                if (tempObj is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    return jsonElement.GetDouble();
                }
            }
        }
        return 0.0;
    }

    private async Task<HeatingParameters> LoadHeatingParametersAsync(CancellationToken cancellationToken)
    {
        // Pobierz parametry z bazy danych przez ConfigurationService
        return await _configurationService.GetHeatingParametersAsync(cancellationToken);
    }
}

/// <summary>
/// Wynik wykonania głównej pętli.
/// </summary>
public class ExecutionResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public List<PhaseResult> PhaseResults { get; set; } = new();
    public bool IsSkipped { get; set; }
    public string? SkipReason { get; set; }

    public static ExecutionResult Success(List<PhaseResult> results)
    {
        return new ExecutionResult
        {
            IsSuccess = true,
            PhaseResults = results
        };
    }

    public static ExecutionResult Error(string errorMessage)
    {
        return new ExecutionResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }

    public static ExecutionResult Skipped(string reason)
    {
        return new ExecutionResult
        {
            IsSkipped = true,
            SkipReason = reason
        };
    }
}
