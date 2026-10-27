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

    /// <summary>Cel domu z pokoi (albo stały) w ostatnim liczeniu, przed ograniczeniem wzrostu [°C].</summary>
    public double? HouseTarget { get; set; }

    /// <summary>Cel domu użyty w modelu: spada od razu, rośnie najwyżej o DoseTargetRiseKPerHour [°C].</summary>
    public double? HouseTargetUsed { get; set; }

    /// <summary>Skąd cel domu: „pokoje” albo „stała …”.</summary>
    public string? HouseTargetSource { get; set; }

    /// <summary>Dodatek na dogrzanie domu przy rosnącym celu [s/dobę] (0, gdy cel nie rośnie).</summary>
    public double StorageBonusSec { get; set; }

    /// <summary>Cel domu z ostatniego wpisu CEL_DOMU w dzienniku i cele pokoi wtedy („salon=26;hol=25”).</summary>
    public double? HouseTargetLogged { get; set; }
    public string? RoomTargetsSnapshot { get; set; }

    /// <summary>Cel domu z samych celów pokoi (bez faktycznych) w ostatnim liczeniu [°C].</summary>
    public double? HouseTargetBase { get; set; }

    /// <summary>
    /// Wygładzone faktyczne temperatury pokoi do celu domu, każda z chwilą ostatniego odczytu:
    /// „salon=24.31@202610201530;hol=…” (czas UTC yyyyMMddHHmm).
    /// </summary>
    public string? RoomActualSmoothed { get; set; }

    /// <summary>Cele pokoi z ostatniego liczenia („salon=26;hol=25”) - do oddzielenia wzrostu celu od wzrostu faktycznych.</summary>
    public string? RoomTargetsLastCalc { get; set; }

    /// <summary>Pokoje, które w ostatnim liczeniu weszły do celu domu po faktycznej („sien;garaz”).</summary>
    public string? HouseTargetActualRooms { get; set; }

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

    /// <summary>Tz wygładzona przy ostatniej zmianie nastaw - do zdania „Tz spadła z … do …” w dzienniku.</summary>
    public double? LastChangeOutdoorSmoothed { get; set; }

    /// <summary>Kod ostatniego trymu (TRYM_KOCIOL_PONIZEJ_NASTAWY / TRYM_PODTRZYMANIA) - przyczyna zmiany w dzienniku.</summary>
    public string? LastTrimCode { get; set; }

    /// <summary>
    /// Klucz ostatniego wpisu „zablokowane / sugestia” w dzienniku - ten sam wpis nie jest
    /// powtarzany co przebieg. Zerowany przy każdej faktycznej zmianie.
    /// </summary>
    public string? LastLoggedBlockKey { get; set; }

    /// <summary>
    /// Kierunek ostatniej zmiany dawki: +1 = więcej węgla, −1 = mniej. Gdy następny krok tej samej
    /// rampy czeka na odstęp, nie piszemy „zablokowane” do dziennika - to oczywiste; piszemy tylko,
    /// gdy reguła blokuje zmianę w drugą stronę albo zmianę czasu pracy.
    /// </summary>
    public int LastChangeDirection { get; set; }

    /// <summary>Ostrzeżenie „kocioł zimny” (DoseBoilerColdMinutes = 0) już wysłane w tym epizodzie.</summary>
    public bool BoilerColdWarned { get; set; }

    // Tryb cienia (automat wyłączony): co automat by ustawił, gdyby był włączony - te same reguły
    // (odstęp, martwa strefa, krok) liczone od jego własnych wirtualnych nastaw, nie od sterownika.
    // Dzięki temu dziennik SUGESTIA czyta się jak dziennik automatu (najwyżej co 2 h).
    public int? ShadowWork { get; set; }
    public int? ShadowPause { get; set; }
    public int? ShadowBlower { get; set; }
    public DateTime? ShadowLastChangeUtc { get; set; }

    public DateTime? LastCalcUtc { get; set; }

    /// <summary>Doba (lokalna), za którą ostatnio policzono korektę k.</summary>
    public DateTime? LastKCorrectionDate { get; set; }

    /// <summary>Narzut na wietrzenie/drzwi/CWU [%] z poprzedniego przebiegu - do wpisu w dzienniku przy zmianie.</summary>
    public double? LastSurchargePct { get; set; }

    /// <summary>Ostatnia doba (lokalna), w której narzut był &gt; 0 - taka doba nie uczy k.</summary>
    public DateTime? SurchargeActiveDate { get; set; }

    /// <summary>Ostatni opis decyzji (co i dlaczego) - widoczny w HA.</summary>
    public string? LastReason { get; set; }

    public string? LastSelfDisableReason { get; set; }
    public DateTime? LastSelfDisableUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
