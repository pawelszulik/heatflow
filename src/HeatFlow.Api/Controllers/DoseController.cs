using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;

namespace HeatFlow.Api.Controllers;

/// <summary>
/// Faza 5 „Dawka”: stan automatu (encje sensor.heatflow_dawka_*, binary_sensor.heatflow_dawka_sufit)
/// i jego wyłącznik (switch.heatflow_dawka_automat). Przełącznik tylko zapisuje życzenie - nastawy
/// zapamiętuje, zmienia i przywraca HeatFlow.Console w najbliższym przebiegu (do 5 min).
/// </summary>
[ApiController]
[Route("api/dose")]
public class DoseController : ControllerBase
{
    private readonly IDoseRepository _repository;
    private readonly IConfigurationService _config;
    private readonly IApplicationErrorLogger _errorLogger;

    public DoseController(IDoseRepository repository, IConfigurationService config, IApplicationErrorLogger errorLogger)
    {
        _repository = repository;
        _config = config;
        _errorLogger = errorLogger;
    }

    public record AutomatDto(bool Enabled);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var automat = await _repository.GetAutomatSwitchAsync(ct);
        var state = await _repository.GetStateAsync(ct) ?? new DoseState();
        var parameters = await _config.GetHeatingParametersAsync(ct);

        return Ok(new
        {
            automatEnabled = automat.Enabled,
            automatChangedAtUtc = automat.ChangedAtUtc == default ? (DateTime?)null : automat.ChangedAtUtc,
            automatChangedBy = automat.ChangedBy,
            automatActive = state.AutomatActive,
            plannedWork = state.PlannedWork,
            plannedPause = state.PlannedPause,
            plannedBlower = state.PlannedBlower,
            modelPause = state.ModelPause,
            controllerWork = state.ControllerWork,
            controllerPause = state.ControllerPause,
            controllerBlower = state.ControllerBlower,
            savedWork = state.SavedWork,
            savedPause = state.SavedPause,
            savedBlower = state.SavedBlower,
            k = parameters.FeedCorrectionK,
            outdoorSmoothed = state.OutdoorSmoothed,
            needSecPerDay = state.NeedSecPerDay,
            ceiling = state.Ceiling,
            shortfallPct = state.ShortfallPct,
            trimSec = state.TrimSec,
            lastCalcUtc = state.LastCalcUtc,
            lastChangeUtc = state.LastChangeUtc,
            lastKCorrectionDate = state.LastKCorrectionDate,
            lastReason = state.LastReason,
            lastSelfDisableReason = state.LastSelfDisableReason,
            lastSelfDisableUtc = state.LastSelfDisableUtc
        });
    }

    [HttpPut("automat")]
    public async Task<IActionResult> PutAutomat([FromBody] AutomatDto body, CancellationToken ct)
    {
        try
        {
            var source = Request.Headers["X-Source"].FirstOrDefault() ?? "HA";
            await _repository.SetAutomatSwitchAsync(body.Enabled, source, ct);
            return Ok(new AutomatDto(body.Enabled));
        }
        catch (Exception ex)
        {
            await _errorLogger.LogAsync(ex, null, nameof(DoseController), new { Action = "PutAutomat", Route = "api/dose/automat" }, "Error", "Api", ct);
            return Problem(detail: ex.Message, statusCode: 500);
        }
    }
}
