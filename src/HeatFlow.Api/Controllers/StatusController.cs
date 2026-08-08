using HeatFlow.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;

namespace HeatFlow.Api.Controllers;

/// <summary>
/// Stan zdrowia sterownika: kiedy ostatnio przebiegł, czy fazy się udały i czy zawory
/// odpowiedziały. Inaczej niż /api/health (który mówi tylko, że API odpowiada), ten
/// endpoint patrzy na HeatFlow.Console - czyli na to, co realnie steruje ogrzewaniem.
/// Wymaga nagłówka X-API-Key jak reszta API.
/// </summary>
[ApiController]
[Route("api/status")]
public class StatusController : ControllerBase
{
    /// <summary>Po tylu minutach bez przebiegu sterownik uznajemy za nieżywy.</summary>
    private const int ProgStaleMinut = 15;

    private readonly IHeatFlowRepository _repo;

    public StatusController(IHeatFlowRepository repo)
    {
        _repo = repo;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var fazy = await _repo.GetLastExecutionAsync(ct);

        if (fazy.Count == 0)
        {
            return Ok(new
            {
                status = "unknown",
                message = "Brak historii wykonania - sterownik nigdy nie zapisał przebiegu",
                lastRun = (DateTime?)null,
                minutesSinceLastRun = (double?)null
            });
        }

        var lastRun = fazy.Max(f => f.ExecutionTime);
        var minut = (DateTime.UtcNow - lastRun).TotalMinutes;
        var bledneFazy = fazy.Where(f => f.Status != "Success").Select(f => f.Phase).ToList();

        // Zawory z Fazy 3 tego samego przebiegu.
        var faza3 = fazy.FirstOrDefault(f => f.Phase == 3);
        var zawory = faza3 is null
            ? new List<Domain.ValveState>()
            : await _repo.GetValveStatesAsync(faza3.Id, ct);

        var zaworyBezOdpowiedzi = zawory.Where(v => !v.Success).Select(v => v.RoomName).ToList();

        // Faza 1 raportuje pokoje bez odczytu temperatury w polu Details.
        var faza1Details = fazy.FirstOrDefault(f => f.Phase == 1)?.Details ?? string.Empty;
        var slepePokoje = faza1Details.Contains("bez odczytu temperatury", StringComparison.OrdinalIgnoreCase);

        var status = bledneFazy.Count > 0 ? "error"
            : minut > ProgStaleMinut ? "stale"
            : "ok";

        return Ok(new
        {
            status,
            lastRun,
            minutesSinceLastRun = Math.Round(minut, 1),
            staleThresholdMinutes = ProgStaleMinut,
            phases = fazy.Select(f => new
            {
                phase = f.Phase,
                status = f.Status,
                durationMs = f.DurationMs,
                error = f.ErrorMessage,
                details = f.Details
            }),
            failedPhases = bledneFazy,
            valvesTotal = zawory.Count,
            valvesFailed = zaworyBezOdpowiedzi.Count,
            valvesFailedRooms = zaworyBezOdpowiedzi,
            roomsWithoutSensor = slepePokoje,
            phase1Details = faza1Details
        });
    }
}
