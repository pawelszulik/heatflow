namespace HeatFlow.Domain;

/// <summary>
/// Serwis zapisujący błędy aplikacji do tabeli ApplicationErrorLog (Console i Api).
/// Umożliwia szybką lokalizację błędu bez debugowania.
/// </summary>
public interface IApplicationErrorLogger
{
    /// <summary>
    /// Zapisuje błąd z wyjątkiem do bazy (Message, ExceptionType, StackTrace, ExceptionJson z łańcuchem InnerException i Data).
    /// </summary>
    Task LogAsync(Exception? ex, int? phase, string? source, object? context = null, string severity = "Error", string? origin = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje błąd bez wyjątku (tylko komunikat i kontekst).
    /// </summary>
    Task LogAsync(string message, int? phase, string? source, object? context = null, string severity = "Error", string? origin = null, CancellationToken cancellationToken = default);
}
