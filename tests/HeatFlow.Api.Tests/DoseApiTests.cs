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
        var controller = new DoseController(_doseRepoMock.Object, _configMock.Object, _errorLoggerMock.Object);

        var result = await controller.Get(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"automatEnabled\":false", json);
        Assert.Contains("\"k\":1", json);
    }
}
