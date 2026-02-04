using HeatFlow.Core.Utils;
using HeatFlow.Domain;
using Xunit;

namespace HeatFlow.Core.Tests.Utils;

public class ScheduleHelperTests
{
    [Fact]
    public void IsTimeInRange_WithSimpleRange_ShouldReturnTrue()
    {
        // Arrange
        var schedule = Schedule.FromString("08:00-16:00");
        var currentTime = new DateTime(2024, 1, 15, 12, 0, 0); // 12:00

        // Act
        var result = ScheduleHelper.IsTimeInRange(currentTime, schedule, false);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsTimeInRange_WithMidnightCrossing_ShouldReturnTrue()
    {
        // Arrange
        var schedule = Schedule.FromString("22:00-07:00");
        var currentTime = new DateTime(2024, 1, 15, 23, 0, 0); // 23:00

        // Act
        var result = ScheduleHelper.IsTimeInRange(currentTime, schedule, false);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsTimeInRange_WithBrakSchedule_ShouldReturnFalse()
    {
        // Arrange
        var schedule = Schedule.FromString("Brak");
        var currentTime = new DateTime(2024, 1, 15, 12, 0, 0);

        // Act
        var result = ScheduleHelper.IsTimeInRange(currentTime, schedule, false);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ParseTimeRange_WithValidFormat_ShouldReturnCorrectValues()
    {
        // Act
        var result = ScheduleHelper.ParseTimeRange("08:30-16:45");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(510, result.Value.startMinutes); // 8*60 + 30
        Assert.Equal(1005, result.Value.endMinutes); // 16*60 + 45
    }

    [Fact]
    public void ParseTimeRange_WithNull_ShouldReturnNull()
    {
        // Act
        var result = ScheduleHelper.ParseTimeRange(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseTimeRange_WithEmpty_ShouldReturnNull()
    {
        // Act
        var result = ScheduleHelper.ParseTimeRange("");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseTimeRange_WithInvalidFormat_ShouldReturnNull()
    {
        // Act & Assert
        Assert.Null(ScheduleHelper.ParseTimeRange("invalid"));
        Assert.Null(ScheduleHelper.ParseTimeRange("08:00"));
        Assert.Null(ScheduleHelper.ParseTimeRange("08:00-16:00-20:00"));
        Assert.Null(ScheduleHelper.ParseTimeRange("25:00-16:00")); // Nieprawidłowa godzina
        Assert.Null(ScheduleHelper.ParseTimeRange("08:60-16:00")); // Nieprawidłowa minuta
    }

    [Fact]
    public void IsTimeInRange_WithPositiveOffset_ShouldShiftForward()
    {
        // Arrange
        var schedule = Schedule.FromString("09:00-17:00");
        var currentTime = new DateTime(2024, 1, 15, 8, 30, 0); // 08:30
        var offsetMinutes = 30; // Przesunięcie o 30 minut w przyszłość

        // Act
        var result = ScheduleHelper.IsTimeInRange(currentTime, schedule, false, offsetMinutes);

        // Assert
        // 08:30 + 30 min = 09:00, więc w zakresie
        Assert.True(result);
    }

    [Fact]
    public void IsTimeInRange_WithNegativeOffset_ShouldShiftBackward()
    {
        // Arrange
        var schedule = Schedule.FromString("08:00-17:00");
        var currentTime = new DateTime(2024, 1, 15, 8, 30, 0); // 08:30
        var offsetMinutes = -30; // Przesunięcie o 30 minut w przeszłość

        // Act
        var result = ScheduleHelper.IsTimeInRange(currentTime, schedule, false, offsetMinutes);

        // Assert
        // 08:30 - 30 min = 08:00, więc w zakresie
        Assert.True(result);
    }

    [Fact]
    public void IsTimeInRange_WithMultipleRanges_ShouldCheckAll()
    {
        // Arrange
        var schedule = Schedule.FromString("08:00-12:00,14:00-18:00");
        var currentTime1 = new DateTime(2024, 1, 15, 10, 0, 0); // 10:00 - w pierwszym zakresie
        var currentTime2 = new DateTime(2024, 1, 15, 15, 0, 0); // 15:00 - w drugim zakresie
        var currentTime3 = new DateTime(2024, 1, 15, 13, 0, 0); // 13:00 - poza zakresami

        // Act
        var result1 = ScheduleHelper.IsTimeInRange(currentTime1, schedule, false);
        var result2 = ScheduleHelper.IsTimeInRange(currentTime2, schedule, false);
        var result3 = ScheduleHelper.IsTimeInRange(currentTime3, schedule, false);

        // Assert
        Assert.True(result1);
        Assert.True(result2);
        Assert.False(result3);
    }

    [Fact]
    public void IsTimeInRange_WithExactStartBoundary_ShouldReturnTrue()
    {
        // Arrange
        var schedule = Schedule.FromString("08:00-16:00");
        var currentTime = new DateTime(2024, 1, 15, 8, 0, 0); // Dokładnie 08:00

        // Act
        var result = ScheduleHelper.IsTimeInRange(currentTime, schedule, false);

        // Assert
        // W kodzie: if (currentMinutes >= startMin && currentMinutes <= endMin)
        Assert.True(result);
    }

    [Fact]
    public void IsTimeInRange_WithExactEndBoundary_ShouldReturnTrue()
    {
        // Arrange
        var schedule = Schedule.FromString("08:00-16:00");
        var currentTime = new DateTime(2024, 1, 15, 16, 0, 0); // Dokładnie 16:00

        // Act
        var result = ScheduleHelper.IsTimeInRange(currentTime, schedule, false);

        // Assert
        // W kodzie: if (currentMinutes >= startMin && currentMinutes <= endMin)
        Assert.True(result);
    }

    [Fact]
    public void IsTimeInRange_WithMidnightCrossingAndOffset_ShouldHandleCorrectly()
    {
        // Arrange
        var schedule = Schedule.FromString("22:00-07:00");
        var currentTime = new DateTime(2024, 1, 15, 21, 30, 0); // 21:30
        var offsetMinutes = 30; // Przesunięcie o 30 minut w przyszłość

        // Act
        var result = ScheduleHelper.IsTimeInRange(currentTime, schedule, false, offsetMinutes);

        // Assert
        // 21:30 + 30 min = 22:00, więc w zakresie
        Assert.True(result);
    }

    [Fact]
    public void IsTimeInRange_WithWeekendSchedule_ShouldUseWeekendSchedule()
    {
        // Arrange
        var schedule = Schedule.FromString("08:00-16:00|09:00-17:00"); // Weekday|Weekend
        var currentTime = new DateTime(2024, 1, 14, 10, 0, 0); // Niedziela 10:00
        var isWeekend = true;

        // Act
        var result = ScheduleHelper.IsTimeInRange(currentTime, schedule, isWeekend);

        // Assert
        // Powinno użyć harmonogramu weekendowego (09:00-17:00)
        Assert.True(result);
    }

    [Fact]
    public void ParseScheduleRanges_WithMultipleRanges_ShouldParseAll()
    {
        // Act
        var result = ScheduleHelper.ParseScheduleRanges("08:00-12:00,14:00-18:00,20:00-22:00");

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal((480, 720), result[0]); // 08:00-12:00
        Assert.Equal((840, 1080), result[1]); // 14:00-18:00
        Assert.Equal((1200, 1320), result[2]); // 20:00-22:00
    }

    [Fact]
    public void ParseScheduleRanges_WithInvalidRanges_ShouldSkipInvalid()
    {
        // Act
        var result = ScheduleHelper.ParseScheduleRanges("08:00-12:00,invalid,14:00-18:00");

        // Assert
        // Powinno pominąć nieprawidłowy zakres
        Assert.Equal(2, result.Count);
        Assert.Equal((480, 720), result[0]); // 08:00-12:00
        Assert.Equal((840, 1080), result[1]); // 14:00-18:00
    }

    [Fact]
    public void ParseScheduleRanges_WithBrak_ShouldReturnEmpty()
    {
        // Act
        var result = ScheduleHelper.ParseScheduleRanges("Brak");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void IsTimeInRange_WithOffsetCrossingMidnight_ShouldNormalize()
    {
        // Arrange
        var schedule = Schedule.FromString("08:00-16:00");
        var currentTime = new DateTime(2024, 1, 15, 23, 30, 0); // 23:30
        var offsetMinutes = 30; // Przesunięcie o 30 minut w przyszłość

        // Act
        var result = ScheduleHelper.IsTimeInRange(currentTime, schedule, false, offsetMinutes);

        // Assert
        // 23:30 + 30 min = 24:00 = 00:00 (następny dzień), normalizowane do 00:00
        // 00:00 nie jest w zakresie 08:00-16:00
        Assert.False(result);
    }
}
