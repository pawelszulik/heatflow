using HeatFlow.Domain;
using HeatFlow.Infrastructure.Database;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Core.Phases;

/// <summary>
/// Faza 4 - Zarządzanie trybem letnim kotła (switch.kociol_tryb_zima_lato).
/// Aktywuje tryb lato gdy temperatura zewnętrzna jest wysoka, żaden włączony pokój nie ma
/// realnego deficytu (TempDeficit > 0) i zapowiada się "ciepły dzień" (max prognozy 24h
/// >= SummerModeWarmDayTemp).
/// Dezaktywuje tryb lato gdy co najmniej 2 pokoje wymagają grzania z deficytem >= 1°C,
/// i nie zrobi się ciepło w najbliższych 6h (max prognozy 6h &lt; SummerModeWarmDayTemp) -
/// krótsze okno niż przy aktywacji, bo okno 24h zawsze łapie jakieś ciepłe popołudnie
/// (dziś albo jutro) i blokowało powrót na zimę bezterminowo. Bez prognozy fallback na
/// temperaturę zewnętrzną.
/// Aktywacja i dezaktywacja możliwa maksymalnie raz dziennie.
/// Dezaktywacja możliwa nie wcześniej niż 3h po aktywacji tego samego dnia.
/// Przez 3h od ostatniej zmiany stanu przełącznika, wykrytej przez samą Fazę 4 (nie z
/// last_changed HA - ten resetuje się też przy krótkim zaniku integracji kotła, bez
/// realnej zmiany stanu), nie wykonuje ani aktywacji, ani dezaktywacji.
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
    private const int WarmDayForecastHours = 24;
    private const int WarmSoonForecastHours = 6;
    private const int SwitchChangeGraceHours = 3;

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
            // 1. Odczytaj aktualny stan przełącznika z HA (razem z last_changed)
            var entityState = await _haClient.GetStateAsync(SummerModeSwitchEntityId, cancellationToken);
            var isSummerModeActive = HomeAssistantClient.ParseBoolState(entityState?.State);
            if (entityState == null || isSummerModeActive == null)
            {
                _logger.LogWarning("Faza 4: Nie można odczytać stanu encji {EntityId}", SummerModeSwitchEntityId);
                var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                return PhaseResult.SuccessResult(PhaseNumber, duration, $"Pominięto - brak odpowiedzi HA dla {SummerModeSwitchEntityId}");
            }

            _logger.LogInformation("Faza 4: Aktualny stan trybu lato: {State} (last_changed HA: {LastChanged})",
                isSummerModeActive.Value ? "aktywny (lato)" : "nieaktywny (zima)",
                entityState.LastChanged == default ? "brak" : entityState.LastChanged.ToString("s"));

            // 2. Karencja po zmianie przełącznika - liczona z własnej pamięci Fazy 4, nie
            // z last_changed HA (ten resetuje się też przy krótkim zaniku integracji kotła).
            var hoursSinceChange = await UpdateAndGetHoursSinceSwitchChangeAsync(
                isSummerModeActive.Value, cancellationToken);
            if (hoursSinceChange < SwitchChangeGraceHours)
            {
                _logger.LogInformation(
                    "Faza 4: Przełącznik {EntityId} zmienił stan {Elapsed:F1}h temu - karencja {Grace}h po zmianie, pomijam",
                    SummerModeSwitchEntityId, hoursSinceChange, SwitchChangeGraceHours);
                var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                return PhaseResult.SuccessResult(PhaseNumber, duration,
                    $"Brak zmian trybu lato - karencja po zmianie przełącznika ({hoursSinceChange:F1}h z {SwitchChangeGraceHours}h)");
            }

            // 3. Załaduj log dla dzisiejszego dnia
            var today = DateTime.Now.Date;
            var todayLog = await _summerModeRepository.GetLogForDateAsync(today, cancellationToken)
                           ?? new SummerModeLog { Date = today };

            string? blockReason = null;

            // 4. Tryb zima → próba aktywacji
            if (!isSummerModeActive.Value)
            {
                if (todayLog.WasActivated)
                {
                    _logger.LogDebug("Faza 4: Tryb lato był już aktywowany dzisiaj - pomijam");
                }
                else if (ShouldActivate(state, parameters, out blockReason))
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
            // 5. Tryb lato → próba dezaktywacji
            else
            {
                if (todayLog.WasDeactivated)
                {
                    _logger.LogDebug("Faza 4: Tryb lato był już dezaktywowany dzisiaj - pomijam");
                }
                else if (ShouldDeactivate(state, parameters, todayLog, out blockReason))
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
            var details = blockReason == null
                ? "Brak zmian trybu lato"
                : $"Brak zmian trybu lato - {blockReason}";
            return PhaseResult.SuccessResult(PhaseNumber, elapsed, details);
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
    /// Karencja po zmianie stanu przełącznika (ręcznej lub automatycznej): przez 3h od chwili,
    /// gdy Faza 4 sama zobaczyła zmianę stanu, nie wykonujemy ani aktywacji, ani dezaktywacji.
    /// Celowo nie używa last_changed z HA - ta wartość resetuje się też przy krótkim zaniku
    /// integracji kotła (unavailable → on/off), bez realnej zmiany stanu przełącznika, co
    /// wcześniej blokowało Fazę 4 praktycznie bezterminowo.
    ///
    /// Brak zapisanego stanu (pierwsze uruchomienie po migracji) nie nakłada karencji - zapisuje
    /// punkt odniesienia w przeszłości, żeby świeży deploy nie zamroził Fazy 4 na kolejne 3h.
    /// </summary>
    private async Task<double> UpdateAndGetHoursSinceSwitchChangeAsync(
        bool isSummerModeActive, CancellationToken cancellationToken)
    {
        var previous = await _summerModeRepository.GetSwitchStateAsync(cancellationToken);
        DateTime sinceUtc;

        if (previous == null)
        {
            sinceUtc = DateTime.UtcNow.AddHours(-SwitchChangeGraceHours);
            await _summerModeRepository.SaveSwitchStateAsync(
                new SummerModeSwitchState { IsSummer = isSummerModeActive, SinceUtc = sinceUtc },
                cancellationToken);
        }
        else if (previous.IsSummer != isSummerModeActive)
        {
            sinceUtc = DateTime.UtcNow;
            await _summerModeRepository.SaveSwitchStateAsync(
                new SummerModeSwitchState { IsSummer = isSummerModeActive, SinceUtc = sinceUtc },
                cancellationToken);
        }
        else
        {
            sinceUtc = previous.SinceUtc;
        }

        // Zegar może minimalnie wahać się między przebiegami - ujemna wartość to wciąż "przed chwilą"
        return Math.Max(0, (DateTime.UtcNow - sinceUtc).TotalHours);
    }

    /// <summary>
    /// "Ciepły dzień": max temperatura z najbliższych <paramref name="forecastHours"/> godzin
    /// prognozy (licząc od teraz, po rzeczywistym znaczniku czasu - patrz
    /// <see cref="ForecastData.GetMaxTemp"/>) >= SummerModeWarmDayTemp.
    /// Bez prognozy (null / przestarzała / bez temperatur w oknie) fallback na
    /// BoilerState.TempExternal (0.0 przy awarii HA = "nie jest ciepło" - bezpieczne).
    ///
    /// Aktywacja i dezaktywacja celowo patrzą na różne okna (24h / 6h) - patrz wywołania
    /// w <see cref="ShouldActivate"/> i <see cref="ShouldDeactivate"/>.
    /// </summary>
    private static bool IsWarmDay(HeatingState state, HeatingParameters parameters, int forecastHours, out double referenceTemp, out string source)
    {
        var forecastMax = state.Forecast?.GetMaxTemp(forecastHours, DateTime.UtcNow);
        if (forecastMax.HasValue)
        {
            referenceTemp = forecastMax.Value;
            source = $"prognoza max {forecastHours}h";
        }
        else
        {
            referenceTemp = state.BoilerState?.TempExternal ?? 0.0;
            source = "temp. zewnętrzna (brak prognozy)";
        }

        return referenceTemp >= parameters.SummerModeWarmDayTemp;
    }

    /// <summary>
    /// Sprawdza warunki aktywacji trybu lato:
    /// - godzina lokalna między 6:00 a 13:59
    /// - temperatura zewnętrzna powyżej 10°C
    /// - żaden włączony pokój nie ma realnego deficytu (TempDeficit > 0)
    /// - ciepły dzień (max prognozy 24h >= SummerModeWarmDayTemp)
    ///
    /// Celowo patrzy na TempDeficit, nie na DeficitClassification: Faza 2 zawsze trzyma co
    /// najmniej jeden pokój w klasyfikacji Max (pokój bezpieczeństwa przy minValvesOpen, albo
    /// pokój utrzymany przez dwell), niezależnie od tego, czy dom jest przegrzany, czy nie -
    /// przez to warunek na klasyfikacji blokował aktywację zawsze, bez wyjątku.
    /// </summary>
    private bool ShouldActivate(HeatingState state, HeatingParameters parameters, out string? blockReason)
    {
        blockReason = null;

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

        var roomsNeedingHeat = state.GetEnabledRooms()
            .Where(r => r.TempDeficit > 0)
            .ToList();

        if (roomsNeedingHeat.Count > 0)
        {
            _logger.LogDebug("Faza 4 [aktywacja]: {Count} pokoje mają realny deficyt - nie aktywuję trybu lato: {Rooms}",
                roomsNeedingHeat.Count,
                string.Join(", ", roomsNeedingHeat.Select(r => $"{r.Name}({r.TempDeficit:F1})")));
            return false;
        }

        // Na końcu, żeby powód blokady pojawiał się tylko gdy reszta warunków pozwala na aktywację
        if (!IsWarmDay(state, parameters, WarmDayForecastHours, out var referenceTemp, out var source))
        {
            _logger.LogDebug("Faza 4 [aktywacja]: Brak ciepłego dnia - {Source} {Temp:F1}°C < progu {Threshold:F1}°C - nie aktywuję trybu lato",
                source, referenceTemp, parameters.SummerModeWarmDayTemp);
            blockReason = $"aktywacja zablokowana: brak ciepłego dnia ({source} {referenceTemp:F1}°C < {parameters.SummerModeWarmDayTemp:F1}°C)";
            return false;
        }

        _logger.LogDebug("Faza 4 [aktywacja]: Ciepły dzień - {Source} {Temp:F1}°C >= progu {Threshold:F1}°C",
            source, referenceTemp, parameters.SummerModeWarmDayTemp);
        return true;
    }

    /// <summary>
    /// Sprawdza warunki dezaktywacji trybu lato:
    /// - co najmniej 2 pokoje z DeficitClassification == Max i TempActual &lt; TempTarget - 1°C
    /// - jeśli aktywowano dziś: min 3h od aktywacji
    /// - nie zrobi się ciepło w najbliższych godzinach (max prognozy <see cref="WarmSoonForecastHours"/>h
    ///   &lt; SummerModeWarmDayTemp) - celowo krótsze okno niż przy aktywacji (<see cref="WarmDayForecastHours"/>h):
    ///   okno 24h zawsze łapie jakieś ciepłe popołudnie (dziś albo jutro), więc latem blokowało
    ///   powrót na zimę bezterminowo, nawet wieczorem przy wychłodzonym domu.
    /// Powód blokady zwracany tylko gdy jest realne zapotrzebowanie na grzanie.
    /// </summary>
    private bool ShouldDeactivate(HeatingState state, HeatingParameters parameters, SummerModeLog todayLog, out string? blockReason)
    {
        blockReason = null;

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

        // Jeśli tryb lato został aktywowany dzisiaj, sprawdź czy minęły co najmniej 3h
        if (todayLog.WasActivated && todayLog.ActivatedAt.HasValue)
        {
            if (DateTime.Now < todayLog.ActivatedAt.Value.AddHours(MinHoursBeforeDeactivation))
            {
                var elapsedHours = (DateTime.Now - todayLog.ActivatedAt.Value).TotalHours;
                _logger.LogDebug("Faza 4 [dezaktywacja]: Za wcześnie na dezaktywację - minęło {Elapsed:F1}h z wymaganych {Required}h od aktywacji",
                    elapsedHours, MinHoursBeforeDeactivation);
                blockReason = $"dezaktywacja zablokowana: {elapsedHours:F1}h z {MinHoursBeforeDeactivation}h od aktywacji";
                return false;
            }
        }

        if (IsWarmDay(state, parameters, WarmSoonForecastHours, out var referenceTemp, out var source))
        {
            _logger.LogDebug("Faza 4 [dezaktywacja]: {Count} pokojów wymaga grzania, ale ciepły dzień - {Source} {Temp:F1}°C >= progu {Threshold:F1}°C - nie dezaktywuję trybu lato",
                coldRoomsNeedingHeat.Count, source, referenceTemp, parameters.SummerModeWarmDayTemp);
            blockReason = $"dezaktywacja zablokowana: ciepły dzień ({source} {referenceTemp:F1}°C >= {parameters.SummerModeWarmDayTemp:F1}°C)";
            return false;
        }

        _logger.LogDebug("Faza 4 [dezaktywacja]: {Count} pokojów wymaga grzania: {Rooms}",
            coldRoomsNeedingHeat.Count,
            string.Join(", ", coldRoomsNeedingHeat.Select(r =>
                $"{r.Name}({r.TempActual:F1}/{r.TempTarget:F1}°C)")));

        return true;
    }
}
