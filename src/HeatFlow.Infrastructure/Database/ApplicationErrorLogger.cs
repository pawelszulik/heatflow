using System.Collections;
using System.Text.Json;
using HeatFlow.Domain;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Infrastructure.Database;

/// <summary>
/// Zapisuje błędy do tabeli ApplicationErrorLog z pełną serializacją wyjątku (InnerException, Data).
/// Nie rzuca wyjątków – przy błędzie zapisu tylko loguje lub pomija.
/// </summary>
public class ApplicationErrorLogger : IApplicationErrorLogger
{
    private const int MaxMessageLength = 16 * 1024;   // 16 KB
    private const int MaxStackTraceLength = 16 * 1024;
    private const int MaxExceptionJsonLength = 64 * 1024; // 64 KB

    private readonly HeatFlowDbContext _context;
    private readonly ILogger<ApplicationErrorLogger>? _logger;

    public ApplicationErrorLogger(HeatFlowDbContext context, ILogger<ApplicationErrorLogger>? logger = null)
    {
        _context = context;
        _logger = logger;
    }

    public async Task LogAsync(Exception? ex, int? phase, string? source, object? context = null, string severity = "Error", string? origin = null, CancellationToken cancellationToken = default)
    {
        if (ex != null)
            _logger?.LogError(ex, "[{Origin}] {Source}: {Message}", origin ?? "", source ?? "", ex.Message);
        var message = ex?.Message ?? "";
        var exceptionType = ex?.GetType().FullName;
        var stackTrace = ex?.StackTrace;
        string? exceptionJson = null;
        if (ex != null)
        {
            try
            {
                exceptionJson = SerializeException(ex);
                if (exceptionJson.Length > MaxExceptionJsonLength)
                    exceptionJson = exceptionJson.Substring(0, MaxExceptionJsonLength) + "…";
            }
            catch (Exception serializationEx)
            {
                _logger?.LogWarning(serializationEx, "Nie udało się zserializować wyjątku do JSON");
                exceptionJson = JsonSerializer.Serialize(new { Type = exceptionType, Message = message, SerializationError = "Fallback" });
            }
        }
        await SaveAsync(
            Truncate(message, MaxMessageLength),
            exceptionType,
            stackTrace != null ? Truncate(stackTrace, MaxStackTraceLength) : null,
            exceptionJson,
            context,
            severity,
            origin,
            phase,
            source,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task LogAsync(string message, int? phase, string? source, object? context = null, string severity = "Error", string? origin = null, CancellationToken cancellationToken = default)
    {
        _logger?.LogError("[{Origin}] {Source}: {Message}", origin ?? "", source ?? "", message ?? "");
        await SaveAsync(
            Truncate(message ?? "", MaxMessageLength),
            null,
            null,
            null,
            context,
            severity,
            origin,
            phase,
            source,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveAsync(string message, string? exceptionType, string? stackTrace, string? exceptionJson, object? contextObj, string severity, string? origin, int? phase, string? source, CancellationToken cancellationToken)
    {
        string? contextJson = null;
        if (contextObj != null)
        {
            try
            {
                contextJson = contextObj is string s ? s : JsonSerializer.Serialize(contextObj);
            }
            catch (Exception serializationEx)
            {
                _logger?.LogDebug(serializationEx, "Serializacja kontekstu nie powiodła się, używam ToString()");
                contextJson = contextObj.ToString();
            }
        }

        var entry = new ApplicationErrorLog
        {
            OccurredAtUtc = DateTime.UtcNow,
            Source = source ?? "",
            Phase = phase,
            Message = message,
            ExceptionType = exceptionType != null ? Truncate(exceptionType, 500) : null,
            StackTrace = stackTrace,
            ExceptionJson = exceptionJson,
            ContextJson = contextJson,
            Severity = severity.Length > 20 ? severity.Substring(0, 20) : severity,
            Origin = origin != null && origin.Length > 50 ? origin.Substring(0, 50) : origin
        };

        try
        {
            _context.ApplicationErrorLogs.Add(entry);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Nie udało się zapisać wpisu do ApplicationErrorLog");
            // Nie rzucamy – logowanie nie może powalić aplikacji
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "…";
    }

    /// <summary>
    /// Serializuje wyjątek do JSON (łańcuch InnerException, Data, dla AggregateException – InnerExceptions).
    /// </summary>
    private static string SerializeException(Exception ex)
    {
        var dto = ExceptionDto.FromException(ex);
        return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>
    /// DTO do serializacji wyjątku (Type, Message, StackTrace, Data, InnerException / InnerExceptions).
    /// </summary>
    private sealed class ExceptionDto
    {
        public string? Type { get; set; }
        public string? Message { get; set; }
        public string? StackTrace { get; set; }
        public string? HelpLink { get; set; }
        public string? TargetSite { get; set; }
        public Dictionary<string, string>? Data { get; set; }
        public ExceptionDto? InnerException { get; set; }
        public List<ExceptionDto>? InnerExceptions { get; set; }

        public static ExceptionDto FromException(Exception? ex)
        {
            if (ex == null) return new ExceptionDto();
            var dto = new ExceptionDto
            {
                Type = ex.GetType().FullName,
                Message = ex.Message,
                StackTrace = ex.StackTrace,
                HelpLink = ex.HelpLink,
                TargetSite = ex.TargetSite?.ToString(),
                Data = GetDataDictionary(ex),
                InnerException = ex.InnerException != null ? FromException(ex.InnerException) : null
            };
            if (ex is AggregateException agg)
            {
                dto.InnerExceptions = agg.InnerExceptions?.Select(FromException).ToList();
            }
            return dto;
        }

        private static Dictionary<string, string>? GetDataDictionary(Exception ex)
        {
            if (ex.Data == null || ex.Data.Count == 0) return null;
            var dict = new Dictionary<string, string>();
            foreach (DictionaryEntry entry in ex.Data)
            {
                var key = entry.Key?.ToString() ?? "";
                var value = entry.Value?.ToString() ?? "";
                dict[key] = value;
            }
            return dict.Count == 0 ? null : dict;
        }
    }
}
