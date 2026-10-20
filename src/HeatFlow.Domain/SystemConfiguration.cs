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
    /// Encja HA z temperaturą kotła (Faza 5 „Dawka”: trym, ostrzeżenie „kocioł zimny”).
    /// U nas: sensor.kociol_temperatura_kotla.
    /// </summary>
    public string? BoilerTempEntityId { get; set; }

    // ---- Faza 5 „Dawka”: encje HA (wartości u nas - seed w ConfigurationSeed.DoseEntityDefaults
    // i migracji AddDosePhase). Pusta = Faza 5 nie działa i pisze, czego brakuje. ----

    /// <summary>Temperatura zewnętrzna do dawki (czujnik ogrodowy, nie weather.*).</summary>
    public string DoseOutdoorTempEntityId { get; set; } = string.Empty;

    /// <summary>Temperatura zadana kotła (tylko odczyt - Faza 5 jej nie zmienia).</summary>
    public string DoseBoilerSetpointEntityId { get; set; } = string.Empty;

    public string DoseFlueTempEntityId { get; set; } = string.Empty;

    /// <summary>Aktualna moc dmuchawy (&gt; 0 = kocioł grzeje).</summary>
    public string DoseBlowerActualEntityId { get; set; } = string.Empty;

    /// <summary>Licznik czasu pracy podajnika od zasypu (format „30m,034s”).</summary>
    public string DoseFeederCounterEntityId { get; set; } = string.Empty;

    /// <summary>Przełącznik zima/lato kotła (on = lato).</summary>
    public string DoseSummerSwitchEntityId { get; set; } = string.Empty;

    /// <summary>Tryb pracy sterownika (on = Auto, off = ręczny).</summary>
    public string DoseControllerModeEntityId { get; set; } = string.Empty;

    /// <summary>Zegar sterownika - jego last_updated mówi, czy dane kotła są świeże.</summary>
    public string DoseControllerClockEntityId { get; set; } = string.Empty;

    /// <summary>Automatyzacja HA „bezpiecznik spalin” (#24) - po jej zadziałaniu dmuchawa nie rośnie.</summary>
    public string DoseFlueSafetyAutomationEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Trzy jedyne encje, które Faza 5 zapisuje (number.set_value). Muszą być number.* i różne od
    /// nastawy kotła - inaczej Faza 5 odmawia zapisu.
    /// </summary>
    public string DoseFeederWorkEntityId { get; set; } = string.Empty;
    public string DoseFeederPauseEntityId { get; set; } = string.Empty;
    public string DoseBlowerEntityId { get; set; } = string.Empty;

    /// <summary>Usługa HA do powiadomień Fazy 5 (notify.&lt;ta nazwa&gt;), u nas homeintegrator = Discord.</summary>
    public string DoseNotifyService { get; set; } = string.Empty;

    /// <summary>Cel powiadomień (kanał Discorda) - ten sam, co alarmy kotła w automatyzacjach HA.</summary>
    public string DoseNotifyTarget { get; set; } = string.Empty;

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
