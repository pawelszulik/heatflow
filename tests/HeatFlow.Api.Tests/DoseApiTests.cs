using System.Text;
using HeatFlow.Api.Controllers;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HeatFlow.Api.Tests;

/// <summary>
/// Faza 5 „Dawka” od strony API: twarde granice parametrów (praca 7-10 s, postój 100-320 s,
/// dmuchawa 28-35 %) i wyłącznik automatu.
/// </summary>
public class DoseApiTests
{
    private readonly Mock<IConfigurationService> _configMock = new();
    private readonly Mock<IConfigurationAuditService> _auditMock = new();
    private readonly Mock<IApplicationErrorLogger> _errorLoggerMock = new();
    private readonly Mock<IDoseRepository> _doseRepoMock = new();

    public DoseApiTests()
    {
        _configMock.Setup(c => c.GetHeatingParametersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new HeatingParameters());
    }

    private HeatingParametersController BuildParamsController(string? body = null)
    {
        var context = new DefaultHttpContext();
        if (body != null) context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return new HeatingParametersController(_configMock.Object, _auditMock.Object, _errorLoggerMock.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    [Theory]
    [InlineData("{\"feederWorkMax\": 11}")]
    [InlineData("{\"feederPauseMin\": 90}")]
    [InlineData("{\"blowerMax\": 45}")]
    [InlineData("{\"feedCorrectionK\": 2.0}")]
    public async Task Patch_OutsideHardLimits_Returns400_AndSavesNothing(string body)
    {
        var result = await BuildParamsController(body).Patch(default);

        Assert.IsType<BadRequestObjectResult>(result);
        _configMock.Verify(c => c.SaveHeatingParametersAsync(It.IsAny<HeatingParameters>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Patch_WithinLimits_SavesIncludingBool()
    {
        var result = await BuildParamsController("{\"blowerMax\": 34, \"doseRestoreOnDisable\": false}").Patch(default);

        Assert.IsType<OkObjectResult>(result);
        _configMock.Verify(c => c.SaveHeatingParametersAsync(
            It.Is<HeatingParameters>(p => p.BlowerMax == 34 && !p.DoseRestoreOnDisable), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Put_OutsideHardLimits_Returns400()
    {
        var result = await BuildParamsController().Put(new HeatingParameters { FeederWorkMin = 5 }, default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task PutAutomat_StoresSwitch()
    {
        var controller = new DoseController(_doseRepoMock.Object, _configMock.Object, _errorLoggerMock.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.PutAutomat(new DoseController.AutomatDto(true), default);

        Assert.IsType<OkObjectResult>(result);
        _doseRepoMock.Verify(r => r.SetAutomatSwitchAsync(true, "HA", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_WithoutStateYet_ReturnsAutomatOffAndK()
    {
        _doseRepoMock.Setup(r => r.GetAutomatSwitchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DoseAutomatSwitch { Enabled = false });
        _doseRepoMock.Setup(r => r.GetStateAsync(It.IsAny<CancellationToken>())).ReturnsAsync((DoseState?)null);
        _doseRepoMock.Setup(r => r.GetChangeLogAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FeedChangeLog>
            {
                new() { Parameter = "postoj", OldValue = 160, NewValue = 140, Status = "ZAPISANE",
                        Message = "Postój 160 → 140 s (w dół = więcej węgla): …" }
            });
        var controller = new DoseController(_doseRepoMock.Object, _configMock.Object, _errorLoggerMock.Object);

        var result = await controller.Get(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"automatEnabled\":false", json);
        Assert.Contains("\"k\":1", json);
        var last = (FeedChangeLog?)ok.Value!.GetType().GetProperty("lastChange")!.GetValue(ok.Value);
        Assert.StartsWith("Postój 160 → 140 s", last!.Message);
    }

    // ---------- przycisk „Ustaw sugerowane” ----------

    private DoseController BuildDoseController() => new(_doseRepoMock.Object, _configMock.Object, _errorLoggerMock.Object)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    private void SetupFreshSuggestion(int minutesAgo = 2, bool automat = false, bool recentRequest = false)
    {
        _doseRepoMock.Setup(r => r.GetAutomatSwitchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DoseAutomatSwitch { Enabled = automat });
        _doseRepoMock.Setup(r => r.GetStateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new DoseState
        {
            PlannedWork = 9, ShadowWork = 7, PlannedPause = 140, PlannedBlower = 31,
            ControllerWork = 7, ControllerPause = 160, ControllerBlower = 35,
            UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-minutesAgo)
        });
        _doseRepoMock.Setup(r => r.AnyApplyRequestSinceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recentRequest);
        _doseRepoMock.Setup(r => r.AddApplyRequestAsync(It.IsAny<FeedChangeLog>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FeedChangeLog e, CancellationToken _) => e);
    }

    [Fact]
    public async Task ApplySuggestion_FreshSuggestionAutomatOff_AddsRequestWithShadowValues()
    {
        SetupFreshSuggestion();

        var result = await BuildDoseController().PostApplySuggestion(default);

        Assert.IsType<AcceptedResult>(result);
        _doseRepoMock.Verify(r => r.AddApplyRequestAsync(It.Is<FeedChangeLog>(e =>
            e.Parameter == "przycisk" && e.Status == "ZLECENIE" && e.ReasonCode == "RECZNE_USTAW_SUGEROWANE"
            && e.Message.Contains("7/140 s, dmuchawa 31 %")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplySuggestion_AutomatOn_Returns409_AndAddsNothing()
    {
        SetupFreshSuggestion(automat: true);

        var result = await BuildDoseController().PostApplySuggestion(default);

        Assert.IsType<ConflictObjectResult>(result);
        _doseRepoMock.Verify(r => r.AddApplyRequestAsync(It.IsAny<FeedChangeLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplySuggestion_StaleSuggestion_Returns409()
    {
        SetupFreshSuggestion(minutesAgo: 40);

        var result = await BuildDoseController().PostApplySuggestion(default);

        Assert.IsType<ConflictObjectResult>(result);
        _doseRepoMock.Verify(r => r.AddApplyRequestAsync(It.IsAny<FeedChangeLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplySuggestion_NoStateYet_Returns409()
    {
        _doseRepoMock.Setup(r => r.GetAutomatSwitchAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new DoseAutomatSwitch());
        _doseRepoMock.Setup(r => r.GetStateAsync(It.IsAny<CancellationToken>())).ReturnsAsync((DoseState?)null);

        var result = await BuildDoseController().PostApplySuggestion(default);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task ApplySuggestion_UsedWithin10Minutes_Returns429()
    {
        SetupFreshSuggestion(recentRequest: true);

        var result = await BuildDoseController().PostApplySuggestion(default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, obj.StatusCode);
        _doseRepoMock.Verify(r => r.AddApplyRequestAsync(It.IsAny<FeedChangeLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
