using HeatFlow.Domain;

namespace HeatFlow.Infrastructure.Configuration;

/// <summary>
/// Serwis do zarządzania konfiguracją systemu przechowywaną w bazie danych.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Pobiera parametry algorytmu z bazy danych.
    /// Jeśli baza jest pusta, zwraca wartości domyślne.
    /// </summary>
    Task<HeatingParameters> GetHeatingParametersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje lub aktualizuje parametry algorytmu w bazie danych.
    /// </summary>
    Task SaveHeatingParametersAsync(HeatingParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aktualizuje parametry algorytmu w bazie danych (używane przez Fazę 0).
    /// </summary>
    Task UpdateHeatingParametersAsync(HeatingParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pobiera wszystkie konfiguracje pokoi z bazy danych.
    /// </summary>
    Task<List<RoomConfiguration>> GetAllRoomsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pobiera konfigurację pokoju po nazwie.
    /// </summary>
    Task<RoomConfiguration?> GetRoomAsync(string roomName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje lub aktualizuje konfigurację pokoju w bazie danych.
    /// </summary>
    Task SaveRoomAsync(RoomConfiguration roomConfig, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pobiera konfigurację systemową z bazy danych.
    /// Jeśli baza jest pusta, zwraca domyślną konfigurację.
    /// </summary>
    Task<SystemConfiguration> GetSystemConfigurationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje lub aktualizuje konfigurację systemową w bazie danych.
    /// </summary>
    Task SaveSystemConfigurationAsync(SystemConfiguration systemConfig, CancellationToken cancellationToken = default);
}
