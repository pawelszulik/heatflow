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
        Assert.Equal(0, DoseCalculator.CatchUpSec(0.2, 0, p));               // martwa strefa 0,2 K
        Assert.Equal(1549 * 0.5, DoseCalculator.CatchUpSec(0.5, 1, p), 6);    // 1 podtrzymanie w 6 h - jeszcze dokłada
        Assert.Equal(0, DoseCalculator.CatchUpSec(0.5, 2, p));               // dobija do nastawy - więcej węgla nie pomoże
        Assert.Equal(p.DoseStorageBonusMaxSec, DoseCalculator.CatchUpSec(3.0, 0, p));
        Assert.Equal(0, DoseCalculator.CatchUpSec(null, 0, p));
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
    public async Task StarvedRooms_PositiveTrimCleared_StandbyTrimBlocked_NoCatchUpWhileAtSetpoint()
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
        Assert.Contains("zamkniętych zaworach", blocked.Message);
        // Kocioł dobija do nastawy - dodatku na dociąganie nie ma (ciepło nie ma którędy odejść).
        Assert.Equal(0, st.StorageBonusSec);
        Assert.Contains("więcej węgla nie pomoże", st.LastReason);
    }

    [Fact]
    public async Task RoomsBelowWithOpenValves_StandbyTrimStillWorks()
    {
        var h = Harness(new DoseState { LastKCorrectionDate = Now.Date });
        Samples(h, Sawtooth, Now.AddHours(-6), Now);
        Rooms(h, Now.AddHours(-3), Now, below: 0.8, open: true);
        PublishNow(h, 64);

        await h.StepAsync(Now, null);

        Assert.Equal(20, h.Repo.State!.TrimSec);
        Assert.Contains(h.Repo.Log, e => e.Parameter == DoseLog.Trim && e.ReasonCode == "TRYM_PODTRZYMANIA" && e.NewValue == 20);
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
}
