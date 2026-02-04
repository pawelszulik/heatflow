using HeatFlow.Domain;

namespace HeatFlow.Infrastructure.Configuration;

/// <summary>
/// Serwis zapisujący zmiany konfiguracji do audit logu.
/// </summary>
public interface IConfigurationAuditService
{
    /// <summary>
    /// Rejestruje zmiany w konfiguracji pokoju (porównanie starej i nowej wersji).
    /// </summary>
    Task LogRoomChangesAsync(string roomName, RoomConfiguration? oldValue, RoomConfiguration newValue, string? source = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejestruje zmiany w parametrach grzania (porównanie starej i nowej wersji).
    /// </summary>
    Task LogHeatingParametersChangesAsync(HeatingParameters? oldValue, HeatingParameters newValue, string? source = null, CancellationToken cancellationToken = default);
}
