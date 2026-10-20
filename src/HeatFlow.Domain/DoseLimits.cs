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
    /// Moc dmuchawy w grzaniu [%] - na start automat trzyma 30-35 %, twardo 28-35 %.
    /// 45 % to sufit Pawła „na później”, podniesienie ponad 35 % tylko jego decyzją (zmiana tej stałej).
    /// 28 % = minimalna moc dmuchawy w sterowniku (regulator spalin schodzi do niej).
    /// </summary>
    public const int BlowerMin = 28;
    public const int BlowerMax = 35;

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
        Range(nameof(p.FeedCorrectionK), p.FeedCorrectionK, 0.6, 1.3);
        Range(nameof(p.FeedSmoothingPastHours), p.FeedSmoothingPastHours, 1, 24);
        Range(nameof(p.FeedSmoothingForecastHours), p.FeedSmoothingForecastHours, 0, 24);
        Range(nameof(p.FeedMinIntervalHours), p.FeedMinIntervalHours, 2, 24);
        Range(nameof(p.FeedDeadbandSec), p.FeedDeadbandSec, 5, 60);
        Range(nameof(p.FeedMaxStepSec), p.FeedMaxStepSec, 5, 60);
        Range(nameof(p.FeedWorkUpHysteresisSec), p.FeedWorkUpHysteresisSec, PauseMin, PauseMax);
        Range(nameof(p.DoseBoilerStaleMinutes), p.DoseBoilerStaleMinutes, 10, 120);
        Range(nameof(p.DoseOutdoorStaleMinutes), p.DoseOutdoorStaleMinutes, 30, 360);

        return errors;
    }
}
