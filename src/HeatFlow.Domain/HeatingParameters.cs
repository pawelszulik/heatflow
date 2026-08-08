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
