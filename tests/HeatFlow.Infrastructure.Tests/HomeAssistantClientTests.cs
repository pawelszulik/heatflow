using HeatFlow.Domain;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HeatFlow.Infrastructure.Tests;

public class HomeAssistantClientTests
{
    private static readonly ILogger<HomeAssistantClient> NullLogger = NullLogger<HomeAssistantClient>.Instance;
    private static readonly IApplicationErrorLogger NoOpErrorLogger = new NoOpApplicationErrorLogger();
    [Fact]
    public async Task GetStateAsync_WithValidEntity_ShouldReturnState()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        handler.SetResponse(new EntityState
        {
            EntityId = "sensor.test",
            State = "21.5",
            Attributes = new Dictionary<string, object>()
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://test")
        };

        var client = new HomeAssistantClient(httpClient, "http://test", "test-token", NullLogger, NoOpErrorLogger);

        // Act
        var result = await client.GetStateAsync("sensor.test");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("sensor.test", result.EntityId);
        Assert.Equal("21.5", result.State);
    }

    [Fact]
    public async Task GetStateAsync_ShouldMapSnakeCaseEntityIdAndLastChanged()
    {
        // Arrange - surowa odpowiedź HA (snake_case, ISO-8601 z offsetem)
        var handler = new TestHttpMessageHandler();
        handler.SetResponse(new
        {
            entity_id = "switch.kociol_tryb_zima_lato",
            state = "on",
            attributes = new { },
            last_changed = "2026-09-18T07:12:34.123456+00:00",
            last_updated = "2026-09-18T07:12:34.123456+00:00"
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new HomeAssistantClient(httpClient, "http://test", "test-token", NullLogger, NoOpErrorLogger);

        // Act
        var result = await client.GetStateAsync("switch.kociol_tryb_zima_lato");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("switch.kociol_tryb_zima_lato", result.EntityId);
        Assert.Equal("on", result.State);
        Assert.NotEqual(default, result.LastChanged);
        var expected = new DateTime(2026, 9, 18, 7, 12, 34, DateTimeKind.Utc);
        Assert.Equal(expected, new DateTime(result.LastChanged.ToUniversalTime().Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("ON", true)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("off", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("unknown", null)]
    [InlineData("unavailable", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("foo", null)]
    public void ParseBoolState_ShouldMapValues(string? input, bool? expected)
    {
        Assert.Equal(expected, HomeAssistantClient.ParseBoolState(input));
    }

    [Fact]
    public async Task GetStateDoubleAsync_WithValidNumber_ShouldReturnDouble()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        handler.SetResponse(new EntityState
        {
            EntityId = "sensor.temp",
            State = "21.5",
            Attributes = new Dictionary<string, object>()
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://test")
        };

        var client = new HomeAssistantClient(httpClient, "http://test", "test-token", NullLogger, NoOpErrorLogger);

        // Act
        var result = await client.GetStateDoubleAsync("sensor.temp");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(21.5, result.Value);
    }

    [Fact]
    public async Task GetStateDoubleAsync_WithUnknownState_ShouldReturnNull()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        handler.SetResponse(new EntityState
        {
            EntityId = "sensor.temp",
            State = "unknown",
            Attributes = new Dictionary<string, object>()
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://test")
        };

        var client = new HomeAssistantClient(httpClient, "http://test", "test-token", NullLogger, NoOpErrorLogger);

        // Act
        var result = await client.GetStateDoubleAsync("sensor.temp");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SetNumberValueAsync_WithValidCall_ShouldReturnTrue()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        handler.SetResponse(HttpStatusCode.OK);

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://test")
        };

        var client = new HomeAssistantClient(httpClient, "http://test", "test-token", NullLogger, NoOpErrorLogger);

        // Act
        var result = await client.SetNumberValueAsync("number.test", 25.0);

        // Assert
        Assert.True(result);
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private HttpResponseMessage? _response;

        public void SetResponse(object? content)
        {
            if (content == null)
            {
                _response = new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            else
            {
                var json = JsonSerializer.Serialize(content);
                _response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
        }

        public void SetResponse(HttpStatusCode statusCode)
        {
            _response = new HttpResponseMessage(statusCode);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_response ?? new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class NoOpApplicationErrorLogger : IApplicationErrorLogger
    {
        public Task LogAsync(Exception? ex, int? phase, string? source, object? context = null, string severity = "Error", string? origin = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogAsync(string message, int? phase, string? source, object? context = null, string severity = "Error", string? origin = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
