using HeatFlow.Domain;
using HeatFlow.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HeatFlow.Infrastructure.Tests;

public class HeatFlowRepositoryErrorLogsTests
{
    private static HeatFlowDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<HeatFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new HeatFlowDbContext(options);
    }

    [Fact]
    public async Task GetErrorLogsAsync_WithNoFilters_ReturnsAllOrderedByOccurredAtDesc()
    {
        await using var context = CreateContext();
        var baseTime = new DateTime(2026, 2, 5, 12, 0, 0, DateTimeKind.Utc);
        context.ApplicationErrorLogs.AddRange(
            new ApplicationErrorLog { OccurredAtUtc = baseTime.AddMinutes(-2), Source = "A", Message = "1", Origin = "Console" },
            new ApplicationErrorLog { OccurredAtUtc = baseTime.AddMinutes(-1), Source = "B", Message = "2", Origin = "Api" },
            new ApplicationErrorLog { OccurredAtUtc = baseTime, Source = "C", Message = "3", Origin = "Console" });
        await context.SaveChangesAsync();

        var repo = new HeatFlowRepository(context);
        var result = await repo.GetErrorLogsAsync(limit: 10);

        Assert.Equal(3, result.Count);
        Assert.Equal("3", result[0].Message);
        Assert.Equal("2", result[1].Message);
        Assert.Equal("1", result[2].Message);
    }

    [Fact]
    public async Task GetErrorLogsAsync_WithOriginFilter_ReturnsOnlyMatching()
    {
        await using var context = CreateContext();
        var baseTime = new DateTime(2026, 2, 5, 12, 0, 0, DateTimeKind.Utc);
        context.ApplicationErrorLogs.AddRange(
            new ApplicationErrorLog { OccurredAtUtc = baseTime, Source = "X", Message = "a", Origin = "Console" },
            new ApplicationErrorLog { OccurredAtUtc = baseTime.AddSeconds(1), Source = "Y", Message = "b", Origin = "Api" });
        await context.SaveChangesAsync();

        var repo = new HeatFlowRepository(context);
        var result = await repo.GetErrorLogsAsync(origin: "Api", limit: 10);

        Assert.Single(result);
        Assert.Equal("b", result[0].Message);
    }

    [Fact]
    public async Task GetErrorLogsAsync_WithPhaseFilter_ReturnsOnlyMatching()
    {
        await using var context = CreateContext();
        var baseTime = new DateTime(2026, 2, 5, 12, 0, 0, DateTimeKind.Utc);
        context.ApplicationErrorLogs.AddRange(
            new ApplicationErrorLog { OccurredAtUtc = baseTime, Source = "P0", Message = "x", Phase = 0 },
            new ApplicationErrorLog { OccurredAtUtc = baseTime.AddSeconds(1), Source = "P3", Message = "y", Phase = 3 });
        await context.SaveChangesAsync();

        var repo = new HeatFlowRepository(context);
        var result = await repo.GetErrorLogsAsync(phase: 3, limit: 10);

        Assert.Single(result);
        Assert.Equal("y", result[0].Message);
    }

    [Fact]
    public async Task GetErrorLogsAsync_WithLimit_RespectsLimit()
    {
        await using var context = CreateContext();
        var baseTime = new DateTime(2026, 2, 5, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 5; i++)
            context.ApplicationErrorLogs.Add(new ApplicationErrorLog { OccurredAtUtc = baseTime.AddMinutes(i), Source = "S", Message = i.ToString(), Origin = "Console" });
        await context.SaveChangesAsync();

        var repo = new HeatFlowRepository(context);
        var result = await repo.GetErrorLogsAsync(limit: 2);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetErrorLogsAsync_WithFromTo_FiltersByDate()
    {
        await using var context = CreateContext();
        var from = new DateTime(2026, 2, 5, 10, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 2, 5, 11, 0, 0, DateTimeKind.Utc);
        context.ApplicationErrorLogs.AddRange(
            new ApplicationErrorLog { OccurredAtUtc = from.AddMinutes(-10), Source = "S", Message = "old", Origin = "Console" },
            new ApplicationErrorLog { OccurredAtUtc = from.AddMinutes(30), Source = "S", Message = "mid", Origin = "Console" },
            new ApplicationErrorLog { OccurredAtUtc = to.AddMinutes(10), Source = "S", Message = "new", Origin = "Console" });
        await context.SaveChangesAsync();

        var repo = new HeatFlowRepository(context);
        var result = await repo.GetErrorLogsAsync(from: from, to: to, limit: 10);

        Assert.Single(result);
        Assert.Equal("mid", result[0].Message);
    }
}
