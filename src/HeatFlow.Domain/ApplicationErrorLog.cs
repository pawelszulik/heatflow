namespace HeatFlow.Domain;

/// <summary>
/// Wpis w dzienniku błędów aplikacji (Console lub Api).
/// Własna tabela – bez rozszerzania ExecutionHistory – umożliwia szybką lokalizację błędu (gdzie, co, kontekst).
/// </summary>
public class ApplicationErrorLog
{
    public int Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    /// <summary>Komponent, w którym wystąpił błąd (np. Phase3ValvesService, RoomsController).</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>Faza 0–5 dla Console; null dla Api.</summary>
    public int? Phase { get; set; }
    /// <summary>Komunikat błędu (ex.Message).</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>Typ wyjątku (np. HttpRequestException).</summary>
    public string? ExceptionType { get; set; }
    /// <summary>Stack trace zewnętrznego wyjątku (może być obcięty).</summary>
    public string? StackTrace { get; set; }
    /// <summary>Pełna serializacja wyjątku do JSON (łańcuch InnerException, Data, typy).</summary>
    public string? ExceptionJson { get; set; }
    /// <summary>Kontekst biznesowy w JSON (np. RoomName, EntityId).</summary>
    public string? ContextJson { get; set; }
    /// <summary>Error lub Warning.</summary>
    public string Severity { get; set; } = "Error";
    /// <summary>Console lub Api – skąd wpis.</summary>
    public string? Origin { get; set; }
}
