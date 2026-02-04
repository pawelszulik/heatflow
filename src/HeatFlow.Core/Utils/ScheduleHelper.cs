using HeatFlow.Domain;

namespace HeatFlow.Core.Utils;

/// <summary>
/// Pomocnicze metody do obsługi harmonogramów czasowych.
/// </summary>
public static class ScheduleHelper
{
    /// <summary>
    /// Parsuje przedział czasowy z formatu "HH:MM-HH:MM".
    /// Zwraca tuple (start_minutes, end_minutes) lub null jeśli nieprawidłowy format.
    /// </summary>
    public static (int startMinutes, int endMinutes)? ParseTimeRange(string timeRangeStr)
    {
        if (string.IsNullOrWhiteSpace(timeRangeStr))
        {
            return null;
        }

        var parts = timeRangeStr.Trim().Split('-');
        if (parts.Length != 2)
        {
            return null;
        }

        try
        {
            var startParts = parts[0].Trim().Split(':');
            var endParts = parts[1].Trim().Split(':');

            if (startParts.Length != 2 || endParts.Length != 2)
            {
                return null;
            }

            var startHour = int.Parse(startParts[0]);
            var startMinute = int.Parse(startParts[1]);
            var endHour = int.Parse(endParts[0]);
            var endMinute = int.Parse(endParts[1]);

            // Walidacja zakresu
            if (startHour < 0 || startHour > 23 || startMinute < 0 || startMinute > 59)
            {
                return null;
            }
            if (endHour < 0 || endHour > 23 || endMinute < 0 || endMinute > 59)
            {
                return null;
            }

            var startMinutes = startHour * 60 + startMinute;
            var endMinutes = endHour * 60 + endMinute;

            return (startMinutes, endMinutes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parsuje harmonogram do listy przedziałów czasowych.
    /// Format: "HH:MM-HH:MM,HH:MM-HH:MM" lub "Brak"
    /// </summary>
    public static List<(int startMinutes, int endMinutes)> ParseScheduleRanges(string scheduleStr)
    {
        if (scheduleStr == "Brak" || string.IsNullOrWhiteSpace(scheduleStr))
        {
            return new List<(int, int)>();
        }

        var ranges = new List<(int, int)>();
        var timeRanges = scheduleStr.Split(',');

        foreach (var rangeStr in timeRanges)
        {
            var parsed = ParseTimeRange(rangeStr.Trim());
            if (parsed.HasValue)
            {
                ranges.Add(parsed.Value);
            }
        }

        return ranges;
    }

    /// <summary>
    /// Sprawdza czy aktualny czas (z opcjonalnym offsetem) jest w harmonogramie.
    /// </summary>
    public static bool IsTimeInRange(DateTime currentTime, Schedule schedule, bool isWeekend, int offsetMinutes = 0)
    {
        var activeSchedule = schedule.GetActiveSchedule(isWeekend);
        if (activeSchedule == "Brak")
        {
            return false;
        }

        var ranges = ParseScheduleRanges(activeSchedule);
        if (ranges.Count == 0)
        {
            return false;
        }

        var currentHour = currentTime.Hour;
        var currentMinute = currentTime.Minute;
        var currentMinutes = currentHour * 60 + currentMinute + offsetMinutes;

        // Normalizuj do zakresu 0-1439 (24h w minutach)
        currentMinutes = currentMinutes % (24 * 60);
        if (currentMinutes < 0)
        {
            currentMinutes += 24 * 60;
        }

        foreach (var (startMin, endMin) in ranges)
        {
            // Obsługa przejścia przez północ (np. 22:00-07:00)
            if (startMin > endMin)
            {
                // Przedział przechodzi przez północ
                if (currentMinutes >= startMin || currentMinutes <= endMin)
                {
                    return true;
                }
            }
            else
            {
                // Normalny przedział
                if (currentMinutes >= startMin && currentMinutes <= endMin)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
