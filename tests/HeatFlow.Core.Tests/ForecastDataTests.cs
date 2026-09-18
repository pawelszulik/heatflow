using HeatFlow.Domain;
using Xunit;

namespace HeatFlow.Core.Tests;

public class ForecastDataTests
{
    [Fact]
    public void GetMaxTemp_ShouldReturnMaxOfFirstNHours()
    {
        var data = BuildForecast(10.0, 12.0, 18.5, 15.0);

        Assert.Equal(18.5, data.GetMaxTemp(24));
    }

    [Fact]
    public void GetMaxTemp_ShouldOnlyConsiderFirstNHours()
    {
        // Godzina 3 jest cieplejsza, ale poza oknem 3h
        var data = BuildForecast(10.0, 12.0, 11.0, 30.0);

        Assert.Equal(12.0, data.GetMaxTemp(3));
    }

    [Fact]
    public void GetMaxTemp_ShouldIgnoreNullTemperatures()
    {
        var data = new ForecastData
        {
            CurrentTemp = 5.0,
            ForecastHours = new List<ForecastHour>
            {
                new() { Temperature = null },
                new() { Temperature = 14.0 },
                new() { Temperature = null }
            }
        };

        Assert.Equal(14.0, data.GetMaxTemp(24));
    }

    [Fact]
    public void GetMaxTemp_WhenNoTemperatures_ShouldReturnNull()
    {
        var empty = new ForecastData { CurrentTemp = 25.0 };
        var onlyNulls = new ForecastData
        {
            CurrentTemp = 25.0,
            ForecastHours = new List<ForecastHour> { new() { Temperature = null } }
        };

        Assert.Null(empty.GetMaxTemp(24));
        Assert.Null(onlyNulls.GetMaxTemp(24));
    }

    private static ForecastData BuildForecast(params double[] temps)
    {
        return new ForecastData
        {
            CurrentTemp = temps[0],
            ForecastHours = temps
                .Select((t, i) => new ForecastHour { DateTime = DateTime.UtcNow.AddHours(i), Temperature = t })
                .ToList()
        };
    }
}
