using HeatFlow.Infrastructure.HomeAssistant;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HeatFlow.Infrastructure.Tests;

public class HomeAssistantClientTests
{
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

        var client = new HomeAssistantClient(httpClient, "http://test", "test-token");

        // Act
        var result = await client.GetStateAsync("sensor.test");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("sensor.test", result.EntityId);
        Assert.Equal("21.5", result.State);
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

        var client = new HomeAssistantClient(httpClient, "http://test", "test-token");

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

        var client = new HomeAssistantClient(httpClient, "http://test", "test-token");

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

        var client = new HomeAssistantClient(httpClient, "http://test", "test-token");

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
}
