using System.Globalization;
using HeatFlow.Domain;

namespace HeatFlow.Core.Dose;

/// <summary>
/// Dane wejściowe w chwili decyzji - kopiowane do każdego wpisu dziennika (FeedChangeLog).
/// Wypełniane stopniowo w trakcie przebiegu Fazy 5.
/// </summary>
public class DoseLogContext
{
    public DateTime NowUtc { get; set; }
    public bool AutomatActive { get; set; }
    public double? OutdoorSmoothed { get; set; }
    public double? OutdoorAtPreviousChange { get; set; }
    public double? ForecastNextHour { get; set; }
    public double? K { get; set; }
    public double? NeedSecPerDay { get; set; }
    public int TrimSec { get; set; }
    public bool Ceiling { get; set; }
    public double ShortfallPct { get; set; }
    public double? BoilerTemp { get; set; }
    public double? BoilerSetpoint { get; set; }
    public double? FlueTemp { get; set; }
    public double? HeatingShare2hPct { get; set; }
    public int? StandbyEntries6h { get; set; }
    public double? HoursSinceFlueSafety { get; set; }
    /// <summary>Cel domu użyty w modelu, cel z pokoi przed ograniczeniem wzrostu, źródło, dodatek na dogrzanie.</summary>
    public double? HouseTarget { get; set; }
    public double? HouseTargetRaw { get; set; }
    public string? HouseTargetSource { get; set; }
    public double StorageBonusSec { get; set; }
    /// <summary>Parametry przebiegu - progi w zdaniach (3 K, 185 °C …) zgodne z tym, co faktycznie działa.</summary>
    public HeatingParameters Parameters { get; set; } = new();
    public ControllerFeedSettingsSnapshot? Controller { get; set; }
}

/// <summary>Nastawy sterownika w chwili decyzji (bez zależności od Infrastructure w dzienniku).</summary>
public record ControllerFeedSettingsSnapshot(int Work, int Pause, int Blower);

/// <summary>
/// Zdania do dziennika decyzji Fazy 5 - po polsku: CO, Z ILU NA ILE, W KTÓRĄ STRONĘ (i co to
/// znaczy dla węgla) i DLACZEGO. Przykłady (prośba Pawła 2026-10-20):
/// „Postój 160 → 140 s (w dół = więcej węgla): średnia Tz z 3 h spadła z 4,1 do 1,2 °C,
/// potrzeba 4 900 s/dobę przy k 0,92; zapis potwierdzony w mapperze.”
/// „Dmuchawa 35 → 33 % (w dół): spaliny 204 °C &gt; 200.”
/// </summary>
public static class DoseLog
{
    public const string Work = "czas_pracy";
    public const string Pause = "postoj";
    public const string Blower = "dmuchawa";
    public const string K = "k";
    public const string Trim = "trym_postoju";
    public const string Automat = "automat";
    public const string HouseTargetParameter = "cel_domu";

    /// <summary>Przycisk „Ustaw sugerowane” (HA): zlecenie zapisuje API, nastawy zapisuje Console w najbliższym przebiegu.</summary>
    public const string ButtonParameter = "przycisk";
    public const string ButtonCode = "RECZNE_USTAW_SUGEROWANE";

    /// <summary>
    /// Wpis „wysłano na Discord nową sugestię” (automat wyłączony). Kolumny Controller* tego wpisu przechowują
    /// SUGEROWANE wartości (praca / postój / dmuchawa) - po nich rozpoznajemy, że daną sugestię już zgłoszono.
    /// </summary>
    public const string SuggestionParameter = "sugestia_discord";
    public const string SuggestionCode = "SUGESTIA_DISCORD";

    public const string StatusWritten = "ZAPISANE";
    public const string StatusUnconfirmed = "NIEPOTWIERDZONE";
    public const string StatusInternal = "WEWNETRZNE";
    public const string StatusBlocked = "ZABLOKOWANE";
    public const string StatusSuggestion = "SUGESTIA";
    public const string StatusWarning = "OSTRZEZENIE";
    /// <summary>Zlecenie z przycisku czeka na przebieg Console / obsłużone / wygasłe (nie obsłużone w ciągu 15 min).</summary>
    public const string StatusRequest = "ZLECENIE";
    public const string StatusRequestDone = "ZLECENIE_OBSL";
    public const string StatusRequestExpired = "ZLECENIE_WYGASLO";

    public static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static string ParameterName(string parameter) => parameter switch
    {
        Work => "Czas pracy",
        Pause => "Postój",
        Blower => "Dmuchawa",
        K => "k",
        Trim => "Trym postoju",
        HouseTargetParameter => "Cel domu",
        ButtonParameter => "Przycisk",
        _ => "Automat"
    };

    public static string Unit(string parameter) => parameter switch
    {
        Work or Pause or Trim => " s",
        Blower => " %",
        HouseTargetParameter => " °C",
        _ => ""
    };

    public static string Direction(double? oldValue, double? newValue)
        => oldValue == null || newValue == null || Math.Abs(newValue.Value - oldValue.Value) < 1e-9
            ? "bez zmiany"
            : newValue > oldValue ? "w górę" : "w dół";

    /// <summary>„w dół = więcej węgla” - co kierunek znaczy dla dawki.</summary>
    public static string DirectionMeaning(string parameter, string direction)
    {
        if (direction == "bez zmiany") return direction;
        var up = direction == "w górę";
        return parameter switch
        {
            Work or K or HouseTargetParameter => $"{direction} = {(up ? "więcej" : "mniej")} węgla",
            Pause or Trim => $"{direction} = {(up ? "mniej" : "więcej")} węgla",
            Blower => $"{direction} = {(up ? "więcej" : "mniej")} powietrza",
            _ => direction
        };
    }

    public static string Number(double? v, string format = "0.#") => v?.ToString(format, Pl) ?? "?";

    /// <summary>„Postój 160 → 140 s (w dół = więcej węgla)”.</summary>
    public static string Head(string parameter, double? oldValue, double? newValue)
    {
        var format = parameter == K ? "0.000" : parameter == HouseTargetParameter ? "0.0" : "0.#";
        var dir = Direction(oldValue, newValue);
        return $"{ParameterName(parameter)} {Number(oldValue, format)} → {Number(newValue, format)}{Unit(parameter)} ({DirectionMeaning(parameter, dir)})";
    }

    /// <summary>Dlaczego, wg kodu przyczyny - z danymi wejściowymi.</summary>
    public static string Why(string code, DoseLogContext c, string? extra = null)
    {
        var tz = c.OutdoorAtPreviousChange.HasValue && c.OutdoorSmoothed.HasValue
                 && Math.Abs(c.OutdoorAtPreviousChange.Value - c.OutdoorSmoothed.Value) >= 0.05
            ? $"średnia Tz z 3 h {(c.OutdoorSmoothed < c.OutdoorAtPreviousChange ? "spadła" : "wzrosła")} z {Number(c.OutdoorAtPreviousChange, "0.0")} do {Number(c.OutdoorSmoothed, "0.0")} °C"
            : $"średnia Tz z 3 h {Number(c.OutdoorSmoothed, "0.0")} °C";
        var target = c.HouseTarget.HasValue
            ? $", cel domu {Number(c.HouseTarget, "0.0")} °C ({c.HouseTargetSource})" +
              (c.StorageBonusSec > 0
                  ? $" rośnie do {Number(c.HouseTargetRaw, "0.0")} °C - w tym +{Number(c.StorageBonusSec, "#,##0")} s/dobę na dogrzanie domu"
                  : "")
            : "";
        var need = $"potrzeba {Number(c.NeedSecPerDay, "#,##0")} s/dobę przy k {Number(c.K, "0.00")}{target}";
        var trim = c.TrimSec != 0 ? $", trym {c.TrimSec.ToString("+0;-0", Pl)} s" : "";
        var boiler = $"kocioł {Number(c.BoilerTemp, "0.0")} °C przy nastawie {Number(c.BoilerSetpoint, "0")} °C";
        var p = c.Parameters;

        var text = code switch
        {
            "POGODA" => $"{tz}, {need}{trim}",
            "SUFIT" => $"kocioł nie nadąża - przy maks. pracy i min. postoju brakuje {Number(c.ShortfallPct, "0")} % ({tz}, {need})",
            "TRYM_KOCIOL_PONIZEJ_NASTAWY" => $"od {p.DoseTrimUnderpowerHours} h dmuchawa chodzi {Number(c.HeatingShare2hPct, "0")} % czasu, a {boiler} (poniżej nastawy − {Number(p.DoseTrimUnderpowerK, "0.#")} K)",
            "TRYM_PODTRZYMANIA" => $"{c.StandbyEntries6h} wejścia w podtrzymanie w ostatnich {p.DoseTrimStandbyHours} h, {boiler}",
            "SPALINY_200" => $"spaliny {Number(c.FlueTemp, "0")} °C > {p.DoseFlueMaxTemp}",
            "SPALINY_185" => $"spaliny {Number(c.FlueTemp, "0")} °C ≥ {p.DoseFlueNoRaiseTemp}",
            "PO_AUTOMATYZACJI_24" => $"bezpiecznik spalin #24 zadziałał {Number(c.HoursSinceFlueSafety, "0.#")} h temu ({p.DoseFlueSafetyHoldHours} h bez podnoszenia dmuchawy)",
            "PRZYWROCENIE_NASTAW" => "przywrócenie nastaw zastanych przy włączeniu automatu",
            "RECZNE_PRZYWROCENIE" => "zapis z przycisku nie potwierdził się w mapperze - przywracam wartości sprzed kliknięcia",
            ButtonCode => $"ręcznie przyciskiem „Ustaw sugerowane” w HA - to, co automat (przy włączeniu) ustawiłby teraz; {tz}, {need}{trim}",
            _ => ""
        };

        if (!string.IsNullOrEmpty(extra))
            text = string.IsNullOrEmpty(text) ? extra : $"{text}; {extra}";
        return text;
    }

    /// <summary>„; zapis potwierdzony w mapperze.” itp.</summary>
    public static string Confirmation(string? confirmation, int? seen) => confirmation switch
    {
        "OK" => "; zapis potwierdzony w mapperze.",
        "BRAK" => "; zapisu NIE potwierdzono - mapper nie odpowiada.",
        "ROZJAZD" => $"; zapisu NIE potwierdzono - sterownik ma {seen}.",
        "ODMOWA" => "; NIE wysłano - poza twardymi granicami.",
        _ => "."
    };

    /// <summary>Nowy wpis z danymi wejściowymi z kontekstu.</summary>
    public static FeedChangeLog Entry(DoseLogContext c, string parameter, double? oldValue, double? newValue,
        string code, string status, string message, string? confirmation = null)
        => new()
        {
            TimestampUtc = c.NowUtc,
            Parameter = parameter,
            OldValue = oldValue,
            NewValue = newValue,
            Direction = Direction(oldValue, newValue),
            ReasonCode = code.Length > 60 ? code[..60] : code,
            Status = status,
            Message = message.Length > 1000 ? message[..999] + "…" : message,
            MapperConfirmation = confirmation,
            AutomatActive = c.AutomatActive,
            OutdoorSmoothed = c.OutdoorSmoothed,
            OutdoorAtPreviousChange = c.OutdoorAtPreviousChange,
            ForecastNextHour = c.ForecastNextHour,
            K = c.K,
            NeedSecPerDay = c.NeedSecPerDay,
            HouseTarget = c.HouseTarget,
            HouseTargetSource = c.HouseTargetSource,
            StorageBonusSec = c.StorageBonusSec,
            TrimSec = c.TrimSec,
            BoilerTemp = c.BoilerTemp,
            BoilerSetpoint = c.BoilerSetpoint,
            FlueTemp = c.FlueTemp,
            HeatingShare2hPct = c.HeatingShare2hPct,
            StandbyEntries6h = c.StandbyEntries6h,
            ControllerWork = c.Controller?.Work,
            ControllerPause = c.Controller?.Pause,
            ControllerBlower = c.Controller?.Blower
        };

    /// <summary>Wpis dla zmiany jednej nastawy: „Postój 160 → 140 s (w dół = więcej węgla): …; zapis potwierdzony w mapperze.”</summary>
    public static FeedChangeLog Change(DoseLogContext c, string parameter, double oldValue, double newValue,
        string code, string status, string? extra = null, string? confirmation = null, int? seen = null)
        => Entry(c, parameter, oldValue, newValue, code, status,
            $"{Head(parameter, oldValue, newValue)}: {Why(code, c, extra)}{Confirmation(confirmation, seen)}", confirmation);
}
