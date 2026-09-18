using System.Text.Json.Serialization;

namespace HeatFlow.Infrastructure.HomeAssistant;

/// <summary>
/// Klient do komunikacji z Home Assistant API.
/// </summary>
public interface IHomeAssistantClient
{
    /// <summary>
    /// Odczytuje stan encji.
    /// </summary>
    Task<EntityState?> GetStateAsync(string entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Odczytuje wartość encji jako string.
    /// </summary>
    Task<string?> GetStateValueAsync(string entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Odczytuje wartość encji jako double.
    /// </summary>
    Task<double?> GetStateDoubleAsync(string entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Odczytuje wartość encji jako bool.
    /// </summary>
    Task<bool?> GetStateBoolAsync(string entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Odczytuje wartość encji jako int.
    /// </summary>
    Task<int?> GetStateIntAsync(string entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ustawia wartość encji typu number.
    /// </summary>
    Task<bool> SetNumberValueAsync(string entityId, double value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ustawia wartość encji typu input_number.
    /// </summary>
    Task<bool> SetInputNumberValueAsync(string entityId, double value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ustawia wartość encji typu boolean.
    /// </summary>
    Task<bool> SetBooleanValueAsync(string entityId, bool value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ustawia temperaturę na encji typu climate.
    /// </summary>
    Task<bool> SetClimateTemperatureAsync(string entityId, double temperature, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wywołuje serwis Home Assistant.
    /// </summary>
    Task<bool> CallServiceAsync(string domain, string service, object? serviceData = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sprawdza czy encja istnieje.
    /// </summary>
    Task<bool> EntityExistsAsync(string entityId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stan encji Home Assistant.
/// </summary>
public class EntityState
{
    // HA zwraca pola w snake_case; deserializacja ma tylko PropertyNameCaseInsensitive,
    // więc bez jawnych nazw EntityId/LastChanged/LastUpdated zostawałyby puste.
    [JsonPropertyName("entity_id")]
    public string EntityId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public Dictionary<string, object> Attributes { get; set; } = new();

    /// <summary>Kiedy HA ostatnio zmieniło wartość stanu (last_changed). Default = brak danych.</summary>
    [JsonPropertyName("last_changed")]
    public DateTime LastChanged { get; set; }

    [JsonPropertyName("last_updated")]
    public DateTime LastUpdated { get; set; }
}
