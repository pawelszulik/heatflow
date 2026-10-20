namespace HeatFlow.Domain;

/// <summary>
/// Dziennik decyzji Fazy 5 „Dawka”: CO się zmieniło, Z ILU NA ILE i DLACZEGO - po polsku,
/// z danymi wejściowymi z chwili decyzji (prośba Pawła 2026-10-20: „w logu gdzieś ma być
/// napisane dlaczego co i jak zostało podniesione i w drugą stronę”).
/// Wpisy także dla decyzji zablokowanych regułą i sugestii przy wyłączonym automacie -
/// te bez powtórek (ten sam wpis tylko raz, aż coś się zmieni).
/// </summary>
public class FeedChangeLog
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }

    /// <summary>czas_pracy / postoj / dmuchawa / k / trym_postoju / automat.</summary>
    public string Parameter { get; set; } = string.Empty;

    public double? OldValue { get; set; }
    public double? NewValue { get; set; }

    /// <summary>w górę / w dół / bez zmiany.</summary>
    public string Direction { get; set; } = string.Empty;

    /// <summary>
    /// Kod przyczyny: POGODA, SUFIT, TRYM_KOCIOL_PONIZEJ_NASTAWY, TRYM_PODTRZYMANIA, SPALINY_185,
    /// SPALINY_200, PO_AUTOMATYZACJI_24, ODSTEP, KOREKTA_K, KOREKTA_K_POMINIETA, WLACZENIE_RECZNE,
    /// WYLACZENIE_RECZNE, SAMOWYLACZENIE_&lt;powód&gt;, PRZYWROCENIE_NASTAW, KOCIOL_ZIMNY (ostrzeżenie).
    /// </summary>
    public string ReasonCode { get; set; } = string.Empty;

    /// <summary>
    /// ZAPISANE (do sterownika, potwierdzone w mapperze), NIEPOTWIERDZONE, WEWNETRZNE (k, trym,
    /// automat), ZABLOKOWANE (reguła nie pozwoliła), SUGESTIA (automat wyłączony), OSTRZEZENIE.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Zdanie po polsku: co, z ilu na ile, dlaczego.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Potwierdzenie z mappera dla zapisu: OK / BRAK (mapper nie odpowiada) / ROZJAZD.</summary>
    public string? MapperConfirmation { get; set; }

    // Dane wejściowe w chwili decyzji
    public bool AutomatActive { get; set; }
    public double? OutdoorSmoothed { get; set; }
    /// <summary>Tz wygładzona przy poprzedniej zmianie nastaw (do „spadła z … do …”).</summary>
    public double? OutdoorAtPreviousChange { get; set; }
    public double? ForecastNextHour { get; set; }
    public double? K { get; set; }
    public double? NeedSecPerDay { get; set; }
    public int TrimSec { get; set; }
    public double? BoilerTemp { get; set; }
    public double? BoilerSetpoint { get; set; }
    public double? FlueTemp { get; set; }
    /// <summary>Udział migawek z dmuchawą &gt; 0 w ostatnich 2 h [%].</summary>
    public double? HeatingShare2hPct { get; set; }
    /// <summary>Wejścia w podtrzymanie w ostatnich 6 h.</summary>
    public int? StandbyEntries6h { get; set; }
    public int? ControllerWork { get; set; }
    public int? ControllerPause { get; set; }
    public int? ControllerBlower { get; set; }
}
