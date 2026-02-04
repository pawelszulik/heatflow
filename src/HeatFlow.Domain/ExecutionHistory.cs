namespace HeatFlow.Domain;

/// <summary>
/// Historia wykonania faz algorytmu.
/// </summary>
public class ExecutionHistory
{
    public int Id { get; set; }
    public DateTime ExecutionTime { get; set; }
    public int Phase { get; set; } // 0-5
    public string Status { get; set; } = string.Empty; // Success/Error/Warning
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Details { get; set; } // JSON lub tekst
}
