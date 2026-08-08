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

    /// <summary>
    /// Od kiedy pokój jest w obecnej klasyfikacji. Przenoszone z poprzedniego cyklu,
    /// gdy klasyfikacja się nie zmieniła - na tym opiera się dwell (anti-flap) w Fazie 2.
    /// </summary>
    public DateTime ClassificationSince { get; set; }
}
