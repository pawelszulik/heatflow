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
    /// <summary>Ile pokoi najwyżej próbuje zabezpieczenie, gdy nie odpowiedział żaden grzejący zawór.</summary>
    public const int SafetyFallbackMaxRooms = 3;

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
            var additionalInfo = $"RoomsToHot: {state.RoomsToHot.Count}, RoomsToStay: {state.RoomsToStay.Count}, RoomsToDisable: {state.RoomsToDisable.Count}";
            var valveMessages = new List<string>();
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

                if (success)
                {
                    successCount++;
                    hotSuccessCount++;
                    valveMessages.Add($"Ustawiono pokój do grzania {room.Name} na {room.TemperatureToSet}°C");
                }
                else
                {
                    failCount++;
                    valveMessages.Add($"NIE udało się ustawić pokoju do grzania {room.Name} na {room.TemperatureToSet}°C");
                }
            }

            // ZABEZPIECZENIE: jeśli żaden zawór z RoomsToHot nie odpowiedział, wymuś pełne grzanie na pierwszym pokoju,
            // którego zawór odpowie: kolejno najwyżej SafetyFallbackMaxRooms kandydatów (limit, bo każda porażka to
            // kilkanaście sekund prób, a przy padniętym HA nie odpowie żaden).
            if (hotSuccessCount == 0)
            {
                var candidates = SafetyFallbackCandidates(state);
                if (candidates.Count > 0)
                {
                    _logger.LogWarning(
                        "BEZPIECZEŃSTWO Faza 3: Wszystkie zawory w RoomsToHot ({Count}) nie odpowiedziały. " +
                        "Próbuję pełnego grzania na: {Rooms}.",
                        state.RoomsToHot.Count, string.Join(", ", candidates.Select(r => r.Name)));

                    foreach (var candidate in candidates)
                    {
                        var fallbackTemp = (int)candidate.MaximalSetTemperature;
                        var (fallbackSuccess, fallbackRetries) = await SetValveTemperatureAsync(
                            candidate, parameters, fallbackTemp, cancellationToken);
                        valveResults.Add(new ValveResult(
                            candidate.Name, candidate.ValveEntityId ?? "",
                            (decimal)fallbackTemp, candidate.TempActual.HasValue ? (decimal)candidate.TempActual.Value : null,
                            fallbackSuccess, fallbackRetries));

                        if (fallbackSuccess)
                        {
                            safetyFallbackRoom = candidate;
                            candidate.SetSafetyRoom();
                            successCount++;
                            valveMessages.Add($"ZABEZPIECZENIE: {candidate.Name} ustawiony na {fallbackTemp}°C (pełne grzanie)");
                            _logger.LogInformation(
                                "Faza 3: Zabezpieczenie aktywne - '{Room}' utrzymany na pełnym grzaniu.", candidate.Name);
                            break;
                        }

                        failCount++;
                        valveMessages.Add($"ZABEZPIECZENIE: {candidate.Name} nie odpowiedział na {fallbackTemp}°C");
                        _logger.LogWarning("Faza 3: Zabezpieczenie - '{Room}' nie odpowiedział.", candidate.Name);
                    }

                    if (safetyFallbackRoom == null)
                    {
                        var names = string.Join(", ", candidates.Select(r => r.Name));
                        _logger.LogError(
                            "Faza 3: Zabezpieczenie NIEUDANE - nie odpowiedział żaden z zaworów: {Rooms}. Brak potwierdzenia pełnego grzania.",
                            names);
                        var errorResult = PhaseResult.ErrorResult(PhaseNumber,
                            $"Faza 3: Zabezpieczenie NIEUDANE - nie odpowiedział żaden z zaworów: {names}. Brak potwierdzenia pełnego grzania.");
                        errorResult.ValveResults = valveResults;
                        errorResult.Details = $"{additionalInfo} | {string.Join(" | ", valveMessages)}";
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

                if (success)
                {
                    successCount++;
                    valveMessages.Add($"Ustawiono pokój do podtrzymania {room.Name} na {room.TemperatureToSet}°C");
                }
                else
                {
                    failCount++;
                    valveMessages.Add($"NIE udało się ustawić pokoju do podtrzymania {room.Name} na {room.TemperatureToSet}°C");
                }
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

                if (success)
                {
                    successCount++;
                    valveMessages.Add($"Ustawiono pokój do wyłączenia {room.Name} na {disableTemp}°C");
                }
                else
                {
                    failCount++;
                    valveMessages.Add($"NIE udało się ustawić pokoju do wyłączenia {room.Name} na {disableTemp}°C");
                }
            }

            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            var valveDetails = string.Join(" | ", valveMessages);
            var details = valveMessages.Count > 0
                ? $"Sukces: {successCount}, Błędy: {failCount}, {additionalInfo} | {valveDetails}"
                : $"Sukces: {successCount}, Błędy: {failCount}, {additionalInfo}";

            _logger.LogInformation(
                "Faza 3 wykonana: sukces {Success}, błędy {Fail} ({Rooms}). Zawory: {Valves}",
                successCount, failCount, additionalInfo, valveDetails);

            var phaseResult = PhaseResult.SuccessResult(PhaseNumber, duration, details);
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

    /// <summary>
    /// Kolejność prób zabezpieczenia: pokoje bez wagi na końcu (garaż nie ogrzewa domu, a jako najzimniejszy zawsze
    /// wygrywałby wybór), potem balast przed zamykanymi, potem najzimniejsze. Pokój bez odczytu po tych z odczytem.
    /// </summary>
    private static List<Room> SafetyFallbackCandidates(HeatingState state)
        => state.RoomsToStay.Select(r => (Room: r, Group: 0))
            .Concat(state.RoomsToDisable.Select(r => (Room: r, Group: 1)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Room.ValveEntityId))
            .OrderBy(x => x.Room.HeatLossW > 0 ? 0 : 1)
            .ThenBy(x => x.Group)
            .ThenBy(x => x.Room.TempActual ?? double.MaxValue)
            .Select(x => x.Room)
            .Take(SafetyFallbackMaxRooms)
            .ToList();

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
