using System.Globalization;
using System.Text;
using HeatFlow.Core.Dose;
using HeatFlow.Domain;
using HeatFlow.DoseSim;

// Wirtualne testy Fazy 5 „Dawka” - bez HA, mappera i bazy. Patrz README.md obok.
if (args.Length < 1)
{
    Console.WriteLine("Użycie: HeatFlow.DoseReplay <migawki.csv> [plik_raportu.md] [katalog_na_tz|-] [cele-pokoi.csv]");
    return 2;
}

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
var warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, warsaw);
DateTime Utc(int y, int m, int d) => TimeZoneInfo.ConvertTimeToUtc(new DateTime(y, m, d), warsaw);

var rows = HmsRow.ReadCsv(args[0]).ToList();
var roomTargets = args.Length > 3 ? ReplayRunner.ReadRoomTargets(args[3]) : null;
var ConfigurationSeedRooms = HeatFlow.Infrastructure.Database.ConfigurationSeed.DoseIndoorMeanRooms;
var report = new StringBuilder();
void W(string s = "") { report.AppendLine(s); Console.WriteLine(s); }

var periods = new (string Name, DateTime From, DateTime To)[]
{
    ("I–II (mróz)", Utc(2026, 1, 5), Utc(2026, 3, 1)),
    ("III–IV", Utc(2026, 3, 1), Utc(2026, 5, 1)),
    ("X", Utc(2026, 10, 1), Utc(2026, 10, 20))
};

W($"# Faza 5 „Dawka” - wirtualne testy ({DateTime.Now:yyyy-MM-dd HH:mm})");
W();
W($"Dane: {rows.Count} migawek HMS {rows[0].Utc:yyyy-MM-dd} → {rows[^1].Utc:yyyy-MM-dd} (UTC). Prognoza w replayu = faktyczna Tz z kolejnych godzin (**idealna**).");
W();

void ReplayTable(ReplayResult r)
{
    W("| Okres | Doby | Zmian dawki | Zmian/dobę | w tym czasu pracy | Godz. na suficie | Automat aktywny | Trymy | Plan s/dobę | Faktycznie (Paweł) s/dobę |");
    W("|---|---|---|---|---|---|---|---|---|---|");
    foreach (var (name, from, to) in periods)
    {
        var days = (to - from).TotalDays;
        var changes = DoseEvents(r.Log, from, to);
        var workChanges = changes.Count(c => c.Any(e => e.Parameter == DoseLog.Work));
        var steps = r.Steps.Where(s => s.Utc >= from && s.Utc < to).ToList();
        var ceilingH = steps.Count(s => s.Ceiling) * 5 / 60.0;
        var active = steps.Count(s => s.AutomatActive) / (double)Math.Max(1, steps.Count);
        var trims = r.Log.Count(e => e.Parameter == DoseLog.Trim && e.ReasonCode.StartsWith("TRYM") && e.Status == DoseLog.StatusInternal && e.TimestampUtc >= from && e.TimestampUtc < to);
        var plan = steps.Where(s => s.AutomatActive).Select(s => s.SimWork * 86400.0 / (s.SimWork + s.SimPause)).DefaultIfEmpty().Average();
        var real = steps.Where(s => s.ActualWork > 0 && s.ActualPause > 0).Select(s => s.ActualWork!.Value * 86400.0 / (s.ActualWork.Value + s.ActualPause!.Value)).DefaultIfEmpty().Average();
        W($"| {name} | {days:F0} | {changes.Count} | {changes.Count / days:F1} | {workChanges} | {ceilingH:F0} | {active:P0} | {trims} | {plan:F0} | {real:F0} |");
    }
    W();
}

// ---------- 1. Replay: automat włączony (pętla otwarta) ----------
var sw = System.Diagnostics.Stopwatch.StartNew();
var on = await ReplayRunner.RunAsync(rows, automatOn: true, roomTargets: roomTargets);
var onFixed = await ReplayRunner.RunAsync(rows, automatOn: true);
W($"## 1. Replay historii, automat włączony (pętla otwarta, {sw.Elapsed.TotalSeconds:F0} s)");
W();
W("Kocioł i dom w danych reagowały na nastawy Pawła, nie automatu - więc trym od kotła i bezpieczniki widzą tamten kocioł. Po samowyłączeniu „Paweł” włącza automat ponownie po 6 h. " +
  (roomTargets == null ? "Cel domu: stała 24 °C (brak pliku z celami pokoi)." : "Cel domu: cele pokoi ważone stratami (HeatLossW), cele z RoomState - średnie tygodniowe, przed 2026-01-26 jak w pierwszym tygodniu."));
W();
W($"Błędy fazy: {on.Harness.Errors.Errors.Count}; przekroczenia granic 7–10 s / 100–320 s / 28–35 %: {on.LimitViolations.Count + onFixed.LimitViolations.Count}.");
W();
ReplayTable(on);

W("### Cel z pokoi vs stała 24 °C (poprzednia wersja) - plan dawki i zużycie");
W();
W("Plan = średnia dawka nastawiona przez automat (praca/(praca+postój) · 86 400 s, czyli przy 24 h grzania), gdy aktywny. " +
  "Nastawy Pawła = ta sama miara dla nastaw ze sterownika. Licznik = faktyczne zużycie z HMS (mniejsze od nominalnego o podtrzymania). 1 s = 4,25 g (§ 3).");
W();
W("| Okres | Cel domu (średnio) | Plan cel 24 °C [s/d] | Plan cel z pokoi [s/d] | Różnica [s/d] | Różnica [kg/d] | Godz. sufit 24 °C → pokoje | Nastawy Pawła [s/d] | Plan z pokoi vs nastawy Pawła [kg/d] | Licznik (faktyczne zużycie) [s/d] (kg/d) |");
W("|---|---|---|---|---|---|---|---|---|---|");
foreach (var (name, from, to) in periods)
{
    double Plan(ReplayResult r) => r.Steps.Where(x => x.Utc >= from && x.Utc < to && x.AutomatActive)
        .Select(x => x.SimWork * 86400.0 / (x.SimWork + x.SimPause)).DefaultIfEmpty().Average();
    double Ceil(ReplayResult r) => r.Steps.Count(x => x.Utc >= from && x.Utc < to && x.Ceiling) * 5 / 60.0;
    var target = on.Steps.Where(x => x.Utc >= from && x.Utc < to && x.HouseTarget.HasValue).Select(x => x.HouseTarget!.Value).DefaultIfEmpty().Average();
    var pf = Plan(onFixed);
    var pr = Plan(on);
    var actual = CounterFeedPerDay(rows, from, to);
    var pawel = on.Steps.Where(x => x.Utc >= from && x.Utc < to && x.AutomatActive && x.ActualWork > 0 && x.ActualPause > 0)
        .Select(x => x.ActualWork!.Value * 86400.0 / (x.ActualWork.Value + x.ActualPause!.Value)).DefaultIfEmpty().Average();
    W($"| {name} | {target:F1} °C | {pf:F0} | {pr:F0} | {pr - pf:+0;-0} | {(pr - pf) * 4.25 / 1000:+0.0;-0.0} | {Ceil(onFixed):F0} → {Ceil(on):F0} | {pawel:F0} | {(pr - pawel) * 4.25 / 1000:+0.0;-0.0} | {actual:F0} ({actual * 4.25 / 1000:F1}) |");
}
W();

W("Samowyłączenia (kod → liczba):");
foreach (var g in on.Log.Where(e => e.ReasonCode.StartsWith("SAMOWYLACZENIE_")).GroupBy(e => e.ReasonCode).OrderByDescending(g => g.Count()))
    W($"- {g.Key}: {g.Count()} (np. {Local(g.First().TimestampUtc):yyyy-MM-dd HH:mm}: {g.First().Message})");
W();

W("### k (koniec doby, czas lokalny)");
W();
var kByDay = on.Steps.GroupBy(s => Local(s.Utc).Date).Select(g => (Day: g.Key, K: g.Last().K)).ToList();
W(string.Join(" · ", kByDay.Where(x => x.Day.Day is 1 or 15 || x.Day == kByDay[^1].Day).Select(x => $"{x.Day:MM-dd} {x.K:F2}")));
W();
foreach (var (name, from, to) in periods)
{
    var ks = on.Steps.Where(s => s.Utc >= from && s.Utc < to).Select(s => s.K).ToList();
    W($"- {name}: k od {ks.First():F2} do {ks.Last():F2} (min {ks.Min():F2}, max {ks.Max():F2})");
}
W();

W("### Rozkład nastaw: automat vs faktycznie w sterowniku (udział czasu, gdy automat aktywny)");
W();
foreach (var (name, from, to) in periods)
{
    var steps = on.Steps.Where(s => s.Utc >= from && s.Utc < to && s.AutomatActive && s.ActualPause != null).ToList();
    if (steps.Count == 0) { W($"- {name}: automat nieaktywny"); continue; }
    W($"**{name}** ({steps.Count * 5 / 60.0:F0} h)");
    W();
    W("| Postój | automat | Paweł |");
    W("|---|---|---|");
    foreach (var (lo, hi) in new[] { (100, 119), (120, 149), (150, 199), (200, 259), (260, 320), (321, 999) })
        W($"| {lo}–{(hi == 999 ? "" : hi)} s | {steps.Count(s => s.SimPause >= lo && s.SimPause <= hi) / (double)steps.Count:P0} | {steps.Count(s => s.ActualPause >= lo && s.ActualPause <= hi) / (double)steps.Count:P0} |");
    W();
    W("| Praca | automat | Paweł |");
    W("|---|---|---|");
    foreach (var w in new[] { 7, 8, 9, 10 })
        W($"| {w} s | {steps.Count(s => s.SimWork == w) / (double)steps.Count:P0} | {steps.Count(s => s.ActualWork == w) / (double)steps.Count:P0} |");
    W();
    W($"Dmuchawa: automat {string.Join(", ", steps.GroupBy(s => s.SimBlower).OrderBy(g => g.Key).Select(g => $"{g.Key} % {g.Count() / (double)steps.Count:P0}"))}; Paweł {string.Join(", ", steps.Where(s => s.ActualBlower != null).GroupBy(s => s.ActualBlower).OrderBy(g => g.Key).Select(g => $"{g.Key} % {g.Count() / (double)steps.Count:P0}"))}");
    W();
}

W("### Przykładowe wpisy FeedChangeLog");
foreach (var day in new[] { new DateTime(2026, 2, 1), new DateTime(2026, 3, 14), new DateTime(2026, 10, 17) })
{
    W();
    W($"**{day:yyyy-MM-dd}** (czas lokalny)");
    W();
    foreach (var e in on.Log.Where(e => Local(e.TimestampUtc).Date == day))
        W($"- {Local(e.TimestampUtc):HH:mm} [{e.Status}, {e.ReasonCode}] {e.Message}");
}
W();

// ---------- 1a. Porównanie z a7_sucho2.py ----------
sw.Restart();
var neutral = await ReplayRunner.RunAsync(rows, automatOn: true, neutralBoiler: true);
W($"## 1a. Ten sam replay z „neutralnym” kotłem - porównanie z a7_sucho2.py ({sw.Elapsed.TotalSeconds:F0} s)");
W();
W("Kocioł w danych podmieniony na nastawa − 1,5 K i spaliny 120 °C: trym i bezpieczniki od kotła milczą, zostaje pogoda + k + tryb lato/ręczny - tak liczy symulacja z analizy.");
W();
ReplayTable(neutral);
W("Symulacja a7_sucho2.py (A=255 B=−934 C=1549, okno −2…+1 h, odstęp 2 h): III–IV 9,7 zmiany/dobę (plan 3261 s/dobę, faktycznie 3201), X 9,0/dobę (plan 2168, faktycznie 1828), k na końcu 0,80 / 0,66.");
W();

// ---------- 1b. Tryb cienia ----------
sw.Restart();
var shadow = await ReplayRunner.RunAsync(rows, automatOn: false);
W($"## 1b. Replay w trybie cienia (przełącznik OFF, {sw.Elapsed.TotalSeconds:F0} s)");
W();
W($"Zapisy do sterownika: {shadow.Harness.Controller.History.Count} (musi być 0). Błędy fazy: {shadow.Harness.Errors.Errors.Count}.");
foreach (var (name, from, to) in periods)
{
    var sug = shadow.Log.Where(e => e.Status == DoseLog.StatusSuggestion && e.TimestampUtc >= from && e.TimestampUtc < to)
        .GroupBy(e => e.TimestampUtc).Count();
    W($"- {name}: {sug} wirtualnych zmian ({sug / (to - from).TotalDays:F1}/dobę) - tyle razy w logu pojawi się SUGESTIA");
}
W();

// ---------- 2. Pętla zamknięta ----------
var hourly = ReplayRunner.HourlyOutdoor(rows).OrderBy(x => x.Key).Select(x => (x.Key, x.Value)).ToList();
if (args.Length > 2)
{
    // Wycinki Tz do testów xUnit (małe pliki, w repo); „-” = pomiń.
    if (args[2] != "-")
    foreach (var (file, from) in new[] { ("tz-luty.csv", Utc(2026, 2, 1)), ("tz-marzec.csv", Utc(2026, 3, 1)), ("tz-pazdziernik.csv", Utc(2026, 10, 6)) })
    {
        var lines = hourly.Where(x => x.Key >= from.AddDays(-1) && x.Key < from.AddDays(15))
            .Select(x => $"{x.Key:yyyy-MM-dd HH:mm},{x.Value.ToString("F2", CultureInfo.InvariantCulture)}");
        File.WriteAllLines(Path.Combine(args[2], file), lines.Prepend("t_utc,tz"));
    }
}

W("## 2. Wirtualny kocioł + dom w pętli zamkniętej (14 dni, prawdziwa Tz, prognoza idealna)");
W();
W("Dom: C = 1549 s/K, strata = f · (255·(Tin − Tz) − 934) s/dobę (f = „prawdziwy dom” względem modelu); ogień: opóźnienie 15 min + stała 45 min; " +
  "woda 9,6 s/K; podtrzymanie ≥ nastawy 67 °C, powrót < 65 °C; 4D przymyka poniżej ~50 °C. Start w równowadze (nastawy z modelu), k = 1,0.");
W();
foreach (var (title, coldMinutes) in new[] { ("bezpiecznik „kocioł zimny” = 0 (tylko ostrzeżenie - domyślnie, decyzja Pawła)", 0), ("dla porównania: 60 min → samowyłączenie (pierwotna spec)", 60) })
{
    W($"### {title}");
    W();
    W("| Okres | f domu | Samowyłączenia | Ostrzeżenia | Zmian/dobę | Min. odstęp [h] | Piłowanie (≤ 4 h) | Godz. sufit | Tin max odchyłka / w ±0,5 K | k: dzień 3 → 7 → 14 | Granice |");
    W("|---|---|---|---|---|---|---|---|---|---|---|");
    foreach (var (name, from) in new[] { ("luty", Utc(2026, 2, 1)), ("marzec", Utc(2026, 3, 1)), ("październik", Utc(2026, 10, 6)) })
    foreach (var f in new[] { 0.7, 1.0, 1.2 })
    {
        var r = await ClosedLoopRunner.RunAsync(hourly, from, 14, f, new HeatingParameters { DoseBoilerColdMinutes = coldMinutes }, startAtModel: true);
        var (maxAbs, within) = r.IndoorDeviation();
        double KAt(int d) => r.Hours.Where(h => h.Utc < from.AddDays(d)).Select(h => h.K).DefaultIfEmpty(1).Last();
        var sd = string.Join(", ", r.SelfDisables.Select(e => $"{e.ReasonCode.Replace("SAMOWYLACZENIE_", "")} {Local(e.TimestampUtc):dd.MM HH:mm}"));
        W($"| {name} | {f:F1} | {(sd.Length == 0 ? "—" : sd)} | {r.Log.Count(e => e.Status == DoseLog.StatusWarning)} | {r.ChangesPerDay:F1} | {r.MinHoursBetweenChanges():F1} | {r.Reversals()} | {r.CeilingHours:F0} | {maxAbs:F2} K / {within:P0} | {KAt(3):F2} → {KAt(7):F2} → {KAt(14):F2} | {(r.LimitViolations.Count == 0 && r.Harness.Errors.Errors.Count == 0 ? "OK" : $"{r.LimitViolations.Count} przekr., {r.Harness.Errors.Errors.Count} błędów")} |");
    }
    W();
}

W("### Cele pokoi ważone stratami a pomiar do korekty k (14 dni, f = 1,0)");
W();
W("Pokoje jak w bazie dziś: cele (garaż 6 °C … salon 26 °C), wagi HeatLossW, cel domu = " +
  $"{DoseCalculator.HouseTarget(SimDefaults.AllRooms(), DateTime.Now, false).Target:F2} °C. Czujniki pokoi = temperatura domu (ważona) + przesunięcie pokoju z RoomState 2026-10-20 " +
  "(7 czujników BMx280 średnio +1,88 K, garaż −9,8 K). „Tin” = temperatura domu ważona stratami.");
W();
W("| Okres | Pomiar do k | Cel domu | Tin (ost. 3 doby) | Tin − cel | Średnia 7 czujników | k: 3 → 7 → 14 | Zmian/dobę | Ostrzeżenia „kocioł zimny” | Śr. dawka [s/d] | Granice |");
W("|---|---|---|---|---|---|---|---|---|---|---|");
foreach (var (name, from) in new[] { ("luty", Utc(2026, 2, 1)), ("marzec", Utc(2026, 3, 1)), ("październik", Utc(2026, 10, 6)) })
foreach (var weighted in new[] { false, true })
{
    var r = await ClosedLoopRunner.RunAsync(hourly, from, 14, 1.0, new HeatingParameters { DoseIndoorMeasureWeighted = weighted },
        startAtModel: true, rooms: SimDefaults.AllRooms(), roomOffsets: SimDefaults.RoomOffsetsToday);
    var last = r.Hours.Where(h => h.Utc >= from.AddDays(11)).ToList();
    double KAt(int d) => r.Hours.Where(h => h.Utc < from.AddDays(d)).Select(h => h.K).DefaultIfEmpty(1).Last();
    var tin = last.Average(h => h.Indoor);
    var goal = last.Average(h => h.Goal);
    var seven = last.Average(h => h.Indoor) + SimDefaults.RoomOffsetsToday.Where(o => ConfigurationSeedRooms.Contains(o.Key)).Average(o => o.Value);
    var dose = r.Hours.Where(h => h.Utc >= from.AddDays(3)).Average(h => h.Work * 86400.0 / (h.Work + h.Pause));
    W($"| {name} | {(weighted ? "ważony (wszystkie pokoje)" : "7 czujników (domyślnie)")} | {goal:F2} °C | {tin:F2} °C | {Signed(tin - goal)} K | {seven:F2} °C | {KAt(3):F2} → {KAt(7):F2} → {KAt(14):F2} | {r.ChangesPerDay:F1} | {r.Log.Count(e => e.Status == DoseLog.StatusWarning)} | {dose:F0} | {(r.LimitViolations.Count == 0 ? "OK" : "PRZEKROCZONE")} |");
}
W();

W("### Podniesienie celów wszystkich pokoi o 1 K (marzec, f = 1,0, cele dzisiejsze, 4. doba 22:00 UTC)");
W();
{
    var from = Utc(2026, 3, 1);
    var atStep = from.AddDays(3).AddHours(22);
    var r = await ClosedLoopRunner.RunAsync(hourly, from, 7, 1.0, new HeatingParameters(), startAtModel: true,
        rooms: SimDefaults.AllRooms(), roomOffsets: SimDefaults.RoomOffsetsToday,
        faults: new[] { new Fault(FaultKind.TargetStep, atStep, from.AddDays(7), 1.0) });
    var after = r.Hours.Where(h => h.Utc >= atStep).ToList();
    var reached = after.FirstOrDefault(h => h.HouseTargetUsed >= h.Goal - 0.01);
    var tinReached = after.FirstOrDefault(h => h.Indoor >= h.Goal - 0.2);
    W($"- cel domu {after.First().Goal - 1:F2} → {after.First().Goal:F2} °C; cel w modelu dochodzi po {(reached == null ? "> okres" : $"{(reached.Utc - atStep).TotalHours:F0} h")}; " +
      $"dodatek na dogrzanie maks. {after.Max(h => h.StorageBonusSec):F0} s/dobę; godzin na suficie 10/100 w pierwszej dobie: {after.Take(24).Count(h => h.Ceiling)}; " +
      $"Tin w 0,2 K od nowego celu po {(tinReached == null ? "> okres" : $"{(tinReached.Utc - atStep).TotalHours:F0} h")}; Tz w tym czasie średnio {after.Take(24).Average(h => h.Outdoor):F1} °C; granice {(r.LimitViolations.Count == 0 ? "OK" : "PRZEKROCZONE")}.");
    W();
    foreach (var e in r.Log.Where(e => e.TimestampUtc >= atStep && e.Status != DoseLog.StatusSuggestion && e.ReasonCode is "CEL_DOMU" or "POGODA").Take(6))
        W($"- {Local(e.TimestampUtc):dd.MM HH:mm} [{e.Status}, {e.ReasonCode}] {e.Message}");
    W();
}

W("### Usterki (marzec, f = 1,0, „kocioł zimny” = 0, usterka 4. doby o 10:00 UTC)");
W();
var t0 = Utc(2026, 3, 1);
var at = t0.AddDays(3).AddHours(10);
var faults = new (string Name, Fault Fault)[]
{
    ("spaliny 210 °C", new Fault(FaultKind.Flue210, at, at.AddMinutes(30))),
    ("mapper nie potwierdza zapisu (2 zgubione zapisy)", new Fault(FaultKind.MapperNoConfirm, at, at.AddDays(2), 2)),
    ("brak danych 40 min", new Fault(FaultKind.DataGap, at, at.AddMinutes(40))),
    ("Tz −15 K w 6 h", new Fault(FaultKind.TzDrop, at, at.AddDays(2), -15)),
    ("tryb lato", new Fault(FaultKind.Summer, at, at.AddHours(12))),
    ("tryb ręczny sterownika", new Fault(FaultKind.ManualMode, at, at.AddHours(2))),
    ("Paweł wyłącza przełącznik", new Fault(FaultKind.SwitchOffByUser, at, at))
};
foreach (var (name, fault) in faults)
{
    var r = await ClosedLoopRunner.RunAsync(hourly, t0, 6, 1.0, new HeatingParameters { DoseBoilerColdMinutes = 0 }, faults: new[] { fault }, startAtModel: true);
    var before = r.Log.LastOrDefault(e => e.Parameter == DoseLog.Automat && e.NewValue == 1 && e.TimestampUtc < at);
    var off = r.Log.FirstOrDefault(e => e.Parameter == DoseLog.Automat && e.NewValue == 0 && e.TimestampUtc >= at);
    var restored = r.Log.Where(e => e.ReasonCode == "PRZYWROCENIE_NASTAW" && e.TimestampUtc >= at).ToList();
    var discord = r.Harness.Notifier.Messages.FirstOrDefault(m => m.Utc >= at && m.Title.Contains("automat"));
    W($"**{name}**: {(off == null ? "automat dalej działa" : $"{Local(off.TimestampUtc):dd.MM HH:mm} {off.ReasonCode} (po {(off.TimestampUtc - at).TotalMinutes:F0} min)")}; " +
      $"przełącznik na końcu {(r.Harness.Repo.Switch.Enabled ? "ON" : "OFF")}; granice {(r.LimitViolations.Count == 0 ? "OK" : "PRZEKROCZONE")}; " +
      $"zapamiętane przy włączeniu {r.Log.First(e => e.ReasonCode == "WLACZENIE_RECZNE").Message.Split("nastawy: ")[1].Split(';')[0]}, sterownik na końcu {r.Harness.Controller.Settings.FeederWork}/{r.Harness.Controller.Settings.FeederPause} s {r.Harness.Controller.Settings.Blower} %.");
    W();
    foreach (var e in r.Log.Where(e => e.TimestampUtc >= at && e.Status != DoseLog.StatusSuggestion).Take(fault.Kind == FaultKind.TzDrop ? 8 : 6))
        W($"- {Local(e.TimestampUtc):dd.MM HH:mm} [{e.Status}, {e.ReasonCode}{(e.MapperConfirmation != null ? ", mapper " + e.MapperConfirmation : "")}] {e.Message}");
    if (discord.Title != null) W($"- Discord: „{discord.Title}” - {discord.Message}");
    W();
}

if (args.Length > 1) File.WriteAllText(args[1], report.ToString());
return 0;

// Podajnik z licznika HMS (jak a2_doby.py): przyrosty 0-400 s, spadek > 5 s = zasyp (licznik od zera).
static string Signed(double v) => (v < -0.005 ? "−" : "+") + Math.Abs(v).ToString("0.00", CultureInfo.GetCultureInfo("pl-PL"));

static double CounterFeedPerDay(IReadOnlyList<HmsRow> rows, DateTime from, DateTime to)
{
    double feed = 0;
    int? prev = null;
    foreach (var r in rows.Where(r => r.Utc >= from && r.Utc < to))
    {
        var c = DoseCalculator.ParseFeederCounter(r.Get("licznik"));
        if (c == null) continue;
        if (prev.HasValue)
        {
            var d = c.Value - prev.Value;
            if (d < -5) d = c.Value;
            if (d is >= 0 and < 400) feed += d;
        }
        prev = c;
    }
    return feed / (to - from).TotalDays;
}

static List<List<FeedChangeLog>> DoseEvents(IReadOnlyList<FeedChangeLog> log, DateTime from, DateTime to)
    => log.Where(e => e.Status == DoseLog.StatusWritten && e.ReasonCode != "PRZYWROCENIE_NASTAW"
                      && e.Parameter is DoseLog.Work or DoseLog.Pause && e.TimestampUtc >= from && e.TimestampUtc < to)
        .GroupBy(e => e.TimestampUtc).Select(g => g.ToList()).ToList();
