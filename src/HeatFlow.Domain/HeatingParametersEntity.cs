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
    public double SummerModeWarmDayTemp { get; set; }

    // Parametry Fazy 5 „Dawka” (wartości domyślne jak w HeatingParameters)
    public int FeederWorkMin { get; set; } = 7;
    public int FeederWorkMax { get; set; } = 10;
    public int FeederPauseMin { get; set; } = 100;
    public int FeederPauseMax { get; set; } = 320;
    public int BlowerMin { get; set; } = 30;
    public int BlowerMax { get; set; } = 35;
    public double FeedPerKelvin { get; set; } = 255.0;
    public double FeedOffset { get; set; } = -934.0;
    public double HouseCapacity { get; set; } = 1549.0;
    public double IndoorTargetForFeed { get; set; } = 24.0;
    public double FeedCorrectionK { get; set; } = 1.0;
    public int FeedSmoothingPastHours { get; set; } = 2;
    public int FeedSmoothingForecastHours { get; set; } = 1;
    public int FeedMinIntervalHours { get; set; } = 2;
    public int FeedDeadbandSec { get; set; } = 15;
    public int FeedMaxStepSec { get; set; } = 20;
    public int FeedWorkUpHysteresisSec { get; set; } = 130;
    public int DoseIndoorMinSensors { get; set; } = 5;
    public int DoseFlueNoRaiseTemp { get; set; } = 185;
    public int DoseFlueMaxTemp { get; set; } = 200;
    public int DoseFlueBlowerCutPct { get; set; } = 2;
    public int DoseFlueSafetyHoldHours { get; set; } = 6;
    public double DoseTrimUnderpowerK { get; set; } = 3.0;
    public int DoseTrimUnderpowerHours { get; set; } = 2;
    public int DoseTrimStandbyEntries { get; set; } = 2;
    public int DoseTrimStandbyHours { get; set; } = 6;
    public int DoseTrimStepSec { get; set; } = 20;
    public int DoseTrimLimitSec { get; set; } = 60;
    public double DoseBoilerColdK { get; set; } = 10.0;
    public int DoseBoilerColdWarnMinutes { get; set; } = 60;
    public double FeedKGain { get; set; } = 0.3;
    public double FeedKMaxStep { get; set; } = 0.05;
    public double FeedKMin { get; set; } = 0.6;
    public double FeedKMax { get; set; } = 1.3;
    public double DoseCeilingDayHeatingHours { get; set; } = 22.5;
    public double DoseBlowerLowDuty { get; set; } = 0.034;
    public bool DoseTargetFromRooms { get; set; } = true;
    public double DoseTargetRiseKPerHour { get; set; } = 0.07;
    public int DoseStorageBonusMaxSec { get; set; } = 2600;
    public bool DoseIndoorMeasureWeighted { get; set; } = false;
    public bool DoseRestoreOnDisable { get; set; } = true;
    public int DoseBoilerColdMinutes { get; set; }
    public int DoseBoilerStaleMinutes { get; set; } = 30;
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
            SummerModeWarmDayTemp = SummerModeWarmDayTemp,
            FeederWorkMin = FeederWorkMin,
            FeederWorkMax = FeederWorkMax,
            FeederPauseMin = FeederPauseMin,
            FeederPauseMax = FeederPauseMax,
            BlowerMin = BlowerMin,
            BlowerMax = BlowerMax,
            FeedPerKelvin = FeedPerKelvin,
            FeedOffset = FeedOffset,
            HouseCapacity = HouseCapacity,
            IndoorTargetForFeed = IndoorTargetForFeed,
            FeedCorrectionK = FeedCorrectionK,
            FeedSmoothingPastHours = FeedSmoothingPastHours,
            FeedSmoothingForecastHours = FeedSmoothingForecastHours,
            FeedMinIntervalHours = FeedMinIntervalHours,
            FeedDeadbandSec = FeedDeadbandSec,
            FeedMaxStepSec = FeedMaxStepSec,
            FeedWorkUpHysteresisSec = FeedWorkUpHysteresisSec,
            DoseIndoorMinSensors = DoseIndoorMinSensors,
            DoseFlueNoRaiseTemp = DoseFlueNoRaiseTemp,
            DoseFlueMaxTemp = DoseFlueMaxTemp,
            DoseFlueBlowerCutPct = DoseFlueBlowerCutPct,
            DoseFlueSafetyHoldHours = DoseFlueSafetyHoldHours,
            DoseTrimUnderpowerK = DoseTrimUnderpowerK,
            DoseTrimUnderpowerHours = DoseTrimUnderpowerHours,
            DoseTrimStandbyEntries = DoseTrimStandbyEntries,
            DoseTrimStandbyHours = DoseTrimStandbyHours,
            DoseTrimStepSec = DoseTrimStepSec,
            DoseTrimLimitSec = DoseTrimLimitSec,
            DoseBoilerColdK = DoseBoilerColdK,
            DoseBoilerColdWarnMinutes = DoseBoilerColdWarnMinutes,
            FeedKGain = FeedKGain,
            FeedKMaxStep = FeedKMaxStep,
            FeedKMin = FeedKMin,
            FeedKMax = FeedKMax,
            DoseCeilingDayHeatingHours = DoseCeilingDayHeatingHours,
            DoseBlowerLowDuty = DoseBlowerLowDuty,
            DoseTargetFromRooms = DoseTargetFromRooms,
            DoseTargetRiseKPerHour = DoseTargetRiseKPerHour,
            DoseStorageBonusMaxSec = DoseStorageBonusMaxSec,
            DoseIndoorMeasureWeighted = DoseIndoorMeasureWeighted,
            DoseRestoreOnDisable = DoseRestoreOnDisable,
            DoseBoilerColdMinutes = DoseBoilerColdMinutes,
            DoseBoilerStaleMinutes = DoseBoilerStaleMinutes,
            DoseOutdoorStaleMinutes = DoseOutdoorStaleMinutes,
            ValveTolerance = ValveTolerance,
            ValveRetryCount = ValveRetryCount,
            ValveRetryDelay = ValveRetryDelay,
            MinReturnTemp = MinReturnTemp,
            BoilerNominalTemp = BoilerNominalTemp,
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
        SummerModeWarmDayTemp = parameters.SummerModeWarmDayTemp;
        FeederWorkMin = parameters.FeederWorkMin;
        FeederWorkMax = parameters.FeederWorkMax;
        FeederPauseMin = parameters.FeederPauseMin;
        FeederPauseMax = parameters.FeederPauseMax;
        BlowerMin = parameters.BlowerMin;
        BlowerMax = parameters.BlowerMax;
        FeedPerKelvin = parameters.FeedPerKelvin;
        FeedOffset = parameters.FeedOffset;
        HouseCapacity = parameters.HouseCapacity;
        IndoorTargetForFeed = parameters.IndoorTargetForFeed;
        FeedCorrectionK = parameters.FeedCorrectionK;
        FeedSmoothingPastHours = parameters.FeedSmoothingPastHours;
        FeedSmoothingForecastHours = parameters.FeedSmoothingForecastHours;
        FeedMinIntervalHours = parameters.FeedMinIntervalHours;
        FeedDeadbandSec = parameters.FeedDeadbandSec;
        FeedMaxStepSec = parameters.FeedMaxStepSec;
        FeedWorkUpHysteresisSec = parameters.FeedWorkUpHysteresisSec;
        DoseIndoorMinSensors = parameters.DoseIndoorMinSensors;
        DoseFlueNoRaiseTemp = parameters.DoseFlueNoRaiseTemp;
        DoseFlueMaxTemp = parameters.DoseFlueMaxTemp;
        DoseFlueBlowerCutPct = parameters.DoseFlueBlowerCutPct;
        DoseFlueSafetyHoldHours = parameters.DoseFlueSafetyHoldHours;
        DoseTrimUnderpowerK = parameters.DoseTrimUnderpowerK;
        DoseTrimUnderpowerHours = parameters.DoseTrimUnderpowerHours;
        DoseTrimStandbyEntries = parameters.DoseTrimStandbyEntries;
        DoseTrimStandbyHours = parameters.DoseTrimStandbyHours;
        DoseTrimStepSec = parameters.DoseTrimStepSec;
        DoseTrimLimitSec = parameters.DoseTrimLimitSec;
        DoseBoilerColdK = parameters.DoseBoilerColdK;
        DoseBoilerColdWarnMinutes = parameters.DoseBoilerColdWarnMinutes;
        FeedKGain = parameters.FeedKGain;
        FeedKMaxStep = parameters.FeedKMaxStep;
        FeedKMin = parameters.FeedKMin;
        FeedKMax = parameters.FeedKMax;
        DoseCeilingDayHeatingHours = parameters.DoseCeilingDayHeatingHours;
        DoseBlowerLowDuty = parameters.DoseBlowerLowDuty;
        DoseTargetFromRooms = parameters.DoseTargetFromRooms;
        DoseTargetRiseKPerHour = parameters.DoseTargetRiseKPerHour;
        DoseStorageBonusMaxSec = parameters.DoseStorageBonusMaxSec;
        DoseIndoorMeasureWeighted = parameters.DoseIndoorMeasureWeighted;
        DoseRestoreOnDisable = parameters.DoseRestoreOnDisable;
        DoseBoilerColdMinutes = parameters.DoseBoilerColdMinutes;
        DoseBoilerStaleMinutes = parameters.DoseBoilerStaleMinutes;
        DoseOutdoorStaleMinutes = parameters.DoseOutdoorStaleMinutes;
        ValveTolerance = parameters.ValveTolerance;
        ValveRetryCount = parameters.ValveRetryCount;
        ValveRetryDelay = parameters.ValveRetryDelay;
        MinReturnTemp = parameters.MinReturnTemp;
        BoilerNominalTemp = parameters.BoilerNominalTemp;
        Hysteresis = parameters.Hysteresis;
        HysteresisSafetyThreshold = parameters.HysteresisSafetyThreshold;
        TempValidationMin = parameters.TempValidationMin;
        TempValidationMax = parameters.TempValidationMax;
        UpdatedAt = DateTime.UtcNow;
    }
}
