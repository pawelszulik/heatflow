namespace HeatFlow.Domain;

/// <summary>
/// Wpis w dzienniku zmian konfiguracji (audit log).
/// </summary>
public class ConfigurationChangeLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string EntityType { get; set; } = string.Empty; // "Room" | "HeatingParameters"
    public string EntityId { get; set; } = string.Empty;   // nazwa pokoju lub "HeatingParameters"
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Source { get; set; } // np. "home_assistant", "api"
}
