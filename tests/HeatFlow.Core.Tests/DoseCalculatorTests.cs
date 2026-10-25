using HeatFlow.Core.Dose;
using HeatFlow.Domain;
using Xunit;

namespace HeatFlow.Core.Tests;

/// <summary>
/// Obliczenia Fazy 5 „Dawka”. Wartości oczekiwane z tablicy nastaw w analiza-kociol-nastawy.md § 5
/// (T_wewn 24 °C, k = 1) i z reguł § 9 / symulacji a7_sucho2.py.
/// </summary>
public class DoseCalculatorTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private static HeatingParameters P(double k = 1.0) => new() { FeedCorrectionK = k };

    // --- Model i tablica § 5 ---

    [Theory]
    [InlineData(0.0, 5186)]
    [InlineData(-10.0, 7736)]
    [InlineData(15.0, 1361)]
    public void Need_MatchesTable(double outdoor, double expected)
    {
        Assert.Equal(expected, DoseCalculator.Need(P(), outdoor), 0);
    }

    [Theory]
    [InlineData(15.0, 7, 320)]   // pod minimum Pawła - postój przycięty do 320
    [InlineData(10.0, 7, 220)]
    [InlineData(0.0, 7, 110)]
    [InlineData(-5.0, 9, 110)]
    [InlineData(-10.0, 10, 100)]
    public void Plan_FromMinimumWork_MatchesTable(double outdoor, int expectedWork, int expectedPause)
    {
        var plan = DoseCalculator.Plan(P(), outdoor, currentWork: 7, trimSec: 0);

        Assert.Equal(expectedWork, plan.Work);
        Assert.Equal(expectedPause, Round5(plan.ModelPause));
    }

    [Fact]
    public void Plan_NeverLeavesHardLimits_ForAnyWeatherKAndStartingPoint()
    {
        foreach (var k in new[] { 0.6, 1.0, 1.3 })
        foreach (var currentWork in new[] { 3, 7, 8, 9, 10, 15 })
        for (var outdoor = -35.0; outdoor <= 35.0; outdoor += 0.5)
        {
            var p = P(k);
            var plan = DoseCalculator.Plan(p, outdoor, currentWork, trimSec: 0);
            var decision = DoseCalculator.Decide(currentWork, 160, plan, null, Now, p);
            var blower = DoseCalculator.BlowerFor(decision.Work, decision.Pause, p);

            Assert.InRange(plan.Work, DoseLimits.WorkMin, DoseLimits.WorkMax);
            Assert.InRange(plan.ModelPause, DoseLimits.PauseMin, DoseLimits.PauseMax);
            Assert.InRange(decision.Work, DoseLimits.WorkMin, DoseLimits.WorkMax);
            Assert.InRange(decision.Pause, DoseLimits.PauseMin, DoseLimits.PauseMax);
            Assert.InRange(blower, DoseLimits.BlowerMin, DoseLimits.BlowerMax);
        }
    }

    [Fact]
    public void Plan_ParametersWiderThanHardLimits_AreClampedToHardLimits()
    {
        var p = new HeatingParameters { FeederWorkMax = 15, FeederPauseMin = 50, BlowerMax = 45 };
        var plan = DoseCalculator.Plan(p, -25, currentWork: 10, trimSec: -60);
        var blower = DoseCalculator.BlowerFor(plan.Work, (int)plan.ModelPause, p);

        Assert.Equal(DoseLimits.WorkMax, plan.Work);
        Assert.Equal(DoseLimits.PauseMin, plan.ModelPause);
        Assert.Equal(DoseLimits.BlowerMax, blower);
    }

    // --- Histereza czasu pracy ---

    [Fact]
    public void WorkHysteresis_SameNeed_KeepsCurrentWork()
    {
        // Przy -5 °C: 9 s daje postój 111 s (≥ 100), 8 s dawałoby 99 s. 10 s → 9 s dopiero,
        // gdy przy 9 s postój ≥ 130 - tu 111, więc 10 s zostaje. Bez histerezy skakałoby 9 ↔ 10.
        var need = DoseCalculator.Need(P(), -5.0);

        Assert.Equal(9, DoseCalculator.WorkWithHysteresis(9, need, P()));
        Assert.Equal(10, DoseCalculator.WorkWithHysteresis(10, need, P()));
    }

    [Fact]
    public void WorkHysteresis_GoesDown_WhenShorterWorkGivesPauseAbove130()
    {
        // +5 °C: przy 7 s postój 148 s ≥ 130 → z 10 s schodzi aż do 7 s.
        var need = DoseCalculator.Need(P(), 5.0);
        Assert.Equal(7, DoseCalculator.WorkWithHysteresis(10, need, P()));
    }

    // --- Martwa strefa, krok, odstęp ---

    [Fact]
    public void Decide_DifferenceInsideDeadband_NoChange()
    {
        var plan = new DosePlan(7, 120, false, 0);
        var d = DoseCalculator.Decide(7, 110, plan, null, Now, P());
        Assert.False(d.Change);
        Assert.Contains("martwej strefie", d.Reason);
    }

    [Fact]
    public void Decide_LargeDifference_StepLimitedTo20s()
    {
        var plan = new DosePlan(7, 222, false, 0);
        var d = DoseCalculator.Decide(7, 160, plan, null, Now, P());
        Assert.True(d.Change);
        Assert.Equal(180, d.Pause);
    }

    [Fact]
    public void Decide_DownwardStep_StepLimitedAndRoundedTo5()
    {
        var plan = new DosePlan(7, 111, false, 0);
        var d = DoseCalculator.Decide(7, 163, plan, null, Now, P());
        Assert.True(d.Change);
        Assert.Equal(145, d.Pause); // 163 − 20 = 143 → 145
    }

    [Fact]
    public void Decide_LessThan2hSinceLastChange_Waits()
    {
        var plan = new DosePlan(7, 222, false, 0);
        var d = DoseCalculator.Decide(7, 160, plan, Now.AddMinutes(-90), Now, P());
        Assert.False(d.Change);
        Assert.Contains("odstęp", d.Reason);

        var later = DoseCalculator.Decide(7, 160, plan, Now.AddHours(-2), Now, P());
        Assert.True(later.Change);
    }

    [Fact]
    public void Decide_SmallWorkChange_PauseGoesToModel()
    {
        // 9/105 → model 10/102: zmiana dawki mieści się w jednym kroku - jak w symulacji, od razu do modelu.
        var plan = new DosePlan(10, 101.7, false, 0);
        var d = DoseCalculator.Decide(9, 105, plan, null, Now, P());
        Assert.True(d.Change);
        Assert.Equal((10, 100), (d.Work, d.Pause));
    }

    [Fact]
    public void Decide_LargeWorkChange_DoseLimitedToOneStep_WorkChosenForLimitedDose()
    {
        // 9/150 → model 10/102 to +40 % dawki naraz. Najwyżej tyle, co krok 20 s postoju przy 9 s
        // (9/130); czas pracy dobrany do tej dawki histerezą - zostaje 9 s.
        var plan = new DosePlan(10, 101.7, false, 0);
        var d = DoseCalculator.Decide(9, 150, plan, null, Now, P());
        Assert.True(d.Change);
        Assert.Equal((9, 130), (d.Work, d.Pause));
        Assert.Contains("krok w stronę 10/102", d.Reason);
    }

    [Fact]
    public void Decide_ActivationAtWarmerSettings_FirstStepKeepsWork()
    {
        // 7/160 → model 9/111 (−5 °C): najpierw 7/140, a nie 9/180 (ta sama dawka, dziwny czas pracy).
        var plan = DoseCalculator.Plan(P(), -5.0, currentWork: 7, trimSec: 0);
        var d = DoseCalculator.Decide(7, 160, plan, null, Now, P());
        Assert.Equal((7, 140), (d.Work, d.Pause));
    }

    // --- Sufit i anti-windup ---

    [Fact]
    public void Plan_MinusTwenty_IsCeilingWithShortfallAbout24Percent()
    {
        var plan = DoseCalculator.Plan(P(), -20.0, currentWork: 10, trimSec: 0);
        Assert.True(plan.Ceiling);
        Assert.Equal(10, plan.Work);
        Assert.Equal(100, plan.ModelPause);
        Assert.InRange(plan.ShortfallPct, 23.0, 25.0); // tablica § 5: „brakuje 24 %”
    }

    [Fact]
    public void AntiWindup_AfterLongCeiling_WarmingComesDownImmediately()
    {
        // Dowolnie długo na suficie - nic się nie zbiera; po ociepleniu do -5 °C plan od razu
        // wraca do modelu, a decyzja schodzi z 10/100 pierwszym krokiem.
        for (var i = 0; i < 48; i++)
        {
            Assert.True(DoseCalculator.Plan(P(), -20.0, 10, 0).Ceiling);
        }

        var warm = DoseCalculator.Plan(P(), -5.0, currentWork: 10, trimSec: 0);
        Assert.False(warm.Ceiling);
        Assert.Equal(0, warm.ShortfallPct);
        Assert.Equal(DoseCalculator.PauseFor(10, DoseCalculator.Need(P(), -5.0)), warm.ModelPause, 3);

        var d = DoseCalculator.Decide(10, 100, warm, Now.AddHours(-3), Now, P());
        Assert.True(d.Change);
        Assert.Equal(120, d.Pause);
    }

    // --- Okno Tz 3 h ---

    [Fact]
    public void SmoothedOutdoorTemp_SingleColdHour_DoesNotMoveDoseBeyondDeadband()
    {
        // § 5: godzina z -10 °C w otoczeniu -5 °C przesuwa średnią 3 h o 1,7 K → postój 111 → 104 s,
        // poniżej martwej strefy 15 s.
        var measured = HourlySamples(Now, (-2, -5.0), (-1, -10.0));
        var forecast = Forecast(Now, -5.0);

        var tz = DoseCalculator.SmoothedOutdoorTemp(measured, forecast, Now, 2, 1);
        Assert.Equal(-6.67, tz!.Value, 2);

        var plan = DoseCalculator.Plan(P(), tz.Value, currentWork: 9, trimSec: 0);
        var d = DoseCalculator.Decide(9, 110, plan, null, Now, P());
        Assert.Equal(9, plan.Work);
        Assert.False(d.Change);
    }

    [Fact]
    public void SmoothedOutdoorTemp_ColdForThreeHours_MovesDose()
    {
        var measured = HourlySamples(Now, (-2, -10.0), (-1, -10.0));
        var tz = DoseCalculator.SmoothedOutdoorTemp(measured, Forecast(Now, -10.0), Now, 2, 1);
        Assert.Equal(-10.0, tz!.Value, 2);

        var plan = DoseCalculator.Plan(P(), tz.Value, currentWork: 9, trimSec: 0);
        var d = DoseCalculator.Decide(9, 110, plan, null, Now, P());
        Assert.True(d.Change);
        Assert.Equal(10, d.Work);
    }

    [Fact]
    public void SmoothedOutdoorTemp_NoForecast_UsesMeasurementOnly()
    {
        var measured = HourlySamples(Now, (-2, 4.0), (-1, 6.0));
        Assert.Equal(5.0, DoseCalculator.SmoothedOutdoorTemp(measured, null, Now, 2, 1)!.Value, 3);
    }

    [Fact]
    public void SmoothedOutdoorTemp_NoMeasurement_ReturnsNull()
    {
        Assert.Null(DoseCalculator.SmoothedOutdoorTemp(
            Array.Empty<(DateTime, double)>(), Forecast(Now, 0), Now, 2, 1));
    }

    // --- Korekta k ---

    [Fact]
    public void CorrectK_HouseTookLess_KGoesDownByMax005()
    {
        var day = new DailyBalance(0.0, 24.0, 4149, 18, true, null); // 0,8 × model
        var (k, _) = DoseCalculator.CorrectK(1.0, day, new DailyBalance(0, 24.0, 4000, 18, true, null), P());
        Assert.Equal(0.95, k!.Value, 3);
    }

    [Fact]
    public void CorrectK_HouseWarmedUp_StorageIsSubtractedFromFeed()
    {
        // Podajnik = model (5313,5 s przy 24,5 °C w środku), ale dom dogrzał się o 0,5 K:
        // netto = 5313,5 − 1549·0,5 = 4539 → 0,854 × model → k 1,0 → 0,956.
        // Bez członu pojemności k zostałoby 1,0 - uczyłoby się „za ciepło” jak model z ręcznych nastaw (§ 4a).
        var day = new DailyBalance(0.0, 24.5, 5313.5, 18, true, null);
        var before = new DailyBalance(0.0, 24.0, 5000, 18, true, null);
        var (k, _) = DoseCalculator.CorrectK(1.0, day, before, P());
        Assert.Equal(0.956, k!.Value, 3);

        var (kNoStorage, _) = DoseCalculator.CorrectK(1.0, day with { IndoorMean = 24.5 }, day, P());
        Assert.Equal(1.0, kNoStorage!.Value, 3);
    }

    [Fact]
    public void CorrectK_CeilingDay_IsSkipped()
    {
        var day = new DailyBalance(-15.0, 23.0, 7800, 23.5, true, null);
        var (k, reason) = DoseCalculator.CorrectK(1.0, day, null, P());
        Assert.Null(k);
        Assert.Contains("sufit", reason);
    }

    [Fact]
    public void CorrectK_FullHeatingDayFarBelowMaxFeed_StillLearns()
    {
        // Doba 22.10.2026: grzanie 23,2 h (kocioł pali ciągle przy trafionej dawce), podajnik 2261 s z możliwych 7855 s,
        // Tz 8,5 °C, w domu 25,9 °C → 0,65 × model 3503 s. To nie sufit - k ma schodzić.
        var day = new DailyBalance(8.5, 25.9, 2261, 23.2, true, null);
        var (k, reason) = DoseCalculator.CorrectK(1.0, day, null, P());
        Assert.Equal(0.95, k!.Value, 3);
        Assert.DoesNotContain("sufit", reason);
    }

    [Fact]
    public void CorrectK_StaysWithin06And13()
    {
        var low = DoseCalculator.CorrectK(0.62, new DailyBalance(5, 24, 500, 10, true, null), null, P());
        var high = DoseCalculator.CorrectK(1.28, new DailyBalance(-5, 24, 9000, 20, true, null), null, P());
        Assert.Equal(0.6, low.K!.Value, 3);
        Assert.Equal(1.3, high.K!.Value, 3);
    }

    [Fact]
    public void BalanceForDay_CountsFeederAcrossRefillReset()
    {
        var start = new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc);
        var samples = new List<DoseSample>();
        var counter = 1000;
        for (var i = -1; i < 288; i++)
        {
            counter = i == 100 ? 15 : counter + 15; // zasyp w środku doby: licznik od zera
            samples.Add(new DoseSample
            {
                TimestampUtc = start.AddMinutes(5 * i),
                OutdoorTemp = 0, IndoorTemp = 24, BlowerActual = i % 4 == 0 ? 0 : 35,
                FeederCounterSec = counter, SummerMode = false
            });
        }

        var day = DoseCalculator.BalanceForDay(samples, start, start.AddDays(1));
        Assert.True(day.Valid, day.InvalidReason);
        Assert.Equal(288 * 15, day.FeedSec);
        Assert.Equal(18.0, day.HeatingHours, 1);
    }

    [Fact]
    public void BalanceForDay_MissingData_IsInvalid()
    {
        var start = new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc);
        var samples = Enumerable.Range(0, 100).Select(i => new DoseSample
        {
            TimestampUtc = start.AddMinutes(5 * i), OutdoorTemp = 0, IndoorTemp = 24, BlowerActual = 35, FeederCounterSec = i * 15
        }).ToList();

        Assert.False(DoseCalculator.BalanceForDay(samples, start, start.AddDays(1)).Valid);
    }

    // --- Szybki trym od kotła i bezpieczniki ---

    [Fact]
    public void BoilerUnderpowered_FullBlowerAndBelowSetpointFor2h()
    {
        var window = Series(24, i => (60.0, 67.0, 35.0));
        Assert.True(DoseCalculator.BoilerUnderpowered(window, TimeSpan.FromHours(2)));

        var oneStandby = Series(24, i => (60.0, 67.0, i == 10 ? 0.0 : 35.0));
        Assert.False(DoseCalculator.BoilerUnderpowered(oneStandby, TimeSpan.FromHours(2)));

        var tooFew = Series(10, i => (60.0, 67.0, 35.0));
        Assert.False(DoseCalculator.BoilerUnderpowered(tooFew, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void StandbyEntries_CountsCrossingsOfSetpoint_WithRearmBelowSetpointMinus1()
    {
        // 64 → 67.5 (wejście 1) → 66.5 (bez uzbrojenia, 66 to nastawa − 1) → 67.2 (nie liczy)
        // → 65 (uzbrojenie) → 67 (wejście 2)
        var temps = new[] { 64.0, 67.5, 66.5, 67.2, 65.0, 67.0 };
        var window = temps.Select((t, i) => new DoseSample { TimestampUtc = Now.AddMinutes(5 * i), BoilerTemp = t, BoilerSetpoint = 67 }).ToList();
        Assert.Equal(2, DoseCalculator.StandbyEntries(window));
    }

    [Fact]
    public void BoilerColdFor_AllBelowSetpointMinus10ForAnHour()
    {
        Assert.True(DoseCalculator.BoilerColdFor(Series(12, i => (55.0, 67.0, 35.0)), TimeSpan.FromHours(1)));
        Assert.False(DoseCalculator.BoilerColdFor(Series(12, i => (i == 5 ? 58.0 : 55.0, 67.0, 35.0)), TimeSpan.FromHours(1)));
    }

    // --- Dmuchawa ---

    [Theory]
    [InlineData(7, 320, 34)]
    [InlineData(7, 200, 34)]  // 3,4 % pracy - jeszcze minimum
    [InlineData(7, 110, 37)]
    [InlineData(10, 100, 40)]
    public void BlowerFor_LinearBetween34And40(int work, int pause, int expected)
    {
        Assert.Equal(expected, DoseCalculator.BlowerFor(work, pause, P()));
    }

    [Fact]
    public void LimitBlower_FlueAtOrAbove185_DoesNotRaise()
    {
        Assert.Equal(36, DoseCalculator.LimitBlower(40, 36, 185, false, P()).Blower);
        Assert.Equal(34, DoseCalculator.LimitBlower(34, 36, 190, false, P()).Blower); // obniżyć wolno
        Assert.Equal(40, DoseCalculator.LimitBlower(40, 36, 150, false, P()).Blower);
    }

    [Fact]
    public void LimitBlower_FlueAbove200_MinusTwoNotBelowMin()
    {
        Assert.Equal(38, DoseCalculator.LimitBlower(40, 40, 205, false, P()).Blower);
        Assert.Equal(34, DoseCalculator.LimitBlower(40, 35, 205, false, P()).Blower);
    }

    [Fact]
    public void LimitBlower_SafetyAutomationWithin6h_DoesNotRaise()
    {
        Assert.Equal(34, DoseCalculator.LimitBlower(36, 34, 120, safetyAutomationRecent: true, P()).Blower);
    }

    // --- Licznik podajnika ---

    [Theory]
    [InlineData("30m,034s", 1834)]
    [InlineData("9m,056s", 596)]
    [InlineData("1h,02m,003s", 3723)]
    [InlineData("0m,000s", 0)]
    [InlineData("unavailable", null)]
    [InlineData(null, null)]
    public void ParseFeederCounter(string? value, int? expected)
    {
        Assert.Equal(expected, DoseCalculator.ParseFeederCounter(value));
    }

    // --- Twarda walidacja ---

    [Fact]
    public void Validate_Defaults_AreValid()
    {
        Assert.Empty(DoseLimits.Validate(new HeatingParameters()));
    }

    [Theory]
    [InlineData(nameof(HeatingParameters.FeederWorkMin), 6)]
    [InlineData(nameof(HeatingParameters.FeederWorkMax), 11)]
    [InlineData(nameof(HeatingParameters.FeederPauseMin), 90)]
    [InlineData(nameof(HeatingParameters.FeederPauseMax), 330)]
    [InlineData(nameof(HeatingParameters.BlowerMin), 32)]
    [InlineData(nameof(HeatingParameters.BlowerMax), 41)]
    [InlineData(nameof(HeatingParameters.FeedMinIntervalHours), 1)]
    public void Validate_OutsideHardLimits_IsRejected(string property, int value)
    {
        var p = new HeatingParameters();
        typeof(HeatingParameters).GetProperty(property)!.SetValue(p, value);
        Assert.Contains(DoseLimits.Validate(p), e => e.StartsWith(property));
    }

    [Fact]
    public void Validate_FeedCorrectionKOutside06To13_IsRejected()
    {
        Assert.NotEmpty(DoseLimits.Validate(new HeatingParameters { FeedCorrectionK = 1.4 }));
        Assert.NotEmpty(DoseLimits.Validate(new HeatingParameters { BlowerMin = 34, BlowerMax = 31 }));
    }

    // --- Helpers ---

    private static int Round5(double v) => (int)(Math.Round(v / 5.0, MidpointRounding.AwayFromZero) * 5);

    /// <summary>12 migawek na godzinę: godzina -2 = [now-2h, now-1h) itd.</summary>
    private static List<(DateTime, double)> HourlySamples(DateTime now, params (int Hour, double Temp)[] hours)
        => hours.SelectMany(h => Enumerable.Range(0, 12)
                .Select(i => (now.AddHours(h.Hour).AddMinutes(5 * i + 2), h.Temp)))
            .ToList();

    private static ForecastData Forecast(DateTime now, double temp) => new()
    {
        CurrentTemp = temp,
        ForecastHours = Enumerable.Range(1, 12).Select(i => new ForecastHour { DateTime = now.AddHours(i), Temperature = temp }).ToList()
    };

    private static List<DoseSample> Series(int count, Func<int, (double Boiler, double Setpoint, double Blower)> f)
        => Enumerable.Range(0, count).Select(i =>
        {
            var (b, z, dm) = f(i);
            return new DoseSample { TimestampUtc = Now.AddMinutes(5 * i), BoilerTemp = b, BoilerSetpoint = z, BlowerActual = dm };
        }).ToList();
}
