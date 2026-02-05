using System.Reflection;
using System.Text.Json;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace HeatFlow.Api.Controllers;

[ApiController]
[Route("api/heating-parameters")]
public class HeatingParametersController : ControllerBase
{
    private readonly IConfigurationService _config;
    private readonly IConfigurationAuditService _audit;
    private readonly IApplicationErrorLogger _errorLogger;

    public HeatingParametersController(IConfigurationService config, IConfigurationAuditService audit, IApplicationErrorLogger errorLogger)
    {
        _config = config;
        _audit = audit;
        _errorLogger = errorLogger;
    }

    [HttpGet]
    public async Task<ActionResult<HeatingParameters>> Get(CancellationToken ct)
    {
        var parameters = await _config.GetHeatingParametersAsync(ct);
        return Ok(parameters);
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] HeatingParameters body, CancellationToken ct)
    {
        HeatingParameters? oldParams = null;
        try { oldParams = await _config.GetHeatingParametersAsync(ct); } catch { /* ignore */ }

        try
        {
            await _config.SaveHeatingParametersAsync(body, ct);
            var source = Request.Headers["X-Source"].FirstOrDefault();
            try { await _audit.LogHeatingParametersChangesAsync(oldParams, body, source, ct); }
            catch (Exception ex)
            {
                await _errorLogger.LogAsync(ex, null, nameof(HeatingParametersController), new { Action = "Put", Route = "api/heating-parameters", Audit = true }, "Warning", "Api", ct);
            }
            return Ok(body);
        }
        catch (Exception ex)
        {
            await _errorLogger.LogAsync(ex, null, nameof(HeatingParametersController), new { Action = "Put", Route = "api/heating-parameters" }, "Error", "Api", ct);
            return Problem(detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPatch]
    public async Task<IActionResult> Patch(CancellationToken ct)
    {
        HeatingParameters? current;
        try { current = await _config.GetHeatingParametersAsync(ct); }
        catch (Exception ex)
        {
            await _errorLogger.LogAsync(ex, null, nameof(HeatingParametersController), new { Action = "Patch", Route = "api/heating-parameters", Step = "GetHeatingParameters" }, "Error", "Api", ct);
            return Problem(detail: "Nie można odczytać parametrów.", statusCode: 500);
        }
        if (current == null) return Problem(detail: "Brak parametrów.", statusCode: 404);

        JsonDocument? doc;
        try { doc = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct); }
        catch (Exception ex)
        {
            await _errorLogger.LogAsync(ex, null, nameof(HeatingParametersController), new { Action = "Patch", Route = "api/heating-parameters", Step = "ParseJson" }, "Error", "Api", ct);
            return BadRequest(new { error = "Nieprawidłowy JSON." });
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return BadRequest(new { error = "Oczekiwano obiektu JSON." });

            var updated = CloneHeatingParameters(current);
            ApplyPartialHeatingParameters(updated, root);

            var source = Request.Headers["X-Source"].FirstOrDefault();
            try
            {
                await _config.SaveHeatingParametersAsync(updated, ct);
                try { await _audit.LogHeatingParametersChangesAsync(current, updated, source, ct); }
                catch (Exception auditEx)
                {
                    await _errorLogger.LogAsync(auditEx, null, nameof(HeatingParametersController), new { Action = "Patch", Route = "api/heating-parameters", Audit = true }, "Warning", "Api", ct);
                }
                return Ok(updated);
            }
            catch (Exception ex)
            {
                await _errorLogger.LogAsync(ex, null, nameof(HeatingParametersController), new { Action = "Patch", Route = "api/heating-parameters" }, "Error", "Api", ct);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }
    }

    private static HeatingParameters CloneHeatingParameters(HeatingParameters src)
    {
        var t = typeof(HeatingParameters);
        var dest = (HeatingParameters)(Activator.CreateInstance(t) ?? throw new InvalidOperationException());
        foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite))
            prop.SetValue(dest, prop.GetValue(src));
        return dest;
    }

    private static void ApplyPartialHeatingParameters(HeatingParameters target, JsonElement json)
    {
        var t = typeof(HeatingParameters);
        foreach (var prop in json.EnumerateObject())
        {
            var pi = t.GetProperty(prop.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (pi == null || !pi.CanWrite) continue;
            try
            {
                if (pi.PropertyType == typeof(int)) pi.SetValue(target, prop.Value.GetInt32());
                else if (pi.PropertyType == typeof(double)) pi.SetValue(target, prop.Value.GetDouble());
            }
            catch { /* skip invalid */ }
        }
    }
}
