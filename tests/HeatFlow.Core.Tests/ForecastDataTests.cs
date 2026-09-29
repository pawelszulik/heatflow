using HeatFlow.Domain;
using Xunit;

namespace HeatFlow.Core.Tests;

public class ForecastDataTests
{
    [Fact]
    public void GetMaxTemp_ShouldReturnMaxWithinWindow()
    {
        var now = DateTime.UtcNow;
        var data = BuildForecast(now, 10.0, 12.0, 18.5, 15.0);

        Assert.Equal(18.5, data.GetMaxTemp(24, now));
    }

    [Fact]
    public void GetMaxTemp_ShouldOnlyConsiderHoursWithinWindow()
    {
        // Godzina 3 jest cieplejsza, ale poza oknem 3h
        var now = DateTime.UtcNow;
        var data = BuildForecast(now, 10.0, 12.0, 11.0, 30.0);

        Assert.Equal(12.0, data.GetMaxTemp(3, now));
    }

    [Fact]
    public void GetMaxTemp_ShouldIgnorePastHours()
    {
        // Regresja: okno liczone od "teraz", nie od początku listy - inaczej krótkie okno
        // (np. dezaktywacja trybu lato, 6h) łapałoby godziny sprzed kilku godzin ze starszego
        // cache'u (ważny do 6h) zamiast najbliższej przyszłości.
        var now = DateTime.UtcNow;
        var data = new ForecastData
        {
            CurrentTemp = 5.0,
            ForecastHours = new List<ForecastHour>
            {
                new() { DateTime = now.AddHours(-2), Temperature = 30.0 }, // przeszłość - poza oknem
                new() { DateTime = now.AddHours(1), Temperature = 12.0 },
                new() { DateTime = now.AddHours(5), Temperature = 14.0 },
                new() { DateTime = now.AddHours(9), Temperature = 25.0 }, // poza oknem 6h
            }
        };

        Assert.Equal(14.0, data.GetMaxTemp(6, now));
    }

    [Fact]
    public void GetMaxTemp_ShouldIgnoreNullTemperatures()
    {
        var now = DateTime.UtcNow;
        var data = new ForecastData
        {
            CurrentTemp = 5.0,
            ForecastHours = new List<ForecastHour>
            {
                new() { DateTime = now, Temperature = null },
                new() { DateTime = now.AddHours(1), Temperature = 14.0 },
                new() { DateTime = now.AddHours(2), Temperature = null }
            }
        };

        Assert.Equal(14.0, data.GetMaxTemp(24, now));
    }

    [Fact]
    public void GetMaxTemp_HoursWithoutTimestamp_ShouldAlwaysCount()
    {
        // Zgodnie z dotychczasowym zachowaniem indeksowym: brak znacznika czasu = wchodzi
        // zawsze, niezależnie od długości okna.
        var now = DateTime.UtcNow;
        var data = new ForecastData
        {
            CurrentTemp = 5.0,
            ForecastHours = new List<ForecastHour> { new() { DateTime = null, Temperature = 19.0 } }
        };

        Assert.Equal(19.0, data.GetMaxTemp(6, now));
    }

    [Fact]
    public void GetMaxTemp_WhenNoTemperatures_ShouldReturnNull()
    {
        var now = DateTime.UtcNow;
        var empty = new ForecastData { CurrentTemp = 25.0 };
        var onlyNulls = new ForecastData
        {
            CurrentTemp = 25.0,
            ForecastHours = new List<ForecastHour> { new() { DateTime = now, Temperature = null } }
        };

        Assert.Null(empty.GetMaxTemp(24, now));
        Assert.Null(onlyNulls.GetMaxTemp(24, now));
    }

    private static ForecastData BuildForecast(DateTime now, params double[] temps)
    {
        return new ForecastData
        {
            CurrentTemp = temps[0],
            ForecastHours = temps
                .Select((t, i) => new ForecastHour { DateTime = now.AddHours(i), Temperature = t })
                .ToList()
        };
    }
}
