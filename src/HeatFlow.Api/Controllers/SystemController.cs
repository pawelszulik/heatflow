using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace HeatFlow.Api.Controllers;

/// <summary>
/// Włącznik całego systemu (SystemConfiguration.SystemEnabled). Po wyłączeniu HeatFlow.Console
/// pomija wszystkie fazy - zawory, mieszacz i tryb lato zostają w ostatnim stanie.
/// Używany przez integrację HA (switch "Sterowanie ogrzewaniem").
/// </summary>
[ApiController]
[Route("api/system")]
public class SystemController : ControllerBase
{
    private readonly IConfigurationService _config;
    private readonly IConfigurationAuditService _audit;
    private readonly IApplicationErrorLogger _errorLogger;

    public SystemController(IConfigurationService config, IConfigurationAuditService audit, IApplicationErrorLogger errorLogger)
    {
        _config = config;
        _audit = audit;
        _errorLogger = errorLogger;
    }

    public record SystemEnabledDto(bool Enabled);

    [HttpGet]
    public async Task<ActionResult<SystemEnabledDto>> Get(CancellationToken ct)
    {
        var systemConfig = await _config.GetSystemConfigurationAsync(ct);
        return Ok(new SystemEnabledDto(systemConfig.SystemEnabled));
    }

    [HttpPut("enabled")]
    public async Task<IActionResult> PutEnabled([FromBody] SystemEnabledDto body, CancellationToken ct)
    {
        SystemConfiguration current;
        try { current = await _config.GetSystemConfigurationAsync(ct); }
        catch (Exception ex)
        {
            await _errorLogger.LogAsync(ex, null, nameof(SystemController), new { Action = "PutEnabled", Route = "api/system/enabled", Step = "GetSystemConfiguration" }, "Error", "Api", ct);
            return Problem(detail: "Nie można odczytać konfiguracji systemowej.", statusCode: 500);
        }

        var updated = ShallowCopy.Of(current);
        updated.SystemEnabled = body.Enabled;

        var source = Request.Headers["X-Source"].FirstOrDefault();
        try
        {
            await _config.SaveSystemConfigurationAsync(updated, ct);
            try { await _audit.LogSystemConfigurationChangesAsync(current, updated, source, ct); }
            catch (Exception auditEx)
            {
                await _errorLogger.LogAsync(auditEx, null, nameof(SystemController), new { Action = "PutEnabled", Route = "api/system/enabled", Audit = true }, "Warning", "Api", ct);
            }
            return Ok(new SystemEnabledDto(updated.SystemEnabled));
        }
        catch (Exception ex)
        {
            await _errorLogger.LogAsync(ex, null, nameof(SystemController), new { Action = "PutEnabled", Route = "api/system/enabled" }, "Error", "Api", ct);
            return Problem(detail: ex.Message, statusCode: 500);
        }
    }
}
