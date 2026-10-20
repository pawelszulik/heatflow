namespace HeatFlow.Domain;

/// <summary>
/// Parametry algorytmu sterowania grzaniem.
/// </summary>
public class HeatingParameters
{
    // Progi deficytów dla każdego priorytetu
    public double DeficitHighP1 { get; set; }
    public double DeficitHighP2 { get; set; }
    public double DeficitHighP3 { get; set; }

    // Wartości bazowe (używane przez Fazę 0)
    public double DeficitHighP1Base { get; set; }
    public double DeficitHighP2Base { get; set; }
    public double DeficitHighP3Base { get; set; }
    public double BufferPreparationBase { get; set; }

    // Bufor przygotowania
    public double BufferPreparation { get; set; }
    public int BufferHeatingTime { get; set; } // minuty

    // Parametry prognozy
    public double ForecastTempDropThreshold { get; set; }
    public double ForecastTempRiseThreshold { get; set; }
    public int ForecastHoursCount { get; set; }
    public double ForecastPreHeatingP1Multiplier { get; set; }
    public double ForecastPreHeatingP2Multiplier { get; set; }
    public double ForecastPreHeatingP3Multiplier { get; set; }
    public double ForecastPreHeatingBufferMultiplier { get; set; }
    public double ForecastReductionP1Multiplier { get; set; }
    public double ForecastReductionP2Multiplier { get; set; }
    public double ForecastReductionP3Multiplier { get; set; }
    public double ForecastReductionBufferMultiplier { get; set; }

    // Parametry arbitrażu
    public int MaxValvesOpen { get; set; }
    public int MinValvesOpen { get; set; }
    public int UsageSoonMinutes { get; set; }
    public int ScorePriorityMultiplier { get; set; }
    public int ScoreDeficitMultiplier { get; set; }
    public int ScoreSensitiveBonus { get; set; }
    public int ScoreUsageSoonBonus { get; set; }
    public int ScoreHeatingScheduleBonus { get; set; }

    /// <summary>Score, od którego pokój wchodzi w klasyfikację Max (pełne grzanie).</summary>
    public double ScoreThresholdMax { get; set; }

    /// <summary>Score, poniżej którego pokój schodzi do Disabled (zawór zamknięty).</summary>
    public double ScoreThresholdDisabled { get; set; }

    /// <summary>Minimalny czas w minutach, przez jaki pokój utrzymuje przydzielony zawór (anti-flap).</summary>
    public int MinDwellMinutes { get; set; }

    /// <summary>
    /// Próg "ciepłego dnia" (°C) dla trybu lato (Faza 4). Max temperatura z najbliższych 24h
    /// prognozy (albo temperatura zewnętrzna, gdy brak prognozy) >= tej wartości blokuje
    /// dezaktywację trybu lato i jest wymagana do jego aktywacji.
    /// Inicjalizator 20.0 jest celowy: zero (niepełny PUT, new HeatingParameters()) oznaczałoby
    /// "zawsze ciepło" i tryb lato nigdy nie wróciłby do zimy.
    /// </summary>
    public double SummerModeWarmDayTemp { get; set; } = 20.0;

    // Parametry Fazy 5 „Dawka” (analiza-kociol-nastawy.md § 9). Inicjalizatory = wartości
    // domyślne z analizy - z tego samego powodu co przy SummerModeWarmDayTemp: zero z niepełnego
    // PUT albo new HeatingParameters() dawałoby nastawy poza granicami Pawła. Twarde granice
    // i walidacja: DoseLimits.

    /// <summary>Czas pracy podajnika w grzaniu - zakres dla automatu [s], twardo 7-10.</summary>
    public int FeederWorkMin { get; set; } = 7;
    public int FeederWorkMax { get; set; } = 10;

    /// <summary>Czas postoju podajnika w grzaniu - zakres dla automatu [s], twardo 100-320.</summary>
    public int FeederPauseMin { get; set; } = 100;
    public int FeederPauseMax { get; set; } = 320;

    /// <summary>Moc dmuchawy w grzaniu - zakres dla automatu [%], twardo 28-35 (decyzja Pawła: 30-35).</summary>
    public int BlowerMin { get; set; } = 30;
    public int BlowerMax { get; set; } = 35;

    /// <summary>Ile sekund pracy podajnika na dobę dom bierze na 1 K różnicy środek-dwór (§ 4a).</summary>
    public double FeedPerKelvin { get; set; } = 255.0;

    /// <summary>Wyraz wolny modelu [s/dobę] - CWU minus zyski (§ 4a).</summary>
    public double FeedOffset { get; set; } = -934.0;

    /// <summary>Pojemność cieplna domu [s pracy podajnika na 1 K] - do bilansu netto w korekcie k (§ 4a).</summary>
    public double HouseCapacity { get; set; } = 1549.0;

    /// <summary>
    /// Cel domu ze stałej [°C]: używany, gdy DoseTargetFromRooms = wył., i awaryjnie, gdy żaden pokój
    /// nie ma wagi (HeatLossW) ani celu.
    /// </summary>
    public double IndoorTargetForFeed { get; set; } = 24.0;

    /// <summary>
    /// Cel domu dla dawki z celów pokoi ważonych stratami (decyzja Pawła 2026-10-20) - ten sam cel
    /// pokoju (po harmonogramie), którego używają fazy 1-3. Wył. = stała IndoorTargetForFeed.
    /// </summary>
    public bool DoseTargetFromRooms { get; set; } = true;

    /// <summary>
    /// Jak szybko może rosnąć cel domu użyty w modelu [K/h]. Spadek celu działa od razu (dom stygnie
    /// sam), wzrost - z tą szybkością, żeby dawka nie skakała na sufit. 0,07 K/h ≈ 1 K w 14 h, tyle ile
    /// przy 0 °C daje nadwyżka kotła ponad straty (analiza § 7: 37 kWh/K, nadwyżka 2,6 kW).
    /// </summary>
    public double DoseTargetRiseKPerHour { get; set; } = 0.07;

    /// <summary>
    /// Górna granica dodatku na dogrzanie domu przy rosnącym celu [s podajnika / dobę]:
    /// dodatek = HouseCapacity · tempo wzrostu celu. 2600 ≈ nadwyżka sufitu 10/100 ponad potrzebę przy 0 °C.
    /// </summary>
    public int DoseStorageBonusMaxSec { get; set; } = 2600;

    /// <summary>
    /// Pomiar do korekty k: wył. (domyślnie) = średnia czujników z flagą UseForDoseIndoorMean (7× BMx280,
    /// na nich kalibrowany model); wł. = średnia wszystkich pokoi ważona HeatLossW - ten sam zestaw co cel
    /// domu, więc w równowadze ważona temperatura domu dochodzi do celu domu (patrz raport symulacji).
    /// </summary>
    public bool DoseIndoorMeasureWeighted { get; set; }

    /// <summary>
    /// Pokój liczy się do celu domu jako wyższa z dwóch: cel pokoju albo jego faktyczna temperatura
    /// (wygładzona, DoseActualSmoothingHours) - decyzja Pawła 2026-10-20: pokój cieplejszy niż cel
    /// i tak zabiera ciepło, a dawka liczona z samych celów zaniża potrzebę. Faktyczna przycięta od góry
    /// (najwyższy cel wśród pokoi z wagą; cały cel domu ≤ cel z samych celów + DoseActualMaxAboveTargetsK),
    /// żeby nadmiar paliwa nie podbijał celu sam siebie. Wył. = same cele. Pokój z RoomConfiguration.DoseUseActual = wył.
    /// zawsze po celu.
    /// </summary>
    public bool DoseTargetUseActual { get; set; } = true;

    /// <summary>
    /// Stała czasowa wygładzania faktycznej temperatury pokoju do celu domu [h] (średnia wykładnicza).
    /// 24 h tłumi dobowe wahania (słońce po południu, gotowanie) ~6× (0,16), 3 h - tylko ~1,3× (0,79),
    /// więc przy 3 h popołudniowe słońce podbijałoby wieczorną dawkę.
    /// </summary>
    public double DoseActualSmoothingHours { get; set; } = 24.0;

    /// <summary>
    /// O ile najwyżej cel domu liczony z faktycznymi może przekroczyć cel domu z samych celów [K].
    /// To granica sprzężenia dodatniego (więcej paliwa → cieplej → wyższy cel → więcej paliwa).
    /// 2,0 K: 20.10 cel z samych celów 21,95 °C, z faktycznymi 23,94 °C - dziś granica jeszcze nie tnie.
    /// </summary>
    public double DoseActualMaxAboveTargetsK { get; set; } = 2.0;

    /// <summary>Współczynnik korekty modelu, uczony raz na dobę z bilansu (0,6-1,3).</summary>
    public double FeedCorrectionK { get; set; } = 1.0;

    /// <summary>Ile godzin pomiaru wstecz wchodzi do średniej Tz (§ 7a).</summary>
    public int FeedSmoothingPastHours { get; set; } = 2;

    /// <summary>Ile godzin prognozy do przodu wchodzi do średniej Tz (§ 7a).</summary>
    public int FeedSmoothingForecastHours { get; set; } = 1;

    /// <summary>Minimalny odstęp między zmianami nastaw [h] - kocioł reaguje w 30 min-2 h (§ 7).</summary>
    public int FeedMinIntervalHours { get; set; } = 2;

    /// <summary>Martwa strefa postoju [s] - mniejszej różnicy nie zapisujemy.</summary>
    public int FeedDeadbandSec { get; set; } = 15;

    /// <summary>Największy krok postoju na jedną zmianę [s].</summary>
    public int FeedMaxStepSec { get; set; } = 20;

    /// <summary>
    /// Histereza czasu pracy [s postoju]: czas pracy schodzi o 1 s dopiero, gdy przy krótszym
    /// czasie postój wyszedłby co najmniej tyle (w górę idzie, gdy postój spadłby poniżej minimum).
    /// </summary>
    public int FeedWorkUpHysteresisSec { get; set; } = 130;

    // Progi i strojenie Fazy 5 - reguły Pawła i wartości z analizy, do zmiany z HA (walidacja: DoseLimits).
    /// <summary>Ile czujników z pokoi (UseForDoseIndoorMean) musi odpowiedzieć, żeby średnia w domu się liczyła.</summary>
    public int DoseIndoorMinSensors { get; set; } = 5;

    /// <summary>Spaliny [°C], od których dmuchawy nie podnosimy (reguła Pawła, margines pod 200).</summary>
    public int DoseFlueNoRaiseTemp { get; set; } = 185;

    /// <summary>Spaliny [°C], powyżej których dmuchawa −DoseFlueBlowerCutPct pkt i automat sam się wyłącza (reguła Pawła).</summary>
    public int DoseFlueMaxTemp { get; set; } = 200;

    /// <summary>O ile punktów zbić dmuchawę przy spalinach > DoseFlueMaxTemp.</summary>
    public int DoseFlueBlowerCutPct { get; set; } = 2;

    /// <summary>Ile godzin po bezpieczniku spalin w HA (#24) nie podnosimy dmuchawy.</summary>
    public int DoseFlueSafetyHoldHours { get; set; } = 6;

    /// <summary>Trym „za mało”: kocioł poniżej nastawy o tyle K ...</summary>
    public double DoseTrimUnderpowerK { get; set; } = 3.0;

    /// <summary>... przez tyle godzin, przy dmuchawie chodzącej cały czas → postój −DoseTrimStepSec.</summary>
    public int DoseTrimUnderpowerHours { get; set; } = 2;

    /// <summary>Trym „za dużo”: tyle wejść w podtrzymanie ...</summary>
    public int DoseTrimStandbyEntries { get; set; } = 2;

    /// <summary>... w tylu ostatnich godzinach → postój +DoseTrimStepSec.</summary>
    public int DoseTrimStandbyHours { get; set; } = 6;

    /// <summary>Krok trymu od kotła [s postoju].</summary>
    public int DoseTrimStepSec { get; set; } = 20;

    /// <summary>Granica trymu w jedną stronę [s] (zerowany o północy).</summary>
    public int DoseTrimLimitSec { get; set; } = 60;

    /// <summary>„Kocioł zimny”: o ile K poniżej nastawy.</summary>
    public double DoseBoilerColdK { get; set; } = 10.0;

    /// <summary>Po ilu minutach „kocioł zimny” daje ostrzeżenie (przy DoseBoilerColdMinutes = 0).</summary>
    public int DoseBoilerColdWarnMinutes { get; set; } = 60;

    /// <summary>Korekta k: wzmocnienie - k ← k + clamp(FeedKGain · (netto/model − k), ±FeedKMaxStep).</summary>
    public double FeedKGain { get; set; } = 0.3;

    /// <summary>Korekta k: największy krok na dobę.</summary>
    public double FeedKMaxStep { get; set; } = 0.05;

    /// <summary>Korekta k: dolna granica.</summary>
    public double FeedKMin { get; set; } = 0.6;

    /// <summary>Korekta k: górna granica.</summary>
    public double FeedKMax { get; set; } = 1.3;

    /// <summary>Doba z grzaniem ≥ tylu godzin = kocioł na suficie, nie uczy k.</summary>
    public double DoseCeilingDayHeatingHours { get; set; } = 22.5;

    /// <summary>Udział pracy podajnika, przy którym dmuchawa jest na minimum (BlowerMin); liniowo do BlowerMax przy maks. dawce.</summary>
    public double DoseBlowerLowDuty { get; set; } = 0.034;

    /// <summary>Czy po wyłączeniu automatu (ręcznie albo samowyłączeniu) przywrócić nastawy zastane przy włączeniu.</summary>
    public bool DoseRestoreOnDisable { get; set; } = true;

    /// <summary>
    /// Kocioł poniżej nastawy − 10 K: 0 (domyślnie, decyzja Pawła 2026-10-20) = tylko ostrzeżenie -
    /// po 60 min wpis OSTRZEZENIE w dzienniku i Discord, automat zostaje włączony, nastaw nie
    /// przywraca. Wartość 30-360 = samowyłączenie po tylu minutach (spec pierwotnie: 60).
    /// Powód (HMS I-X 2026, nastawy Pawła): takich epizodów ≥ 1 h było 76, ≥ 2 h 11, ≥ 3 h 3 -
    /// zawsze z dmuchawą 100 % i spalinami ≥ 91 °C, czyli ogień się palił; samowyłączenie w mróz
    /// przywracało niższą dawkę.
    /// </summary>
    public int DoseBoilerColdMinutes { get; set; }

    /// <summary>Po ilu minutach bez danych z kotła automat sam się wyłącza.</summary>
    public int DoseBoilerStaleMinutes { get; set; } = 30;

    /// <summary>
    /// Po ilu minutach bez nowej wartości z czujnika ogrodowego automat sam się wyłącza. Nie 30 min
    /// jak dla kotła: przy stałej temperaturze stan czujnika zmienia się w HA zwykle co ok. 58 min
    /// (HMS, X 2026: kilkadziesiąt przerw 53-59 min), więc 30 min wyłączałoby automat co godzinę.
    /// </summary>
    public int DoseOutdoorStaleMinutes { get; set; } = 120;

    // Parametry zaworów
    public double ValveTolerance { get; set; }
    public int ValveRetryCount { get; set; }
    public double ValveRetryDelay { get; set; }

    // Parametry pieca
    public double MinReturnTemp { get; set; }
    public double BoilerNominalTemp { get; set; }

    // Parametry bezpieczeństwa
    public double Hysteresis { get; set; }
    public double HysteresisSafetyThreshold { get; set; }
    public double TempValidationMin { get; set; }
    public double TempValidationMax { get; set; }

    /// <summary>
    /// Zwraca próg HIGH dla danego priorytetu.
    /// </summary>
    public double GetDeficitHigh(int priority)
    {
        return priority switch
        {
            1 => DeficitHighP1,
            2 => DeficitHighP2,
            _ => DeficitHighP3
        };
    }
}
