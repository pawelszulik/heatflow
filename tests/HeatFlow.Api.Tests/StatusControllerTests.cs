using System.Text.Json;
using HeatFlow.Api.Controllers;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HeatFlow.Api.Tests;

public class StatusControllerTests
{
    private readonly Mock<IHeatFlowRepository> _repoMock = new();
    private readonly Mock<IConfigurationService> _configMock = new();

    private StatusController BuildController(bool systemEnabled)
    {
        _configMock
            .Setup(c => c.GetSystemConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemConfiguration { SystemEnabled = systemEnabled });
        _repoMock
            .Setup(r => r.GetValveStatesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValveState>());
        return new StatusController(_repoMock.Object, _configMock.Object);
    }

    private static List<ExecutionHistory> RunAt(DateTime time) => new()
    {
        new ExecutionHistory { Id = 1, Phase = 1, Status = "Success", ExecutionTime = time },
        new ExecutionHistory { Id = 2, Phase = 3, Status = "Success", ExecutionTime = time },
        new ExecutionHistory { Id = 3, Phase = 4, Status = "Success", ExecutionTime = time }
    };

    private static JsonElement Body(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonSerializer.SerializeToElement(ok.Value);
    }

    [Fact]
    public async Task Get_WhenSystemDisabled_ReturnsDisabledEvenIfStale()
    {
        // Sterownik nie zapisuje przebiegów przy wyłączonym systemie - stary wpis nie może dawać "stale"
        _repoMock
            .Setup(r => r.GetLastExecutionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RunAt(DateTime.UtcNow.AddHours(-5)));

        var body = Body(await BuildController(systemEnabled: false).Get(default));

        Assert.Equal("disabled", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("systemEnabled").GetBoolean());
    }

    [Fact]
    public async Task Get_WhenSystemDisabledAndNoHistory_ReturnsDisabled()
    {
        _repoMock
            .Setup(r => r.GetLastExecutionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExecutionHistory>());

        var body = Body(await BuildController(systemEnabled: false).Get(default));

        Assert.Equal("disabled", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Get_WhenEnabledAndFresh_ReturnsOk()
    {
        _repoMock
            .Setup(r => r.GetLastExecutionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RunAt(DateTime.UtcNow.AddMinutes(-1)));

        var body = Body(await BuildController(systemEnabled: true).Get(default));

        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("systemEnabled").GetBoolean());
    }

    [Fact]
    public async Task Get_WhenEnabledAndOld_ReturnsStale()
    {
        _repoMock
            .Setup(r => r.GetLastExecutionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RunAt(DateTime.UtcNow.AddMinutes(-30)));

        var body = Body(await BuildController(systemEnabled: true).Get(default));

        Assert.Equal("stale", body.GetProperty("status").GetString());
    }
}
