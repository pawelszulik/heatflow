using HeatFlow.Domain;
using HeatFlow.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HeatFlow.Infrastructure.Tests;

public class ApplicationErrorLoggerTests
{
    private static HeatFlowDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<HeatFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new HeatFlowDbContext(options);
    }

    [Fact]
    public async Task LogAsync_WithException_ShouldPersistEntryWithExceptionJsonAndMessage()
    {
        await using var context = CreateContext();
        var logger = new ApplicationErrorLogger(context);

        var ex = new InvalidOperationException("Test message");

        await logger.LogAsync(ex, 2, "TestSource", null, "Error", "Console");

        var entries = await context.ApplicationErrorLogs.AsNoTracking().ToListAsync();
        Assert.Single(entries);
        var e = entries[0];
        Assert.Equal("Test message", e.Message);
        Assert.Equal("System.InvalidOperationException", e.ExceptionType);
        Assert.Equal(2, e.Phase);
        Assert.Equal("TestSource", e.Source);
        Assert.Equal("Console", e.Origin);
        Assert.NotNull(e.ExceptionJson);
        Assert.Contains("InvalidOperationException", e.ExceptionJson);
        Assert.Contains("Test message", e.ExceptionJson);
    }

    [Fact]
    public async Task LogAsync_WithMessageOnly_ShouldPersistEntryWithoutExceptionType()
    {
        await using var context = CreateContext();
        var logger = new ApplicationErrorLogger(context);

        await logger.LogAsync("Błąd ręczny", null, "Program", new { X = 1 }, "Error", "Api");

        var entries = await context.ApplicationErrorLogs.AsNoTracking().ToListAsync();
        Assert.Single(entries);
        var e = entries[0];
        Assert.Equal("Błąd ręczny", e.Message);
        Assert.Null(e.ExceptionType);
        Assert.Null(e.ExceptionJson);
        Assert.NotNull(e.ContextJson);
        Assert.Contains("X", e.ContextJson);
        Assert.Equal("Api", e.Origin);
    }

    [Fact]
    public async Task LogAsync_WithInnerException_ShouldSerializeFullChain()
    {
        await using var context = CreateContext();
        var logger = new ApplicationErrorLogger(context);

        var inner = new ArgumentException("Inner");
        var ex = new InvalidOperationException("Outer", inner);

        await logger.LogAsync(ex, null, "Phase3", null, "Error", "Console");

        var entries = await context.ApplicationErrorLogs.AsNoTracking().ToListAsync();
        Assert.Single(entries);
        Assert.NotNull(entries[0].ExceptionJson);
        Assert.Contains("Outer", entries[0].ExceptionJson);
        Assert.Contains("Inner", entries[0].ExceptionJson);
        Assert.Contains("ArgumentException", entries[0].ExceptionJson);
    }

    [Fact]
    public async Task LogAsync_MultipleCalls_ShouldAddMultipleEntries()
    {
        await using var context = CreateContext();
        var logger = new ApplicationErrorLogger(context);

        await logger.LogAsync("First", null, "A", null, "Error", "Console");
        await logger.LogAsync(new ArgumentException("Second"), 1, "B", null, "Error", "Api");

        var entries = await context.ApplicationErrorLogs.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Null(entries[0].ExceptionType);
        Assert.Equal("Second", entries[1].Message);
        Assert.Equal("Api", entries[1].Origin);
    }
}
