using HeatFlow.Domain;
using HeatFlow.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;

namespace HeatFlow.Api.Controllers;

[ApiController]
[Route("api/configuration-changes")]
public class ConfigurationChangesController : ControllerBase
{
    private readonly IHeatFlowRepository _repo;

    public ConfigurationChangesController(IHeatFlowRepository repo)
    {
        _repo = repo;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ConfigurationChangeLog>>> Get(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        var list = await _repo.GetConfigurationChangeLogsAsync(
            entityType, entityId, from, to, limit ?? 100, ct);
        return Ok(list);
    }
}
