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

    /// <summary>Temperatura w domu, na którą liczona jest dawka [°C] - „pierwsze pokrętło” (§ 5).</summary>
    public double IndoorTargetForFeed { get; set; } = 24.0;

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

    /// <summary>Czy po wyłączeniu automatu (ręcznie albo samowyłączeniu) przywrócić nastawy zastane przy włączeniu.</summary>
    public bool DoseRestoreOnDisable { get; set; } = true;

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
    public double FrostCompensationFactor { get; set; }
    public double Mixer4DDefault { get; set; }
    public double FeederTimeDefault { get; set; }
    public double FeederBoostMultiplier { get; set; }
    public double FeederEconomyMultiplier { get; set; }
    public double FeederNormalMultiplier { get; set; }
    public int FeederBoostThreshold { get; set; }
    public int FeederEconomyThreshold { get; set; }
    public double BoilerTempTolerance { get; set; }
    public double FeederTimeTolerance { get; set; }
    public int BoilerRetryCount { get; set; }
    public double BoilerRetryDelay { get; set; }

    // Parametry bezpieczeństwa
    public double MinTempDiff { get; set; }
    public double MinMixer4D { get; set; }
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
