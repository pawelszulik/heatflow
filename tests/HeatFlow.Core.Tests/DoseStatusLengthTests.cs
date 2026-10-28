using System.Reflection;
using HeatFlow.Core.Dose;
using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace HeatFlow.Core.Tests;

/// <summary>
/// Statusy wpisów FeedChangeLog muszą się mieścić w kolumnie Status. 2026-10-28 „WSTRZYMANIE_PRZEKROCZONE” (24 znaki)
/// w nvarchar(20) wywracał zapis całego przebiegu (ExecutionHistory też), a testy na sztucznym repozytorium tego nie widziały.
/// </summary>
public class DoseStatusLengthTests
{
    [Fact]
    public void AllFeedChangeLogStatuses_FitStatusColumn()
    {
        var options = new DbContextOptionsBuilder<HeatFlowDbContext>().UseSqlServer("Server=.;Database=niepolaczone").Options;
        using var context = new HeatFlowDbContext(options);
        var maxLength = context.Model.FindEntityType(typeof(FeedChangeLog))!.FindProperty(nameof(FeedChangeLog.Status))!.GetMaxLength();
        Assert.NotNull(maxLength);

        var statuses = Constants(typeof(DoseLog), "Status")
            .Concat(Constants(typeof(Phase5DoseService), "Hold").Where(c => c.Name != "HoldParameter"))
            .ToList();
        Assert.Contains(statuses, c => c.Name == "HoldTimedOut");

        var tooLong = statuses.Where(c => c.Value.Length > maxLength).Select(c => $"{c.Name} = {c.Value} ({c.Value.Length})");
        Assert.Empty(tooLong);
    }

    private static IEnumerable<(string Name, string Value)> Constants(Type type, string prefix) =>
        type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!));
}
