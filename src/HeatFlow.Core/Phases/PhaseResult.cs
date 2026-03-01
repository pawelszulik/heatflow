namespace HeatFlow.Core.Phases;

/// <summary>
/// Wynik wykonania fazy algorytmu.
/// </summary>
public class PhaseResult
{
    /// <summary>
    /// Numer fazy (0-5).
    /// </summary>
    public int PhaseNumber { get; set; }

    /// <summary>
    /// Czy wykonanie zakończyło się sukcesem.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Czas wykonania w milisekundach.
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// Komunikat błędu (jeśli wystąpił).
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Szczegóły wykonania (JSON lub tekst).
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Ostrzeżenia (lista komunikatów).
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Wyniki sterowania zaworami (wypełniane przez Phase3).
    /// </summary>
    public List<ValveResult> ValveResults { get; set; } = new();

    public static PhaseResult SuccessResult(int phaseNumber, long durationMs, string? details = null)
    {
        return new PhaseResult
        {
            PhaseNumber = phaseNumber,
            Success = true,
            DurationMs = durationMs,
            Details = details
        };
    }

    public static PhaseResult ErrorResult(int phaseNumber, string errorMessage, long durationMs = 0)
    {
        return new PhaseResult
        {
            PhaseNumber = phaseNumber,
            Success = false,
            DurationMs = durationMs,
            ErrorMessage = errorMessage
        };
    }
}
