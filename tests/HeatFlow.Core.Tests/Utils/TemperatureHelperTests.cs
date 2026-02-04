using HeatFlow.Core.Utils;
using Xunit;

namespace HeatFlow.Core.Tests.Utils;

public class TemperatureHelperTests
{
    [Fact]
    public void ValidateTemperature_WithinRange_ShouldReturnOriginal()
    {
        // Act
        var result = TemperatureHelper.ValidateTemperature(20.0, 0.0, 40.0);

        // Assert
        Assert.Equal(20.0, result);
    }

    [Fact]
    public void ValidateTemperature_BelowMin_ShouldReturnMin()
    {
        // Act
        var result = TemperatureHelper.ValidateTemperature(-5.0, 0.0, 40.0);

        // Assert
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void ValidateTemperature_AboveMax_ShouldReturnMax()
    {
        // Act
        var result = TemperatureHelper.ValidateTemperature(50.0, 0.0, 40.0);

        // Assert
        Assert.Equal(40.0, result);
    }

    [Fact]
    public void CalculateDeficit_WithValidTemps_ShouldReturnCorrect()
    {
        // Act
        var result = TemperatureHelper.CalculateDeficit(21.0, 19.0);

        // Assert
        Assert.Equal(2.0, result);
    }

    [Fact]
    public void CalculateDeficitWithBuffer_WithUsageSoon_ShouldAddBuffer()
    {
        // Act
        var result = TemperatureHelper.CalculateDeficitWithBuffer(1.0, 0.8, true);

        // Assert
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void CalculateDeficitWithBuffer_WithoutUsageSoon_ShouldNotAddBuffer()
    {
        // Act
        var result = TemperatureHelper.CalculateDeficitWithBuffer(1.0, 0.8, false);

        // Assert
        Assert.Equal(1.0, result);
    }
}
