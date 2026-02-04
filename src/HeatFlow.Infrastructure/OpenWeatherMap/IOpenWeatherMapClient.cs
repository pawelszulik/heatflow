namespace HeatFlow.Infrastructure.OpenWeatherMap;

/// <summary>
/// Klient do komunikacji z OpenWeatherMap One Call API 3.0.
/// </summary>
public interface IOpenWeatherMapClient
{
    /// <summary>
    /// Pobiera aktualną pogodę i prognozę godzinową dla podanych współrzędnych geograficznych.
    /// </summary>
    /// <param name="latitude">Szerokość geograficzna (-90 do 90).</param>
    /// <param name="longitude">Długość geograficzna (-180 do 180).</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    /// <returns>Odpowiedź z danymi pogodowymi lub null w przypadku błędu.</returns>
    Task<OpenWeatherMapResponse?> GetWeatherDataAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default);
}
