using System.Text.RegularExpressions;
using HeatFlow.Domain;

namespace HeatFlow.Core.Dose;

/// <summary>Plan dawki z modelu pogodowego (krok 1-4 i 6 specyfikacji).</summary>
/// <param name="Work">Czas pracy podajnika po histerezie [s].</param>
/// <param name="ModelPause">Postój z modelu z trymem, przycięty do zakresu, niezaokrąglony [s].</param>
/// <param name="Ceiling">Kocioł na suficie: przy maks. czasie pracy postój wyszedłby poniżej minimum.</param>
/// <param name="ShortfallPct">O ile procent sufit nie pokrywa potrzeby (0, gdy nie na suficie).</param>
public record DosePlan(int Work, double ModelPause, bool Ceiling, double ShortfallPct);

/// <summary>
/// Decyzja o zmianie nastaw (krok 5 specyfikacji). Przy <paramref name="WaitingForInterval"/>
/// Work/Pause to wartości, które automat chciałby ustawić po upływie odstępu.
/// </summary>
public record DoseDecision(bool Change, int Work, int Pause, string Reason,
    bool WaitingForInterval = false, double MinutesSinceLastChange = 0);

/// <summary>Cel pokoju po harmonogramie, waga HeatLossW i czy jego faktyczna może podnieść cel (DoseUseActual).</summary>
public record RoomTargetInput(string Name, double Target, double Weight, bool UseActual = true);

/// <summary>Wkład pokoju do celu domu: cel, faktyczna wygładzona, użyta wartość i czy weszła po faktycznej.</summary>
public record RoomTargetPart(string Name, double Target, double? Actual, double Used, double Weight, bool ByActual);

/// <summary>
/// Cel domu (z faktycznymi, po przycięciu) i cel z samych celów pokoi. <paramref name="RoomCap"/> - najwyższy
/// cel wśród pokoi z wagą, <paramref name="HouseCapped"/> - zadziałała granica „cel z celów + X K”.
/// </summary>
public record HouseTargetResult(double? Target, double? BaseTarget, IReadOnlyList<RoomTargetPart> Rooms, double? RoomCap,
    bool HouseCapped);

/// <summary>Bilans jednej doby do korekty k.</summary>
/// <param name="FeedSec">Sekundy pracy podajnika z licznika (null = dziura w danych).</param>
/// <param name="HeatingHours">Godziny z dmuchawą &gt; 0 (grzanie, nie podtrzymanie).</param>
public record DailyBalance(double? OutdoorMean, double? IndoorMean, double? FeedSec, double HeatingHours, bool Valid, string? InvalidReason);

/// <summary>
/// Obliczenia Fazy 5 „Dawka” - bez wejścia/wyjścia, żeby dało się je sprawdzić testami
/// i porównać z symulacją a7_sucho2.py (analiza-kociol-nastawy.md § 9, § 11).
/// Wszystko liczone w sekundach pracy podajnika na dobę, nie w kilogramach (§ 3).
/// </summary>
public static class DoseCalculator
{
    /// <summary>
    /// Krok 1: średnia Tz z <paramref name="pastHours"/> godzin pomiaru wstecz (średnie godzinowe
    /// czujnika ogrodowego) i <paramref name="forecastHours"/> godzin prognozy do przodu - § 7a.
    /// Odpowiada oknu OK_OD=-2, OK_DO=1 z symulacji: godzina bieżąca to w czasie rzeczywistym
    /// jeszcze przyszłość, więc bierze ją prognoza. Null, gdy brak jakiegokolwiek pomiaru.
    /// </summary>
    public static double? SmoothedOutdoorTemp(
        IEnumerable<(DateTime TimestampUtc, double Temp)> measured,
        ForecastData? forecast,
        DateTime nowUtc,
        int pastHours,
        int forecastHours)
    {
        var hourly = measured
            .Where(m => m.TimestampUtc <= nowUtc && m.TimestampUtc > nowUtc.AddHours(-pastHours))
            .GroupBy(m => (int)Math.Floor((nowUtc - m.TimestampUtc).TotalHours))
            .Select(g => g.Average(m => m.Temp))
            .ToList();

        if (hourly.Count == 0)
        {
            return null;
        }

        var window = new List<double>(hourly);
        if (forecastHours > 0 && forecast != null)
        {
            // Prognoza OWM ma pełne godziny - bierzemy najbliższe od „teraz − 30 min”.
            window.AddRange(forecast.ForecastHours
                .Where(f => f.Temperature.HasValue && f.DateTime.HasValue
                            && f.DateTime.Value > nowUtc.AddMinutes(-30)
                            && f.DateTime.Value <= nowUtc.AddHours(forecastHours))
                .OrderBy(f => f.DateTime)
                .Take(forecastHours)
                .Select(f => f.Temperature!.Value));
        }

        return window.Average();
    }

    /// <summary>
    /// Krok 2: potrzeba domu = k · (FeedPerKelvin · (cel domu − Tz) + FeedOffset) [s/dobę].
    /// Cel domu: z pokoi (<see cref="HouseTarget"/>, po ograniczeniu wzrostu) albo stała IndoorTargetForFeed.
    /// </summary>
    public static double Need(HeatingParameters p, double outdoor, double? houseTarget = null)
        => p.FeedCorrectionK * (p.FeedPerKelvin * ((houseTarget ?? p.IndoorTargetForFeed) - outdoor) + p.FeedOffset)
           * (1 + p.DoseVentilationSurchargePct / 100.0);

    /// <summary>
    /// Cel domu = Σ(HeatLossW · cel pokoju) / Σ HeatLossW po pokojach z wagą &gt; 0. Cel pokoju tak jak
    /// w fazach 1-3: <see cref="Room.GetTargetTemperature"/> po harmonogramie grzania. Pokoje z wyłączoną
    /// automatyką też wchodzą - ich cel to wciąż temperatura, której Paweł chce, a straty liczą się tak samo.
    /// Null, gdy żaden pokój nie ma wagi.
    /// </summary>
    public static (double? Target, List<(string Name, double Target, double Weight)> Rooms) HouseTarget(
        IEnumerable<RoomConfiguration> rooms, DateTime localTime, bool isWeekend)
    {
        var list = RoomTargets(rooms, localTime, isWeekend).Select(x => (x.Name, x.Target, x.Weight)).ToList();
        if (list.Count == 0) return (null, list);
        return (list.Sum(x => x.Target * x.Weight) / list.Sum(x => x.Weight), list);
    }

    /// <summary>
    /// Cele pokoi z wagą (HeatLossW &gt; 0) po harmonogramie, jak w <see cref="HouseTarget"/>, z flagą
    /// RoomConfiguration.DoseUseActual.
    /// </summary>
    public static List<RoomTargetInput> RoomTargets(IEnumerable<RoomConfiguration> rooms, DateTime localTime, bool isWeekend)
        => rooms
            .Where(r => r.HeatLossW > 0)
            .Select(r =>
            {
                var room = r.ToRoom();
                var active = Utils.ScheduleHelper.IsTimeInRange(localTime, room.HeatingSchedule, isWeekend);
                return new RoomTargetInput(r.Name, room.GetTargetTemperature(active), r.HeatLossW, r.DoseUseActual);
            })
            .Where(x => x.Target > 0)
            .ToList();

    /// <summary>
    /// Cel domu z pokojem liczonym jako wyższa z dwóch: cel albo faktyczna (wygładzona) - decyzja Pawła
    /// 2026-10-20. Nadwyżka faktycznej ponad cel:
    /// <list type="bullet">
    /// <item>przycięta do najwyższego celu wśród pokoi z wagą (pokój cieplejszy niż ktokolwiek chce nie podbija celu dalej),</item>
    /// <item>cały cel domu ≤ cel z samych celów + DoseActualMaxAboveTargetsK (granica sprzężenia dodatniego).</item>
    /// </list>
    /// Bez faktycznych (albo przy DoseTargetUseActual = wył.) = cel z samych celów.
    /// </summary>
    public static HouseTargetResult CombineHouseTarget(IReadOnlyList<RoomTargetInput> rooms,
        IReadOnlyDictionary<string, double>? actuals, HeatingParameters p)
    {
        if (rooms.Count == 0) return new HouseTargetResult(null, null, new List<RoomTargetPart>(), null, false);
        var weightSum = rooms.Sum(r => r.Weight);
        var baseTarget = rooms.Sum(r => r.Target * r.Weight) / weightSum;
        var roomCap = rooms.Max(r => r.Target);
        var useActual = p.DoseTargetUseActual && actuals != null;

        var parts = rooms.Select(r =>
        {
            double? actual = actuals != null && actuals.TryGetValue(r.Name, out var a) ? a : null;
            var used = r.Target;
            if (useActual && r.UseActual && actual > r.Target)
                used = Math.Min(actual.Value, roomCap);
            return new RoomTargetPart(r.Name, r.Target, actual, used, r.Weight, used > r.Target + 0.005);
        }).ToList();

        var target = parts.Sum(x => x.Used * x.Weight) / weightSum;
        var houseCap = baseTarget + p.DoseActualMaxAboveTargetsK;
        var capped = useActual && target > houseCap;
        return new HouseTargetResult(capped ? houseCap : target, baseTarget, parts, roomCap, capped);
    }

    /// <summary>
    /// Wygładza faktyczne temperatury pokoi średnią wykładniczą o stałej <paramref name="tauHours"/>,
    /// każdy pokój ze swoim czasem ostatniego odczytu. Przerwa dłuższa niż 3·τ = start od nowa od odczytu;
    /// pokój bez odczytu przez τ wypada (liczy się po celu), żeby martwy czujnik nie trzymał starej wartości.
    /// </summary>
    public static Dictionary<string, (double Value, DateTime Utc)> SmoothRoomActuals(
        IReadOnlyDictionary<string, (double Value, DateTime Utc)> previous, IReadOnlyDictionary<string, double> readings,
        DateTime nowUtc, double tauHours)
    {
        var result = new Dictionary<string, (double Value, DateTime Utc)>();
        foreach (var (name, x) in readings)
        {
            if (previous.TryGetValue(name, out var prev))
            {
                var dt = (nowUtc - prev.Utc).TotalHours;
                if (dt <= 0) { result[name] = prev; continue; }
                if (dt <= 3 * tauHours)
                {
                    result[name] = (prev.Value + (x - prev.Value) * (1 - Math.Exp(-dt / tauHours)), nowUtc);
                    continue;
                }
            }
            result[name] = (x, nowUtc);
        }
        foreach (var (name, prev) in previous)
            if (!result.ContainsKey(name) && (nowUtc - prev.Utc).TotalHours <= tauHours)
                result[name] = prev;
        return result;
    }

    /// <summary>„salon=24.31@202610201530;…” ↔ słownik (czas UTC).</summary>
    public static Dictionary<string, (double Value, DateTime Utc)> ParseRoomActuals(string? text)
    {
        var result = new Dictionary<string, (double Value, DateTime Utc)>();
        foreach (var item in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = Regex.Match(item, @"^([^=]+)=(-?[0-9.]+)@([0-9]{12})$");
            if (!m.Success) continue;
            result[m.Groups[1].Value] = (double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                DateTime.SpecifyKind(DateTime.ParseExact(m.Groups[3].Value, "yyyyMMddHHmm",
                    System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc));
        }
        return result;
    }

    public static string FormatRoomActuals(IReadOnlyDictionary<string, (double Value, DateTime Utc)> actuals)
        => string.Join(";", actuals.OrderBy(x => x.Key).Select(x =>
            $"{x.Key}={x.Value.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}@{x.Value.Utc:yyyyMMddHHmm}"));

    /// <summary>
    /// Cel użyty w modelu: spadek od razu (dom stygnie sam), wzrost najwyżej o DoseTargetRiseKPerHour
    /// na godzinę. Zwraca też dodatek na dogrzanie domu: HouseCapacity · tempo wzrostu [K/dobę],
    /// najwyżej DoseStorageBonusMaxSec - bez niego dawka liczona dla wyższego celu pokrywałaby tylko
    /// straty, a nie podniesienie temperatury murów (37 kWh/K, analiza § 7).
    /// </summary>
    public static (double Used, double StorageBonusSec) LimitTargetRise(
        double target, double? previousUsed, double hoursSinceLast, HeatingParameters p)
    {
        // Tolerancja 0,005 K: zapamiętany cel jest zaokrąglany - bez niej „dogrzewanie” wisiałoby
        // w nieskończoność o ułamki kelwina (wirtualne testy: wieczne +12 s/dobę).
        if (previousUsed == null || target <= previousUsed.Value + 0.005)
            return (target, 0);
        var hours = Math.Clamp(hoursSinceLast, 0, 2);
        var used = Math.Min(target, previousUsed.Value + p.DoseTargetRiseKPerHour * hours);
        var ratePerDay = hours > 0 ? (used - previousUsed.Value) / hours * 24.0 : 0;
        // Cel nadal poniżej żądanego - dogrzewanie trwa, dodatek w pełnym tempie wzrostu.
        if (used < target) ratePerDay = p.DoseTargetRiseKPerHour * 24.0;
        return (used, Math.Min(p.DoseStorageBonusMaxSec, p.HouseCapacity * ratePerDay));
    }

    /// <summary>Krok 4: postój dający potrzebę przy danym czasie pracy: praca · 86400 / potrzeba − praca.</summary>
    public static double PauseFor(int work, double need) => work * 86400.0 / Math.Max(need, 1.0) - work;

    /// <summary>
    /// Krok 3: czas pracy z histerezą - w górę, dopóki postój wychodzi poniżej minimum; w dół, gdy
    /// przy krótszym czasie postój wyszedłby ≥ FeedWorkUpHysteresisSec (130 s). Bez histerezy
    /// czas pracy skakał 9 ↔ 10 s co kilka godzin (symulacja).
    /// </summary>
    public static int WorkWithHysteresis(int currentWork, double need, HeatingParameters p)
    {
        var (workMin, workMax, pauseMin, _) = Bounds(p);
        var work = Math.Clamp(currentWork, workMin, workMax);
        while (work < workMax && PauseFor(work, need) < pauseMin) work++;
        while (work > workMin && PauseFor(work - 1, need) >= p.FeedWorkUpHysteresisSec) work--;
        return work;
    }

    /// <summary>Kroki 2-4 i 6: czas pracy, postój z modelu (z trymem), sufit.</summary>
    public static DosePlan Plan(HeatingParameters p, double outdoor, int currentWork, int trimSec,
        double? houseTarget = null, double storageBonusSec = 0)
    {
        var (_, workMax, pauseMin, pauseMax) = Bounds(p);
        var need = Need(p, outdoor, houseTarget) + storageBonusSec;
        var work = WorkWithHysteresis(currentWork, need, p);
        var rawPause = PauseFor(work, need);

        // Krok 6 (anti-windup): na suficie stoi maks. praca / min. postój i nic się nie zbiera -
        // brak członu całkującego, więc po ociepleniu od razu schodzi.
        var ceiling = rawPause < pauseMin;
        var shortfall = 0.0;
        if (ceiling)
        {
            var capacity = workMax * 86400.0 / (workMax + pauseMin);
            shortfall = need > 0 ? Math.Max(0, (need - capacity) / need * 100.0) : 0;
        }

        var modelPause = Math.Clamp(rawPause + trimSec, pauseMin, pauseMax);
        return new DosePlan(work, modelPause, ceiling, shortfall);
    }

    /// <summary>
    /// Krok 5: zmiana tylko gdy minął odstęp i różnica postoju ≥ martwej strefy (albo zmienia się
    /// czas pracy); krok postoju najwyżej FeedMaxStepSec, zaokrąglenie do 5 s. Przy zmianie czasu
    /// pracy postój idzie od razu do wartości z modelu - tak jak w symulacji, inaczej dawka
    /// skoczyłaby o cały czas pracy (9 → 10 s przy starym postoju to +10 %).
    /// </summary>
    public static DoseDecision Decide(
        int currentWork, int currentPause, DosePlan plan,
        DateTime? lastChangeUtc, DateTime nowUtc, HeatingParameters p)
    {
        var (workMin, workMax, pauseMin, pauseMax) = Bounds(p);
        var targetWork = Math.Clamp(plan.Work, workMin, workMax);
        var target = Math.Clamp(plan.ModelPause, pauseMin, pauseMax);
        var outOfBounds = currentWork < workMin || currentWork > workMax
                          || currentPause < pauseMin || currentPause > pauseMax;

        int newPause;
        string reason;
        if (targetWork != currentWork)
        {
            // Czas pracy się zmienia: dawka (udział pracy podajnika) najwyżej tyle, ile dałby krok
            // FeedMaxStepSec postoju przy obecnym czasie pracy - inaczej np. 8/120 → 7/175 to −38 %
            // dawki naraz (wirtualny kocioł schodził wtedy pod nastawę − 10 K w godzinę). Czas pracy
            // dobierany histerezą do tej ograniczonej dawki, więc 7/160 → cel 9/111 daje najpierw 7/140,
            // a nie 9/180 (ta sama dawka, dziwny czas pracy).
            var safePause = Math.Clamp(currentPause, pauseMin, pauseMax);
            var curWork = Math.Clamp(currentWork, workMin, workMax);
            var upDuty = (double)curWork / (curWork + Math.Max(1, safePause - p.FeedMaxStepSec));
            var downDuty = (double)curWork / (curWork + safePause + p.FeedMaxStepSec);
            var targetDuty = targetWork / (targetWork + target);
            var duty = Math.Clamp(targetDuty, downDuty, upDuty);
            var limited = Math.Abs(duty - targetDuty) > 1e-9;
            var work = limited ? WorkWithHysteresis(currentWork, duty * 86400.0, p) : targetWork;
            newPause = Round5(work / duty - work, pauseMin, pauseMax);
            reason = limited
                ? $"krok w stronę {targetWork}/{target:F0} s (dawka najwyżej o krok {p.FeedMaxStepSec} s postoju)"
                : $"czas pracy {currentWork} → {targetWork} s";
            targetWork = work;
        }
        else if (Math.Abs(target - currentPause) >= p.FeedDeadbandSec || outOfBounds)
        {
            var step = Math.Clamp(target - currentPause, -p.FeedMaxStepSec, p.FeedMaxStepSec);
            newPause = Round5(currentPause + step, pauseMin, pauseMax);
            reason = outOfBounds
                ? $"nastawy poza zakresem ({currentWork}/{currentPause} s)"
                : $"postój z modelu {target:F0} s, różnica {target - currentPause:+0;-0} s ≥ {p.FeedDeadbandSec} s";
        }
        else
        {
            return new DoseDecision(false, currentWork, currentPause,
                $"bez zmian: różnica postoju {target - currentPause:+0;-0} s w martwej strefie {p.FeedDeadbandSec} s");
        }

        if (newPause == currentPause && targetWork == currentWork)
        {
            return new DoseDecision(false, currentWork, currentPause, "bez zmian po zaokrągleniu do 5 s");
        }

        if (lastChangeUtc.HasValue && nowUtc - lastChangeUtc.Value < TimeSpan.FromHours(p.FeedMinIntervalHours))
        {
            var left = TimeSpan.FromHours(p.FeedMinIntervalHours) - (nowUtc - lastChangeUtc.Value);
            return new DoseDecision(false, targetWork, newPause,
                $"czeka na odstęp {p.FeedMinIntervalHours} h (jeszcze {left.TotalMinutes:F0} min): {reason}",
                WaitingForInterval: true, MinutesSinceLastChange: (nowUtc - lastChangeUtc.Value).TotalMinutes);
        }

        return new DoseDecision(true, targetWork, newPause, reason);
    }

    /// <summary>
    /// Dmuchawa w grzaniu: BlowerMin przy ≤ DoseBlowerLowDuty (3,4 %) pracy podajnika, liniowo do BlowerMax przy
    /// maks. pracy / min. postoju (10 s / 100 s). Zmienia się razem z dawką (§ 9 „Dmuchawa”).
    /// </summary>
    public static int BlowerFor(int work, int pause, HeatingParameters p)
    {
        var (_, workMax, pauseMin, _) = Bounds(p);
        var (blowerMin, blowerMax) = BlowerBounds(p);
        var duty = (double)work / (work + pause);
        var highDuty = (double)workMax / (workMax + pauseMin);
        var low = p.DoseBlowerLowDuty;
        var frac = highDuty > low
            ? Math.Clamp((duty - low) / (highDuty - low), 0, 1)
            : 1;
        return (int)Math.Round(blowerMin + frac * (blowerMax - blowerMin), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Ograniczenia dmuchawy od spalin: nie podnosić przy spalinach ≥ DoseFlueNoRaiseTemp (185 °C) ani przez
    /// DoseFlueSafetyHoldHours (6 h) po bezpieczniku spalin w HA (#24); przy &gt; DoseFlueMaxTemp (200 °C)
    /// −DoseFlueBlowerCutPct (2) pkt, nie niżej niż BlowerMin.
    /// Nigdy poza twarde 33-40 %.
    /// </summary>
    /// <returns>Dmuchawa po ograniczeniach, opis i kod przyczyny (SPALINY_200 / SPALINY_185 /
    /// PO_AUTOMATYZACJI_24) - null, gdy nic nie ograniczyło.</returns>
    public static (int Blower, string? Note, string? Code) LimitBlower(
        int target, int current, double? flueTemp, bool safetyAutomationRecent, HeatingParameters p)
    {
        var (blowerMin, _) = BlowerBounds(p);
        string? note = null;
        string? code = null;
        var result = target;

        if (flueTemp > p.DoseFlueMaxTemp)
        {
            result = current > blowerMin ? Math.Max(blowerMin, current - p.DoseFlueBlowerCutPct) : current;
            note = $"spaliny {flueTemp:F0} °C > {p.DoseFlueMaxTemp} → dmuchawa −{p.DoseFlueBlowerCutPct} pkt";
            code = "SPALINY_200";
        }
        else if (result > current && flueTemp >= p.DoseFlueNoRaiseTemp)
        {
            result = current;
            note = $"spaliny {flueTemp:F0} °C ≥ {p.DoseFlueNoRaiseTemp} → dmuchawy nie podnoszę";
            code = "SPALINY_185";
        }
        else if (result > current && safetyAutomationRecent)
        {
            result = current;
            note = $"bezpiecznik spalin (#24) w ostatnich {p.DoseFlueSafetyHoldHours} h → dmuchawy nie podnoszę";
            code = "PO_AUTOMATYZACJI_24";
        }

        return (Math.Clamp(result, DoseLimits.BlowerMin, DoseLimits.BlowerMax), note, code);
    }

    /// <summary>Udział fizycznego maksimum podajnika na dobę, od którego doba uchodzi za „sufit”.</summary>
    private const double CeilingFeedShare = 0.9;

    /// <summary>
    /// Korekta k raz na dobę (§ 9): netto = podajnik − HouseCapacity · ΔT_wewn (dziś − wczoraj),
    /// k ← k + clamp(FeedKGain (0,3) · (netto / model(ΔT faktyczne) − k), ±FeedKMaxStep (0,05)),
    /// w granicach FeedKMin-FeedKMax (0,6-1,3).
    /// Model bez k, z faktyczną temperaturą w domu - jak w symulacji. Null = doba nie uczy.
    /// </summary>
    public static (double? K, string Reason) CorrectK(double k, DailyBalance day, DailyBalance? dayBefore, HeatingParameters p)
    {
        if (!day.Valid)
            return (null, $"doba pominięta: {day.InvalidReason}");
        // Sufit = kocioł grzał prawie bez przerwy ORAZ podajnik dał niemal tyle, ile fizycznie może (maks. praca /
        // min. postój). Same godziny grzania nie dowodzą sufitu: przy trafionej dawce kocioł też grzeje całą dobę
        // (22.10: 23,2 h przy 2261 s z możliwych 7855 s) i k wtedy nigdy by się nie uczyło.
        var (_, workMax, pauseMin, _) = Bounds(p);
        var maxFeedSec = 86400.0 * workMax / (workMax + pauseMin);
        if (day.HeatingHours >= p.DoseCeilingDayHeatingHours && day.FeedSec!.Value >= CeilingFeedShare * maxFeedSec)
            return (null, $"doba pominięta: grzanie {day.HeatingHours:F1} h ≥ {p.DoseCeilingDayHeatingHours} h i podajnik {day.FeedSec:F0} s ≥ {CeilingFeedShare:P0} możliwych {maxFeedSec:F0} s (sufit)");

        var model = p.FeedPerKelvin * (day.IndoorMean!.Value - day.OutdoorMean!.Value) + p.FeedOffset;
        if (model <= 0)
            return (null, $"doba pominięta: model {model:F0} s ≤ 0 (ciepło)");

        var dIndoor = dayBefore is { IndoorMean: not null } ? day.IndoorMean!.Value - dayBefore.IndoorMean.Value : 0.0;
        var net = day.FeedSec!.Value - p.HouseCapacity * dIndoor;
        var newK = Math.Clamp(k + Math.Clamp(p.FeedKGain * (net / model - k), -p.FeedKMaxStep, p.FeedKMaxStep),
            p.FeedKMin, p.FeedKMax);
        newK = Math.Round(newK, 3);
        return (newK, string.Create(System.Globalization.CultureInfo.GetCultureInfo("pl-PL"),
            $"podajnik {day.FeedSec:F0} s, Tz {day.OutdoorMean:F1} °C, w domu {day.IndoorMean:F2} °C (ΔT_wewn {(dIndoor < -0.005 ? "−" : "+")}{Math.Abs(dIndoor):0.00} K) → netto {net:F0} s = {net / model:F2} × model {model:F0} s → k {k:F3} → {newK:F3}"));
    }

    /// <summary>
    /// Bilans doby z migawek (jak a2_doby.py): sekundy podajnika z przyrostów licznika (spadek
    /// &gt; 5 s = zasyp, licznik od zera), godziny grzania = udział migawek z dmuchawą &gt; 0,
    /// średnie temperatur. Doba nieważna przy dziurach w danych albo w trybie lato.
    /// </summary>
    public static DailyBalance BalanceForDay(IReadOnlyList<DoseSample> samples, DateTime dayStartUtc, DateTime dayEndUtc)
    {
        var inDay = samples.Where(s => s.TimestampUtc >= dayStartUtc && s.TimestampUtc < dayEndUtc).ToList();
        var withBlower = inDay.Where(s => s.BlowerActual.HasValue).ToList();
        var outdoor = inDay.Where(s => s.OutdoorTemp.HasValue).Select(s => s.OutdoorTemp!.Value).ToList();
        var indoor = inDay.Where(s => s.IndoorTemp.HasValue).Select(s => s.IndoorTemp!.Value).ToList();
        var expected = (dayEndUtc - dayStartUtc).TotalMinutes / 5.0;

        double? Mean(List<double> v) => v.Count > 0 ? v.Average() : null;
        var heatingHours = withBlower.Count > 0
            ? 24.0 * withBlower.Count(s => s.BlowerActual > 0) / withBlower.Count
            : 0.0;

        // Licznik: przyrost przypisany do doby późniejszej migawki - poprzednia może być sprzed północy.
        double feed = 0;
        var gaps = 0;
        int? prev = samples.LastOrDefault(s => s.TimestampUtc < dayStartUtc && s.FeederCounterSec.HasValue)?.FeederCounterSec;
        foreach (var s in inDay.Where(s => s.FeederCounterSec.HasValue))
        {
            if (prev.HasValue)
            {
                var d = s.FeederCounterSec!.Value - prev.Value;
                if (d < -5) d = s.FeederCounterSec.Value; // zasyp - licznik od zera
                if (d >= 0 && d < 400) feed += d;
                else gaps++;
            }
            prev = s.FeederCounterSec;
        }

        string? invalid = null;
        if (withBlower.Count < expected * 250 / 288) invalid = $"za mało migawek kotła ({withBlower.Count} z {expected:F0})";
        else if (outdoor.Count < expected * 100 / 288) invalid = $"za mało odczytów Tz ({outdoor.Count})";
        else if (indoor.Count < expected * 100 / 288) invalid = $"za mało odczytów z pokoi ({indoor.Count})";
        else if (gaps > 0) invalid = $"dziury w liczniku podajnika ({gaps})";
        else if (inDay.Count(s => s.SummerMode == true) > inDay.Count * 0.1) invalid = "tryb lato";

        return new DailyBalance(Mean(outdoor), Mean(indoor), invalid == null ? feed : null, heatingHours, invalid == null, invalid);
    }

    /// <summary>
    /// Szybki trym, sygnał „za mało”: dmuchawa chodziła w każdej migawce i kocioł był poniżej
    /// nastawy − <paramref name="kelvinBelow"/> (3 K) przez całe okno (§ 9). Wymaga ≥ 75 % migawek z okna.
    /// </summary>
    public static bool BoilerUnderpowered(IReadOnlyList<DoseSample> window, TimeSpan length, double kelvinBelow = 3)
    {
        var valid = window.Where(s => s.BlowerActual.HasValue && s.BoilerTemp.HasValue && s.BoilerSetpoint.HasValue).ToList();
        if (valid.Count < length.TotalMinutes / 5.0 * 0.75) return false;
        return valid.All(s => s.BlowerActual > 0 && s.BoilerTemp < s.BoilerSetpoint - kelvinBelow);
    }

    /// <summary>
    /// Szybki trym, sygnał „za dużo”: ile razy kocioł wszedł w podtrzymanie. W trybie ręcznym
    /// sterownik przechodzi w podtrzymanie powyżej nastawy kotła, wraca poniżej nastawy minus
    /// histereza (instrukcja eCoal V4 § 4.1.3) - liczymy przejścia przez nastawę w górę, z ponownym
    /// uzbrojeniem dopiero po zejściu poniżej nastawy − 1 K (jak próg podtrzymania w a2_doby.py).
    /// </summary>
    public static int StandbyEntries(IReadOnlyList<DoseSample> window)
    {
        var count = 0;
        bool? armed = null;
        foreach (var s in window.Where(s => s.BoilerTemp.HasValue && s.BoilerSetpoint.HasValue))
        {
            var k = s.BoilerTemp!.Value;
            var z = s.BoilerSetpoint!.Value;
            armed ??= k < z - 1;
            if (armed == true && k >= z)
            {
                count++;
                armed = false;
            }
            else if (armed == false && k < z - 1)
            {
                armed = true;
            }
        }

        return count;
    }

    /// <summary>
    /// „Kocioł zimny”: poniżej nastawy − <paramref name="kelvinBelow"/> (10 K) w każdej migawce przez całe
    /// okno (min. 75 % migawek).
    /// </summary>
    public static bool BoilerColdFor(IReadOnlyList<DoseSample> window, TimeSpan length, double kelvinBelow = 10)
    {
        var valid = window.Where(s => s.BoilerTemp.HasValue && s.BoilerSetpoint.HasValue).ToList();
        if (valid.Count < length.TotalMinutes / 5.0 * 0.75) return false;
        return valid.All(s => s.BoilerTemp < s.BoilerSetpoint - kelvinBelow);
    }

    /// <summary>
    /// Licznik sensor.kociol_czas_pracy_podajnika_2, format „30m,034s” (zeruje się przy zasypie).
    /// Rozumie też godziny („1h,02m,003s”). Null, gdy format nieznany.
    /// </summary>
    public static int? ParseFeederCounter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var matches = Regex.Matches(value, @"(\d+)\s*([hms])", RegexOptions.IgnoreCase);
        if (matches.Count == 0) return null;

        var total = 0;
        foreach (Match m in matches)
        {
            var n = int.Parse(m.Groups[1].Value);
            total += char.ToLowerInvariant(m.Groups[2].Value[0]) switch
            {
                'h' => n * 3600,
                'm' => n * 60,
                _ => n
            };
        }

        return total;
    }

    /// <summary>Widełki z parametrów, zawsze w twardych granicach Pawła.</summary>
    public static (int WorkMin, int WorkMax, int PauseMin, int PauseMax) Bounds(HeatingParameters p)
    {
        var workMin = Math.Clamp(p.FeederWorkMin, DoseLimits.WorkMin, DoseLimits.WorkMax);
        var workMax = Math.Clamp(p.FeederWorkMax, workMin, DoseLimits.WorkMax);
        var pauseMin = Math.Clamp(p.FeederPauseMin, DoseLimits.PauseMin, DoseLimits.PauseMax);
        var pauseMax = Math.Clamp(p.FeederPauseMax, pauseMin, DoseLimits.PauseMax);
        return (workMin, workMax, pauseMin, pauseMax);
    }

    public static (int BlowerMin, int BlowerMax) BlowerBounds(HeatingParameters p)
    {
        var min = Math.Clamp(p.BlowerMin, DoseLimits.BlowerMin, DoseLimits.BlowerMax);
        var max = Math.Clamp(p.BlowerMax, min, DoseLimits.BlowerMax);
        return (min, max);
    }

    private static int Round5(double value, int min, int max)
        => Math.Clamp((int)(Math.Round(value / 5.0, MidpointRounding.AwayFromZero) * 5), min, max);
}
