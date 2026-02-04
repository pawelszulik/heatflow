using HeatFlow.Domain;
using HeatFlow.Core.Utils;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Core.Phases;

/// <summary>
/// Faza 5 - Histereza i bezpieczeństwo.
/// Stosuje histerezę termiczną i monitoruje warunki bezpieczeństwa.
/// </summary>
public class Phase5HysteresisService : IPhaseService
{
    private readonly IHomeAssistantClient _haClient;
    private readonly ILogger<Phase5HysteresisService> _logger;

    public int PhaseNumber => 5;

    public Phase5HysteresisService(
        IHomeAssistantClient haClient,
        ILogger<Phase5HysteresisService> logger)
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
            var disabledCount = 0;
            var safetyAlarms = new List<string>();

            // Sprawdź histerezę dla każdego pokoju
            foreach (var room in enabledRooms)
            {
                var (shouldDisable, isSafetyAlarm) = CheckHysteresis(room, state, parameters);
                
                if (shouldDisable)
                {
                    room.HeatingEnabled = false;
                    disabledCount++;

                    if (isSafetyAlarm)
                    {
                        safetyAlarms.Add(room.Name);
                        _logger.LogWarning("ALARM BEZPIECZEŃSTWA: Przegrzanie w pokoju {Room}", room.Name);
                    }
                }
            }

            // Sprawdź warunki bezpieczeństwa systemu
            var safetyCheck = await CheckSafetyConditionsAsync(state, parameters, cancellationToken);
            if (!safetyCheck.AllOk)
            {
                _logger.LogWarning("Warunki bezpieczeństwa: {Alarms}", string.Join(", ", safetyCheck.Alarms));
            }

            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            var details = $"Wyłączono {disabledCount} pokoi, Alarmy bezpieczeństwa: {safetyAlarms.Count}";

            return PhaseResult.SuccessResult(PhaseNumber, duration, details);
        }
        catch (Exception ex)
        {
            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Błąd podczas wykonania Fazę 5");
            return PhaseResult.ErrorResult(PhaseNumber, ex.Message, duration);
        }
    }

    private (bool shouldDisable, bool isSafetyAlarm) CheckHysteresis(
        Room room,
        HeatingState state,
        HeatingParameters parameters)
    {
        if (!room.HeatingEnabled)
        {
            return (false, false);
        }

        if (!room.TempActual.HasValue)
        {
            return (false, false);
        }

        var tempActual = TemperatureHelper.ValidateTemperature(
            room.TempActual.Value,
            parameters.TempValidationMin,
            parameters.TempValidationMax);

        // Oblicz docelową temperaturę
        var isHeatingActive = ScheduleHelper.IsTimeInRange(
            state.CurrentTime,
            room.HeatingSchedule,
            state.IsWeekend);
        var tempTarget = room.GetTargetTemperature(isHeatingActive);

        var tempDiff = tempActual - tempTarget;

        // Sprawdź próg bezpieczeństwa
        if (tempDiff > parameters.HysteresisSafetyThreshold)
        {
            return (true, true);
        }

        // Sprawdź histerezę
        if (tempDiff > parameters.Hysteresis)
        {
            return (true, false);
        }

        return (false, false);
    }

    private async Task<SafetyCheckResult> CheckSafetyConditionsAsync(
        HeatingState state,
        HeatingParameters parameters,
        CancellationToken cancellationToken)
    {
        var enabledRooms = state.GetEnabledRooms();
        var openValvesCount = enabledRooms.Count(r => r.HeatingEnabled);

        var result = new SafetyCheckResult();

        // 1. Temperatura powrotu
        var tempReturn = state.BoilerState?.TempReturn ?? parameters.MinReturnTemp;
        result.TempReturnOk = tempReturn >= parameters.MinReturnTemp;

        // 2. Różnica temp zadana-powrót
        var boilerTarget = parameters.BoilerNominalTemp;
        var tempDiffBoiler = boilerTarget - tempReturn;
        result.TempDiffOk = tempDiffBoiler <= parameters.MinTempDiff;

        // 3. Pozycja zaworu 4D
        var mixer4DPosition = state.BoilerState?.Mixer4DPosition ?? parameters.Mixer4DDefault;
        result.Mixer4DOk = mixer4DPosition >= parameters.MinMixer4D;

        // 4. Liczba otwartych zaworów
        result.ValvesCountOk = openValvesCount >= parameters.MinValvesOpen;

        result.AllOk = result.TempReturnOk && result.TempDiffOk && result.Mixer4DOk && result.ValvesCountOk;

        if (!result.TempReturnOk) result.Alarms.Add("temp_return");
        if (!result.TempDiffOk) result.Alarms.Add("temp_diff");
        if (!result.Mixer4DOk) result.Alarms.Add("mixer_4d");
        if (!result.ValvesCountOk) result.Alarms.Add("valves_count");

        return result;
    }

    private class SafetyCheckResult
    {
        public bool TempReturnOk { get; set; }
        public bool TempDiffOk { get; set; }
        public bool Mixer4DOk { get; set; }
        public bool ValvesCountOk { get; set; }
        public bool AllOk { get; set; }
        public List<string> Alarms { get; set; } = new();
    }
}
