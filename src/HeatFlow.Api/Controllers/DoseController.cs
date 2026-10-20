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

    /// <summary>Ile minut musi minąć między zleceniami z przycisku „Ustaw sugerowane”.</summary>
    public const int ApplyMinIntervalMinutes = 10;

    /// <summary>Sugestia jest świeża, gdy Faza 5 policzyła ją w ciągu tylu minut.</summary>
    public const int SuggestionFreshMinutes = 15;

    private static int? SuggestedWork(DoseState state)
        => state.AutomatActive ? state.PlannedWork : state.ShadowWork ?? state.PlannedWork;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var automat = await _repository.GetAutomatSwitchAsync(ct);
        var state = await _repository.GetStateAsync(ct) ?? new DoseState();
        var parameters = await _config.GetHeatingParametersAsync(ct);
        var recent = await _repository.GetChangeLogAsync(10, ct);

        return Ok(new
        {
            automatEnabled = automat.Enabled,
            automatChangedAtUtc = automat.ChangedAtUtc == default ? (DateTime?)null : automat.ChangedAtUtc,
            automatChangedBy = automat.ChangedBy,
            automatActive = state.AutomatActive,
            // Przy wyłączonym automacie sugestia = cień (to ustawi przycisk „Ustaw sugerowane”), nie surowy plan modelu.
            plannedWork = SuggestedWork(state),
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
            houseTarget = state.HouseTarget,
            houseTargetUsed = state.HouseTargetUsed,
            houseTargetSource = state.HouseTargetSource,
            houseTargetBase = state.HouseTargetBase,
            houseTargetActualRooms = state.HouseTargetActualRooms,
            storageBonusSec = state.StorageBonusSec,
            ceiling = state.Ceiling,
            shortfallPct = state.ShortfallPct,
            trimSec = state.TrimSec,
            lastCalcUtc = state.LastCalcUtc,
            lastChangeUtc = state.LastChangeUtc,
            lastKCorrectionDate = state.LastKCorrectionDate,
            lastReason = state.LastReason,
            lastSelfDisableReason = state.LastSelfDisableReason,
            lastSelfDisableUtc = state.LastSelfDisableUtc,
            // Dziennik decyzji: ostatni wpis w całości (sensor.heatflow_dawka_ostatnia_zmiana)
            // i 10 ostatnich zdań.
            lastChange = recent.FirstOrDefault(),
            recentChanges = recent.Select(e => $"{e.TimestampUtc:yyyy-MM-dd HH:mm} UTC [{e.Status}] {e.Message}").ToList()
        });
    }

    /// <summary>Dziennik decyzji Fazy 5 (FeedChangeLog), od najnowszego: co, z ilu na ile, dlaczego.</summary>
    [HttpGet("log")]
    public async Task<ActionResult<List<FeedChangeLog>>> GetLog([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        return Ok(await _repository.GetChangeLogAsync(Math.Clamp(limit, 1, 500), ct));
    }

    /// <summary>
    /// Przycisk „Ustaw sugerowane” (HA): zleca jednorazowy zapis sugerowanych nastaw do kotła mimo
    /// wyłączonego automatu. API tylko dopisuje zlecenie (wpis dziennika ze statusem ZLECENIE) -
    /// nastawy zapisuje HeatFlow.Console w najbliższym przebiegu (do 5 min), z tymi samymi ścianami
    /// bezpieczeństwa co automat i z potwierdzeniem w mapperze. Wynik: sensor.heatflow_dawka_ostatnia_zmiana
    /// i Discord.
    /// </summary>
    [HttpPost("apply-suggestion")]
    public async Task<IActionResult> PostApplySuggestion(CancellationToken ct)
    {
        try
        {
            var now = DateTime.UtcNow;
            var automat = await _repository.GetAutomatSwitchAsync(ct);
            if (automat.Enabled)
                return Conflict(new { error = "Automat dawki jest włączony i steruje sam - przycisk działa tylko przy wyłączonym automacie." });

            var state = await _repository.GetStateAsync(ct);
            var work = state == null ? null : SuggestedWork(state);
            if (state == null || work == null || state.PlannedPause == null || state.PlannedBlower == null)
                return Conflict(new { error = "Brak sugestii - Faza 5 jeszcze nie policzyła dawki." });

            var ageMin = (now - state.UpdatedAtUtc).TotalMinutes;
            if (ageMin > SuggestionFreshMinutes)
                return Conflict(new { error = $"Sugestia nieświeża - ostatni przebieg Fazy 5 był {ageMin:0} min temu (limit {SuggestionFreshMinutes} min)." });

            if (await _repository.AnyApplyRequestSinceAsync(now.AddMinutes(-ApplyMinIntervalMinutes), ct))
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { error = $"Przycisk był już użyty w ciągu ostatnich {ApplyMinIntervalMinutes} min - poczekaj." });

            var source = Request.Headers["X-Source"].FirstOrDefault() ?? "HA";
            var suggested = $"{work}/{state.PlannedPause} s, dmuchawa {state.PlannedBlower} %";
            var message = $"Zlecono przyciskiem w HA ({source}): ustaw sugerowane ({suggested}). " +
                          "Zapis w najbliższym przebiegu HeatFlow.Console (do 5 min), ze ścianami bezpieczeństwa jak przy automacie.";
            await _repository.AddApplyRequestAsync(new FeedChangeLog
            {
                TimestampUtc = now,
                Parameter = "przycisk",
                Direction = "bez zmiany",
                ReasonCode = "RECZNE_USTAW_SUGEROWANE",
                Status = "ZLECENIE",
                Message = message,
                AutomatActive = false,
                ControllerWork = state.ControllerWork,
                ControllerPause = state.ControllerPause,
                ControllerBlower = state.ControllerBlower
            }, ct);

            return Accepted(new { message, suggested });
        }
        catch (Exception ex)
        {
            await _errorLogger.LogAsync(ex, null, nameof(DoseController), new { Action = "PostApplySuggestion", Route = "api/dose/apply-suggestion" }, "Error", "Api", ct);
            return Problem(detail: ex.Message, statusCode: 500);
        }
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
