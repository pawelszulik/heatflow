using System.Globalization;
using HeatFlow.Core.Dose;
using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.EsterownikMapper;

namespace HeatFlow.DoseSim;

/// <summary>Jedna migawka HMS (czas UTC, kolumny z migawki.csv; brak = pusta).</summary>
public sealed record HmsRow(DateTime Utc, IReadOnlyDictionary<string, string> Values)
{
    public string? Get(string column) => Values.TryGetValue(column, out var v) && v.Length > 0 ? v : null;

    public double? Num(string column)
        => double.TryParse(Get(column), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Czytnik migawki.csv (t_utc,tz_ogrod,…; patrz tools/HeatFlow.DoseReplay/README.md).</summary>
    public static IEnumerable<HmsRow> ReadCsv(string path)
    {
        using var reader = new StreamReader(path);
        var header = reader.ReadLine()!.Split(',');
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var cells = SplitCsv(line);
            var dict = new Dictionary<string, string>();
            for (var i = 1; i < header.Length && i < cells.Count; i++) dict[header[i]] = cells[i];
            var t = DateTime.SpecifyKind(DateTime.ParseExact(cells[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), DateTimeKind.Utc);
            yield return new HmsRow(t, dict);
        }
    }

    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var cur = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"') quoted = !quoted;
            else if (ch == ',' && !quoted) { cells.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(ch);
        }
        cells.Add(cur.ToString());
        return cells;
    }
}

/// <summary>Krok replayu: co sterownik miał naprawdę (Paweł) i co by miał z automatem.</summary>
public record ReplayStep(DateTime Utc, int? ActualWork, int? ActualPause, int? ActualBlower,
    int SimWork, int SimPause, int SimBlower, bool AutomatActive, bool Ceiling, double K, double? Outdoor,
    double? HouseTarget = null, double? NeedSecPerDay = null);

public sealed class ReplayResult
{
    public required DoseHarness Harness { get; init; }
    public List<ReplayStep> Steps { get; } = new();
    public List<(DateTime Utc, string Reason)> ReEnables { get; } = new();
    public List<string> LimitViolations { get; } = new();
    public IReadOnlyList<FeedChangeLog> Log => Harness.Repo.Log;
}

/// <summary>
/// Replay historii na prawdziwym kodzie Fazy 5: migawki HMS co ~5 min zasilają atrapę HA
/// (z last_updated = czas migawki, więc dziury w danych wyglądają jak w HA), Faza 5 liczy i - przy
/// włączonym automacie - „zapisuje” do atrapy sterownika. Pętla otwarta: kocioł i dom w danych
/// reagowały na nastawy Pawła, nie automatu, więc trym i bezpieczniki widzą tamten kocioł.
/// Prognoza = faktyczna Tz z kolejnych godzin (idealna). Po samowyłączeniu „Paweł” włącza automat
/// ponownie po <paramref name="reEnableAfter"/>.
/// </summary>
public static class ReplayRunner
{
    /// <summary>Do 2026-09-10 Tz z sensor.t_h_sensor_2, potem z czujnika ogrodowego (jak w analizie).</summary>
    public static readonly DateTime GardenSensorFrom = new(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);

    /// <param name="neutralBoiler">
    /// Kocioł „neutralny”: temperatura kotła = nastawa − 1,5 K, spaliny 120 °C (dmuchawa z danych, bo z niej
    /// liczą się godziny grzania do korekty k) - trym
    /// i bezpieczniki od kotła milczą, zostaje sama pogoda + k. Tak liczy symulacja a7_sucho2.py, więc
    /// ten wariant służy do porównania z nią; licznik podajnika i reszta danych bez zmian.
    /// </param>
    public static async Task<ReplayResult> RunAsync(IReadOnlyList<HmsRow> rows, bool automatOn,
        TimeSpan? reEnableAfter = null, HeatingParameters? parameters = null, DateTime? fromUtc = null, DateTime? toUtc = null,
        bool neutralBoiler = false,
        IReadOnlyList<(DateTime FromUtc, Dictionary<string, double> Targets)>? roomTargets = null)
    {
        reEnableAfter ??= TimeSpan.FromHours(6);
        var hourlyTz = HourlyOutdoor(rows);
        double? TzAt(DateTime t) => hourlyTz.TryGetValue(Floor(t), out var v) ? v : null;

        var selected = rows.Where(r => (fromUtc == null || r.Utc >= fromUtc) && (toUtc == null || r.Utc < toUtc)).ToList();
        var first = selected.First(r => r.Num("praca") != null && r.Num("postoj") != null && r.Num("dmuchawa") != null);
        var initial = new ControllerFeedSettings((int)first.Num("praca")!, (int)first.Num("postoj")!, (int)first.Num("dmuchawa")!);
        var h = new DoseHarness(initial, parameters);
        // Cele pokoi: z historii (tygodniowe średnie z RoomState), wagi z inwentaryzacji. Bez historii -
        // pokoje bez wag, czyli cel stały IndoorTargetForFeed (jak przed decyzją Pawła).
        if (roomTargets is { Count: > 0 }) h.Config.Rooms = SimDefaults.AllRooms(roomTargets[0].Targets);
        h.Repo.Switch.Enabled = automatOn;
        var result = new ReplayResult { Harness = h };

        var latest = new Dictionary<string, (string Value, DateTime Utc)>();
        var lastStep = DateTime.MinValue;
        var lastHistory = 0;
        DateTime? disabledAt = null;

        foreach (var row in selected)
        {
            foreach (var (k, v) in row.Values)
                if (v.Length > 0) latest[k] = (v, row.Utc);

            if (row.Utc - lastStep < TimeSpan.FromMinutes(4.5)) continue;
            lastStep = row.Utc;
            var t = row.Utc;

            if (roomTargets is { Count: > 0 })
            {
                var week = roomTargets.LastOrDefault(x => x.FromUtc <= t);
                var targets = (week.Targets ?? roomTargets[0].Targets);
                foreach (var room in h.Config.Rooms) room.TempTarget = targets.GetValueOrDefault(room.Name, room.TempTarget);
            }

            PublishLatest(h, latest, t);
            if (neutralBoiler && latest.TryGetValue("nastawa", out var z)
                && double.TryParse(z.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var zv))
            {
                h.Ha.Set(h.Entities.BoilerTemp, zv - 1.5, t);
                h.Ha.Set(h.Entities.FlueTemp, 120.0, t);
                h.Ha.Set(h.Entities.ControllerClock, t.ToString("O"), t);
            }

            if (automatOn && !h.Repo.Switch.Enabled && !(h.Repo.State?.AutomatActive ?? false))
            {
                disabledAt ??= t;
                if (t - disabledAt.Value >= reEnableAfter.Value)
                {
                    h.Repo.Switch.Enabled = true;
                    result.ReEnables.Add((t, h.Repo.State?.LastSelfDisableReason ?? ""));
                    disabledAt = null;
                }
            }
            else
            {
                disabledAt = null;
            }

            await h.StepAsync(t, DoseHarness.IdealForecast(TzAt, t));

            foreach (var w in h.Controller.History.Skip(lastHistory))
            {
                var e = h.Entities;
                var ok = w.Entity == e.FeederWork ? w.Value is >= DoseLimits.WorkMin and <= DoseLimits.WorkMax
                    : w.Entity == e.FeederPause ? w.Value is >= DoseLimits.PauseMin and <= DoseLimits.PauseMax
                    : w.Entity == e.Blower && w.Value is >= DoseLimits.BlowerMin and <= DoseLimits.BlowerMax;
                if (!ok) result.LimitViolations.Add($"{w.Utc:u} {w.Entity} = {w.Value}");
            }
            lastHistory = h.Controller.History.Count;

            var st = h.Repo.State!;
            var c = h.Controller.Settings;
            result.Steps.Add(new ReplayStep(t,
                ToInt(latest, "praca"), ToInt(latest, "postoj"), ToInt(latest, "dmuchawa"),
                c.FeederWork, c.FeederPause, c.Blower, st.AutomatActive, st.Ceiling, h.Parameters.FeedCorrectionK,
                st.OutdoorSmoothed, st.HouseTargetUsed, st.NeedSecPerDay));
        }

        return result;
    }

    private static void PublishLatest(DoseHarness h, Dictionary<string, (string Value, DateTime Utc)> latest, DateTime t)
    {
        void Num(string entity, string column)
        {
            if (latest.TryGetValue(column, out var x)) h.Ha.Set(entity, x.Value, x.Utc);
            else h.Ha.Set(entity, (string?)null, t);
        }

        var e = h.Entities;
        var outdoorColumn = t >= GardenSensorFrom ? "tz_ogrod" : "tz_thsensor";
        Num(e.OutdoorTemp, outdoorColumn);
        // Czujniki pokojowe z flagą UseForDoseIndoorMean (kolumna in_<pokój>); stare odczyty (> 15 min) = brak.
        foreach (var room in h.Config.Rooms.Where(r => r.UseForDoseIndoorMean))
        {
            if (latest.TryGetValue($"in_{room.Name}", out var x) && t - x.Utc <= TimeSpan.FromMinutes(15))
                h.Ha.Set(room.SensorTemperatureEntityId, x.Value, x.Utc);
            else h.Ha.Set(room.SensorTemperatureEntityId, (string?)null, t);
        }

        Num(e.BoilerTemp, "kociol");
        Num(e.BoilerSetpoint, "nastawa");
        Num(e.FlueTemp, "spaliny");
        Num(e.BlowerActual, "dmuchawa_akt");
        Num(e.FeederCounter, "licznik");
        Num(e.SummerSwitch, "lato");
        Num(e.ControllerMode, "tryb_auto");

        // Zegara sterownika nie ma w eksporcie - świeżość kotła = świeżość temperatury kotła.
        if (latest.TryGetValue("kociol", out var k)) h.Ha.Set(e.ControllerClock, k.Utc.ToString("O"), k.Utc);
        else h.Ha.Set(e.ControllerClock, (string?)null, t);
    }

    /// <summary>Średnie godzinowe Tz (UTC) - do idealnej prognozy.</summary>
    public static Dictionary<DateTime, double> HourlyOutdoor(IEnumerable<HmsRow> rows)
        => rows.Select(r => (Hour: Floor(r.Utc), Tz: r.Num(r.Utc >= GardenSensorFrom ? "tz_ogrod" : "tz_thsensor")))
            .Where(x => x.Tz.HasValue)
            .GroupBy(x => x.Hour)
            .ToDictionary(g => g.Key, g => g.Average(x => x.Tz!.Value));

    /// <summary>Czytnik cele-pokoi.csv: „tydzien,cele” z celami „pokój=°C” rozdzielonymi spacją.</summary>
    public static List<(DateTime FromUtc, Dictionary<string, double> Targets)> ReadRoomTargets(string path)
        => File.ReadAllLines(path).Skip(1).Where(l => l.Contains(','))
            .Select(l => l.Split(',', 2))
            .Select(c => (DateTime.SpecifyKind(DateTime.ParseExact(c[0], "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc),
                c[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('='))
                    .ToDictionary(x => x[0], x => double.Parse(x[1], CultureInfo.InvariantCulture))))
            .OrderBy(x => x.Item1)
            .ToList();

    private static DateTime Floor(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc);

    private static int? ToInt(Dictionary<string, (string Value, DateTime Utc)> latest, string column)
        => latest.TryGetValue(column, out var x) && double.TryParse(x.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? (int)Math.Round(v) : null;
}
