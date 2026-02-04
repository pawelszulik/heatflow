using System.Reflection;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace HeatFlow.Infrastructure.Configuration;

/// <summary>
/// Implementacja serwisu audit logu – porównuje stare/nowe wartości i zapisuje zmiany.
/// </summary>
public class ConfigurationAuditService : IConfigurationAuditService
{
    private readonly HeatFlowDbContext _dbContext;

    public ConfigurationAuditService(HeatFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task LogRoomChangesAsync(string roomName, RoomConfiguration? oldValue, RoomConfiguration newValue, string? source = null, CancellationToken cancellationToken = default)
    {
        var entries = CompareObjects("Room", roomName, oldValue, newValue, source);
        await SaveEntriesAsync(entries, cancellationToken);
    }

    public async Task LogHeatingParametersChangesAsync(HeatingParameters? oldValue, HeatingParameters newValue, string? source = null, CancellationToken cancellationToken = default)
    {
        var entries = CompareObjects("HeatingParameters", "HeatingParameters", oldValue, newValue, source);
        await SaveEntriesAsync(entries, cancellationToken);
    }

    private static List<ConfigurationChangeLog> CompareObjects(string entityType, string entityId, object? oldObj, object newObj, string? source)
    {
        var list = new List<ConfigurationChangeLog>();
        var type = newObj.GetType();
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.Name != nameof(ConfigurationChangeLog.Id));

        foreach (var prop in props)
        {
            object? oldVal = null;
            object? newVal = null;
            try
            {
                if (oldObj != null) oldVal = prop.GetValue(oldObj);
                newVal = prop.GetValue(newObj);
            }
            catch
            {
                continue;
            }

            var oldStr = ToStringValue(oldVal);
            var newStr = ToStringValue(newVal);
            if (oldStr == newStr) continue;

            list.Add(new ConfigurationChangeLog
            {
                Timestamp = DateTime.UtcNow,
                EntityType = entityType,
                EntityId = entityId,
                FieldName = prop.Name,
                OldValue = oldStr,
                NewValue = newStr,
                Source = source
            });
        }

        return list;
    }

    private static string? ToStringValue(object? value)
    {
        if (value == null) return null;
        if (value is string s) return s;
        if (value is DateTime dt) return dt.ToString("O");
        return value.ToString();
    }

    private async Task SaveEntriesAsync(List<ConfigurationChangeLog> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return;
        await _dbContext.ConfigurationChangeLogs.AddRangeAsync(entries, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
