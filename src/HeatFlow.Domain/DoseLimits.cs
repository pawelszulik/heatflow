namespace HeatFlow.Domain;

/// <summary>
/// Twarde granice nastaw kotła dla Fazy 5 „Dawka” - decyzje Pawła (2026-10-20), nie strojenie.
/// Parametry w <see cref="HeatingParameters"/> mogą tylko zawęzić te widełki, nigdy ich poszerzyć:
/// API odrzuca takie wartości (400), a Faza 5 i tak przycina każdy zapis do tych stałych.
/// Źródło: analiza-kociol-nastawy.md § 2 i § 9.
/// </summary>
public static class DoseLimits
{
    /// <summary>Czas pracy podajnika w grzaniu [s] - nie przekraczać (Paweł).</summary>
    public const int WorkMin = 7;
    public const int WorkMax = 10;

    /// <summary>Czas postoju podajnika w grzaniu [s] - nie przekraczać (Paweł).</summary>
    public const int PauseMin = 100;
    public const int PauseMax = 320;

    /// <summary>
    /// Moc dmuchawy w grzaniu [%] - automat trzyma 34-40 %, twardo 33-40 %.
    /// Podniesienie ponad 40 % tylko decyzją Pawła (zmiana tej stałej).
    /// 33 % = dolna granica od 2026-10-25 (28-30 % dawało niedopałki).
    /// </summary>
    public const int BlowerMin = 33;
    public const int BlowerMax = 40;

    /// <summary>
    /// Sprawdza parametry Fazy 5. Zwraca listę błędów (pusta = poprawne).
    /// </summary>
    public static List<string> Validate(HeatingParameters p)
    {
        var errors = new List<string>();

        void Range(string name, double value, double min, double max)
        {
            if (double.IsNaN(value) || value < min || value > max)
                errors.Add($"{name} = {value} poza zakresem {min}-{max}");
        }

        Range(nameof(p.FeederWorkMin), p.FeederWorkMin, WorkMin, WorkMax);
        Range(nameof(p.FeederWorkMax), p.FeederWorkMax, WorkMin, WorkMax);
        Range(nameof(p.FeederPauseMin), p.FeederPauseMin, PauseMin, PauseMax);
        Range(nameof(p.FeederPauseMax), p.FeederPauseMax, PauseMin, PauseMax);
        Range(nameof(p.BlowerMin), p.BlowerMin, BlowerMin, BlowerMax);
        Range(nameof(p.BlowerMax), p.BlowerMax, BlowerMin, BlowerMax);
        if (p.FeederWorkMin > p.FeederWorkMax) errors.Add($"FeederWorkMin ({p.FeederWorkMin}) > FeederWorkMax ({p.FeederWorkMax})");
        if (p.FeederPauseMin > p.FeederPauseMax) errors.Add($"FeederPauseMin ({p.FeederPauseMin}) > FeederPauseMax ({p.FeederPauseMax})");
        if (p.BlowerMin > p.BlowerMax) errors.Add($"BlowerMin ({p.BlowerMin}) > BlowerMax ({p.BlowerMax})");

        Range(nameof(p.FeedPerKelvin), p.FeedPerKelvin, 100, 400);
        Range(nameof(p.FeedOffset), p.FeedOffset, -5000, 5000);
        Range(nameof(p.HouseCapacity), p.HouseCapacity, 500, 3000);
        Range(nameof(p.IndoorTargetForFeed), p.IndoorTargetForFeed, 18, 26);
        Range(nameof(p.DoseTargetRiseKPerHour), p.DoseTargetRiseKPerHour, 0.01, 1.0);
        Range(nameof(p.DoseStorageBonusMaxSec), p.DoseStorageBonusMaxSec, 0, 8000);
        Range(nameof(p.DoseActualSmoothingHours), p.DoseActualSmoothingHours, 3, 72);
        Range(nameof(p.DoseActualMaxAboveTargetsK), p.DoseActualMaxAboveTargetsK, 0, 6);
        Range(nameof(p.FeedKMin), p.FeedKMin, 0.3, 1.0);
        Range(nameof(p.FeedKMax), p.FeedKMax, 1.0, 2.0);
        Range(nameof(p.FeedCorrectionK), p.FeedCorrectionK, Math.Max(0.3, p.FeedKMin), Math.Min(2.0, p.FeedKMax));
        Range(nameof(p.FeedKGain), p.FeedKGain, 0.05, 1.0);
        Range(nameof(p.FeedKMaxStep), p.FeedKMaxStep, 0.01, 0.2);
        Range(nameof(p.DoseIndoorMinSensors), p.DoseIndoorMinSensors, 1, 20);
        Range(nameof(p.DoseFlueNoRaiseTemp), p.DoseFlueNoRaiseTemp, 100, 250);
        Range(nameof(p.DoseFlueMaxTemp), p.DoseFlueMaxTemp, 150, 250);
        if (p.DoseFlueNoRaiseTemp > p.DoseFlueMaxTemp)
            errors.Add($"DoseFlueNoRaiseTemp ({p.DoseFlueNoRaiseTemp}) > DoseFlueMaxTemp ({p.DoseFlueMaxTemp})");
        Range(nameof(p.DoseFlueBlowerCutPct), p.DoseFlueBlowerCutPct, 1, 5);
        Range(nameof(p.DoseFlueSafetyHoldHours), p.DoseFlueSafetyHoldHours, 0, 48);
        Range(nameof(p.DoseTrimUnderpowerK), p.DoseTrimUnderpowerK, 1, 15);
        Range(nameof(p.DoseTrimUnderpowerHours), p.DoseTrimUnderpowerHours, 1, 12);
        Range(nameof(p.DoseTrimStandbyEntries), p.DoseTrimStandbyEntries, 1, 10);
        Range(nameof(p.DoseTrimStandbyHours), p.DoseTrimStandbyHours, 1, 24);
        Range(nameof(p.DoseTrimStepSec), p.DoseTrimStepSec, 5, 60);
        Range(nameof(p.DoseTrimLimitSec), p.DoseTrimLimitSec, 0, 200);
        Range(nameof(p.DoseBoilerColdK), p.DoseBoilerColdK, 3, 30);
        Range(nameof(p.DoseBoilerColdWarnMinutes), p.DoseBoilerColdWarnMinutes, 15, 360);
        Range(nameof(p.DoseCeilingDayHeatingHours), p.DoseCeilingDayHeatingHours, 12, 24);
        Range(nameof(p.DoseBlowerLowDuty), p.DoseBlowerLowDuty, 0.01, 0.09);
        Range(nameof(p.FeedSmoothingPastHours), p.FeedSmoothingPastHours, 1, 24);
        Range(nameof(p.FeedSmoothingForecastHours), p.FeedSmoothingForecastHours, 0, 24);
        Range(nameof(p.FeedMinIntervalHours), p.FeedMinIntervalHours, 2, 24);
        Range(nameof(p.FeedDeadbandSec), p.FeedDeadbandSec, 5, 60);
        Range(nameof(p.FeedMaxStepSec), p.FeedMaxStepSec, 5, 60);
        Range(nameof(p.FeedWorkUpHysteresisSec), p.FeedWorkUpHysteresisSec, PauseMin, PauseMax);
        Range(nameof(p.DoseBoilerStaleMinutes), p.DoseBoilerStaleMinutes, 10, 360);
        if (p.DoseBoilerColdMinutes != 0) Range(nameof(p.DoseBoilerColdMinutes), p.DoseBoilerColdMinutes, 30, 360);
        Range(nameof(p.DoseOutdoorStaleMinutes), p.DoseOutdoorStaleMinutes, 30, 360);

        return errors;
    }
}
