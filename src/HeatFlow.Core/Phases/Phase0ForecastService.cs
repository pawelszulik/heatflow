using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.Database;
using HeatFlow.Infrastructure.HomeAssistant;
using HeatFlow.Infrastructure.OpenWeatherMap;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Core.Phases;

/// <summary>
/// Faza 0 - Analiza prognozy pogody i przygotowanie systemu.
/// Wykonywana co godzinę.
/// </summary>
public class Phase0ForecastService : IPhaseService
{
    private readonly IHomeAssistantClient _haClient;
    private readonly IOpenWeatherMapClient _openWeatherMapClient;
    private readonly IConfigurationService _configurationService;
    private readonly IHeatFlowRepository _repository;
    private readonly ILogger<Phase0ForecastService> _logger;

    public int PhaseNumber => 0;

    public Phase0ForecastService(
        IHomeAssistantClient haClient,
        IOpenWeatherMapClient openWeatherMapClient,
        IConfigurationService configurationService,
        IHeatFlowRepository repository,
        ILogger<Phase0ForecastService> logger)
    {
        _haClient = haClient;
        _openWeatherMapClient = openWeatherMapClient;
        _configurationService = configurationService;
        _repository = repository;
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
            // 1. Pobierz prognozę pogody
            ForecastData? forecastData = await LoadForecastDataAsync(parameters, cancellationToken);
            if (forecastData == null)
            {
                return PhaseResult.ErrorResult(PhaseNumber, "Nie udało się pobrać prognozy pogody");
            }

            // 2. Analizuj prognozę i określ tryb
            var minTemp24h = forecastData.GetMinTemp24h(parameters.ForecastHoursCount);
            var tempDiff = forecastData.GetTempDiff(parameters.ForecastHoursCount);

            var mode = DetermineForecastMode(
                tempDiff,
                parameters.ForecastTempDropThreshold,
                parameters.ForecastTempRiseThreshold);

            // 3. Zastosuj tryb do parametrów (modyfikacja progów deficytów w bazie danych)
            await ApplyForecastModeAsync(mode, parameters, cancellationToken);

            // 4. Zapisz tryb prognozy do HA (wartość runtime - pozostaje w HA)
            await _haClient.SetInputNumberValueAsync(
                "input_number.forecast_mode",
                (int)mode,
                cancellationToken);

            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            var details = $"Tryb: {mode}, Temp różnica: {tempDiff:F1}°C, Min temp 24h: {minTemp24h:F1}°C";

            _logger.LogInformation("Faza 0 wykonana: {Mode}, różnica temp: {TempDiff:F1}°C", mode, tempDiff);

            return PhaseResult.SuccessResult(PhaseNumber, duration, details);
        }
        catch (Exception ex)
        {
            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Błąd podczas wykonania Fazę 0");
            return PhaseResult.ErrorResult(PhaseNumber, ex.Message, duration);
        }
    }

    private async Task<ForecastData?> LoadForecastDataAsync(
        HeatingParameters parameters,
        CancellationToken cancellationToken)
    {
        // Pobierz konfigurację systemową z współrzędnymi geograficznymi
        var systemConfig = await _configurationService.GetSystemConfigurationAsync(cancellationToken);
        
        if (systemConfig.Latitude == 0.0 && systemConfig.Longitude == 0.0)
        {
            _logger.LogError(
                "Współrzędne geograficzne nie są skonfigurowane. Ustaw Latitude i Longitude w SystemConfiguration.");
            return null;
        }

        // 1. Sprawdź cache w bazie danych
        try
        {
            var cachedEntity = await _repository.GetForecastDataCacheAsync(
                systemConfig.Latitude,
                systemConfig.Longitude,
                cancellationToken);

            if (cachedEntity != null)
            {
                // Sprawdź czy cache jest ważny (mniej niż 1 godzina)
                var cacheAge = DateTime.UtcNow - cachedEntity.UpdatedAt;
                if (cacheAge.TotalHours < 1.0)
                {
                    _logger.LogDebug(
                        "Użyto cache prognozy pogody (wiek: {Age} minut)",
                        cacheAge.TotalMinutes);

                    var cachedData = cachedEntity.ToForecastData();
                    // Upewnij się, że progi są aktualne z parametrów
                    cachedData.TempDropThreshold = parameters.ForecastTempDropThreshold;
                    cachedData.TempRiseThreshold = parameters.ForecastTempRiseThreshold;
                    return cachedData;
                }
                else
                {
                    _logger.LogDebug(
                        "Cache prognozy pogody jest przestarzały (wiek: {Age} godzin), pobieranie z API",
                        cacheAge.TotalHours);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd podczas pobierania cache prognozy pogody, kontynuowanie z API");
        }

        // 2. Pobierz dane pogodowe z OpenWeatherMap API
        var weatherData = await _openWeatherMapClient.GetWeatherDataAsync(
            systemConfig.Latitude,
            systemConfig.Longitude,
            cancellationToken);

        if (weatherData == null || weatherData.Current == null)
        {
            _logger.LogWarning("Nie udało się pobrać danych pogodowych z OpenWeatherMap API");
            return null;
        }

        // Pobierz aktualną temperaturę
        var currentTemp = weatherData.Current.Temperature;

        // Mapuj prognozę godzinową na ForecastHour
        var forecastHours = new List<ForecastHour>();
        if (weatherData.Hourly != null && weatherData.Hourly.Count > 0)
        {
            foreach (var hourlyForecast in weatherData.Hourly)
            {
                // Konwertuj Unix timestamp na DateTime
                var dateTime = DateTimeOffset.FromUnixTimeSeconds(hourlyForecast.DateTimeUnix).DateTime;

                forecastHours.Add(new ForecastHour
                {
                    DateTime = dateTime,
                    Temperature = hourlyForecast.Temperature
                });
            }
        }

        var forecastData = new ForecastData
        {
            CurrentTemp = currentTemp,
            ForecastHours = forecastHours,
            TempDropThreshold = parameters.ForecastTempDropThreshold,
            TempRiseThreshold = parameters.ForecastTempRiseThreshold
        };

        _logger.LogDebug(
            "Pobrano dane pogodowe z API: aktualna temp: {CurrentTemp}°C, prognoz godzinowych: {ForecastCount}",
            currentTemp,
            forecastHours.Count);

        // 3. Zapisz do cache
        try
        {
            var cacheEntity = ForecastDataEntity.FromForecastData(
                forecastData,
                systemConfig.Latitude,
                systemConfig.Longitude);

            await _repository.SaveForecastDataCacheAsync(cacheEntity, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogDebug("Zapisano prognozę pogody do cache");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd podczas zapisywania cache prognozy pogody, kontynuowanie bez cache");
            // Nie przerywamy działania - cache nie jest krytyczny
        }

        return forecastData;
    }

    private ForecastMode DetermineForecastMode(
        double tempDiff,
        double tempDropThreshold,
        double tempRiseThreshold)
    {
        if (tempDiff <= -tempDropThreshold)
        {
            return ForecastMode.PreHeating;
        }
        if (tempDiff >= tempRiseThreshold)
        {
            return ForecastMode.Reduction;
        }
        return ForecastMode.Normal;
    }

    private async Task ApplyForecastModeAsync(
        ForecastMode mode,
        HeatingParameters parameters,
        CancellationToken cancellationToken)
    {
        if (mode == ForecastMode.Normal)
        {
            // Przywróć wartości bazowe
            await RestoreBaseValuesAsync(parameters, cancellationToken);
        }
        else
        {
            // Zastosuj mnożniki do wartości bazowych
            await ApplyMultipliersAsync(mode, parameters, cancellationToken);
        }
    }

    private async Task RestoreBaseValuesAsync(
        HeatingParameters parameters,
        CancellationToken cancellationToken)
    {
        // Przywróć wartości bazowe w obiekcie parameters
        parameters.DeficitHighP1 = parameters.DeficitHighP1Base;
        parameters.DeficitHighP2 = parameters.DeficitHighP2Base;
        parameters.DeficitHighP3 = parameters.DeficitHighP3Base;
        parameters.BufferPreparation = parameters.BufferPreparationBase;

        // Zaktualizuj wartości w bazie danych
        await _configurationService.UpdateHeatingParametersAsync(parameters, cancellationToken);

        _logger.LogDebug("Przywrócono wartości bazowe parametrów w bazie danych");
    }

    private async Task ApplyMultipliersAsync(
        ForecastMode mode,
        HeatingParameters parameters,
        CancellationToken cancellationToken)
    {
        double p1Multiplier, p2Multiplier, p3Multiplier, bufferMultiplier;

        if (mode == ForecastMode.PreHeating)
        {
            p1Multiplier = parameters.ForecastPreHeatingP1Multiplier;
            p2Multiplier = parameters.ForecastPreHeatingP2Multiplier;
            p3Multiplier = parameters.ForecastPreHeatingP3Multiplier;
            bufferMultiplier = parameters.ForecastPreHeatingBufferMultiplier;
        }
        else // Reduction
        {
            p1Multiplier = parameters.ForecastReductionP1Multiplier;
            p2Multiplier = parameters.ForecastReductionP2Multiplier;
            p3Multiplier = parameters.ForecastReductionP3Multiplier;
            bufferMultiplier = parameters.ForecastReductionBufferMultiplier;
        }

        // Zastosuj mnożniki do wartości bazowych w obiekcie parameters
        parameters.DeficitHighP1 = Math.Round(parameters.DeficitHighP1Base * p1Multiplier, 1);
        parameters.DeficitHighP2 = Math.Round(parameters.DeficitHighP2Base * p2Multiplier, 1);
        parameters.DeficitHighP3 = Math.Round(parameters.DeficitHighP3Base * p3Multiplier, 1);
        parameters.BufferPreparation = Math.Round(parameters.BufferPreparationBase * bufferMultiplier, 1);

        // Zaktualizuj wartości w bazie danych
        await _configurationService.UpdateHeatingParametersAsync(parameters, cancellationToken);

        _logger.LogDebug("Zastosowano mnożniki trybu {Mode} do parametrów w bazie danych", mode);
    }
}
