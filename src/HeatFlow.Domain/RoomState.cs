namespace HeatFlow.Domain;

/// <summary>
/// Stan pokoju w danym momencie wykonania.
/// </summary>
public class RoomState
{
    public int Id { get; set; }
    public int ExecutionId { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public decimal TempActual { get; set; }
    public decimal TempTarget { get; set; }
    public decimal TempDeficit { get; set; }
    public int Classification { get; set; } // 0=Disabled, 1=Low, 2=Medium, 3=High
    public decimal Score { get; set; }
    public bool HeatingEnabled { get; set; }
    public DateTime RecordedAt { get; set; }
}
