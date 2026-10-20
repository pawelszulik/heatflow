using System.Globalization;
using System.Text.Json;
using HeatFlow.Core.Dose;
using HeatFlow.Domain;
using HeatFlow.DoseSim;
using HeatFlow.Infrastructure.EsterownikMapper;
using Xunit;

namespace HeatFlow.Core.Tests;

/// <summary>
/// Cel domu dla dawki z celów pokoi ważonych stratami (decyzja Pawła 2026-10-20): wagi HeatLossW,
/// cel pokoju jak w fazach 1-3, spadek od razu, wzrost ograniczony z dodatkiem na dogrzanie domu.
/// </summary>
public class DoseHouseTargetTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    [Fact]
    public void HouseTarget_TodayRooms_WeightedByHeatLoss()
    {
        // Dzisiejsze cele z bazy: Σ(waga·cel)/Σ wag = 179 597 / 8 183 = 21,95 °C (garaż 6 °C × 965 W ciągnie w dół).
        var (target, rooms) = DoseCalculator.HouseTarget(SimDefaults.AllRooms(), Now, false);
        Assert.Equal(21.95, target!.Value, 2);
        Assert.Equal(15, rooms.Count);
    }

    [Fact]
    public void HouseTarget_RoomsWithoutWeight_AreSkipped_DisabledRoomsCount()
    {
        var rooms = new List<RoomConfiguration>
        {
            new() { Name = "a", TempTarget = 20, HeatLossW = 100, AutomationDisabled = true },
            new() { Name = "b", TempTarget = 26, HeatLossW = 300 },
            new() { Name = "c", TempTarget = 10, HeatLossW = 0 }
        };
        Assert.Equal(24.5, DoseCalculator.HouseTarget(rooms, Now, false).Target!.Value, 3);
        Assert.Null(DoseCalculator.HouseTarget(new[] { rooms[2] }, Now, false).Target);
    }

    [Fact]
    public void HouseTarget_UsesScheduleTargetLikePhase1()
    {
        // Harmonogram grzania 10:00-14:00: w nim cel „aktywny”, poza nim „nieaktywny” - jak Room.GetTargetTemperature.
        var room = new RoomConfiguration
        {
            Name = "salon", TempTarget = 22, TempTargetActive = 25, TempTargetInactive = 19, HeatLossW = 626,
            HeatingSchedule = "10:00-14:00|10:00-14:00"
        };
        Assert.Equal(25, DoseCalculator.HouseTarget(new[] { room }, new DateTime(2026, 11, 20, 12, 0, 0), false).Target);
        Assert.Equal(19, DoseCalculator.HouseTarget(new[] { room }, new DateTime(2026, 11, 20, 20, 0, 0), false).Target);
    }

    [Fact]
    public void Need_UsesHouseTargetInsteadOfFixed24()
    {
        var p = new HeatingParameters();
        Assert.Equal(DoseCalculator.Need(p, 0), DoseCalculator.Need(p, 0, 24.0), 6);
        Assert.Equal(255 * 22 - 934, DoseCalculator.Need(p, 0, 22.0), 6);
    }

    [Fact]
    public void LimitTargetRise_DropIsImmediate_NoBonus()
    {
        var (used, bonus) = DoseCalculator.LimitTargetRise(21.0, 23.0, 1, new HeatingParameters());
        Assert.Equal((21.0, 0.0), (used, bonus));
    }

    [Fact]
    public void LimitTargetRise_RiseLimited_WithStorageBonusCapped()
    {
        var p = new HeatingParameters(); // 0,07 K/h, dodatek ≤ 2600 s/dobę
        var (used, bonus) = DoseCalculator.LimitTargetRise(23.0, 22.0, 1, p);
        Assert.Equal(22.07, used, 3);
        // 1549 s/K · 0,07 K/h · 24 h = 2602 → przycięte do 2600
        Assert.Equal(2600, bonus, 0);

        // Ostatni kawałek: cel dochodzi, dodatek proporcjonalny do faktycznego wzrostu.
        var (used2, bonus2) = DoseCalculator.LimitTargetRise(22.03, 22.0, 1, p);
        Assert.Equal(22.03, used2, 3);
        Assert.Equal(1549 * 0.03 * 24, bonus2, 0);

        // Po przerwie (np. 6 h bez przebiegu) cel nie skacze - najwyżej 2 h wzrostu.
        Assert.Equal(22.14, DoseCalculator.LimitTargetRise(25.0, 22.0, 6, p).Used, 3);
    }

    [Fact]
    public void LimitTargetRise_RoundingNoise_IsNotARise()
    {
        Assert.Equal(0, DoseCalculator.LimitTargetRise(21.9504, 21.950, 1, new HeatingParameters()).StorageBonusSec);
    }

    [Fact]
    public void LimitTargetRise_OneKelvin_TakesAbout14Hours()
    {
        var p = new HeatingParameters();
        double used = 22.0;
        var hours = 0;
        while (used < 23.0 - 1e-9 && hours < 100) { used = DoseCalculator.LimitTargetRise(23.0, used, 1, p).Used; hours++; }
        Assert.InRange(hours, 14, 15);
    }

    [Fact]
    public async Task Phase_FixedSourceOrNoWeights_FallsBackTo24_AndSaysSo()
    {
        var h = new DoseHarness(new ControllerFeedSettings(7, 160, 35));       // pokoje bez wag
        h.Publish(new DoseInputsSnapshot(0, 24, 66, 67, 140, 35, 1000, false, true), Now);
        await h.StepAsync(Now, null);
        Assert.Equal(24.0, h.Repo.State!.HouseTargetUsed);
        Assert.StartsWith("stała", h.Repo.State.HouseTargetSource);

        var h2 = new DoseHarness(new ControllerFeedSettings(7, 160, 35), new HeatingParameters { DoseTargetFromRooms = false });
        h2.Config.Rooms = SimDefaults.AllRooms();
        h2.Publish(new DoseInputsSnapshot(0, 24, 66, 67, 140, 35, 1000, false, true), Now);
        await h2.StepAsync(Now, null);
        Assert.Equal(("stała", 24.0), (h2.Repo.State!.HouseTargetSource, h2.Repo.State.HouseTargetUsed!.Value));
    }

    [Fact]
    public async Task Phase_RoomTargets_PogodaEntryNamesHouseTarget_AndTargetChangeGetsOwnEntry()
    {
        // Same cele (bez faktycznych) - mechanika wpisów CEL_DOMU; faktyczne: DoseActualTargetTests.
        var h = new DoseHarness(new ControllerFeedSettings(7, 160, 35), new HeatingParameters { DoseTargetUseActual = false });
        h.Config.Rooms = SimDefaults.AllRooms();
        h.Repo.Switch.Enabled = true;
        h.Publish(new DoseInputsSnapshot(-5, 24, 66, 67, 140, 35, 1000, false, true), Now);
        await h.StepAsync(Now, null);

        var first = Assert.Single(h.Repo.Log, e => e.ReasonCode == "CEL_DOMU");
        Assert.Contains("cel z pokoi ważony stratami (15 pokoi, Σ 8183 W)", first.Message);
        var pogoda = h.Repo.Log.First(e => e.ReasonCode == "POGODA" && e.Parameter == DoseLog.Pause);
        Assert.Contains("cel domu 21,9 °C (pokoje)", pogoda.Message);
        Assert.Equal(21.95, pogoda.HouseTarget!.Value, 2);

        // Mała zmiana (< 0,3 K) - bez nowego wpisu; salon +5 K (626 W) = +0,38 K - wpis z rozbiciem.
        h.Config.Rooms.First(r => r.Name == "toaleta").TempTarget += 1;
        await h.StepAsync(Now.AddHours(1), null);
        Assert.Single(h.Repo.Log, e => e.ReasonCode == "CEL_DOMU");

        h.Config.Rooms.First(r => r.Name == "salon").TempTarget += 5;
        await h.StepAsync(Now.AddHours(2), null);
        var change = h.Repo.Log.Last(e => e.ReasonCode == "CEL_DOMU");
        Assert.Equal("w górę", change.Direction);
        Assert.Contains("salon 26 → 31", change.Message);
        Assert.Contains("toaleta 25 → 26", change.Message);
        Assert.Contains("dawka rośnie stopniowo", change.Message);
        // Cel w modelu rośnie stopniowo, z dodatkiem na dogrzanie.
        Assert.True(h.Repo.State!.HouseTargetUsed < h.Repo.State.HouseTarget);
        Assert.True(h.Repo.State.StorageBonusSec > 0);
    }

    [Fact]
    public async Task Phase_WeightedMeasurement_UsesAllRoomsWeighted_IncludingTrvCurrentTemperature()
    {
        var p = new HeatingParameters { DoseIndoorMeasureWeighted = true };
        var h = new DoseHarness(new ControllerFeedSettings(7, 160, 35), p);
        h.Config.Rooms = new List<RoomConfiguration>
        {
            new() { Name = "salon", HeatLossW = 600, TempTarget = 24, SensorTemperatureEntityId = "sensor.salon" },
            new() { Name = "balkonowy", HeatLossW = 1200, TempTarget = 24, SensorTemperatureEntityId = "climate.trv_balkonowy" },
        };
        p.DoseIndoorMinSensors = 2;
        h.Publish(new DoseInputsSnapshot(0, null, 66, 67, 140, 35, 1000, false, true), Now);
        h.Ha.Set("sensor.salon", 24.0, Now);
        h.Ha.States["climate.trv_balkonowy"] = new Infrastructure.HomeAssistant.EntityState
        {
            State = "heat",
            Attributes = new Dictionary<string, object> { ["current_temperature"] = JsonDocument.Parse("21").RootElement }
        };

        await h.StepAsync(Now, null);

        Assert.Equal((24.0 * 600 + 21.0 * 1200) / 1800, h.Repo.Samples.Last().IndoorTemp!.Value, 2);
    }

    [Fact]
    public void Validation_TargetParameters()
    {
        Assert.Empty(DoseLimits.Validate(new HeatingParameters()));
        Assert.NotEmpty(DoseLimits.Validate(new HeatingParameters { DoseTargetRiseKPerHour = 0 }));
        Assert.NotEmpty(DoseLimits.Validate(new HeatingParameters { DoseStorageBonusMaxSec = 10000 }));
    }

    // --- Pętla zamknięta: cele pokoi + przesunięcia pomiaru jak dziś w domu ---

    private static List<(DateTime Utc, double Tz)> Tz(string file)
        => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Data", file)).Skip(1)
            .Select(l => l.Split(','))
            .Select(c => (DateTime.SpecifyKind(DateTime.ParseExact(c[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), DateTimeKind.Utc),
                double.Parse(c[1], CultureInfo.InvariantCulture)))
            .ToList();

    private static DateTime Start(int month, int day) => TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, month, day), Warsaw);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedLoop_RoomTargets_IndoorReachesInstallationGoal_KStable(bool weighted)
    {
        var from = Start(3, 1);
        var r = await ClosedLoopRunner.RunAsync(Tz("tz-marzec.csv"), from, 14, 1.0,
            new HeatingParameters { DoseIndoorMeasureWeighted = weighted }, startAtModel: true,
            rooms: SimDefaults.AllRooms(), roomOffsets: SimDefaults.RoomOffsetsToday);

        Assert.Empty(r.LimitViolations);
        Assert.Empty(r.Harness.Errors.Errors);
        // Goal = cel instalacji: temperatura domu, przy której każdy pokój ma swój cel (24,59 °C dziś).
        var last = r.Hours.Where(h => h.Utc >= from.AddDays(11)).ToList();
        Assert.InRange(last.Average(h => h.Indoor - h.Goal), -0.3, 0.3);
        // k stabilne w ostatnich 4 dobach (zmiana ≤ 0,1)
        var kLast = r.Hours.Where(h => h.Utc >= from.AddDays(10)).Select(h => h.K).ToList();
        Assert.True(kLast.Max() - kLast.Min() <= 0.1, $"k {kLast.Min():F2}-{kLast.Max():F2}");
        // Pomiar 7 czujników (+1,88 K wobec domu) daje k niższe niż pomiar ważony - patrz raport.
        if (weighted) Assert.InRange(r.Hours.Last().K, 0.95, 1.15);
        else Assert.InRange(r.Hours.Last().K, 0.8, 0.98);
    }

    [Fact]
    public async Task ClosedLoop_TargetUp1K_GradualWithBonus_NoCeilingJump()
    {
        var from = Start(3, 1);
        var at = from.AddDays(3).AddHours(22);
        // Same cele i dom bez przesunięć pokoi (zawory ciągną do celu domu): test ograniczenia wzrostu celu
        // i dodatku na dogrzanie - wzrost od faktycznych idzie bez nich (DoseActualTargetTests).
        var r = await ClosedLoopRunner.RunAsync(Tz("tz-marzec.csv"), from, 6, 1.0, new HeatingParameters { DoseTargetUseActual = false },
            startAtModel: true, rooms: SimDefaults.AllRooms(),
            faults: new[] { new Fault(FaultKind.TargetStep, at, from.AddDays(6), 1.0) });

        Assert.Empty(r.LimitViolations);
        Assert.True(r.MinHoursBetweenChanges() >= 2.0 - 1e-9);
        var after = r.Hours.Where(h => h.Utc >= at).ToList();
        Assert.Contains(after, h => h.StorageBonusSec > 0);
        var reached = after.First(h => h.HouseTargetUsed >= after.Last().HouseTargetBase - 0.01);
        Assert.InRange((reached.Utc - at).TotalHours, 12, 17);
        Assert.Contains(r.Log, e => e.ReasonCode == "CEL_DOMU" && e.TimestampUtc >= at && e.Direction == "w górę");
        Assert.True(after.Last().Indoor > after.First().Indoor + 0.5, "dom się dogrzewa");
    }
}
