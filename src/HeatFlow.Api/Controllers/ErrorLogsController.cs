using HeatFlow.Domain;
using HeatFlow.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;

namespace HeatFlow.Api.Controllers;

/// <summary>
/// Endpoint do odczytu dziennika błędów aplikacji (Console i Api).
/// Wymaga nagłówka X-API-Key jak reszta API.
/// </summary>
[ApiController]
[Route("api/error-logs")]
public class ErrorLogsController : ControllerBase
{
    private readonly IHeatFlowRepository _repo;

    public ErrorLogsController(IHeatFlowRepository repo)
    {
        _repo = repo;
    }

    /// <summary>
    /// Pobiera wpisy z dziennika błędów z opcjonalnymi filtrami.
    /// </summary>
    /// <param name="from">Data od (UTC).</param>
    /// <param name="to">Data do (UTC).</param>
    /// <param name="phase">Faza 0–5 (dla Console).</param>
    /// <param name="source">Komponent (np. Phase3ValvesService, Api.Unhandled).</param>
    /// <param name="origin">Console lub Api.</param>
    /// <param name="limit">Maks. liczba wpisów (1–500, domyślnie 100).</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApplicationErrorLog>>> Get(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? phase,
        [FromQuery] string? source,
        [FromQuery] string? origin,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        var list = await _repo.GetErrorLogsAsync(
            from, to, phase, source, origin, limit ?? 100, ct);
        return Ok(list);
    }
}
