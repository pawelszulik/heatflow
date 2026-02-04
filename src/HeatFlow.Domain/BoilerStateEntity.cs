namespace HeatFlow.Domain;

/// <summary>
/// Stan pieca w danym momencie wykonania.
/// </summary>
public class BoilerStateEntity
{
    public int Id { get; set; }
    public int ExecutionId { get; set; }
    public decimal TempExternal { get; set; }
    public decimal TempReturn { get; set; }
    public decimal TempTarget { get; set; }
    public decimal FeederTime { get; set; }
    public decimal Mixer4DPosition { get; set; }
    public int RoomsHeatedCount { get; set; }
    public int ForecastMode { get; set; }
    public DateTime RecordedAt { get; set; }
}
