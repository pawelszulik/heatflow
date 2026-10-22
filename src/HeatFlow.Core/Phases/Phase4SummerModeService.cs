using HeatFlow.Domain;
using HeatFlow.Infrastructure.Database;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Core.Phases;

/// <summary>
/// Faza 4 - Zarządzanie trybem letnim kotła (switch.kociol_tryb_zima_lato).
/// Aktywuje tryb lato gdy temperatura zewnętrzna jest wysoka, żaden włączony pokój nie ma
/// realnego deficytu (TempDeficit > 0) i w najbliższych godzinach zrobi się ciepło (max
/// prognozy z SummerModeForecastHours, domyślnie 6h, >= SummerModeWarmDayTemp).
/// Dezaktywuje tryb lato gdy co najmniej 2 pokoje wymagają grzania z deficytem >= 1°C,
/// i nie zrobi się ciepło w tym samym oknie (max prognozy &lt; SummerModeWarmDayTemp).
/// Okno 24h łapało ciepłe popołudnie jutra: blokowało powrót na zimę bezterminowo, a
/// 2026-10-20 o 12:09 włączyło lato przy 12-15 °C w ogrodzie. Bez prognozy fallback na
/// temperaturę zewnętrzną.
/// Aktywacja i dezaktywacja możliwa maksymalnie raz dziennie.
/// Dezaktywacja możliwa nie wcześniej niż 3h po aktywacji tego samego dnia.
/// Nastawa kotła (2026-10-22, decyzja Pawła): przy włączeniu lata przez Fazę 4 zapamiętuje obecną
/// nastawę i ustawia SummerModeBoilerSetpoint (64 °C); na zimie, także po ręcznym przełączeniu,
/// przywraca zapamiętaną. 2026-10-21 lato przy 74 °C dobiło kocioł do 82 °C.
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
    private const int DefaultForecastHours = 6;
    private const int SwitchChangeGraceHours = 3;

    private readonly IHomeAssistantClient _haClient;
    private readonly ISummerModeRepository _summerModeRepository;
    private readonly IApplicationErrorLogger _errorLogger;
    private readonly ILogger<Phase4SummerModeService> _logger;
    private readonly TimeProvider _time;
    private readonly ISummerBoilerSetpoint? _boilerSetpoint;

    public int PhaseNumber => 4;

    public Phase4SummerModeService(
        IHomeAssistantClient haClient,
        ISummerModeRepository summerModeRepository,
        IApplicationErrorLogger errorLogger,
        ILogger<Phase4SummerModeService> logger,
        TimeProvider? time = null,
        ISummerBoilerSetpoint? boilerSetpoint = null)
    {
        // Bez niego (stare testy) Faza 4 przełącza tryb jak dawniej i nie dotyka nastawy kotła.
        _boilerSetpoint = boilerSetpoint;
        _haClient = haClient;
        _summerModeRepository = summerModeRepository;
        _errorLogger = errorLogger;
        _logger = logger;
        // Zegar do decyzji (godzina okna aktywacji, data logu, karencja, okno prognozy) - w testach
        // stały; czas trwania fazy dalej z DateTime.UtcNow.
        _time = time ?? TimeProvider.System;
    }

    private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;
    private DateTime NowLocal => _time.GetLocalNow().DateTime;

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

            // 1b. Powrót na zimę (nasz albo ręczny): nastawa kotła wraca do tej sprzed lata. Przed
            // karencją, bo ta dotyczy tylko przełączania trybu, a nie przywracania nastawy.
            if (!isSummerModeActive.Value)
            {
                await RestoreBoilerSetpointAsync(cancellationToken);
            }

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
            var today = NowLocal.Date;
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
                        todayLog.ActivatedAt = NowLocal;
                        await _summerModeRepository.SaveLogAsync(todayLog, cancellationToken);
                        _logger.LogInformation("Faza 4: Tryb lato aktywowany o {Time}", todayLog.ActivatedAt);
                        var setpointNote = await ApplySummerBoilerSetpointAsync(parameters, cancellationToken);
                        var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                        return PhaseResult.SuccessResult(PhaseNumber, duration, "Tryb lato aktywowany" + setpointNote);
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
                        todayLog.DeactivatedAt = NowLocal;
                        await _summerModeRepository.SaveLogAsync(todayLog, cancellationToken);
                        _logger.LogInformation("Faza 4: Tryb lato dezaktywowany o {Time}", todayLog.DeactivatedAt);
                        var setpointNote = await RestoreBoilerSetpointAsync(cancellationToken);
                        var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                        return PhaseResult.SuccessResult(PhaseNumber, duration, "Tryb lato dezaktywowany" + setpointNote);
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
    /// Po włączeniu lata przez Fazę 4: zapamiętuje obecną nastawę kotła i ustawia letnią
    /// (SummerModeBoilerSetpoint, 64 °C). W lecie obieg stoi, a przy nastawie zimowej 74 °C kocioł
    /// dobijał do 82 °C (2026-10-21). Zwraca dopisek do opisu wyniku fazy.
    /// </summary>
    private async Task<string> ApplySummerBoilerSetpointAsync(HeatingParameters parameters, CancellationToken ct)
    {
        if (_boilerSetpoint == null) return string.Empty;

        try
        {
            var target = SummerSetpoint(parameters);
            var before = await _boilerSetpoint.ReadAsync(ct);
            if (before.HasValue && Math.Round(before.Value) != target)
            {
                await _summerModeRepository.SaveBoilerSetpointBeforeSummerAsync(before.Value, ct);
            }

            var ok = await _boilerSetpoint.WriteAsync(target, ct);
            var from = before.HasValue ? $"{before.Value:0.#}" : "?";
            var text = ok
                ? $"Lato: nastawa kotła {from} → {target} °C (potwierdzone w sterowniku)."
                : $"Lato: nastawa kotła {from} → {target} °C NIEPOTWIERDZONA - sprawdź na panelu kotła.";
            if (!before.HasValue)
            {
                text += " Nie udało się odczytać poprzedniej nastawy - po powrocie na zimę ustaw ją ręcznie.";
            }

            _logger.LogInformation("Faza 4: {Text}", text);
            await _boilerSetpoint.NotifyAsync("HeatFlow: tryb lato", text, ct);
            return $"; nastawa kotła {from} → {target}" + (ok ? string.Empty : " (niepotwierdzona)");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Faza 4: nie udało się ustawić letniej nastawy kotła");
            return "; błąd ustawiania nastawy kotła";
        }
    }

    /// <summary>
    /// Na zimie: jeśli Faza 4 zapamiętała nastawę sprzed lata, wraca ona do kotła (też gdy powrót na
    /// zimę zrobił Paweł ręcznie - 2026-10-21 został wtedy 74 i nikt tego nie cofnął). Pamięć jest
    /// czyszczona zawsze, także po nieudanym zapisie: powiadomienie podaje wtedy wartość do ręcznego
    /// ustawienia, zamiast ponawiać co 5 minut. Zwraca dopisek do opisu wyniku fazy.
    /// </summary>
    private async Task<string> RestoreBoilerSetpointAsync(CancellationToken ct)
    {
        if (_boilerSetpoint == null) return string.Empty;

        try
        {
            var remembered = await _summerModeRepository.GetBoilerSetpointBeforeSummerAsync(ct);
            if (remembered == null) return string.Empty;

            var target = (int)Math.Round(remembered.Value);
            var current = await _boilerSetpoint.ReadAsync(ct);
            var ok = current.HasValue && Math.Round(current.Value) == target || await _boilerSetpoint.WriteAsync(target, ct);
            await _summerModeRepository.SaveBoilerSetpointBeforeSummerAsync(null, ct);

            var from = current.HasValue ? $"{current.Value:0.#}" : "?";
            var text = ok
                ? $"Zima: nastawa kotła wróciła {from} → {target} °C (potwierdzone w sterowniku)."
                : $"Zima: nastawa kotła {from} → {target} °C NIEPOTWIERDZONA - ustaw {target} °C na panelu kotła.";
            _logger.LogInformation("Faza 4: {Text}", text);
            await _boilerSetpoint.NotifyAsync("HeatFlow: powrót na zimę", text, ct);
            return $"; nastawa kotła {from} → {target}" + (ok ? string.Empty : " (niepotwierdzona)");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Faza 4: nie udało się przywrócić nastawy kotła");
            return "; błąd przywracania nastawy kotła";
        }
    }

    /// <summary>SummerModeBoilerSetpoint; spoza 50-75 (np. 0 z niepełnego PUT) - 64.</summary>
    private static int SummerSetpoint(HeatingParameters parameters)
        => parameters.SummerModeBoilerSetpoint is >= 50 and <= 75 ? parameters.SummerModeBoilerSetpoint : 64;

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
            sinceUtc = NowUtc.AddHours(-SwitchChangeGraceHours);
            await _summerModeRepository.SaveSwitchStateAsync(
                new SummerModeSwitchState { IsSummer = isSummerModeActive, SinceUtc = sinceUtc },
                cancellationToken);
        }
        else if (previous.IsSummer != isSummerModeActive)
        {
            sinceUtc = NowUtc;
            await _summerModeRepository.SaveSwitchStateAsync(
                new SummerModeSwitchState { IsSummer = isSummerModeActive, SinceUtc = sinceUtc },
                cancellationToken);
        }
        else
        {
            sinceUtc = previous.SinceUtc;
        }

        // Zegar może minimalnie wahać się między przebiegami - ujemna wartość to wciąż "przed chwilą"
        return Math.Max(0, (NowUtc - sinceUtc).TotalHours);
    }

    /// <summary>
    /// "Ciepły dzień": max temperatura z najbliższych SummerModeForecastHours godzin prognozy
    /// (licząc od teraz, po rzeczywistym znaczniku czasu - patrz <see cref="ForecastData.GetMaxTemp"/>)
    /// >= SummerModeWarmDayTemp. To samo okno przy aktywacji i dezaktywacji.
    /// Znaczniki godzin prognozy są w UTC (Faza 0: DateTimeOffset.FromUnixTimeSeconds(...).DateTime,
    /// w JSON bez strefy), więc porównanie z DateTime.UtcNow jest poprawne.
    /// Bez prognozy (null / przestarzała / bez temperatur w oknie) fallback na
    /// BoilerState.TempExternal (0.0 przy awarii HA = "nie jest ciepło" - bezpieczne).
    /// </summary>
    private bool IsWarmDay(HeatingState state, HeatingParameters parameters, out double referenceTemp, out string source)
    {
        var forecastHours = ForecastHours(parameters);
        var forecastMax = state.Forecast?.GetMaxTemp(forecastHours, NowUtc);
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

    /// <summary>SummerModeForecastHours; spoza 1-48 (np. 0 z niepełnego PUT) - 6h.</summary>
    private static int ForecastHours(HeatingParameters parameters)
        => parameters.SummerModeForecastHours is >= 1 and <= 48 ? parameters.SummerModeForecastHours : DefaultForecastHours;

    /// <summary>
    /// Sprawdza warunki aktywacji trybu lato:
    /// - godzina lokalna między 6:00 a 13:59
    /// - temperatura zewnętrzna powyżej 10°C
    /// - żaden włączony pokój nie ma realnego deficytu (TempDeficit > 0)
    /// - ciepło w najbliższych godzinach (max prognozy z SummerModeForecastHours, domyślnie 6h,
    ///   >= SummerModeWarmDayTemp) - nie ciepłe popołudnie jutra
    ///
    /// Celowo patrzy na TempDeficit, nie na DeficitClassification: Faza 2 zawsze trzyma co
    /// najmniej jeden pokój w klasyfikacji Max (pokój bezpieczeństwa przy minValvesOpen, albo
    /// pokój utrzymany przez dwell), niezależnie od tego, czy dom jest przegrzany, czy nie -
    /// przez to warunek na klasyfikacji blokował aktywację zawsze, bez wyjątku.
    /// </summary>
    private bool ShouldActivate(HeatingState state, HeatingParameters parameters, out string? blockReason)
    {
        blockReason = null;

        var currentHour = NowLocal.Hour;
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
        if (!IsWarmDay(state, parameters, out var referenceTemp, out var source))
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
    /// - nie zrobi się ciepło w najbliższych godzinach (max prognozy z SummerModeForecastHours,
    ///   domyślnie 6h, &lt; SummerModeWarmDayTemp): okno 24h zawsze łapało jakieś ciepłe popołudnie
    ///   (dziś albo jutro), więc latem blokowało powrót na zimę bezterminowo.
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
            if (NowLocal < todayLog.ActivatedAt.Value.AddHours(MinHoursBeforeDeactivation))
            {
                var elapsedHours = (NowLocal - todayLog.ActivatedAt.Value).TotalHours;
                _logger.LogDebug("Faza 4 [dezaktywacja]: Za wcześnie na dezaktywację - minęło {Elapsed:F1}h z wymaganych {Required}h od aktywacji",
                    elapsedHours, MinHoursBeforeDeactivation);
                blockReason = $"dezaktywacja zablokowana: {elapsedHours:F1}h z {MinHoursBeforeDeactivation}h od aktywacji";
                return false;
            }
        }

        if (IsWarmDay(state, parameters, out var referenceTemp, out var source))
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
