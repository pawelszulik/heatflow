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
    private readonly IApplicationErrorLogger _errorLogger;
    private readonly ILogger<Phase3ValvesService> _logger;

    public int PhaseNumber => 3;

    public Phase3ValvesService(
        IHomeAssistantClient haClient,
        IApplicationErrorLogger errorLogger,
        ILogger<Phase3ValvesService> logger)
    {
        _haClient = haClient;
        _errorLogger = errorLogger;
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
            var hotSuccessCount = 0;
            Room? safetyFallbackRoom = null;
            var valveResults = new List<ValveResult>();

            foreach (var room in state.RoomsToHot)
            {
                var (success, retries) = await SetValveTemperatureAsync(room, parameters, room.TemperatureToSet, cancellationToken);
                valveResults.Add(new ValveResult(
                    room.Name, room.ValveEntityId ?? "",
                    (decimal)room.TemperatureToSet, room.TempActual.HasValue ? (decimal)room.TempActual.Value : null,
                    success, retries));

                if (success) { successCount++; hotSuccessCount++; }
                else failCount++;
            }

            // ZABEZPIECZENIE: jeśli żaden zawór z RoomsToHot nie odpowiedział,
            // wymusz pełne grzanie na najlepszym dostępnym pokoju ze Stay lub Disable.
            if (hotSuccessCount == 0)
            {
                safetyFallbackRoom =
                    state.RoomsToStay.OrderBy(r => r.TempActual).FirstOrDefault()
                    ?? state.RoomsToDisable.OrderBy(r => r.TempActual).FirstOrDefault();

                if (safetyFallbackRoom != null)
                {
                    _logger.LogWarning(
                        "BEZPIECZEŃSTWO Faza 3: Wszystkie zawory w RoomsToHot ({Count}) nie odpowiedziały. " +
                        "Wymuszam pełne grzanie na pokoju '{Room}'.",
                        state.RoomsToHot.Count, safetyFallbackRoom.Name);

                    safetyFallbackRoom.SetSafetyRoom();
                    var fallbackTemp = (int)safetyFallbackRoom.MaximalSetTemperature;
                    var (fallbackSuccess, fallbackRetries) = await SetValveTemperatureAsync(
                        safetyFallbackRoom, parameters, fallbackTemp, cancellationToken);
                    valveResults.Add(new ValveResult(
                        safetyFallbackRoom.Name, safetyFallbackRoom.ValveEntityId ?? "",
                        (decimal)fallbackTemp, safetyFallbackRoom.TempActual.HasValue ? (decimal)safetyFallbackRoom.TempActual.Value : null,
                        fallbackSuccess, fallbackRetries));

                    if (fallbackSuccess)
                    {
                        successCount++;
                        _logger.LogInformation(
                            "Faza 3: Zabezpieczenie aktywne - '{Room}' utrzymany na pełnym grzaniu.",
                            safetyFallbackRoom.Name);
                    }
                    else
                    {
                        _logger.LogError(
                            "Faza 3: Zabezpieczenie NIEUDANE - '{Room}' nie odpowiedział. Żaden zawór nie grzeje na pełną moc!",
                            safetyFallbackRoom.Name);
                        var errorResult = PhaseResult.ErrorResult(PhaseNumber,
                            $"Faza 3: Zabezpieczenie NIEUDANE - '{safetyFallbackRoom.Name}' nie odpowiedział. Żaden zawór nie grzeje na pełną moc!");
                        errorResult.ValveResults = valveResults;
                        return errorResult;
                    }
                }
                else
                {
                    _logger.LogError(
                        "Faza 3: Brak pokoju do podtrzymania grzania. RoomsToStay i RoomsToDisable puste.");
                }
            }

            foreach (var room in state.RoomsToStay)
            {
                if (room == safetyFallbackRoom) continue;
                var (success, retries) = await SetValveTemperatureAsync(room, parameters, room.TemperatureToSet, cancellationToken);
                valveResults.Add(new ValveResult(
                    room.Name, room.ValveEntityId ?? "",
                    (decimal)room.TemperatureToSet, room.TempActual.HasValue ? (decimal)room.TempActual.Value : null,
                    success, retries));

                if (success) successCount++;
                else failCount++;
            }
            foreach (var room in state.RoomsToDisable)
            {
                if (room == safetyFallbackRoom) continue;
                var disableTemp = (int)room.MinimalSetTemperature;
                var (success, retries) = await SetValveTemperatureAsync(room, parameters, disableTemp, cancellationToken);
                valveResults.Add(new ValveResult(
                    room.Name, room.ValveEntityId ?? "",
                    (decimal)disableTemp, room.TempActual.HasValue ? (decimal)room.TempActual.Value : null,
                    success, retries));

                if (success) successCount++;
                else failCount++;
            }

            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("Faza 3 wykonana: sukces {Success}, błędy {Fail}", successCount, failCount);

            var phaseResult = PhaseResult.SuccessResult(PhaseNumber, duration, $"Sukces: {successCount}, Błędy: {failCount}");
            phaseResult.ValveResults = valveResults;

            if (safetyFallbackRoom != null)
                phaseResult.Warnings.Add(
                    $"BEZPIECZEŃSTWO: '{safetyFallbackRoom.Name}' utrzymany na pełnym grzaniu (RoomsToHot zawiodły).");

            return phaseResult;
        }
        catch (Exception ex)
        {
            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Błąd podczas wykonania Fazę 3");
            await _errorLogger.LogAsync(ex, PhaseNumber, nameof(Phase3ValvesService), null, "Error", "Console", cancellationToken);
            return PhaseResult.ErrorResult(PhaseNumber, ex.Message, duration);
        }
    }

    // Zwraca (success, retryCount) gdzie retryCount = liczba wykonanych prób retry (0 = sukces od razu lub brak potrzeby)
    private async Task<(bool Success, int RetryCount)> SetValveTemperatureAsync(
        Room room,
        HeatingParameters parameters, int temperatureToSet,
        CancellationToken cancellationToken)
    {
        // Użyj encji zaworu z konfiguracji pokoju
        if (string.IsNullOrWhiteSpace(room.ValveEntityId))
        {
            _logger.LogWarning("Brak encji zaworu dla pokoju {Room}", room.Name);
            return (false, 0);
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
            return (true, 0); // Już ustawione poprawnie
        }

        // Retry z weryfikacją
        for (int i = 0; i < parameters.ValveRetryCount; i++)
        {
            bool setSuccess = false;

            if (valveEntityId.StartsWith("climate."))
            {
                setSuccess = await _haClient.SetClimateTemperatureAsync(valveEntityId, temperatureToSet, cancellationToken);
            }
            else if (valveEntityId.StartsWith("number."))
            {
                setSuccess = await _haClient.SetNumberValueAsync(valveEntityId, temperatureToSet, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Nieobsługiwany typ encji zaworu: {EntityId}", valveEntityId);
                return (false, i);
            }

            if (setSuccess)
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
                    return (true, i);
                }
            }

            if (i < parameters.ValveRetryCount - 1)
            {
                await Task.Delay((int)(parameters.ValveRetryDelay * 1000), cancellationToken);
            }
        }

        return (false, parameters.ValveRetryCount);
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
