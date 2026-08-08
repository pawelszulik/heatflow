using HeatFlow.Domain;
using Xunit;

namespace HeatFlow.Core.Tests;

public class RoomTests
{
    [Fact]
    public void GetTargetTemperature_WithBrakSchedule_ShouldReturnTempTarget()
    {
        // Arrange
        var room = new Room
        {
            TempTarget = 21.0,
            TempTargetActive = 22.0,
            TempTargetInactive = 20.0,
            HeatingSchedule = Schedule.FromString("Brak")
        };

        // Act
        var result = room.GetTargetTemperature(true);

        // Assert
        // W kodzie jest return TempTarget na początku, więc zawsze zwraca TempTarget
        Assert.Equal(21.0, result);
    }

    [Fact]
    public void GetTargetTemperature_WithActiveHeating_ShouldReturnTempTargetActive()
    {
        // Arrange
        var room = new Room
        {
            TempTarget = 21.0,
            TempTargetActive = 22.0,
            TempTargetInactive = 20.0,
            HeatingSchedule = Schedule.FromString("08:00-16:00")
        };

        // Act
        var result = room.GetTargetTemperature(true);

        // Assert
        Assert.Equal(22.0, result); // TempTargetActive gdy isHeatingActive == true i harmonogram != Brak
    }

    [Fact]
    public void GetTargetTemperature_WithInactiveHeating_ShouldReturnTempTargetInactive()
    {
        // Arrange
        var room = new Room
        {
            TempTarget = 21.0,
            TempTargetActive = 22.0,
            TempTargetInactive = 20.0,
            HeatingSchedule = Schedule.FromString("08:00-16:00")
        };

        // Act
        var result = room.GetTargetTemperature(false);

        // Assert
        Assert.Equal(20.0, result); // TempTargetInactive gdy isHeatingActive == false i harmonogram != Brak
    }

    [Fact]
    public void ChangeTemperatureToSet_WithDisabled_ShouldSetMinimalTemperature()
    {
        // Arrange
        var room = new Room
        {
            TempTarget = 21.0,
            MinimalSetTemperature = 5.0,
            MaximalSetTemperature = 35.0,
            Score = -60.0 // Score < -50 dla Disabled
        };
        room.ClassifyDeficit(TestParameters.Default()); // Ustawi DeficitClassification na Disabled

        // Act
        room.ChangeTemperatureToSet();

        // Assert
        Assert.Equal(5, room.TemperatureToSet);
    }

    [Fact]
    public void ChangeTemperatureToSet_WithStay_ShouldSetActualTemperature()
    {
        // Arrange
        var room = new Room
        {
            TempTarget = 21.0,
            TempActual = 20.5,
            MinimalSetTemperature = 5.0,
            MaximalSetTemperature = 35.0,
            Score = 0.0 // Score w zakresie -50 do 50 dla Stay
        };
        room.ClassifyDeficit(TestParameters.Default()); // Ustawi DeficitClassification na Stay

        // Act
        room.ChangeTemperatureToSet();

        // Assert
        Assert.Equal(20, room.TemperatureToSet); // (int)20.5 = 20
    }

    [Fact]
    public void ChangeTemperatureToSet_WithStayAndNullTempActual_ShouldSetTempTarget()
    {
        // Arrange
        var room = new Room
        {
            TempTarget = 21.0,
            TempActual = null,
            MinimalSetTemperature = 5.0,
            MaximalSetTemperature = 35.0,
            Score = 0.0 // Score w zakresie -50 do 50 dla Stay
        };
        room.ClassifyDeficit(TestParameters.Default()); // Ustawi DeficitClassification na Stay

        // Act
        room.ChangeTemperatureToSet();

        // Assert
        Assert.Equal(21, room.TemperatureToSet); // Fallback do TempTarget
    }

    [Fact]
    public void ChangeTemperatureToSet_WithMax_ShouldSetMaximalTemperature()
    {
        // Arrange
        var room = new Room
        {
            TempTarget = 21.0,
            MinimalSetTemperature = 5.0,
            MaximalSetTemperature = 35.0,
            Score = 60.0 // Score > 50 dla Max
        };
        room.ClassifyDeficit(TestParameters.Default()); // Ustawi DeficitClassification na Max

        // Act
        room.ChangeTemperatureToSet();

        // Assert
        Assert.Equal(35, room.TemperatureToSet);
    }

    [Fact]
    public void ChangeTemperatureToSet_WithDefault_ShouldSetTempTarget()
    {
        // Arrange
        var room = new Room
        {
            TempTarget = 21.0,
            MinimalSetTemperature = 5.0,
            MaximalSetTemperature = 35.0
            // DeficitClassification pozostaje None (domyślna wartość)
        };

        // Act
        room.ChangeTemperatureToSet();

        // Assert
        Assert.Equal(21, room.TemperatureToSet);
    }

    [Fact]
    public void ClassifyDeficit_WithScoreGreaterThan50_ShouldSetMax()
    {
        // Arrange
        var room = new Room
        {
            Score = 60.0
        };

        // Act
        room.ClassifyDeficit(TestParameters.Default());

        // Assert
        Assert.Equal(DeficitClassification.Max, room.DeficitClassification);
    }

    [Fact]
    public void ClassifyDeficit_WithScoreLessThanMinus50_ShouldSetDisabled()
    {
        // Arrange
        var room = new Room
        {
            Score = -60.0
        };

        // Act
        room.ClassifyDeficit(TestParameters.Default());

        // Assert
        Assert.Equal(DeficitClassification.Disabled, room.DeficitClassification);
    }

    [Fact]
    public void ClassifyDeficit_WithScoreInRange_ShouldSetStay()
    {
        // Arrange
        var room = new Room
        {
            Score = 0.0
        };

        // Act
        room.ClassifyDeficit(TestParameters.Default());

        // Assert
        Assert.Equal(DeficitClassification.Stay, room.DeficitClassification);
    }

    [Fact]
    public void ClassifyDeficit_WithScoreExactly50_ShouldSetStay()
    {
        // Arrange
        var room = new Room
        {
            Score = 50.0
        };

        // Act
        room.ClassifyDeficit(TestParameters.Default());

        // Assert
        // W kodzie: if (Score > 50) Max, więc dokładnie 50 to Stay
        Assert.Equal(DeficitClassification.Stay, room.DeficitClassification);
    }

    [Fact]
    public void ClassifyDeficit_WithScoreExactlyMinus50_ShouldSetDisabled()
    {
        // Arrange
        var room = new Room
        {
            Score = -50.0
        };

        // Act
        room.ClassifyDeficit(TestParameters.Default());

        // Assert
        // W kodzie: if (Score < 0) Disabled, więc dokładnie -50 to Disabled
        Assert.Equal(DeficitClassification.Disabled, room.DeficitClassification);
    }

    [Fact]
    public void SetSafetyRoom_ShouldSetMaxAndMaximalTemperature()
    {
        // Arrange
        var room = new Room
        {
            TempTarget = 21.0,
            MinimalSetTemperature = 5.0,
            MaximalSetTemperature = 35.0,
            Score = 0.0
        };
        room.ClassifyDeficit(TestParameters.Default()); // Ustawi DeficitClassification na Stay

        // Act
        room.SetSafetyRoom();

        // Assert
        Assert.Equal(DeficitClassification.Max, room.DeficitClassification);
        Assert.Equal(35, room.TemperatureToSet);
    }
}
