using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Infrastructure.OpenWeatherMap;

/// <summary>
/// Implementacja klienta OpenWeatherMap One Call API 3.0.
/// </summary>
public class OpenWeatherMapClient : IOpenWeatherMapClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<OpenWeatherMapClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string ApiBaseUrl = "https://api.openweathermap.org/data/3.0/onecall";

    public OpenWeatherMapClient(
        HttpClient httpClient,
        string apiKey,
        ILogger<OpenWeatherMapClient> logger)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<OpenWeatherMapResponse?> GetWeatherDataAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Walidacja współrzędnych
            if (latitude < -90 || latitude > 90)
            {
                _logger.LogError("Nieprawidłowa szerokość geograficzna: {Latitude}. Musi być w zakresie -90 do 90", latitude);
                return null;
            }

            if (longitude < -180 || longitude > 180)
            {
                _logger.LogError("Nieprawidłowa długość geograficzna: {Longitude}. Musi być w zakresie -180 do 180", longitude);
                return null;
            }

            // Buduj URL z parametrami
            var url = $"{ApiBaseUrl}?lat={latitude:F6}&lon={longitude:F6}&units=metric&appid={_apiKey}&lang=pl";

            _logger.LogDebug("Wywołanie OpenWeatherMap API: {Url}", url.Replace(_apiKey, "***"));

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Błąd podczas pobierania danych z OpenWeatherMap API. Status: {StatusCode}, Response: {ErrorContent}",
                    response.StatusCode,
                    errorContent);

                // Obsługa konkretnych kodów błędów
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogError("Błąd autoryzacji (401) - sprawdź klucz API");
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogError("Błąd 404 - dane dla podanych współrzędnych nie zostały znalezione");
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _logger.LogError("Błąd 429 - przekroczono limit zapytań do API");
                }

                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var weatherData = JsonSerializer.Deserialize<OpenWeatherMapResponse>(content, _jsonOptions);

            if (weatherData == null)
            {
                _logger.LogError("Nie udało się zdeserializować odpowiedzi z OpenWeatherMap API");
                return null;
            }

            _logger.LogDebug(
                "Pobrano dane pogodowe: aktualna temp: {CurrentTemp}°C, prognoz godzinowych: {HourlyCount}",
                weatherData.Current?.Temperature,
                weatherData.Hourly?.Count ?? 0);

            return weatherData;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Błąd sieci podczas pobierania danych z OpenWeatherMap API");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Przerwano pobieranie danych z OpenWeatherMap API (timeout)");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Błąd parsowania JSON z OpenWeatherMap API");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nieoczekiwany błąd podczas pobierania danych z OpenWeatherMap API");
            return null;
        }
    }
}
