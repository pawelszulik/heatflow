using HeatFlow.Domain;

namespace HeatFlow.Core.Tests;

/// <summary>
/// Parametry grzewcze z wartościami domyślnymi z ConfigurationSeed. Wspólne dla testów,
/// żeby progi klasyfikacji i histereza nie były przepisywane w każdym teście osobno.
/// Dwell jest domyślnie wyłączony (MinDwellMinutes = 0) - testy, które go sprawdzają,
/// podają wartość jawnie.
/// </summary>
internal static class TestParameters
{
    public static HeatingParameters Default(
        double scoreThresholdMax = 50.0,
        double scoreThresholdDisabled = 0.0,
        double hysteresis = 0.5,
        double hysteresisSafetyThreshold = 2.0,
        int scoreDeficitMultiplier = 10,
        int minDwellMinutes = 0,
        int maxValvesOpen = 5,
        int minValvesOpen = 1)
    {
        return new HeatingParameters
        {
            ScoreThresholdMax = scoreThresholdMax,
            ScoreThresholdDisabled = scoreThresholdDisabled,
            Hysteresis = hysteresis,
            HysteresisSafetyThreshold = hysteresisSafetyThreshold,
            ScoreDeficitMultiplier = scoreDeficitMultiplier,
            MinDwellMinutes = minDwellMinutes,
            MaxValvesOpen = maxValvesOpen,
            MinValvesOpen = minValvesOpen
        };
    }
}
