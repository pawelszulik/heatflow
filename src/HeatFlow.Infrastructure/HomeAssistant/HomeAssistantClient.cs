using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HeatFlow.Domain;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Infrastructure.HomeAssistant;

/// <summary>
/// Implementacja klienta Home Assistant API.
/// </summary>
public class HomeAssistantClient : IHomeAssistantClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _accessToken;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<HomeAssistantClient> _logger;
    private readonly IApplicationErrorLogger _errorLogger;

    public HomeAssistantClient(HttpClient httpClient, string baseUrl, string accessToken, ILogger<HomeAssistantClient> logger, IApplicationErrorLogger errorLogger)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _accessToken = accessToken;
        _logger = logger;
        _errorLogger = errorLogger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        _httpClient.BaseAddress = new Uri(_baseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    public async Task<EntityState?> GetStateAsync(string entityId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/states/{entityId}", cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var state = JsonSerializer.Deserialize<EntityState>(content, _jsonOptions);
            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Home Assistant GetState failed: {EntityId}", entityId);
            await _errorLogger.LogAsync(ex, null, "HomeAssistantClient.GetState", new { EntityId = entityId }, "Error", "Console", cancellationToken);
            return null;
        }
    }

    public async Task<string?> GetStateValueAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(entityId, cancellationToken);
        return state?.State;
    }

    public async Task<double?> GetStateDoubleAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var valueStr = await GetStateValueAsync(entityId, cancellationToken);
        if (string.IsNullOrWhiteSpace(valueStr) || valueStr == "unknown" || valueStr == "unavailable")
        {
            return null;
        }

        if (double.TryParse(valueStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return null;
    }

    public async Task<bool?> GetStateBoolAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var valueStr = await GetStateValueAsync(entityId, cancellationToken);
        if (string.IsNullOrWhiteSpace(valueStr) || valueStr == "unknown" || valueStr == "unavailable")
        {
            return null;
        }

        return valueStr.ToLowerInvariant() switch
        {
            "on" or "true" or "1" => true,
            "off" or "false" or "0" => false,
            _ => null
        };
    }

    public async Task<int?> GetStateIntAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var value = await GetStateDoubleAsync(entityId, cancellationToken);
        return value.HasValue ? (int?)Math.Round(value.Value) : null;
    }

    public async Task<bool> SetNumberValueAsync(string entityId, double value, CancellationToken cancellationToken = default)
    {
        return await CallServiceAsync("number", "set_value", new { entity_id = entityId, value }, cancellationToken);
    }

    public async Task<bool> SetInputNumberValueAsync(string entityId, double value, CancellationToken cancellationToken = default)
    {
        return await CallServiceAsync("input_number", "set_value", new { entity_id = entityId, value }, cancellationToken);
    }

    public async Task<bool> SetBooleanValueAsync(string entityId, bool value, CancellationToken cancellationToken = default)
    {
        var service = value ? "turn_on" : "turn_off";
        return await CallServiceAsync("input_boolean", service, new { entity_id = entityId }, cancellationToken);
    }

    public async Task<bool> SetClimateTemperatureAsync(string entityId, double temperature, CancellationToken cancellationToken = default)
    {
        return await CallServiceAsync("climate", "set_temperature", new { entity_id = entityId, temperature }, cancellationToken);
    }

    public async Task<bool> CallServiceAsync(string domain, string service, object? serviceData = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = serviceData != null 
                ? JsonSerializer.Serialize(serviceData, _jsonOptions)
                : "{}";

            var content = new StringContent(json, Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            var response = await _httpClient.PostAsync($"/api/services/{domain}/{service}", content, cancellationToken);
            var tmp = await response.Content.ReadAsStringAsync(cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Home Assistant CallService failed: {Domain}/{Service}", domain, service);
            await _errorLogger.LogAsync(ex, null, "HomeAssistantClient.CallService", new { Domain = domain, Service = service }, "Error", "Console", cancellationToken);
            return false;
        }
    }

    public async Task<bool> EntityExistsAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(entityId, cancellationToken);
        return state != null && state.State != "unknown" && state.State != "unavailable";
    }
}
