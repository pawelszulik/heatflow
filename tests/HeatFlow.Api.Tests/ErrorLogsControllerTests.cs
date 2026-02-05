using HeatFlow.Api.Controllers;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HeatFlow.Api.Tests;

public class ErrorLogsControllerTests
{
    [Fact]
    public async Task Get_WithNoParams_ReturnsOkAndListFromRepository()
    {
        var list = new List<ApplicationErrorLog>
        {
            new() { Id = 1, OccurredAtUtc = DateTime.UtcNow, Source = "Test", Message = "Err", Origin = "Console" }
        };
        var repoMock = new Mock<IHeatFlowRepository>();
        repoMock
            .Setup(r => r.GetErrorLogsAsync(null, null, null, null, null, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var controller = new ErrorLogsController(repoMock.Object);
        var result = await controller.Get(null, null, null, null, null, null, default);

        var okResult = Assert.IsType<ActionResult<IEnumerable<ApplicationErrorLog>>>(result);
        var ok = Assert.IsType<OkObjectResult>(okResult.Result);
        var body = Assert.IsAssignableFrom<IEnumerable<ApplicationErrorLog>>(ok.Value);
        Assert.Single(body);
        Assert.Equal("Err", body.First().Message);
    }

    [Fact]
    public async Task Get_WithFilters_PassesFiltersToRepository()
    {
        var from = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 2, 5, 23, 59, 59, DateTimeKind.Utc);
        var repoMock = new Mock<IHeatFlowRepository>();
        repoMock
            .Setup(r => r.GetErrorLogsAsync(from, to, 3, "Phase3ValvesService", "Console", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApplicationErrorLog>());

        var controller = new ErrorLogsController(repoMock.Object);
        await controller.Get(from, to, 3, "Phase3ValvesService", "Console", 50, default);

        repoMock.Verify(r => r.GetErrorLogsAsync(from, to, 3, "Phase3ValvesService", "Console", 50, It.IsAny<CancellationToken>()), Times.Once);
    }
}
