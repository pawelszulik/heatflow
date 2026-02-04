using System.Text.Json.Serialization;

namespace HeatFlow.Infrastructure.OpenWeatherMap;

/// <summary>
/// Odpowiedź z OpenWeatherMap One Call API 3.0.
/// </summary>
public class OpenWeatherMapResponse
{
    /// <summary>
    /// Szerokość geograficzna lokalizacji.
    /// </summary>
    [JsonPropertyName("lat")]
    public double Latitude { get; set; }

    /// <summary>
    /// Długość geograficzna lokalizacji.
    /// </summary>
    [JsonPropertyName("lon")]
    public double Longitude { get; set; }

    /// <summary>
    /// Nazwa strefy czasowej.
    /// </summary>
    [JsonPropertyName("timezone")]
    public string Timezone { get; set; } = string.Empty;

    /// <summary>
    /// Przesunięcie strefy czasowej w sekundach od UTC.
    /// </summary>
    [JsonPropertyName("timezone_offset")]
    public int TimezoneOffset { get; set; }

    /// <summary>
    /// Aktualna pogoda.
    /// </summary>
    [JsonPropertyName("current")]
    public CurrentWeather? Current { get; set; }

    /// <summary>
    /// Prognoza godzinowa (48 godzin).
    /// </summary>
    [JsonPropertyName("hourly")]
    public List<HourlyForecast> Hourly { get; set; } = new();
}

/// <summary>
/// Aktualna pogoda.
/// </summary>
public class CurrentWeather
{
    /// <summary>
    /// Timestamp Unix UTC.
    /// </summary>
    [JsonPropertyName("dt")]
    public long DateTimeUnix { get; set; }

    /// <summary>
    /// Temperatura w stopniach Celsius (gdy użyto units=metric).
    /// </summary>
    [JsonPropertyName("temp")]
    public double Temperature { get; set; }

    /// <summary>
    /// Temperatura odczuwalna.
    /// </summary>
    [JsonPropertyName("feels_like")]
    public double FeelsLike { get; set; }

    /// <summary>
    /// Ciśnienie atmosferyczne w hPa.
    /// </summary>
    [JsonPropertyName("pressure")]
    public double Pressure { get; set; }

    /// <summary>
    /// Wilgotność w %.
    /// </summary>
    [JsonPropertyName("humidity")]
    public int Humidity { get; set; }

    /// <summary>
    /// Punkt rosy.
    /// </summary>
    [JsonPropertyName("dew_point")]
    public double DewPoint { get; set; }

    /// <summary>
    /// Zachmurzenie w %.
    /// </summary>
    [JsonPropertyName("clouds")]
    public int Clouds { get; set; }

    /// <summary>
    /// Prędkość wiatru w m/s.
    /// </summary>
    [JsonPropertyName("wind_speed")]
    public double WindSpeed { get; set; }

    /// <summary>
    /// Kierunek wiatru w stopniach.
    /// </summary>
    [JsonPropertyName("wind_deg")]
    public int WindDeg { get; set; }
}

/// <summary>
/// Prognoza godzinowa.
/// </summary>
public class HourlyForecast
{
    /// <summary>
    /// Timestamp Unix UTC.
    /// </summary>
    [JsonPropertyName("dt")]
    public long DateTimeUnix { get; set; }

    /// <summary>
    /// Temperatura w stopniach Celsius (gdy użyto units=metric).
    /// </summary>
    [JsonPropertyName("temp")]
    public double Temperature { get; set; }

    /// <summary>
    /// Temperatura odczuwalna.
    /// </summary>
    [JsonPropertyName("feels_like")]
    public double FeelsLike { get; set; }

    /// <summary>
    /// Ciśnienie atmosferyczne w hPa.
    /// </summary>
    [JsonPropertyName("pressure")]
    public double Pressure { get; set; }

    /// <summary>
    /// Wilgotność w %.
    /// </summary>
    [JsonPropertyName("humidity")]
    public int Humidity { get; set; }

    /// <summary>
    /// Punkt rosy.
    /// </summary>
    [JsonPropertyName("dew_point")]
    public double DewPoint { get; set; }

    /// <summary>
    /// Zachmurzenie w %.
    /// </summary>
    [JsonPropertyName("clouds")]
    public int Clouds { get; set; }

    /// <summary>
    /// Prędkość wiatru w m/s.
    /// </summary>
    [JsonPropertyName("wind_speed")]
    public double WindSpeed { get; set; }

    /// <summary>
    /// Kierunek wiatru w stopniach.
    /// </summary>
    [JsonPropertyName("wind_deg")]
    public int WindDeg { get; set; }

    /// <summary>
    /// Prawdopodobieństwo opadów (0-1).
    /// </summary>
    [JsonPropertyName("pop")]
    public double ProbabilityOfPrecipitation { get; set; }
}
