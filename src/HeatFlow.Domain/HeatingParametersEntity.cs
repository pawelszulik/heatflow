namespace HeatFlow.Domain;

/// <summary>
/// Encja bazy danych dla parametrów algorytmu sterowania grzaniem.
/// Zawsze powinna być jedna instancja (Id = 1).
/// </summary>
public class HeatingParametersEntity
{
    /// <summary>
    /// Identyfikator (klucz główny).
    /// Zawsze powinien być 1 (jedna konfiguracja parametrów).
    /// </summary>
    public int Id { get; set; } = 1;

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
    public int BufferHeatingTime { get; set; }

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
    public double ScoreThresholdMax { get; set; }
    public double ScoreThresholdDisabled { get; set; }
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
    /// Data ostatniej aktualizacji.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Konwertuje encję na obiekt HeatingParameters.
    /// </summary>
    public HeatingParameters ToHeatingParameters()
    {
        return new HeatingParameters
        {
            DeficitHighP1 = DeficitHighP1,
            DeficitHighP2 = DeficitHighP2,
            DeficitHighP3 = DeficitHighP3,
            DeficitHighP1Base = DeficitHighP1Base,
            DeficitHighP2Base = DeficitHighP2Base,
            DeficitHighP3Base = DeficitHighP3Base,
            BufferPreparationBase = BufferPreparationBase,
            BufferPreparation = BufferPreparation,
            BufferHeatingTime = BufferHeatingTime,
            ForecastTempDropThreshold = ForecastTempDropThreshold,
            ForecastTempRiseThreshold = ForecastTempRiseThreshold,
            ForecastHoursCount = ForecastHoursCount,
            ForecastPreHeatingP1Multiplier = ForecastPreHeatingP1Multiplier,
            ForecastPreHeatingP2Multiplier = ForecastPreHeatingP2Multiplier,
            ForecastPreHeatingP3Multiplier = ForecastPreHeatingP3Multiplier,
            ForecastPreHeatingBufferMultiplier = ForecastPreHeatingBufferMultiplier,
            ForecastReductionP1Multiplier = ForecastReductionP1Multiplier,
            ForecastReductionP2Multiplier = ForecastReductionP2Multiplier,
            ForecastReductionP3Multiplier = ForecastReductionP3Multiplier,
            ForecastReductionBufferMultiplier = ForecastReductionBufferMultiplier,
            MaxValvesOpen = MaxValvesOpen,
            MinValvesOpen = MinValvesOpen,
            UsageSoonMinutes = UsageSoonMinutes,
            ScorePriorityMultiplier = ScorePriorityMultiplier,
            ScoreDeficitMultiplier = ScoreDeficitMultiplier,
            ScoreSensitiveBonus = ScoreSensitiveBonus,
            ScoreUsageSoonBonus = ScoreUsageSoonBonus,
            ScoreHeatingScheduleBonus = ScoreHeatingScheduleBonus,
            ScoreThresholdMax = ScoreThresholdMax,
            ScoreThresholdDisabled = ScoreThresholdDisabled,
            MinDwellMinutes = MinDwellMinutes,
            ValveTolerance = ValveTolerance,
            ValveRetryCount = ValveRetryCount,
            ValveRetryDelay = ValveRetryDelay,
            MinReturnTemp = MinReturnTemp,
            BoilerNominalTemp = BoilerNominalTemp,
            FrostCompensationFactor = FrostCompensationFactor,
            Mixer4DDefault = Mixer4DDefault,
            FeederTimeDefault = FeederTimeDefault,
            FeederBoostMultiplier = FeederBoostMultiplier,
            FeederEconomyMultiplier = FeederEconomyMultiplier,
            FeederNormalMultiplier = FeederNormalMultiplier,
            FeederBoostThreshold = FeederBoostThreshold,
            FeederEconomyThreshold = FeederEconomyThreshold,
            BoilerTempTolerance = BoilerTempTolerance,
            FeederTimeTolerance = FeederTimeTolerance,
            BoilerRetryCount = BoilerRetryCount,
            BoilerRetryDelay = BoilerRetryDelay,
            MinTempDiff = MinTempDiff,
            MinMixer4D = MinMixer4D,
            Hysteresis = Hysteresis,
            HysteresisSafetyThreshold = HysteresisSafetyThreshold,
            TempValidationMin = TempValidationMin,
            TempValidationMax = TempValidationMax
        };
    }

    /// <summary>
    /// Aktualizuje encję z obiektu HeatingParameters.
    /// </summary>
    public void UpdateFrom(HeatingParameters parameters)
    {
        DeficitHighP1 = parameters.DeficitHighP1;
        DeficitHighP2 = parameters.DeficitHighP2;
        DeficitHighP3 = parameters.DeficitHighP3;
        DeficitHighP1Base = parameters.DeficitHighP1Base;
        DeficitHighP2Base = parameters.DeficitHighP2Base;
        DeficitHighP3Base = parameters.DeficitHighP3Base;
        BufferPreparationBase = parameters.BufferPreparationBase;
        BufferPreparation = parameters.BufferPreparation;
        BufferHeatingTime = parameters.BufferHeatingTime;
        ForecastTempDropThreshold = parameters.ForecastTempDropThreshold;
        ForecastTempRiseThreshold = parameters.ForecastTempRiseThreshold;
        ForecastHoursCount = parameters.ForecastHoursCount;
        ForecastPreHeatingP1Multiplier = parameters.ForecastPreHeatingP1Multiplier;
        ForecastPreHeatingP2Multiplier = parameters.ForecastPreHeatingP2Multiplier;
        ForecastPreHeatingP3Multiplier = parameters.ForecastPreHeatingP3Multiplier;
        ForecastPreHeatingBufferMultiplier = parameters.ForecastPreHeatingBufferMultiplier;
        ForecastReductionP1Multiplier = parameters.ForecastReductionP1Multiplier;
        ForecastReductionP2Multiplier = parameters.ForecastReductionP2Multiplier;
        ForecastReductionP3Multiplier = parameters.ForecastReductionP3Multiplier;
        ForecastReductionBufferMultiplier = parameters.ForecastReductionBufferMultiplier;
        MaxValvesOpen = parameters.MaxValvesOpen;
        MinValvesOpen = parameters.MinValvesOpen;
        UsageSoonMinutes = parameters.UsageSoonMinutes;
        ScorePriorityMultiplier = parameters.ScorePriorityMultiplier;
        ScoreDeficitMultiplier = parameters.ScoreDeficitMultiplier;
        ScoreSensitiveBonus = parameters.ScoreSensitiveBonus;
        ScoreUsageSoonBonus = parameters.ScoreUsageSoonBonus;
        ScoreHeatingScheduleBonus = parameters.ScoreHeatingScheduleBonus;
        ScoreThresholdMax = parameters.ScoreThresholdMax;
        ScoreThresholdDisabled = parameters.ScoreThresholdDisabled;
        MinDwellMinutes = parameters.MinDwellMinutes;
        ValveTolerance = parameters.ValveTolerance;
        ValveRetryCount = parameters.ValveRetryCount;
        ValveRetryDelay = parameters.ValveRetryDelay;
        MinReturnTemp = parameters.MinReturnTemp;
        BoilerNominalTemp = parameters.BoilerNominalTemp;
        FrostCompensationFactor = parameters.FrostCompensationFactor;
        Mixer4DDefault = parameters.Mixer4DDefault;
        FeederTimeDefault = parameters.FeederTimeDefault;
        FeederBoostMultiplier = parameters.FeederBoostMultiplier;
        FeederEconomyMultiplier = parameters.FeederEconomyMultiplier;
        FeederNormalMultiplier = parameters.FeederNormalMultiplier;
        FeederBoostThreshold = parameters.FeederBoostThreshold;
        FeederEconomyThreshold = parameters.FeederEconomyThreshold;
        BoilerTempTolerance = parameters.BoilerTempTolerance;
        FeederTimeTolerance = parameters.FeederTimeTolerance;
        BoilerRetryCount = parameters.BoilerRetryCount;
        BoilerRetryDelay = parameters.BoilerRetryDelay;
        MinTempDiff = parameters.MinTempDiff;
        MinMixer4D = parameters.MinMixer4D;
        Hysteresis = parameters.Hysteresis;
        HysteresisSafetyThreshold = parameters.HysteresisSafetyThreshold;
        TempValidationMin = parameters.TempValidationMin;
        TempValidationMax = parameters.TempValidationMax;
        UpdatedAt = DateTime.UtcNow;
    }
}
