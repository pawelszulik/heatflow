namespace HeatFlow.Domain;

/// <summary>
/// Harmonogram czasowy (użytkowania lub grzania pokoju).
/// </summary>
public class Schedule
{
    /// <summary>
    /// Harmonogram dla dni roboczych (poniedziałek-piątek).
    /// Format: "HH:MM-HH:MM,HH:MM-HH:MM" lub "Brak"
    /// </summary>
    public string Weekday { get; set; } = "Brak";

    /// <summary>
    /// Harmonogram dla weekendu (sobota-niedziela).
    /// Format: "HH:MM-HH:MM,HH:MM-HH:MM" lub "Brak"
    /// </summary>
    public string Weekend { get; set; } = "Brak";

    /// <summary>
    /// Tworzy harmonogram z ciągu znaków.
    /// Format: "weekday|weekend" lub "Brak"
    /// </summary>
    public static Schedule FromString(string scheduleStr)
    {
        if (string.IsNullOrWhiteSpace(scheduleStr) || scheduleStr == "Brak")
        {
            return new Schedule { Weekday = "Brak", Weekend = "Brak" };
        }

        var parts = scheduleStr.Split('|');
        var weekday = parts.Length > 0 ? parts[0].Trim() : "Brak";
        var weekend = parts.Length > 1 ? parts[1].Trim() : weekday;

        return new Schedule { Weekday = weekday, Weekend = weekend };
    }

    /// <summary>
    /// Zwraca aktywny harmonogram dla danego dnia.
    /// </summary>
    public string GetActiveSchedule(bool isWeekend)
    {
        return isWeekend ? Weekend : Weekday;
    }
}
