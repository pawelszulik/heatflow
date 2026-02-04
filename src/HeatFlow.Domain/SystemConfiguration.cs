namespace HeatFlow.Domain;

/// <summary>
/// Konfiguracja systemowa przechowywana w bazie danych.
/// </summary>
public class SystemConfiguration
{
    /// <summary>
    /// Identyfikator konfiguracji (klucz główny).
    /// Zawsze powinien być 1 (jedna konfiguracja systemowa).
    /// </summary>
    public int Id { get; set; } = 1;

    /// <summary>
    /// Lista wszystkich pokoi oddzielona przecinkami.
    /// Przykład: "sypialnia,lazienka,edyta,salon"
    /// </summary>
    public string RoomsList { get; set; } = string.Empty;

    /// <summary>
    /// Numer seryjny pieca ekopiec.
    /// Używany do budowania nazw encji pieca.
    /// </summary>
    public string EkoPiecDeviceSn { get; set; } = string.Empty;

    /// <summary>
    /// Encja Home Assistant dla temperatury powrotu wody z instalacji.
    /// Przykład: "sensor.temp_return" lub "sensor.ekopiec_ABC123_temp_return"
    /// </summary>
    public string TempReturnEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Encja Home Assistant dla pozycji zaworu mieszającego 4D.
    /// Przykład: "sensor.mixer_4d_position" lub "number.mixer_4d_position"
    /// </summary>
    public string Mixer4DPositionEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Encja Home Assistant dla temperatury zadanej kotła (opcjonalne).
    /// Jeśli puste, system zbuduje nazwę automatycznie: number.ekopiec_{device_sn}_kot_tzad
    /// Przykład: "number.ekopiec_ABC123_kot_tzad"
    /// </summary>
    public string? BoilerTempEntityId { get; set; }

    /// <summary>
    /// Encja Home Assistant dla czasu pracy podajnika (opcjonalne).
    /// Jeśli puste, system zbuduje nazwę automatycznie: number.ekopiec_{device_sn}_p_pod_on
    /// Przykład: "number.ekopiec_ABC123_p_pod_on"
    /// </summary>
    public string? FeederTimeEntityId { get; set; }

    /// <summary>
    /// Czy system grzania jest włączony.
    /// </summary>
    public bool SystemEnabled { get; set; } = true;

    /// <summary>
    /// Szerokość geograficzna lokalizacji dla prognozy pogody (OpenWeatherMap).
    /// Wartość w zakresie -90 do 90.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Długość geograficzna lokalizacji dla prognozy pogody (OpenWeatherMap).
    /// Wartość w zakresie -180 do 180.
    /// </summary>
    public double Longitude { get; set; }
}
