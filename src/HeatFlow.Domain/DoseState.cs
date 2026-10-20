namespace HeatFlow.Domain;

/// <summary>
/// Stan Fazy 5 „Dawka” między przebiegami. Jeden wiersz (Id = 1). Wypełnia go tylko Faza 5;
/// API czyta go dla encji w HA (sensor.heatflow_dawka_*).
/// </summary>
public class DoseState
{
    public int Id { get; set; } = 1;

    /// <summary>
    /// Czy automat faktycznie przejął sterowanie (Faza 5 zapamiętała zastane nastawy). Różni się
    /// od <see cref="DoseAutomatSwitch.Enabled"/> przez jeden przebieg po przełączeniu - po tym
    /// Faza 5 rozpoznaje włączenie (zapamiętanie nastaw) i wyłączenie (przywrócenie).
    /// </summary>
    public bool AutomatActive { get; set; }

    // Nastawy zastane przy włączeniu automatu (do przywrócenia przy wyłączeniu).
    public int? SavedWork { get; set; }
    public int? SavedPause { get; set; }
    public int? SavedBlower { get; set; }

    // Nastawy w sterowniku z ostatniego odczytu mappera (rrGPodOn / rrGPodOff / rrGDiffuserMotorSpeed).
    public int? ControllerWork { get; set; }
    public int? ControllerPause { get; set; }
    public int? ControllerBlower { get; set; }
    public DateTime? LastMapperOkUtc { get; set; }

    /// <summary>Ostatni świeży odczyt kotła w HA (last_updated zegara sterownika) - bezpiecznik „brak danych”.</summary>
    public DateTime? LastBoilerDataUtc { get; set; }

    /// <summary>Ostatni odczyt czujnika ogrodowego w HA (last_updated) - bezpiecznik „brak danych”.</summary>
    public DateTime? LastOutdoorDataUtc { get; set; }

    // Wynik ostatniego liczenia (co godzinę) - przy wyłączonym automacie to tylko sugestia.
    public int? PlannedWork { get; set; }
    public int? PlannedPause { get; set; }
    public int? PlannedBlower { get; set; }

    /// <summary>Postój z modelu (z trymem), przycięty, przed martwą strefą i krokiem [s].</summary>
    public double? ModelPause { get; set; }

    /// <summary>Temperatura zewnętrzna wygładzona (2 h pomiaru + 1 h prognozy) użyta do liczenia [°C].</summary>
    public double? OutdoorSmoothed { get; set; }

    /// <summary>Potrzeba domu z modelu, z k [s pracy podajnika / dobę].</summary>
    public double? NeedSecPerDay { get; set; }

    /// <summary>Kocioł na suficie: przy maks. czasie pracy postój wyszedłby poniżej minimum.</summary>
    public bool Ceiling { get; set; }

    /// <summary>O ile procent sufit nie pokrywa potrzeby (0 gdy nie na suficie).</summary>
    public double ShortfallPct { get; set; }

    /// <summary>
    /// Trym od kotła [s postoju], dodawany do postoju z modelu: −20 s gdy kocioł nie dochodzi
    /// do nastawy, +20 s przy częstym podtrzymaniu. Zerowany o północy razem z korektą k.
    /// </summary>
    public int TrimSec { get; set; }
    public DateTime? LastTrimUtc { get; set; }

    /// <summary>Kiedy automat ostatnio zmienił nastawy w sterowniku (odstęp min. 2 h).</summary>
    public DateTime? LastChangeUtc { get; set; }

    public DateTime? LastCalcUtc { get; set; }

    /// <summary>Doba (lokalna), za którą ostatnio policzono korektę k.</summary>
    public DateTime? LastKCorrectionDate { get; set; }

    /// <summary>Ostatni opis decyzji (co i dlaczego) - widoczny w HA.</summary>
    public string? LastReason { get; set; }

    public string? LastSelfDisableReason { get; set; }
    public DateTime? LastSelfDisableUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
