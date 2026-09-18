using HeatFlow.Api.Controllers;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HeatFlow.Api.Tests;

public class SystemControllerTests
{
    private readonly Mock<IConfigurationService> _configMock = new();
    private readonly Mock<IConfigurationAuditService> _auditMock = new();
    private readonly Mock<IApplicationErrorLogger> _errorLoggerMock = new();

    private SystemController BuildController()
    {
        var controller = new SystemController(_configMock.Object, _auditMock.Object, _errorLoggerMock.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Headers["X-Source"] = "test";
        return controller;
    }

    [Fact]
    public async Task Get_ReturnsEnabledFlag()
    {
        _configMock
            .Setup(c => c.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemConfiguration { SystemEnabled = false });

        var result = await BuildController().Get(default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<SystemController.SystemEnabledDto>(ok.Value);
        Assert.False(dto.Enabled);
    }

    [Fact]
    public async Task PutEnabled_SavesFlippedFlagAndLogsAudit()
    {
        var current = new SystemConfiguration { SystemEnabled = true, RoomsList = "salon", EkoPiecDeviceSn = "SN1" };
        _configMock
            .Setup(c => c.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);

        var result = await BuildController().PutEnabled(new SystemController.SystemEnabledDto(false), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.False(Assert.IsType<SystemController.SystemEnabledDto>(ok.Value).Enabled);

        // Zapis kopii z wyłączonym systemem, reszta pól bez zmian
        _configMock.Verify(c => c.SaveSystemConfigurationAsync(
            It.Is<SystemConfiguration>(s => !s.SystemEnabled && s.RoomsList == "salon" && s.EkoPiecDeviceSn == "SN1"),
            It.IsAny<CancellationToken>()), Times.Once);

        // Oryginał nietknięty -> audyt widzi True -> False
        Assert.True(current.SystemEnabled);
        _auditMock.Verify(a => a.LogSystemConfigurationChangesAsync(
            It.Is<SystemConfiguration>(s => s.SystemEnabled),
            It.Is<SystemConfiguration>(s => !s.SystemEnabled),
            "test",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PutEnabled_WhenSaveThrows_Returns500()
    {
        _configMock
            .Setup(c => c.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemConfiguration { SystemEnabled = true });
        _configMock
            .Setup(c => c.SaveSystemConfigurationAsync(It.IsAny<SystemConfiguration>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB down"));

        var result = await BuildController().PutEnabled(new SystemController.SystemEnabledDto(false), default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
        _auditMock.Verify(a => a.LogSystemConfigurationChangesAsync(
            It.IsAny<SystemConfiguration?>(), It.IsAny<SystemConfiguration>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PutEnabled_WhenAuditThrows_StillReturnsOk()
    {
        _configMock
            .Setup(c => c.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemConfiguration { SystemEnabled = false });
        _auditMock
            .Setup(a => a.LogSystemConfigurationChangesAsync(
                It.IsAny<SystemConfiguration?>(), It.IsAny<SystemConfiguration>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit down"));

        var result = await BuildController().PutEnabled(new SystemController.SystemEnabledDto(true), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.True(Assert.IsType<SystemController.SystemEnabledDto>(ok.Value).Enabled);
        _errorLoggerMock.Verify(e => e.LogAsync(It.IsAny<Exception?>(), null, nameof(SystemController),
            It.IsAny<object?>(), "Warning", "Api", It.IsAny<CancellationToken>()), Times.Once);
    }
}
