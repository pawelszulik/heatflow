using HeatFlow.Domain;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Core.Phases;

/// <summary>
/// Faza 3 - Sterowanie zaworami termostatycznymi.
/// Ustawia temperatury na zaworach z mechanizmem retry i weryfikacją.
/// </summary>
public class Phase3ValvesService : IPhaseService
{
    private readonly IHomeAssistantClient _haClient;
    private readonly ILogger<Phase3ValvesService> _logger;

    public int PhaseNumber => 3;

    public Phase3ValvesService(
        IHomeAssistantClient haClient,
        ILogger<Phase3ValvesService> logger)
    {
        _haClient = haClient;
        _logger = logger;
    }

    public async Task<PhaseResult> ExecuteAsync(
        HeatingState state,
        HeatingParameters parameters,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            var successCount = 0;
            var failCount = 0;

            foreach (var room in state.RoomsToHot)
            {
                var success = await SetValveTemperatureAsync(room, parameters, room.TemperatureToSet ,cancellationToken);
                
                if (success)
                    successCount++;
                else
                    failCount++;
            }
            foreach (var room in state.RoomsToStay)
            {
                var success = await SetValveTemperatureAsync(room, parameters, room.TemperatureToSet ,cancellationToken);
                
                if (success)
                    successCount++;
                else
                    failCount++;
            }
            foreach (var room in state.RoomsToDisable)
            {
                var success = await SetValveTemperatureAsync(room, parameters, (int)room.MinimalSetTemperature ,cancellationToken);
                
                if (success)
                    successCount++;
                else
                    failCount++;
            }

            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("Faza 3 wykonana: sukces {Success}, błędy {Fail}", successCount, failCount);

            return PhaseResult.SuccessResult(PhaseNumber, duration, $"Sukces: {successCount}, Błędy: {failCount}");
        }
        catch (Exception ex)
        {
            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Błąd podczas wykonania Fazę 3");
            return PhaseResult.ErrorResult(PhaseNumber, ex.Message, duration);
        }
    }

    private async Task<bool> SetValveTemperatureAsync(
        Room room,
        HeatingParameters parameters, int temperatureToSet,
        CancellationToken cancellationToken)
    {
        // Użyj encji zaworu z konfiguracji pokoju
        if (string.IsNullOrWhiteSpace(room.ValveEntityId))
        {
            _logger.LogWarning("Brak encji zaworu dla pokoju {Room}", room.Name);
            return false;
        }

        var valveEntityId = room.ValveEntityId;

        // Sprawdź aktualną wartość przed ustawieniem
        double? currentTemp = null;

        // Spróbuj odczytać aktualną temperaturę w zależności od typu encji
        if (valveEntityId.StartsWith("climate."))
        {
            currentTemp = await GetCurrentValveTemperatureAsync(valveEntityId, cancellationToken);
        }
        else if (valveEntityId.StartsWith("number."))
        {
            currentTemp = await _haClient.GetStateDoubleAsync(valveEntityId, cancellationToken);
        }

        if (currentTemp.HasValue && Math.Abs(currentTemp.Value - temperatureToSet) <= parameters.ValveTolerance)
        {
            return true; // Już ustawione poprawnie
        }

        // Retry z weryfikacją
        for (int i = 0; i < parameters.ValveRetryCount; i++)
        {
            bool success = false;

            if (valveEntityId.StartsWith("climate."))
            {
                success = await _haClient.SetClimateTemperatureAsync(valveEntityId, temperatureToSet, cancellationToken);
            }
            else if (valveEntityId.StartsWith("number."))
            {
                success = await _haClient.SetNumberValueAsync(valveEntityId, temperatureToSet, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Nieobsługiwany typ encji zaworu: {EntityId}", valveEntityId);
                return false;
            }

            if (success)
            {
                await Task.Delay((int)(parameters.ValveRetryDelay * 1000), cancellationToken);
                
                double? verified = null;
                if (valveEntityId.StartsWith("climate."))
                {
                    verified = await GetCurrentValveTemperatureAsync(valveEntityId, cancellationToken);
                }
                else if (valveEntityId.StartsWith("number."))
                {
                    verified = await _haClient.GetStateDoubleAsync(valveEntityId, cancellationToken);
                }

                if (verified.HasValue && Math.Abs(verified.Value - temperatureToSet) <= parameters.ValveTolerance)
                {
                    return true;
                }
            }

            if (i < parameters.ValveRetryCount - 1)
            {
                await Task.Delay((int)(parameters.ValveRetryDelay * 1000), cancellationToken);
            }
        }

        return false;
    }

    private async Task<double?> GetCurrentValveTemperatureAsync(
        string climateEntityId,
        CancellationToken cancellationToken)
    {
        var state = await _haClient.GetStateAsync(climateEntityId, cancellationToken);
        if (state != null && state.Attributes.TryGetValue("temperature", out var tempObj))
        {
            if (tempObj is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                return jsonElement.GetDouble();
            }
        }
        return null;
    }

}
