using HeatFlow.Domain;
using HeatFlow.Core.Utils;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Core.Phases;

/// <summary>
/// Faza 1 - Diagnoza zapotrzebowania grzewczego.
/// Oblicza deficyty temperatur i ustawia score
/// </summary>
public class Phase1DiagnoseService : IPhaseService
{
    private readonly IHomeAssistantClient _haClient;
    private readonly ILogger<Phase1DiagnoseService> _logger;

    public int PhaseNumber => 1;

    public Phase1DiagnoseService(
        IHomeAssistantClient haClient,
        ILogger<Phase1DiagnoseService> logger)
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
            var enabledRooms = state.GetEnabledRooms();

            foreach (var room in enabledRooms)
            {
                await DiagnoseRoomAsync(room, state, parameters, cancellationToken);
            }

            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("Faza 1 wykonana: przetworzono {Count} pokoi", enabledRooms.Count);

            return PhaseResult.SuccessResult(PhaseNumber, duration, $"Przetworzono {enabledRooms.Count} pokoi");
        }
        catch (Exception ex)
        {
            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Błąd podczas wykonania Fazę 1");
            return PhaseResult.ErrorResult(PhaseNumber, ex.Message, duration);
        }
    }

    private async Task DiagnoseRoomAsync(
        Room room,
        HeatingState state,
        HeatingParameters parameters,
        CancellationToken cancellationToken)
    {
        // Sprawdź czy pokój jest w przedziale czasowym grzania
        var isHeatingActive = ScheduleHelper.IsTimeInRange(
            state.CurrentTime,
            room.HeatingSchedule,
            state.IsWeekend);

        // Oblicz docelową temperaturę na podstawie harmonogramu grzania
        var tempTarget = room.GetTargetTemperature(isHeatingActive);

        // Pobierz aktualną temperaturę pokoju (używając encji z konfiguracji)
        var tempActual = await GetRoomTemperatureAsync(room, cancellationToken);
        if (!tempActual.HasValue)
        {
            tempActual = tempTarget; // Fallback
        }

        room.TempActual = tempActual.Value;

        // Waliduj temperaturę
        var tempActualValidated = TemperatureHelper.ValidateTemperature(
            tempActual.Value,
            parameters.TempValidationMin,
            parameters.TempValidationMax);

        // Sprawdź czy pokój będzie używany wkrótce
        var usageSoon = ScheduleHelper.IsTimeInRange(
            state.CurrentTime,
            room.UsageSchedule,
            state.IsWeekend,
            offsetMinutes: parameters.BufferHeatingTime);

        // Oblicz deficyt podstawowy
        var deficitBase = TemperatureHelper.CalculateDeficit(tempTarget, tempActualValidated);

        // Oblicz deficyt z buforem przygotowania
        var deficitFinal = TemperatureHelper.CalculateDeficitWithBuffer(
            deficitBase,
            parameters.BufferPreparation,
            usageSoon);
        
        // Zaktualizuj pokój
        room.TempDeficit = Math.Round(deficitFinal, 1);

        CalculateScore(room, state, parameters);

        room.ClassifyDeficit();

        room.ChangeTemperatureToSet();
    }

    private void CalculateScore(
        Room room,
        HeatingState state,
        HeatingParameters parameters)
    {
        // Sprawdź czy pokój będzie używany wkrótce
        var usageSoon = ScheduleHelper.IsTimeInRange(
            state.CurrentTime,
            room.UsageSchedule,
            state.IsWeekend,
            offsetMinutes: parameters.UsageSoonMinutes);

        // Sprawdź czy pokój jest w przedziale czasowym grzania
        var isHeatingActive = ScheduleHelper.IsTimeInRange(
            state.CurrentTime,
            room.HeatingSchedule,
            state.IsWeekend);

        // Oblicz score
        var scoreBase = 1d/room.Priority * parameters.ScorePriorityMultiplier;
        var scoreDeficit = room.TempDeficit * parameters.ScoreDeficitMultiplier;
        var scoreSensitive = room.Sensitive ? parameters.ScoreSensitiveBonus : 0;
        var scoreUsage = usageSoon ? parameters.ScoreUsageSoonBonus : 0;
        var scoreHeatingSchedule = isHeatingActive ? parameters.ScoreHeatingScheduleBonus : 0;

        var scoreTotal = scoreBase + scoreDeficit + scoreSensitive + scoreUsage + scoreHeatingSchedule;

        room.Score = scoreTotal;
    }



    private async Task<double?> GetRoomTemperatureAsync(Room room, CancellationToken cancellationToken)
    {
        // Użyj encji z konfiguracji pokoju
        if (string.IsNullOrWhiteSpace(room.SensorTemperatureEntityId))
        {
            return null;
        }

        // Spróbuj odczytać temperaturę z podanej encji
        var temp = await _haClient.GetStateDoubleAsync(room.SensorTemperatureEntityId, cancellationToken);
        if (temp.HasValue)
        {
            return temp.Value;
        }

        // Jeśli encja to climate, spróbuj odczytać current_temperature
        if (room.SensorTemperatureEntityId.StartsWith("climate."))
        {
            var climateState = await _haClient.GetStateAsync(room.SensorTemperatureEntityId, cancellationToken);


            if (climateState != null && climateState.Attributes.TryGetValue("min_temp", out var minTempObj))
            {
                if (minTempObj is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    room.MinimalSetTemperature = jsonElement.GetDouble();
                }
            }
            if (climateState != null && climateState.Attributes.TryGetValue("max_temp", out var maxTempObj))
            {
                if (maxTempObj is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    room.MaximalSetTemperature = jsonElement.GetDouble();
                }
            }

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

}
