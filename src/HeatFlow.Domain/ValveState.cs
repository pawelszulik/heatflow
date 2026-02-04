namespace HeatFlow.Domain;

/// <summary>
/// Stan zaworu w danym momencie wykonania.
/// </summary>
public class ValveState
{
    public int Id { get; set; }
    public int ExecutionId { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public string ValveEntityId { get; set; } = string.Empty;
    public decimal TempSet { get; set; }
    public decimal? TempActual { get; set; }
    public bool Success { get; set; }
    public int RetryCount { get; set; }
    public DateTime RecordedAt { get; set; }
}
