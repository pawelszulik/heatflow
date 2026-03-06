using HeatFlow.Domain;
using HeatFlow.Infrastructure.Database;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Core.Phases;

/// <summary>
/// Faza 4 - Zarządzanie trybem letnim kotła (switch.kociol_tryb_zima_lato).
/// Aktywuje tryb lato gdy temperatura zewnętrzna jest wysoka i pokoje są w pełni nagrzane.
/// Dezaktywuje tryb lato gdy co najmniej 2 pokoje wymagają grzania z deficytem >= 1°C.
/// Aktywacja i dezaktywacja możliwa maksymalnie raz dziennie.
/// Dezaktywacja możliwa nie wcześniej niż 3h po aktywacji tego samego dnia.
/// </summary>
public class Phase4SummerModeService : IPhaseService
{
    private const string SummerModeSwitchEntityId = "switch.kociol_tryb_zima_lato";
    private const int ActivationHourStart = 6;
    private const int ActivationHourEnd = 14;
    private const double MinExternalTempForActivation = 10.0;
    private const int MinRoomsForDeactivation = 2;
    private const double DeactivationTempDelta = 1.0;
    private const int MinHoursBeforeDeactivation = 3;

    private readonly IHomeAssistantClient _haClient;
    private readonly ISummerModeRepository _summerModeRepository;
    private readonly IApplicationErrorLogger _errorLogger;
    private readonly ILogger<Phase4SummerModeService> _logger;

    public int PhaseNumber => 4;

    public Phase4SummerModeService(
        IHomeAssistantClient haClient,
        ISummerModeRepository summerModeRepository,
        IApplicationErrorLogger errorLogger,
        ILogger<Phase4SummerModeService> logger)
    {
        _haClient = haClient;
        _summerModeRepository = summerModeRepository;
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
            // 1. Odczytaj aktualny stan przełącznika z HA
            var isSummerModeActive = await _haClient.GetStateBoolAsync(SummerModeSwitchEntityId, cancellationToken);
            if (isSummerModeActive == null)
            {
                _logger.LogWarning("Faza 4: Nie można odczytać stanu encji {EntityId}", SummerModeSwitchEntityId);
                var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                return PhaseResult.SuccessResult(PhaseNumber, duration, $"Pominięto - brak odpowiedzi HA dla {SummerModeSwitchEntityId}");
            }

            _logger.LogInformation("Faza 4: Aktualny stan trybu lato: {State}",
                isSummerModeActive.Value ? "aktywny (lato)" : "nieaktywny (zima)");

            // 2. Załaduj log dla dzisiejszego dnia
            var today = DateTime.Now.Date;
            var todayLog = await _summerModeRepository.GetLogForDateAsync(today, cancellationToken)
                           ?? new SummerModeLog { Date = today };

            // 3. Tryb zima → próba aktywacji
            if (!isSummerModeActive.Value)
            {
                if (todayLog.WasActivated)
                {
                    _logger.LogDebug("Faza 4: Tryb lato był już aktywowany dzisiaj - pomijam");
                }
                else if (ShouldActivate(state))
                {
                    _logger.LogInformation("Faza 4: Warunki aktywacji trybu lato spełnione - aktywuję");
                    var activated = await _haClient.CallServiceAsync(
                        "switch", "turn_on",
                        new { entity_id = SummerModeSwitchEntityId },
                        cancellationToken);

                    if (activated)
                    {
                        todayLog.WasActivated = true;
                        todayLog.ActivatedAt = DateTime.Now;
                        await _summerModeRepository.SaveLogAsync(todayLog, cancellationToken);
                        _logger.LogInformation("Faza 4: Tryb lato aktywowany o {Time}", todayLog.ActivatedAt);
                        var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                        return PhaseResult.SuccessResult(PhaseNumber, duration, "Tryb lato aktywowany");
                    }

                    _logger.LogWarning("Faza 4: Wywołanie turn_on dla trybu lato nie powiodło się");
                }
                else
                {
                    _logger.LogDebug("Faza 4: Warunki aktywacji trybu lato nie spełnione");
                }
            }
            // 4. Tryb lato → próba dezaktywacji
            else
            {
                if (todayLog.WasDeactivated)
                {
                    _logger.LogDebug("Faza 4: Tryb lato był już dezaktywowany dzisiaj - pomijam");
                }
                else if (ShouldDeactivate(state, todayLog))
                {
                    _logger.LogInformation("Faza 4: Warunki dezaktywacji trybu lato spełnione - dezaktywuję");
                    var deactivated = await _haClient.CallServiceAsync(
                        "switch", "turn_off",
                        new { entity_id = SummerModeSwitchEntityId },
                        cancellationToken);

                    if (deactivated)
                    {
                        todayLog.WasDeactivated = true;
                        todayLog.DeactivatedAt = DateTime.Now;
                        await _summerModeRepository.SaveLogAsync(todayLog, cancellationToken);
                        _logger.LogInformation("Faza 4: Tryb lato dezaktywowany o {Time}", todayLog.DeactivatedAt);
                        var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                        return PhaseResult.SuccessResult(PhaseNumber, duration, "Tryb lato dezaktywowany");
                    }

                    _logger.LogWarning("Faza 4: Wywołanie turn_off dla trybu lato nie powiodło się");
                }
                else
                {
                    _logger.LogDebug("Faza 4: Warunki dezaktywacji trybu lato nie spełnione");
                }
            }

            var elapsed = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            return PhaseResult.SuccessResult(PhaseNumber, elapsed, "Brak zmian trybu lato");
        }
        catch (Exception ex)
        {
            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Błąd podczas wykonania Fazy 4 (tryb lato)");
            await _errorLogger.LogAsync(ex, PhaseNumber, nameof(Phase4SummerModeService), null, "Error", "Console", cancellationToken);
            return PhaseResult.ErrorResult(PhaseNumber, ex.Message, duration);
        }
    }

    /// <summary>
    /// Sprawdza warunki aktywacji trybu lato:
    /// - godzina lokalna między 6:00 a 13:59
    /// - temperatura zewnętrzna powyżej 10°C
    /// - żaden włączony pokój nie ma klasyfikacji Max
    /// </summary>
    private bool ShouldActivate(HeatingState state)
    {
        var currentHour = DateTime.Now.Hour;
        if (currentHour < ActivationHourStart || currentHour >= ActivationHourEnd)
        {
            _logger.LogDebug("Faza 4 [aktywacja]: Godzina {Hour} poza oknem {Start}-{End}",
                currentHour, ActivationHourStart, ActivationHourEnd);
            return false;
        }

        if (state.BoilerState == null || state.BoilerState.TempExternal <= MinExternalTempForActivation)
        {
            _logger.LogDebug("Faza 4 [aktywacja]: Temperatura zewnętrzna {Temp}°C nie przekracza progu {Min}°C",
                state.BoilerState?.TempExternal, MinExternalTempForActivation);
            return false;
        }

        var roomsWithMaxDeficit = state.GetEnabledRooms()
            .Where(r => r.DeficitClassification == DeficitClassification.Max)
            .ToList();

        if (roomsWithMaxDeficit.Count > 0)
        {
            _logger.LogDebug("Faza 4 [aktywacja]: {Count} pokoje mają deficyt Max - nie aktywuję trybu lato: {Rooms}",
                roomsWithMaxDeficit.Count,
                string.Join(", ", roomsWithMaxDeficit.Select(r => r.Name)));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Sprawdza warunki dezaktywacji trybu lato:
    /// - jeśli aktywowano dziś: min 3h od aktywacji
    /// - co najmniej 2 pokoje z DeficitClassification == Max i TempActual &lt; TempTarget - 1°C
    /// </summary>
    private bool ShouldDeactivate(HeatingState state, SummerModeLog todayLog)
    {
        // Jeśli tryb lato został aktywowany dzisiaj, sprawdź czy minęły co najmniej 3h
        if (todayLog.WasActivated && todayLog.ActivatedAt.HasValue)
        {
            if (DateTime.Now < todayLog.ActivatedAt.Value.AddHours(MinHoursBeforeDeactivation))
            {
                _logger.LogDebug("Faza 4 [dezaktywacja]: Za wcześnie na dezaktywację - minęło {Elapsed:F1}h z wymaganych {Required}h od aktywacji",
                    (DateTime.Now - todayLog.ActivatedAt.Value).TotalHours,
                    MinHoursBeforeDeactivation);
                return false;
            }
        }

        var coldRoomsNeedingHeat = state.GetEnabledRooms()
            .Where(r => r.DeficitClassification == DeficitClassification.Max
                        && r.TempActual.HasValue
                        && r.TempActual.Value < r.TempTarget - DeactivationTempDelta)
            .ToList();

        if (coldRoomsNeedingHeat.Count < MinRoomsForDeactivation)
        {
            _logger.LogDebug("Faza 4 [dezaktywacja]: Tylko {Count} pokój/pokoje spełnia warunki (wymagane min. {Min})",
                coldRoomsNeedingHeat.Count, MinRoomsForDeactivation);
            return false;
        }

        _logger.LogDebug("Faza 4 [dezaktywacja]: {Count} pokojów wymaga grzania: {Rooms}",
            coldRoomsNeedingHeat.Count,
            string.Join(", ", coldRoomsNeedingHeat.Select(r =>
                $"{r.Name}({r.TempActual:F1}/{r.TempTarget:F1}°C)")));

        return true;
    }
}
