namespace HeatFlow.Domain;

/// <summary>
/// Dane prognozy pogody z Home Assistant.
/// </summary>
public class ForecastData
{
    /// <summary>
    /// Aktualna temperatura zewnętrzna.
    /// </summary>
    public double CurrentTemp { get; set; }
    
    /// <summary>
    /// Lista prognoz godzinowych (z temperaturą).
    /// </summary>
    public List<ForecastHour> ForecastHours { get; set; } = new();
    
    /// <summary>
    /// Próg spadku temperatury dla aktywacji PRE-HEATING.
    /// </summary>
    public double TempDropThreshold { get; set; }
    
    /// <summary>
    /// Próg wzrostu temperatury dla aktywacji REDUCTION.
    /// </summary>
    public double TempRiseThreshold { get; set; }

    /// <summary>
    /// Zwraca minimalną temperaturę w ciągu najbliższych N godzin.
    /// </summary>
    public double GetMinTemp24h(int hoursCount)
    {
        if (ForecastHours.Count == 0)
        {
            return CurrentTemp;
        }

        var temps = ForecastHours
            .Take(hoursCount)
            .Where(f => f.Temperature.HasValue)
            .Select(f => f.Temperature!.Value)
            .ToList();

        return temps.Count > 0 ? temps.Min() : CurrentTemp;
    }

    /// <summary>
    /// Zwraca różnicę temperatury (min_24h - current).
    /// </summary>
    public double GetTempDiff(int hoursCount)
    {
        var minTemp = GetMinTemp24h(hoursCount);
        return minTemp - CurrentTemp;
    }
}

/// <summary>
/// Prognoza dla jednej godziny.
/// </summary>
public class ForecastHour
{
    public DateTime? DateTime { get; set; }
    public double? Temperature { get; set; }
}
