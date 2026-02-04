using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace HeatFlow.Api.Controllers;

[ApiController]
[Route("api/rooms")]
public class RoomsController : ControllerBase
{
    private readonly IConfigurationService _config;
    private readonly IConfigurationAuditService _audit;

    public RoomsController(IConfigurationService config, IConfigurationAuditService audit)
    {
        _config = config;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RoomConfiguration>>> GetAll(CancellationToken ct)
    {
        var rooms = await _config.GetAllRoomsAsync(ct);
        return Ok(rooms);
    }

    [HttpGet("{name}")]
    public async Task<ActionResult<RoomConfiguration>> GetByName(string name, CancellationToken ct)
    {
        var room = await _config.GetRoomAsync(name, ct);
        return room is null ? NotFound() : Ok(room);
    }

    [HttpPut("{name}")]
    public async Task<IActionResult> Put(string name, [FromBody] RoomConfiguration body, CancellationToken ct)
    {
        if (!string.Equals(name, body.Name, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Nazwa w URL musi być zgodna z Name w body." });

        RoomConfiguration? oldRoom = null;
        try { oldRoom = await _config.GetRoomAsync(name, ct); } catch { /* ignore */ }

        try
        {
            await _config.SaveRoomAsync(body, ct);
            var source = Request.Headers["X-Source"].FirstOrDefault();
            try { await _audit.LogRoomChangesAsync(name, oldRoom, body, source, ct); }
            catch (Exception ex) { await Console.Error.WriteLineAsync($"Audit: {ex.Message}"); }
            return Ok(body);
        }
        catch (Exception ex)
        {
            return Problem(detail: ex.Message, statusCode: 500);
        }
    }
}
