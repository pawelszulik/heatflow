using System.Globalization;
using HeatFlow.Core.Dose;
using HeatFlow.Domain;
using HeatFlow.DoseSim;
using HeatFlow.Infrastructure.EsterownikMapper;
using Xunit;

namespace HeatFlow.Core.Tests;

/// <summary>
/// Pokój liczy się do celu domu jako wyższa z dwóch: cel albo faktyczna wygładzona (decyzja Pawła
/// 2026-10-20), z przycięciem od góry: najwyższy cel wśród pokoi z wagą i cel z samych celów + 2 K.
/// </summary>
public class DoseActualTargetTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    private static List<RoomTargetInput> Inputs(params (string Name, double Target, double Weight)[] rooms)
        => rooms.Select(r => new RoomTargetInput(r.Name, r.Target, r.Weight)).ToList();

    [Fact]
    public void Combine_RoomAboveTarget_CountsActual_BelowTarget_CountsTarget()
    {
        var rooms = Inputs(("salon", 26, 600), ("sien", 22, 300), ("kuchnia", 22, 300));
        var actuals = new Dictionary<string, double> { ["salon"] = 24.0, ["sien"] = 25.0 }; // kuchnia bez odczytu

        var r = DoseCalculator.CombineHouseTarget(rooms, actuals, new HeatingParameters());

        Assert.Equal((26 * 600 + 22 * 300 + 22 * 300) / 1200.0, r.BaseTarget!.Value, 6);
        Assert.Equal((26 * 600 + 25 * 300 + 22 * 300) / 1200.0, r.Target!.Value, 6);
        Assert.Equal(new[] { "sien" }, r.Rooms.Where(x => x.ByActual).Select(x => x.Name));
        Assert.Null(r.Rooms.Single(x => x.Name == "kuchnia").Actual);
        Assert.False(r.HouseCapped);
    }

    [Fact]
    public void Combine_ActualCappedAtHighestTarget_AndHouseAtBasePlusX()
    {
        var rooms = Inputs(("salon", 26, 100), ("spizarnia", 19, 100));

        // Spiżarnia 28 °C liczy się najwyżej jak najwyższy cel (26), nie 28.
        var r = DoseCalculator.CombineHouseTarget(rooms, new Dictionary<string, double> { ["spizarnia"] = 28 },
            new HeatingParameters { DoseActualMaxAboveTargetsK = 6 });
        Assert.Equal(26.0, r.Rooms.Single(x => x.Name == "spizarnia").Used);
        Assert.Equal(26.0, r.Target!.Value, 6);

        // Cały cel domu ≤ cel z samych celów (22,5) + 2 K.
        var capped = DoseCalculator.CombineHouseTarget(rooms, new Dictionary<string, double> { ["spizarnia"] = 28 }, new HeatingParameters());
        Assert.True(capped.HouseCapped);
        Assert.Equal(24.5, capped.Target!.Value, 6);
    }

    [Fact]
    public void Combine_ParameterOffOrRoomFlagOff_UsesTargetsOnly()
    {
        var rooms = new List<RoomTargetInput> { new("garaz", 6, 965, UseActual: false), new("salon", 26, 626) };
        var actuals = new Dictionary<string, double> { ["garaz"] = 15.8, ["salon"] = 26.5 };

        var flagOff = DoseCalculator.CombineHouseTarget(rooms, actuals, new HeatingParameters());
        Assert.Equal(flagOff.BaseTarget, flagOff.Target);
        Assert.DoesNotContain(flagOff.Rooms, x => x.ByActual);   // salon 26,5 > 26 przycięty do 26 = cel

        var off = DoseCalculator.CombineHouseTarget(Inputs(("garaz", 6, 965)), actuals,
            new HeatingParameters { DoseTargetUseActual = false });
        Assert.Equal(6.0, off.Target!.Value);
    }

    [Fact]
    public void Combine_TodayRooms_23_94_NotCapped()
    {
        // 20.10 15:34: faktyczne = 23,79 + przesunięcie pokoju. Rachunek kontrolny: 23,94 °C, z samych celów 21,95.
        var inputs = DoseCalculator.RoomTargets(SimDefaults.AllRooms(), Now, false);
        var actuals = SimDefaults.RoomOffsetsToday.ToDictionary(x => x.Key, x => 23.79 + x.Value);

        var r = DoseCalculator.CombineHouseTarget(inputs, actuals, new HeatingParameters());

        Assert.Equal(21.95, r.BaseTarget!.Value, 2);
        Assert.Equal(23.94, r.Target!.Value, 2);
        Assert.False(r.HouseCapped);
        Assert.Contains("garaz", r.Rooms.Where(x => x.ByActual).Select(x => x.Name));
        Assert.DoesNotContain("grzes", r.Rooms.Where(x => x.ByActual).Select(x => x.Name)); // 25,2 < 26
    }

    [Fact]
    public void Smooth_ExponentialWithTau_StaleRoomDropped_LongGapRestarts()
    {
        var state = new Dictionary<string, (double Value, DateTime Utc)>();
        state = DoseCalculator.SmoothRoomActuals(state, new Dictionary<string, double> { ["salon"] = 20, ["sien"] = 22 }, Now, 24);
        Assert.Equal(20.0, state["salon"].Value);

        // Skok 20 → 30 i 24 h odczytów co 5 min: po τ średnia przechodzi 1 − 1/e drogi (≈ 26,3).
        for (var t = Now.AddMinutes(5); t <= Now.AddHours(24); t = t.AddMinutes(5))
            state = DoseCalculator.SmoothRoomActuals(state, new Dictionary<string, double> { ["salon"] = 30 }, t, 24);
        Assert.Equal(20 + 10 * (1 - Math.Exp(-1)), state["salon"].Value, 2);
        // Sień bez odczytu przez 24 h - jeszcze jest (≤ τ); po kolejnej godzinie wypada.
        Assert.True(state.ContainsKey("sien"));
        state = DoseCalculator.SmoothRoomActuals(state, new Dictionary<string, double> { ["salon"] = 30 }, Now.AddHours(25), 24);
        Assert.False(state.ContainsKey("sien"));

        // Przerwa > 3 τ - start od odczytu.
        state = DoseCalculator.SmoothRoomActuals(state, new Dictionary<string, double> { ["salon"] = 18 }, Now.AddDays(5), 24);
        Assert.Equal(18.0, state["salon"].Value);
    }

    [Fact]
    public void Smooth_FormatParse_RoundTrip()
    {
        var state = new Dictionary<string, (double Value, DateTime Utc)>
        {
            ["salon"] = (24.3125, new DateTime(2026, 10, 20, 15, 30, 0, DateTimeKind.Utc)),
            ["garaz"] = (-2.5, new DateTime(2026, 10, 20, 15, 35, 0, DateTimeKind.Utc))
        };
        var text = DoseCalculator.FormatRoomActuals(state);
        Assert.Equal("garaz=-2.5@202610201535;salon=24.313@202610201530", text);
        var back = DoseCalculator.ParseRoomActuals(text + ";zepsute");
        Assert.Equal(2, back.Count);
        Assert.Equal(24.313, back["salon"].Value);
        Assert.Equal(state["garaz"].Utc, back["garaz"].Utc);
    }

    [Fact]
    public void Validation_ActualParameters()
    {
        Assert.Empty(DoseLimits.Validate(new HeatingParameters()));
        Assert.NotEmpty(DoseLimits.Validate(new HeatingParameters { DoseActualSmoothingHours = 1 }));
        Assert.NotEmpty(DoseLimits.Validate(new HeatingParameters { DoseActualMaxAboveTargetsK = -1 }));
    }

    // --- Faza 5 ---

    private static DoseHarness Harness(HeatingParameters? p = null)
    {
        var h = new DoseHarness(new ControllerFeedSettings(7, 160, 35), p);
        h.Config.Rooms = SimDefaults.AllRooms();
        return h;
    }

    private static void Publish(DoseHarness h, DateTime t, double house = 23.79)
    {
        h.Publish(new DoseInputsSnapshot(-5, null, 66, 67, 140, 35, 1000, false, true), t);
        h.PublishRoomTemperatures(house, SimDefaults.RoomOffsetsToday, t);
    }

    [Fact]
    public async Task Phase_CelDomuEntry_ListsRoomsByActualAndByTarget()
    {
        var h = Harness();
        Publish(h, Now);
        await h.StepAsync(Now, null);

        var st = h.Repo.State!;
        Assert.Equal(23.94, st.HouseTarget!.Value, 2);
        Assert.Equal(21.95, st.HouseTargetBase!.Value, 2);
        Assert.Contains("garaz", st.HouseTargetActualRooms!.Split(';'));
        var entry = Assert.Single(h.Repo.Log, e => e.ReasonCode == "CEL_DOMU");
        Assert.Contains("po faktycznej: garaz 14 (cel 6)", entry.Message);
        Assert.Contains("po celu: balkonowy 25", entry.Message);
        Assert.Contains("grzes 26", entry.Message);
        Assert.Contains("z samych celów 21,95 °C", entry.Message);
        Assert.Contains("garaz=14@", st.RoomActualSmoothed);
    }

    [Fact]
    public async Task Phase_RoomFlagOff_GarageByTarget()
    {
        var h = Harness();
        h.Config.Rooms.First(r => r.Name == "garaz").DoseUseActual = false;
        Publish(h, Now);
        await h.StepAsync(Now, null);

        Assert.DoesNotContain("garaz", h.Repo.State!.HouseTargetActualRooms!.Split(';'));
        Assert.Equal(23.94 - 965 * (14.0 - 6.0) / 8183, h.Repo.State.HouseTarget!.Value, 2);
    }

    [Fact]
    public async Task Phase_RiseFromActuals_NoStorageBonus_RiseFromTargets_WithBonus()
    {
        var h = Harness(new HeatingParameters { DoseActualMaxAboveTargetsK = 6 }); // bez granicy +2 K - tu chodzi o wzrost
        var t = Now;
        for (; t <= Now.AddHours(1); t = t.AddMinutes(5)) { Publish(h, t); await h.StepAsync(t, null); }
        var before = h.Repo.State!.HouseTargetUsed!.Value;

        // Dom cieplejszy o 2 K przez dobę: faktyczne rosną (wygładzone), cel idzie za nimi od razu i bez dodatku.
        for (; t <= Now.AddHours(25); t = t.AddMinutes(5)) { Publish(h, t, 25.79); await h.StepAsync(t, null); }
        var st = h.Repo.State!;
        Assert.True(st.HouseTargetUsed > before + 0.2, $"{before} → {st.HouseTargetUsed}");
        Assert.Equal(st.HouseTarget!.Value, st.HouseTargetUsed!.Value, 2);
        Assert.Equal(0, st.StorageBonusSec);

        // Paweł podnosi cel salonu i grzesia ponad ich faktyczne: wzrost ograniczony, z dodatkiem na dogrzanie.
        h.Config.Rooms.First(r => r.Name == "salon").TempTarget = 30;
        h.Config.Rooms.First(r => r.Name == "grzes").TempTarget = 30;
        t = t.AddHours(1);
        Publish(h, t, 25.79);
        await h.StepAsync(t, null);
        st = h.Repo.State!;
        Assert.True(st.HouseTargetUsed < st.HouseTarget - 0.05, $"{st.HouseTargetUsed} vs {st.HouseTarget}");
        Assert.True(st.StorageBonusSec > 0);
    }

    // --- Pętla zamknięta ---

    private static List<(DateTime Utc, double Tz)> Tz(string file)
        => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Data", file)).Skip(1)
            .Select(l => l.Split(','))
            .Select(c => (DateTime.SpecifyKind(DateTime.ParseExact(c[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), DateTimeKind.Utc),
                double.Parse(c[1], CultureInfo.InvariantCulture)))
            .ToList();

    /// <summary>
    /// Nadmiar paliwa: k startowe 1,3 przy domu f = 0,8, połowa nadwyżki ognia omija zawory. Temperatura
    /// domu nie rośnie dzień po dniu - k schodzi, cel z faktycznymi zostaje przy celu instalacji.
    /// </summary>
    [Fact]
    public async Task ClosedLoop_ExcessFuel_NoDailyDriftUp()
    {
        var from = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 3, 1), Warsaw);
        var r = await ClosedLoopRunner.RunAsync(Tz("tz-marzec.csv"), from, 14, 0.8, new HeatingParameters { FeedCorrectionK = 1.3 },
            startAtModel: true, rooms: SimDefaults.AllRooms(), roomOffsets: SimDefaults.RoomOffsetsToday, uncontrolledShare: 0.5);

        Assert.Empty(r.LimitViolations);
        var daily = r.Hours.GroupBy(x => (x.Utc - from).Days).Select(g => g.Average(x => x.Indoor)).ToList();
        Assert.True(daily.Max() <= r.Hours[0].Goal + 0.3, $"Tin dobowa maks. {daily.Max():F2} przy celu instalacji {r.Hours[0].Goal:F2}");
        Assert.True(daily[^1] <= daily[1] + 0.1, $"Tin doba 1 {daily[1]:F2} → ostatnia {daily[^1]:F2}");
        Assert.InRange(r.Hours.Last().K, 0.6, 0.8);
        Assert.True(r.Hours.Last().HouseTargetUsed <= r.Hours.Last().HouseTargetBase + 2.0 + 1e-6);
    }

    /// <summary>
    /// Październik, cele dzisiejsze, przesunięcia pokoi jak 20.10: same cele zostawiają kilka pokoi pod
    /// celem (dawka liczona dla 21,95 °C), z faktycznymi pokoje z celem 25-26 °C trzymają cel.
    /// </summary>
    [Fact]
    public async Task ClosedLoop_October_ActualKeepsRoomsAtTarget_TargetsOnlyDoesNot()
    {
        var from = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 10, 5), Warsaw);
        async Task<double> RoomsBelow(HeatingParameters p)
        {
            var r = await ClosedLoopRunner.RunAsync(Tz("tz-pazdziernik.csv"), from, 14, 1.0, p, startAtModel: true,
                rooms: SimDefaults.AllRooms(), roomOffsets: SimDefaults.RoomOffsetsToday);
            Assert.Empty(r.LimitViolations);
            return r.Hours.Where(x => x.Utc >= from.AddDays(10)).Average(x => x.RoomsBelowTarget);
        }

        Assert.True(await RoomsBelow(new HeatingParameters()) <= 0.5);
        Assert.True(await RoomsBelow(new HeatingParameters { DoseTargetUseActual = false }) >= 2.0);
    }
}
