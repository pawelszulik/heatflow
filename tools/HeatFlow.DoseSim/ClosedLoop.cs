using HeatFlow.Core.Dose;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.EsterownikMapper;

namespace HeatFlow.DoseSim;

public enum FaultKind
{
    /// <summary>Spaliny 210 °C w oknie.</summary>
    Flue210,
    /// <summary>Od From sterownik gubi Value kolejnych zapisów (HA mówi ok) - 2 = próba i ponowienie.</summary>
    MapperNoConfirm,
    /// <summary>Wszystkie encje niedostępne w oknie.</summary>
    DataGap,
    /// <summary>Przełącznik kotła na lato w oknie.</summary>
    Summer,
    /// <summary>Sterownik w trybie ręcznym w oknie.</summary>
    ManualMode,
    /// <summary>Paweł wyłącza automat w chwili From.</summary>
    SwitchOffByUser,
    /// <summary>Tz spada liniowo o Value K w 6 h od From i tak zostaje do To.</summary>
    TzDrop
}

public record Fault(FaultKind Kind, DateTime FromUtc, DateTime ToUtc, double Value = 0);

public record HourRecord(DateTime Utc, double Outdoor, double Indoor, double Boiler, bool Heating, int Work, int Pause,
    int Blower, double K, bool Ceiling, bool AutomatActive, int TrimSec);

/// <summary>Wynik przebiegu w pętli zamkniętej.</summary>
public sealed class ClosedLoopResult
{
    public required DoseHarness Harness { get; init; }
    public required VirtualPlant Plant { get; init; }
    public List<HourRecord> Hours { get; } = new();
    public List<string> LimitViolations { get; } = new();
    public DateTime Start { get; init; }
    public int Days { get; init; }

    public IReadOnlyList<FeedChangeLog> Log => Harness.Repo.Log;

    /// <summary>Zmiany dawki zrobione przez automat (bez przywracania nastaw) - po jednej na chwilę zapisu.</summary>
    public List<(DateTime Utc, double PauseFrom, double PauseTo, double WorkFrom, double WorkTo)> DoseChanges()
        => Log.Where(e => e.Status == DoseLog.StatusWritten && e.ReasonCode != "PRZYWROCENIE_NASTAW"
                          && e.Parameter is DoseLog.Work or DoseLog.Pause)
            .GroupBy(e => e.TimestampUtc)
            .Select(g =>
            {
                var p = g.FirstOrDefault(e => e.Parameter == DoseLog.Pause);
                var w = g.FirstOrDefault(e => e.Parameter == DoseLog.Work);
                return (g.Key, p?.OldValue ?? 0, p?.NewValue ?? 0, w?.OldValue ?? 0, w?.NewValue ?? 0);
            })
            .OrderBy(x => x.Key)
            .ToList();

    /// <summary>„Piłowanie”: dwie kolejne zmiany dawki w przeciwne strony w ciągu 4 h.</summary>
    public int Reversals(double withinHours = 4)
    {
        var changes = DoseChanges();
        var n = 0;
        for (var i = 1; i < changes.Count; i++)
        {
            var a = Dose(changes[i - 1].WorkFrom, changes[i - 1].PauseFrom, changes[i - 1].WorkTo, changes[i - 1].PauseTo);
            var b = Dose(changes[i].WorkFrom, changes[i].PauseFrom, changes[i].WorkTo, changes[i].PauseTo);
            if (Math.Sign(a) != 0 && Math.Sign(a) == -Math.Sign(b)
                && (changes[i].Utc - changes[i - 1].Utc).TotalHours <= withinHours) n++;
        }
        return n;

        // Zmiana dawki (udziału pracy podajnika); 0 gdy brak danych.
        static double Dose(double w0, double p0, double w1, double p1)
        {
            if (w0 == 0) w0 = w1;
            if (w1 == 0) w1 = w0;
            if (p0 == 0) p0 = p1;
            if (p1 == 0) p1 = p0;
            return w0 + p0 == 0 || w1 + p1 == 0 ? 0 : w1 / (w1 + p1) - w0 / (w0 + p0);
        }
    }

    public double MinHoursBetweenChanges()
    {
        var c = DoseChanges();
        return c.Count < 2 ? double.PositiveInfinity : c.Zip(c.Skip(1), (a, b) => (b.Utc - a.Utc).TotalHours).Min();
    }

    public double ChangesPerDay => DoseChanges().Count / (double)Days;
    public double CeilingHours => Hours.Count(h => h.Ceiling);

    /// <summary>Odchyłka Tin od celu po rozbiegu, bez godzin na suficie.</summary>
    public (double MaxAbs, double ShareWithin05) IndoorDeviation(int warmupDays = 2)
    {
        var h = Hours.Where(x => x.Utc >= Start.AddDays(warmupDays) && !x.Ceiling).ToList();
        if (h.Count == 0) return (0, 1);
        return (h.Max(x => Math.Abs(x.Indoor - Plant.IndoorGoal)),
            h.Count(x => Math.Abs(x.Indoor - Plant.IndoorGoal) <= 0.5) / (double)h.Count);
    }

    public IEnumerable<FeedChangeLog> SelfDisables => Log.Where(e => e.ReasonCode.StartsWith("SAMOWYLACZENIE_"));
}

/// <summary>
/// Pętla zamknięta: <see cref="VirtualPlant"/> co minutę, prawdziwa Faza 5 co 5 min (jak timer
/// HeatFlow.Console), Tz z historii (godzinowa, interpolowana), prognoza idealna.
/// </summary>
public static class ClosedLoopRunner
{
    public static async Task<ClosedLoopResult> RunAsync(
        IReadOnlyList<(DateTime Utc, double Tz)> hourlyTz,
        DateTime startUtc,
        int days,
        double trueFactor,
        HeatingParameters? parameters = null,
        ControllerFeedSettings? initial = null,
        IReadOnlyList<Fault>? faults = null,
        bool automatOn = true,
        bool startAtModel = false)
    {
        faults ??= Array.Empty<Fault>();
        var p0 = parameters ?? new HeatingParameters();
        if (startAtModel)
        {
            // Start „w równowadze”: w sterowniku już to, co policzyłby automat dla Tz z chwili startu.
            var plan = DoseCalculator.Plan(p0, Interpolate(hourlyTz.OrderBy(x => x.Utc).ToList(), startUtc) ?? 0, 7, 0);
            var pause = (int)(Math.Round(plan.ModelPause / 5.0) * 5);
            initial = new ControllerFeedSettings(plan.Work, pause, DoseCalculator.BlowerFor(plan.Work, pause, p0));
        }
        initial ??= new ControllerFeedSettings(8, 120, 33);
        var harness = new DoseHarness(initial, p0);
        harness.Repo.Switch.Enabled = automatOn;
        var plant = new VirtualPlant(trueFactor,
            (double)initial.FeederWork / (initial.FeederWork + initial.FeederPause) * 86400.0,
            harness.Parameters.IndoorTargetForFeed);

        var series = hourlyTz.OrderBy(x => x.Utc).ToList();
        double? BaseTz(DateTime t) => Interpolate(series, t);
        double? Tz(DateTime t)
        {
            var v = BaseTz(t);
            if (v == null) return null;
            foreach (var f in faults.Where(f => f.Kind == FaultKind.TzDrop && t >= f.FromUtc && t < f.ToUtc))
                v += f.Value * Math.Min(1.0, (t - f.FromUtc).TotalHours / 6.0);
            return v;
        }

        var result = new ClosedLoopResult { Harness = harness, Plant = plant, Start = startUtc, Days = days };
        var end = startUtc.AddDays(days);
        var noConfirmArmed = faults.Where(f => f.Kind == FaultKind.MapperNoConfirm).ToList();
        var switchedOff = false;
        var lastHistory = 0;

        for (var t = startUtc; t < end; t = t.AddMinutes(1))
        {
            harness.Clock.UtcNow = t;
            bool Active(FaultKind k) => faults.Any(f => f.Kind == k && t >= f.FromUtc && t < f.ToUtc);

            var outdoor = Tz(t) ?? 0;
            var s = harness.Controller.Settings;
            plant.FlueOverride = Active(FaultKind.Flue210) ? 210 : null;
            plant.Step(outdoor, s.FeederWork, s.FeederPause, s.Blower);

            if (t.Minute % 5 != 0) continue;

            foreach (var f in noConfirmArmed.Where(f => t >= f.FromUtc).ToList())
            {
                harness.Controller.DropNextWrites = (int)Math.Max(1, f.Value);
                noConfirmArmed.Remove(f);
            }

            if (!switchedOff && faults.FirstOrDefault(f => f.Kind == FaultKind.SwitchOffByUser) is { } off && t >= off.FromUtc)
            {
                harness.Repo.Switch.Enabled = false;
                switchedOff = true;
            }

            if (Active(FaultKind.DataGap))
            {
                harness.Publish(new DoseInputsSnapshot(null, null, null, null, null, null, null, null, null, BoilerDataFresh: false), t);
            }
            else
            {
                harness.Publish(new DoseInputsSnapshot(
                    Math.Round(outdoor, 1), Math.Round(plant.Indoor, 2), Math.Round(plant.Boiler, 2), plant.Setpoint,
                    Math.Round(plant.Flue, 1), plant.BlowerActual, (int)plant.FeederCounterSec,
                    Summer: Active(FaultKind.Summer), ControllerAuto: !Active(FaultKind.ManualMode)), t);
            }

            await harness.StepAsync(t, DoseHarness.IdealForecast(Tz, t));

            foreach (var w in harness.Controller.History.Skip(lastHistory))
            {
                var ok = w.Entity switch
                {
                    DoseControllerWriter.WorkEntityId => w.Value is >= DoseLimits.WorkMin and <= DoseLimits.WorkMax,
                    DoseControllerWriter.PauseEntityId => w.Value is >= DoseLimits.PauseMin and <= DoseLimits.PauseMax,
                    _ => w.Value is >= 30 and <= 35
                };
                if (!ok) result.LimitViolations.Add($"{w.Utc:u} {w.Entity} = {w.Value}");
            }
            lastHistory = harness.Controller.History.Count;

            if (t.Minute == 0)
            {
                var st = harness.Repo.State!;
                var c = harness.Controller.Settings;
                result.Hours.Add(new HourRecord(t, outdoor, plant.Indoor, plant.Boiler, plant.Heating, c.FeederWork,
                    c.FeederPause, c.Blower, harness.Parameters.FeedCorrectionK, st.Ceiling, st.AutomatActive, st.TrimSec));
            }
        }

        return result;
    }

    public static double? Interpolate(IReadOnlyList<(DateTime Utc, double Tz)> series, DateTime t)
    {
        if (series.Count == 0) return null;
        var lo = 0;
        var hi = series.Count - 1;
        if (t <= series[0].Utc) return series[0].Tz;
        if (t >= series[hi].Utc) return series[hi].Tz;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (series[mid].Utc <= t) lo = mid; else hi = mid;
        }
        var a = series[lo];
        var b = series[hi];
        var f = (t - a.Utc).TotalMinutes / (b.Utc - a.Utc).TotalMinutes;
        return a.Tz + (b.Tz - a.Tz) * f;
    }
}
