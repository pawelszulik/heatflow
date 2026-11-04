using System.Globalization;
using HeatFlow.Core.Dose;
using HeatFlow.Domain;
using HeatFlow.DoseSim;
using HeatFlow.Infrastructure.EsterownikMapper;
using Xunit;
using Xunit.Abstractions;

namespace HeatFlow.Core.Tests;

/// <summary>
/// Dociąganie domu do celu (2026-11-03, Paweł: „powinno trzymać zadaną, a nie stopień niżej”):
/// <list type="bullet">
/// <item>dom pod celem i kocioł nie dobija do nastawy → dodatek HouseCapacity · brak s/dobę;</item>
/// <item>pokoje pod celem przy zamkniętych zaworach → trym podtrzymania nie tnie węgla, dodatni trym zdjęty;</item>
/// <item>doba z domem pod celem, w której kocioł rzadko dobijał do nastawy → k nie spada.</item>
/// </list>
/// Tło: 25.10-3.11 dom zjechał z 25,9 do 24,8 °C przy limicie 4 zaworów - trym podtrzymania dokładał
/// +60 s postoju, a k uczyło się z przyciętych dób.
/// </summary>
public class DoseCatchUpTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private readonly ITestOutputHelper _out;

    public DoseCatchUpTests(ITestOutputHelper output) => _out = output;

    private static RoomState Rs(string room, double target, double actual, bool open, DateTime at)
        => new()
        {
            RoomName = room, TempTarget = (decimal)target, TempActual = (decimal)actual,
            TempDeficit = (decimal)(target - actual), HeatingEnabled = open, RecordedAt = at
        };

    // --- Obliczenia ---

    [Fact]
    public void HouseBelowTarget_WarmerOutside_DoseDoesNotDrop()
    {
        var p = new HeatingParameters { FeederWorkMin = 7, FeederWorkMax = 10, FeederPauseMin = 100, FeederPauseMax = 320,
            FeedMaxStepSec = 30, FeedDeadbandSec = 15, FeedMinIntervalHours = 1 };
        var warmer = new DosePlan(7, 175, false, 0);
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

        var held = DoseCalculator.Decide(7, 130, warmer, null, now, p, holdDose: true);
        Assert.False(held.Change);
        Assert.Equal((7, 130), (held.Work, held.Pause));
        Assert.Equal((7, 160), held.HeldBack);

        var free = DoseCalculator.Decide(7, 130, warmer, null, now, p);   // dom na celu - tnie jak dotąd
        Assert.True(free.Change);
        Assert.Equal(160, free.Pause);
    }

    [Fact]
    public void HouseBelowTarget_DoseCanStillGoUp_AndOutOfBoundsIsFixed()
    {
        var p = new HeatingParameters { FeederWorkMin = 7, FeederWorkMax = 10, FeederPauseMin = 100, FeederPauseMax = 320,
            FeedMaxStepSec = 30, FeedDeadbandSec = 15, FeedMinIntervalHours = 1 };
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

        var up = DoseCalculator.Decide(7, 175, new DosePlan(7, 130, false, 0), null, now, p, holdDose: true);
        Assert.True(up.Change);
        Assert.Equal(145, up.Pause);

        var bounds = DoseCalculator.Decide(7, 90, new DosePlan(7, 200, false, 0), null, now, p, holdDose: true);
        Assert.True(bounds.Change);   // poza twardą granicą 100 s - poprawiamy mimo braku
    }

    [Theory]
    [InlineData(0.31, true)]
    [InlineData(0.30, false)]    // próg
    [InlineData(0.24, false)]    // sam hol - nie może trzymać dawki na stałe
    public void HoldDose_Threshold(double deficit, bool hold)
        => Assert.Equal(hold, DoseCalculator.HoldDose(new HouseDeficit(deficit, 0, 14, Array.Empty<(string, double, double, double)>())));

    [Fact]
    public void HouseDeficit_WeightedBelowOnly_StarvedOnlyFromClosedValves()
    {
        var weights = new Dictionary<string, double> { ["salon"] = 600, ["hol"] = 300, ["kuchnia"] = 300, ["garaz"] = 0 };
        var states = new List<RoomState>
        {
            // salon 1 K pod celem, zawór zamknięty połowę czasu
            Rs("salon", 26, 25, false, Now.AddMinutes(-10)), Rs("salon", 26, 25, true, Now.AddMinutes(-5)),
            // hol 2 K pod celem, zawór otwarty - nie „głodzony”
            Rs("hol", 25, 23, true, Now.AddMinutes(-10)), Rs("hol", 25, 23, true, Now.AddMinutes(-5)),
            // kuchnia 1 K ponad celem - nie odejmuje braku innym
            Rs("kuchnia", 24, 25, false, Now.AddMinutes(-10)), Rs("kuchnia", 24, 25, false, Now.AddMinutes(-5)),
            // garaż bez wagi i odczyt spoza zakresu - pomijane
            Rs("garaz", 15, 10, false, Now.AddMinutes(-5)), Rs("salon", 26, 0, false, Now.AddMinutes(-1))
        };

        var d = DoseCalculator.HouseDeficitFrom(states, weights, minRooms: 3)!;

        Assert.Equal(3, d.Rooms);
        Assert.Equal((600 * 1.0 + 300 * 2.0) / 1200.0, d.DeficitK, 6);
        Assert.Equal(600 * 0.5 / 1200.0, d.StarvedK, 6);
        Assert.Equal(new[] { "salon", "hol" }, d.Below.Select(b => b.Name));
        Assert.Null(DoseCalculator.HouseDeficitFrom(states, weights, minRooms: 4));
    }

    [Fact]
    public void CatchUp_OnlyAboveDeadband_AndOnlyWhenBoilerDoesNotReachSetpoint_Capped()
    {
        var p = new HeatingParameters();
        Assert.Equal(0, DoseCalculator.CatchUpSec(0.2, p));               // martwa strefa 0,2 K
        Assert.Equal(1549 * 0.5, DoseCalculator.CatchUpSec(0.5, p), 6);
        Assert.Equal(p.DoseStorageBonusMaxSec, DoseCalculator.CatchUpSec(3.0, p));
        Assert.Equal(0, DoseCalculator.CatchUpSec(null, p));
    }

    [Fact]
    public void KNoDecrease_OnlyWhenHouseBelowAndBoilerRarelyAtSetpoint()
    {
        var p = new HeatingParameters(); // 2 wejścia / 6 h → granica 8 na dobę
        Assert.Null(DoseCalculator.KNoDecreaseReason(0.15, 0, p));
        Assert.Null(DoseCalculator.KNoDecreaseReason(0.6, 8, p));
        Assert.Null(DoseCalculator.KNoDecreaseReason(null, 0, p));
        var reason = DoseCalculator.KNoDecreaseReason(0.6, 3, p);
        Assert.NotNull(reason);
        Assert.Contains("0,60 K pod celem", reason);
        Assert.Contains("tylko 3×", reason);
    }

    [Fact]
    public void CorrectK_WithNoDecreaseReason_HoldsK_ButStillRises()
    {
        var p = new HeatingParameters();
        var lean = new DailyBalance(0, 24, 4032, 18, true, null);   // 0,78 modelu → k w dół
        var rich = new DailyBalance(0, 24, 6200, 18, true, null);   // 1,2 modelu → k w górę

        Assert.Equal(0.95, DoseCalculator.CorrectK(1.0, lean, null, p).K!.Value, 3);
        var held = DoseCalculator.CorrectK(1.0, lean, null, p, "dom pod celem");
        Assert.Equal(1.0, held.K!.Value, 6);
        Assert.Contains("k NIE spada: dom pod celem", held.Reason);
        Assert.True(DoseCalculator.CorrectK(1.0, rich, null, p, "dom pod celem").K > 1.0);
    }

    // --- Faza 5 ---

    private static DoseHarness Harness(DoseState state)
    {
        var h = new DoseHarness(new ControllerFeedSettings(7, 200, 34), new HeatingParameters());
        h.Config.Rooms = SimDefaults.AllRooms();
        h.Repo.State = state;
        return h;
    }

    /// <summary>Migawki kotła z 6 h: <paramref name="boilerAt"/> = temperatura kotła od minut przed „teraz”.</summary>
    private static void Samples(DoseHarness h, Func<int, double> boilerAt, DateTime from, DateTime to, int counterStep = 12)
    {
        var counter = 1000;
        for (var t = from; t < to; t = t.AddMinutes(5))
        {
            counter += counterStep;
            h.Repo.Samples.Add(new DoseSample
            {
                TimestampUtc = t, OutdoorTemp = 0, IndoorTemp = 24, BoilerTemp = boilerAt((int)(to - t).TotalMinutes),
                BoilerSetpoint = 67, BlowerActual = 35, FeederCounterSec = counter, SummerMode = false, FlueTemp = 140
            });
        }
    }

    /// <summary>Migawki Fazy 1 dla wszystkich pokoi z wagą: faktyczna = cel − <paramref name="below"/>.</summary>
    private static void Rooms(DoseHarness h, DateTime from, DateTime to, double below, bool open)
    {
        foreach (var room in h.Config.Rooms.Where(r => r.HeatLossW > 0))
            for (var t = from; t < to; t = t.AddMinutes(5))
                h.Repo.RoomStates.Add(Rs(room.Name, room.TempTarget, room.TempTarget - below, open, t));
    }

    private static void PublishNow(DoseHarness h, double boiler)
        => h.Publish(new DoseInputsSnapshot(0, 24, boiler, 67, 140, 35, 5000, false, true), Now);

    // Kocioł co ~1,5 h dobija do 67 i spada - 4 wejścia w podtrzymanie w 6 h.
    private static double Sawtooth(int minutesAgo) => minutesAgo % 90 < 15 ? 67.5 : 64.0;

    [Fact]
    public async Task StarvedRooms_PositiveTrimCleared_StandbyTrimBlocked_CatchUpDespiteSetpoint()
    {
        var h = Harness(new DoseState { TrimSec = 60, LastKCorrectionDate = Now.Date });
        Samples(h, Sawtooth, Now.AddHours(-6), Now);
        Rooms(h, Now.AddHours(-3), Now, below: 0.8, open: false);
        PublishNow(h, 64);

        await h.StepAsync(Now, null);

        var st = h.Repo.State!;
        Assert.Equal(0, st.TrimSec);
        var cleared = Assert.Single(h.Repo.Log, e => e.Parameter == DoseLog.Trim && e.ReasonCode == "DOM_POD_CELEM");
        Assert.Equal((60.0, 0.0), (cleared.OldValue!.Value, cleared.NewValue!.Value));
        Assert.Contains("dom pod celem o 0,8 K", cleared.Message);
        var blocked = Assert.Single(h.Repo.Log, e => e.ReasonCode == "TRYM_PODTRZYMANIA" && e.Status == DoseLog.StatusBlocked);
        Assert.Contains("węgiel nie spada", blocked.Message);
        // 2026-11-04: kocioł dobija do nastawy, a dociąganie zostaje - dojścia nie dowodzą, że dom nie weźmie ciepła.
        Assert.Equal(Math.Round(1549 * 0.8), st.StorageBonusSec);
        Assert.Contains("dociąganie: dom pod celem o 0,80 K", st.LastReason);
    }

    [Fact]
    public async Task RoomsBelowWithOpenValves_StandbyTrimBlocked_ButAtTargetItWorks()
    {
        // 4.11: hol, łazienka, Edyta i sypialnia pod celem przy zaworach otwartych 100 % - trym nie może wtedy ciąć węgla.
        var h = Harness(new DoseState { LastKCorrectionDate = Now.Date });
        Samples(h, Sawtooth, Now.AddHours(-6), Now);
        Rooms(h, Now.AddHours(-3), Now, below: 0.8, open: true);
        PublishNow(h, 64);

        await h.StepAsync(Now, null);

        Assert.Equal(0, h.Repo.State!.TrimSec);
        Assert.Contains(h.Repo.Log, e => e.ReasonCode == "TRYM_PODTRZYMANIA" && e.Status == DoseLog.StatusBlocked);

        // Dom na celu: dojścia do nastawy dalej tną węgiel.
        var at = Harness(new DoseState { LastKCorrectionDate = Now.Date });
        Samples(at, Sawtooth, Now.AddHours(-6), Now);
        Rooms(at, Now.AddHours(-3), Now, below: 0.1, open: true);
        PublishNow(at, 64);

        await at.StepAsync(Now, null);

        Assert.Equal(20, at.Repo.State!.TrimSec);
        Assert.Contains(at.Repo.Log, e => e.Parameter == DoseLog.Trim && e.ReasonCode == "TRYM_PODTRZYMANIA" && e.NewValue == 20);
    }

    [Fact]
    public async Task HouseBelow_BoilerShortOfFire_CatchUpAddsHouseCapacityTimesDeficit()
    {
        var h = Harness(new DoseState { LastKCorrectionDate = Now.Date });
        Samples(h, _ => 66.0, Now.AddHours(-6), Now);           // pod nastawą, bez podtrzymania
        Rooms(h, Now.AddHours(-3), Now, below: 0.5, open: true);
        PublishNow(h, 66);

        await h.StepAsync(Now, null);
        var st = h.Repo.State!;
        Assert.Equal(Math.Round(1549 * 0.5), st.StorageBonusSec);
        Assert.Contains("dociąganie: dom pod celem o 0,50 K → +774 s/dobę", st.LastReason);

        // Bez braku - bez dodatku, a potrzeba mniejsza dokładnie o dodatek.
        var h0 = Harness(new DoseState { LastKCorrectionDate = Now.Date });
        Samples(h0, _ => 66.0, Now.AddHours(-6), Now);
        Rooms(h0, Now.AddHours(-3), Now, below: 0.1, open: true);
        PublishNow(h0, 66);
        await h0.StepAsync(Now, null);
        Assert.Equal(0, h0.Repo.State!.StorageBonusSec);
        Assert.Equal(st.NeedSecPerDay!.Value - 775, h0.Repo.State.NeedSecPerDay!.Value, 0);
    }

    [Fact]
    public async Task KCorrection_HouseBelowAndBoilerShortOfFire_KHeld_OtherwiseFalls()
    {
        async Task<FeedChangeLog> Night(double below)
        {
            var h = Harness(new DoseState { LastKCorrectionDate = Now.Date.AddDays(-2) });
            h.Parameters.FeedCorrectionK = 1.0;
            Samples(h, _ => 66.0, Now.Date.AddDays(-2), Now, counterStep: 14);   // 4032 s/dobę ≈ 0,78 modelu
            Rooms(h, Now.Date.AddDays(-2), Now, below, open: true);
            PublishNow(h, 66);
            await h.StepAsync(Now, null);
            return Assert.Single(h.Repo.Log, e => e.Parameter == DoseLog.K);
        }

        var held = await Night(0.5);
        Assert.Equal((1.0, 1.0), (held.OldValue!.Value, held.NewValue!.Value));
        Assert.Contains("k NIE spada: dom był średnio 0,50 K pod celem, a kocioł dobił do nastawy tylko 0×", held.Message);

        var falls = await Night(0.0);
        Assert.Equal((1.0, 0.95), (falls.OldValue!.Value, falls.NewValue!.Value));
    }

    // --- Pętla zamknięta ---

    private static List<(DateTime Utc, double Tz)> Tz(string file)
        => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Data", file)).Skip(1)
            .Select(l => l.Split(','))
            .Select(c => (DateTime.SpecifyKind(DateTime.ParseExact(c[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), DateTimeKind.Utc),
                double.Parse(c[1], CultureInfo.InvariantCulture)))
            .ToList();

    /// <summary>
    /// Jak u Pawła 3.11: k za niskie (0,75 przy domu f = 1,0), dom 0,7 K pod celem. Dom wraca do celu w ~2 doby
    /// bez przegrzania, k idzie w górę, granice i odstęp 2 h zachowane.
    /// </summary>
    [Theory]
    [InlineData("tz-pazdziernik.csv", 10, 6)]
    [InlineData("tz-marzec.csv", 3, 1)]
    public async Task ClosedLoop_KTooLow_HouseBelow_ReturnsToTarget(string file, int month, int day)
    {
        var from = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, month, day), Warsaw);
        var rooms = SimDefaults.AllRooms();
        var goal = DoseCalculator.HouseTarget(rooms, from, false).Target!.Value;
        var r = await ClosedLoopRunner.RunAsync(Tz(file), from, 10, 1.0, new HeatingParameters { FeedCorrectionK = 0.75 },
            startAtModel: true, rooms: rooms, startIndoor: goal - 0.7);

        var daily = r.Hours.GroupBy(x => (x.Utc - from).Days).Select(g => g.Average(x => x.Indoor)).ToList();
        _out.WriteLine($"cel {goal:F2}; Tin dobowo: {string.Join(" ", daily.Select(d => d.ToString("F2", CultureInfo.InvariantCulture)))}; " +
                       $"k {r.Hours.Last().K:F3}; zmian/dobę {r.ChangesPerDay:F1}");

        Assert.Empty(r.LimitViolations);
        Assert.Empty(r.Harness.Errors.Errors);
        Assert.True(r.MinHoursBetweenChanges() >= 1.0 - 1e-9, $"min. odstęp {r.MinHoursBetweenChanges():F2} h");
        Assert.True(daily.Skip(2).All(d => d >= goal - 0.3), $"Tin dobowa od 3. doby min {daily.Skip(2).Min():F2} przy celu {goal:F2}");
        Assert.True(r.Hours.Max(x => x.Indoor) <= goal + 0.5, $"Tin maks. {r.Hours.Max(x => x.Indoor):F2} przy celu {goal:F2}");
        Assert.True(r.Hours.Last().K > 0.85, $"k {r.Hours.Last().K:F3}");
    }

    // Cele i temperatury pokoi z RoomState 4.11 od 12:00 UTC: brak domu 1,2 °C przy średniej ważonej ok. 24,7 °C.
    private const double HouseNov4 = 24.7;
    private static readonly Dictionary<string, double> TargetsNov4 = new()
    {
        ["balkonowy"] = 26, ["garaz"] = 6, ["salon"] = 27, ["jadalnia"] = 26, ["hol"] = 26, ["edyta"] = 26, ["sypialnia"] = 26,
        ["grzes"] = 26, ["przejsciowy"] = 25, ["suszarnia"] = 23, ["kuchnia"] = 24, ["sien"] = 22, ["lazienka"] = 25,
        ["spizarnia"] = 24, ["toaleta"] = 25
    };
    private static readonly Dictionary<string, double> ActualNov4 = new()
    {
        ["balkonowy"] = 24.68, ["garaz"] = 14, ["salon"] = 25.62, ["jadalnia"] = 24.93, ["hol"] = 23.23, ["edyta"] = 24.45,
        ["sypialnia"] = 24.96, ["grzes"] = 25.07, ["przejsciowy"] = 24.26, ["suszarnia"] = 25.99, ["kuchnia"] = 25.27,
        ["sien"] = 21.37, ["lazienka"] = 23.95, ["spizarnia"] = 25.54, ["toaleta"] = 24.15
    };

    private async Task<(ClosedLoopResult R, Func<DateTime, double> Deficit, List<DateTime> Cuts)> Nov4Run(double plantRise, double uncontrolled)
    {
        var tz = Tz("tz-listopad.csv");
        var from = tz[0].Utc;
        tz = tz.Concat(tz.Select(x => (x.Utc.AddDays(4), x.Tz))).Concat(tz.Select(x => (x.Utc.AddDays(8), x.Tz))).ToList();
        var rooms = SimDefaults.AllRooms(TargetsNov4);
        var offsets = ActualNov4.ToDictionary(x => x.Key, x => x.Value - HouseNov4);
        var r = await ClosedLoopRunner.RunAsync(tz, from, 10, 1.0,
            new HeatingParameters { FeedCorrectionK = 0.947, FeedMaxStepSec = 30, FeedMinIntervalHours = 1, DoseTrimLimitSec = 100 },
            startAtModel: true, rooms: rooms, roomOffsets: offsets, startIndoor: HouseNov4, uncontrolledShare: uncontrolled,
            plantGoal: HouseNov4 + plantRise);

        // Brak domu z 3 h jak w Fazie 5, z godzinowych zapisów (RoomState symulator czyści po dobie).
        var weighted = rooms.Where(x => x.HeatLossW > 0).ToList();
        double DeficitOf(double house) => weighted.Sum(x => x.HeatLossW * Math.Max(0, x.TempTarget - house - offsets[x.Name]))
                                          / weighted.Sum(x => x.HeatLossW);
        double Deficit(DateTime t) => r.Hours.Where(h => h.Utc > t.AddHours(-3) && h.Utc <= t)
            .Select(h => DeficitOf(h.Indoor)).DefaultIfEmpty(DeficitOf(HouseNov4)).Average();
        // DoseChanges daje 0 dla parametru, który się nie zmienił - wtedy liczy się wartość drugiej strony.
        static double Duty(double w, double p, double otherW, double otherP)
        {
            var work = w > 0 ? w : otherW > 0 ? otherW : 7;
            var pause = p > 0 ? p : otherP;
            return work / (work + pause);
        }
        var cuts = r.DoseChanges()
            .Where(c => Duty(c.WorkTo, c.PauseTo, c.WorkFrom, c.PauseFrom) < Duty(c.WorkFrom, c.PauseFrom, c.WorkTo, c.PauseTo) - 1e-9)
            .Select(c => c.Utc).ToList();

        foreach (var h in r.Hours.Where(h => h.Utc.Hour % 12 == 0))
            _out.WriteLine($"{h.Utc:dd.MM HH:mm} Tz {h.Outdoor,5:F1} dom {h.Indoor:F2} brak {Deficit(h.Utc):F2} {h.Work}/{h.Pause} = " +
                           $"{h.Work / (double)(h.Work + h.Pause) * 86400 * 0.00425:F1} kg/dobę kocioł {h.Boiler:F0}");
        _out.WriteLine($"węgiel {r.Plant.FeederCounterSec * 0.00425 / 10:F1} kg/dobę, podtrzymanie {r.Hours.Count(h => !h.Heating) / (double)r.Hours.Count:P0} godzin, " +
                       $"kocioł maks. {r.Hours.Max(h => h.Boiler):F1}, zmiany dawki {r.ChangesPerDay:F1}/dobę");
        foreach (var c in cuts) _out.WriteLine($"cięcie {c:dd.MM HH:mm} przy braku {Deficit(c):F2}");
        return (r, Deficit, cuts);
    }

    /// <summary>
    /// Wymaganie Pawła z 4.11: „skoro temp nie jest osiągnięta, to nie powinno być mimo zwiększania się temp na
    /// zewnątrz zmniejszania dawki węgla”. Pogoda 1-4.11 z DoseSample, cele i pokoje z RoomState, parametry z produkcji.
    /// Dom bierze mało (jak 4.11: kocioł dobija do nastawy), więc brak zostaje ok. 0,7 °C. Dawka nie spada ani razu,
    /// ale kocioł się nie zalewa: przy nastawie wyższej niż odbiór podajnik i tak staje w podtrzymaniu.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public async Task ClosedLoop_HouseBelow_WarmerOutside_DoseNeverDrops(double uncontrolled)
    {
        var (r, deficit, cuts) = await Nov4Run(0.3, uncontrolled);

        Assert.Empty(r.LimitViolations);
        Assert.Empty(r.Harness.Errors.Errors);
        Assert.True(r.Hours.Skip(24).All(h => deficit(h.Utc) > DoseCalculator.HoldDoseDeadbandK));
        Assert.All(cuts, c => Assert.True(deficit(c) <= DoseCalculator.HoldDoseDeadbandK + 0.02, $"{c:u}: brak {deficit(c):F2} °C"));
        Assert.True(r.ChangesPerDay <= 1, $"{r.ChangesPerDay:F1} zmian/dobę");
        Assert.Contains(r.Log, e => e.ReasonCode == "DOM_POD_CELEM" && e.Status == DoseLog.StatusBlocked && e.Message.Contains("kg/dobę"));
        Assert.True(r.Hours.Max(h => h.Boiler) <= 70, $"kocioł maks. {r.Hours.Max(h => h.Boiler):F1}");
    }

    /// <summary>Dom bierze ciepło i dochodzi do celu: blokada puszcza, dawka zjeżdża, nie zostaje w górze.</summary>
    [Fact]
    public async Task ClosedLoop_HouseReachesTarget_HoldReleases_DoseFalls()
    {
        var (r, deficit, cuts) = await Nov4Run(1.2, 0.0);

        Assert.Empty(r.LimitViolations);
        Assert.True(r.Hours.Last().Indoor >= HouseNov4 + 1.0, $"dom {r.Hours.Last().Indoor:F2}");
        Assert.True(r.Hours.Max(h => h.Indoor) <= HouseNov4 + 1.7, $"dom maks. {r.Hours.Max(h => h.Indoor):F2}");
        Assert.All(cuts, c => Assert.True(deficit(c) <= DoseCalculator.HoldDoseDeadbandK + 0.02, $"{c:u}: brak {deficit(c):F2} °C"));
        Assert.True(cuts.Count > 0, "dawka nie spadła po dojściu do celu");
    }
}
