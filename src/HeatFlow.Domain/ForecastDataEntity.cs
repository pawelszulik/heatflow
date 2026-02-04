namespace HeatFlow.Domain;

/// <summary>
/// Encja bazy danych do cache'owania danych prognozy pogody.
/// </summary>
public class ForecastDataEntity
{
    /// <summary>
    /// Identyfikator rekordu (klucz główny).
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Szerokość geograficzna lokalizacji.
    /// </summary>
    public decimal Latitude { get; set; }

    /// <summary>
    /// Długość geograficzna lokalizacji.
    /// </summary>
    public decimal Longitude { get; set; }

    /// <summary>
    /// Aktualna temperatura zewnętrzna.
    /// </summary>
    public decimal CurrentTemp { get; set; }

    /// <summary>
    /// Lista prognoz godzinowych w formacie JSON.
    /// </summary>
    public string ForecastHoursJson { get; set; } = string.Empty;

    /// <summary>
    /// Próg spadku temperatury dla aktywacji PRE-HEATING.
    /// </summary>
    public decimal TempDropThreshold { get; set; }

    /// <summary>
    /// Próg wzrostu temperatury dla aktywacji REDUCTION.
    /// </summary>
    public decimal TempRiseThreshold { get; set; }

    /// <summary>
    /// Data utworzenia rekordu (używana do sprawdzania ważności cache).
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Data ostatniej aktualizacji rekordu.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Konwertuje encję bazy danych na obiekt domenowy ForecastData.
    /// </summary>
    public ForecastData ToForecastData()
    {
        var forecastHours = new List<ForecastHour>();
        
        if (!string.IsNullOrEmpty(ForecastHoursJson))
        {
            forecastHours = System.Text.Json.JsonSerializer.Deserialize<List<ForecastHour>>(
                ForecastHoursJson,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<ForecastHour>();
        }

        return new ForecastData
        {
            CurrentTemp = (double)CurrentTemp,
            ForecastHours = forecastHours,
            TempDropThreshold = (double)TempDropThreshold,
            TempRiseThreshold = (double)TempRiseThreshold
        };
    }

    /// <summary>
    /// Tworzy encję bazy danych z obiektu domenowego ForecastData.
    /// </summary>
    public static ForecastDataEntity FromForecastData(
        ForecastData forecastData,
        double latitude,
        double longitude)
    {
        var forecastHoursJson = System.Text.Json.JsonSerializer.Serialize(
            forecastData.ForecastHours,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        return new ForecastDataEntity
        {
            CurrentTemp = (decimal)forecastData.CurrentTemp,
            ForecastHoursJson = forecastHoursJson,
            TempDropThreshold = (decimal)forecastData.TempDropThreshold,
            TempRiseThreshold = (decimal)forecastData.TempRiseThreshold,
            Latitude = (decimal)latitude,
            Longitude = (decimal)longitude,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
