using System.Globalization;
using HeatFlow.Core.Dose;
using HeatFlow.Domain;
using HeatFlow.DoseSim;
using Xunit;

namespace HeatFlow.Core.Tests;

/// <summary>
/// Faza 5 w pętli zamkniętej z wirtualnym kotłem i domem (<see cref="VirtualPlant"/>), na prawdziwej
/// godzinowej Tz z HMS (Data/tz-*.csv), prognoza idealna. Kryteria z prośby Pawła (2026-10-20):
/// granice nigdy nieprzekroczone, bez piłowania, Tin przy celu, k zbiega, każda usterka kończy się
/// właściwym samowyłączeniem + wpisem w dzienniku + przywróceniem nastaw.
///
/// Bezpiecznik „kocioł zimny” domyślnie = 0 (tylko ostrzeżenie, decyzja Pawła 2026-10-20).
/// <see cref="ColdRule60Min_SelfDisablesWithinDays_AsSeenInRealData"/> pokazuje, dlaczego: z regułą
/// 60 min z pierwotnej spec automat wyłącza się w 1-2 doby - to samo widać w danych z HMS (76 takich
/// epizodów I-X przy nastawach Pawła, zawsze z palącym się ogniem).
/// </summary>
public class DoseClosedLoopTests
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private static DateTime Start(int month, int day) => TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, month, day), Warsaw);
    private static HeatingParameters WarnOnlyCold() => new(); // domyślnie DoseBoilerColdMinutes = 0

    private static List<(DateTime Utc, double Tz)> Tz(string file)
        => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Data", file)).Skip(1)
            .Select(l => l.Split(','))
            .Select(c => (DateTime.SpecifyKind(DateTime.ParseExact(c[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), DateTimeKind.Utc),
                double.Parse(c[1], CultureInfo.InvariantCulture)))
            .ToList();

    private static void AssertHealthy(ClosedLoopResult r)
    {
        Assert.Empty(r.LimitViolations);
        Assert.Empty(r.Harness.Errors.Errors);
        // Odstęp ≥ 2 h między zmianami - nigdy częściej.
        Assert.True(r.MinHoursBetweenChanges() >= 2.0 - 1e-9, $"min. odstęp {r.MinHoursBetweenChanges():F2} h");
        Assert.True(r.ChangesPerDay <= 12, $"{r.ChangesPerDay:F1} zmian/dobę");
    }

    [Theory]
    [InlineData("tz-marzec.csv", 3, 1, 1.0)]
    [InlineData("tz-pazdziernik.csv", 10, 6, 1.0)]
    [InlineData("tz-marzec.csv", 3, 1, 0.7)]
    [InlineData("tz-pazdziernik.csv", 10, 6, 0.7)]
    [InlineData("tz-luty.csv", 2, 1, 1.0)]
    public async Task FourteenDays_StaysWithinLimits_NoSawtooth_IndoorAtTarget(string file, int month, int day, double houseFactor)
    {
        var r = await ClosedLoopRunner.RunAsync(Tz(file), Start(month, day), 14, houseFactor, WarnOnlyCold(), startAtModel: true);

        AssertHealthy(r);
        Assert.Empty(r.SelfDisables);
        Assert.True(r.Harness.Repo.Switch.Enabled);
        // Piłowanie: zmiana w przeciwną stronę w ciągu 4 h - pojedyncze przypadki, nie reguła.
        Assert.True(r.Reversals() <= 3, $"piłowanie: {r.Reversals()}");
        var (maxAbs, within) = r.IndoorDeviation();
        Assert.True(within >= 0.95, $"Tin w ±0,5 K tylko {within:P0} godzin (max odchyłka {maxAbs:F2} K)");
    }

    [Theory]
    [InlineData(0.7, 0.68, 0.80)]
    [InlineData(1.2, 1.12, 1.26)]
    public async Task K_ConvergesToTrueHouse_WithinTwoWeeks(double houseFactor, double kMin, double kMax)
    {
        var r = await ClosedLoopRunner.RunAsync(Tz("tz-marzec.csv"), Start(3, 1), 14, houseFactor, WarnOnlyCold(), startAtModel: true);

        AssertHealthy(r);
        var k = r.Hours.Last().K;
        Assert.InRange(k, kMin, kMax);
        // Krok korekty ±0,05/dobę - k idzie w dobrą stronę od pierwszych dni.
        var kDay3 = r.Hours.Where(h => h.Utc < Start(3, 4)).Last().K;
        Assert.True(houseFactor < 1 ? kDay3 < 1.0 : kDay3 > 1.0, $"k po 3 dobach {kDay3:F3}");
    }

    [Fact]
    public async Task Ceiling_FebruaryFrostAndBiggerHouse_DoseStaysAtMaxWithinLimits()
    {
        // Luty, dom „bierze” 20 % więcej niż model: potrzeba ponad 10/100 - dawka stoi na suficie,
        // Tin spada (fizycznie nie do uniknięcia), granice nie mogą pęknąć.
        var r = await ClosedLoopRunner.RunAsync(Tz("tz-luty.csv"), Start(2, 1), 14, 1.2, WarnOnlyCold(), startAtModel: true);

        AssertHealthy(r);
        Assert.Contains(r.Hours, h => h.Work == DoseLimits.WorkMax && h.Pause == DoseLimits.PauseMin);
    }

    public static IEnumerable<object[]> FaultCases => new[]
    {
        new object[] { FaultKind.Flue210, "SAMOWYLACZENIE_SPALINY_200", 0 },
        new object[] { FaultKind.MapperNoConfirm, "SAMOWYLACZENIE_ZAPIS_NIEPOTWIERDZONY", 1000 },
        new object[] { FaultKind.DataGap, "SAMOWYLACZENIE_BRAK_DANYCH_KOTLA", 40 },
        new object[] { FaultKind.ManualMode, "SAMOWYLACZENIE_TRYB_RECZNY", 0 },
        new object[] { FaultKind.SwitchOffByUser, "WYLACZENIE_RECZNE", 0 }
    };

    [Theory]
    [MemberData(nameof(FaultCases))]
    public async Task Fault_EndsWithSelfDisable_LogEntry_RestoreAndDiscord(FaultKind kind, string expectedCode, int value)
    {
        var at = Start(3, 4).AddHours(10);
        var fault = kind switch
        {
            FaultKind.DataGap => new Fault(kind, at, at.AddMinutes(value)),
            FaultKind.MapperNoConfirm => new Fault(kind, at, at.AddDays(2), value),
            FaultKind.Flue210 => new Fault(kind, at, at.AddMinutes(30)),
            _ => new Fault(kind, at, at.AddHours(12))
        };

        var r = await ClosedLoopRunner.RunAsync(Tz("tz-marzec.csv"), Start(3, 1), 5, 1.0, WarnOnlyCold(),
            faults: new[] { fault }, startAtModel: true);

        AssertHealthy(r);
        var on = r.Log.First(e => e.ReasonCode == "WLACZENIE_RECZNE");
        var saved = (r.Log.First().ControllerWork, r.Log.First().ControllerPause, r.Log.First().ControllerBlower);
        var off = Assert.Single(r.Log, e => e.Parameter == DoseLog.Automat && e.NewValue == 0);
        Assert.Equal(expectedCode, off.ReasonCode);
        // Niepotwierdzony zapis: 3 próby po 2 h (dawka liczona od nowa), dopiero potem wyłączenie; pierwszy zapis może wypaść do 2 h po usterce.
        var latest = kind == FaultKind.MapperNoConfirm ? at.AddHours(9) : at.AddHours(2);
        Assert.True(off.TimestampUtc >= at && off.TimestampUtc <= latest, $"wyłączenie o {off.TimestampUtc:u}");
        Assert.False(r.Harness.Repo.Switch.Enabled);

        if (kind == FaultKind.MapperNoConfirm)
        {
            // Zapisy ginęły, a stare zapamiętane nastawy nie mają wracać - wyłączenie zostawia to, co jest w sterowniku.
            Assert.DoesNotContain(r.Log, e => e.ReasonCode == "PRZYWROCENIE_NASTAW");
            Assert.Contains(r.Harness.Notifier.Messages, m => m.Utc >= at && m.Message.Contains("NIE przywracam"));
            return;
        }

        // Nastawy wróciły do zastanych przy włączeniu (dmuchawa przy spalinach - nie wyżej niż po zbiciu).
        var c = r.Harness.Controller.Settings;
        Assert.Equal((saved.ControllerWork, saved.ControllerPause), (c.FeederWork, c.FeederPause));
        if (kind == FaultKind.Flue210) Assert.True(c.Blower < saved.ControllerBlower);
        else Assert.Equal(saved.ControllerBlower, c.Blower);
        Assert.All(r.Log.Where(e => e.ReasonCode == "PRZYWROCENIE_NASTAW"), e => Assert.Equal("OK", e.MapperConfirmation));

        Assert.Contains(r.Harness.Notifier.Messages, m => m.Utc >= at && m.Title.Contains("automat dawki")
            && m.Message.Contains("Przywrócone"));
        Assert.NotNull(on);
    }

    [Fact]
    public async Task MapperDropsOneWrite_AutomatHolds_ThenRetriesRecalculated_AndStaysOn()
    {
        var at = Start(3, 4).AddHours(10);
        var r = await ClosedLoopRunner.RunAsync(Tz("tz-marzec.csv"), Start(3, 1), 5, 1.0, WarnOnlyCold(),
            faults: new[] { new Fault(FaultKind.MapperNoConfirm, at, at.AddDays(2), 2) }, startAtModel: true);

        AssertHealthy(r);
        Assert.Empty(r.SelfDisables);
        Assert.True(r.Harness.Repo.Switch.Enabled);
        var hold = Assert.Single(r.Log, e => e.Parameter == "wstrzymanie_zapisu" && e.ReasonCode == "ZAPIS_NIEPOTWIERDZONY");
        Assert.Equal("WSTRZYMANIE_PRZEKROCZONE", hold.Status);
        var retry = Assert.Single(r.Log, e => e.ReasonCode == "ZAPIS_PONOWNIE");
        Assert.InRange((retry.TimestampUtc - hold.TimestampUtc).TotalMinutes, 120, 130);
        // Ponowna próba trafia w sterownik i dawka rusza dalej.
        Assert.Contains(r.Log, e => e.Status == DoseLog.StatusWritten && e.TimestampUtc >= retry.TimestampUtc);
    }

    [Fact]
    public async Task SummerMode_PausesAutomat_ThenResumesWithoutSelfDisable()
    {
        var at = Start(3, 4).AddHours(10);
        var r = await ClosedLoopRunner.RunAsync(Tz("tz-marzec.csv"), Start(3, 1), 5, 1.0, WarnOnlyCold(),
            faults: new[] { new Fault(FaultKind.Summer, at, at.AddHours(12)) }, startAtModel: true);

        AssertHealthy(r);
        Assert.Empty(r.SelfDisables);
        Assert.True(r.Harness.Repo.Switch.Enabled);
        Assert.Contains(r.Log, e => e.ReasonCode == "PAUZA_TRYB_LATO" && e.TimestampUtc >= at && e.TimestampUtc <= at.AddHours(1));
        var resumed = Assert.Single(r.Log, e => e.ReasonCode == "PAUZA_KONIEC");
        Assert.True(resumed.TimestampUtc >= at.AddHours(12) && resumed.TimestampUtc <= at.AddHours(13), $"wznowienie o {resumed.TimestampUtc:u}");
        // W pauzie żadnych zapisów do sterownika.
        Assert.DoesNotContain(r.Log, e => e.Status == DoseLog.StatusWritten && e.TimestampUtc > at.AddHours(1) && e.TimestampUtc < at.AddHours(12));
    }

    [Fact]
    public async Task TzDrop15KIn6h_AutomatStays_DoseGoesUp()
    {
        var at = Start(3, 4).AddHours(10);
        var r = await ClosedLoopRunner.RunAsync(Tz("tz-marzec.csv"), Start(3, 1), 6, 1.0, WarnOnlyCold(),
            faults: new[] { new Fault(FaultKind.TzDrop, at, at.AddDays(2), -15) }, startAtModel: true);

        AssertHealthy(r);
        Assert.Empty(r.SelfDisables);
        var before = r.Hours.Last(h => h.Utc <= at);
        var after = r.Hours.Last(h => h.Utc <= at.AddHours(18));
        double Duty(HourRecord h) => (double)h.Work / (h.Work + h.Pause);
        Assert.True(Duty(after) > Duty(before) * 1.3, $"dawka {before.Work}/{before.Pause} → {after.Work}/{after.Pause}");
    }

    [Fact]
    public async Task ColdRule60Min_SelfDisablesWithinDays_AsSeenInRealData()
    {
        // Charakterystyka, nie wymaganie: przy regule z pierwotnej spec (60 min) wirtualny kocioł po
        // podtrzymaniu albo przy szybkim spadku Tz schodzi pod nastawę − 10 K na ponad godzinę
        // i automat sam się wyłącza (z przywróceniem nastaw). Patrz komentarz klasy.
        var r = await ClosedLoopRunner.RunAsync(Tz("tz-marzec.csv"), Start(3, 1), 7, 1.0,
            new HeatingParameters { DoseBoilerColdMinutes = 60 }, startAtModel: true);

        AssertHealthy(r);
        var off = Assert.Single(r.SelfDisables);
        Assert.Equal("SAMOWYLACZENIE_KOCIOL_ZIMNY", off.ReasonCode);
        Assert.Contains(r.Log, e => e.ReasonCode == "PRZYWROCENIE_NASTAW" || e.Parameter == DoseLog.Automat);
    }
}
