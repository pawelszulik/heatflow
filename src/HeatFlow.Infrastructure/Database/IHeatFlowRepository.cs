using HeatFlow.Domain;

namespace HeatFlow.Infrastructure.Database;

/// <summary>
/// Repozytorium do zapisu stanów systemu grzania.
/// </summary>
public interface IHeatFlowRepository
{
    /// <summary>
    /// Zapisuje historię wykonania fazy.
    /// </summary>
    Task<int> SaveExecutionHistoryAsync(ExecutionHistory executionHistory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje stan pokoju.
    /// </summary>
    Task SaveRoomStateAsync(RoomState roomState, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje stan pieca.
    /// </summary>
    Task SaveBoilerStateAsync(BoilerStateEntity boilerState, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje stan zaworu.
    /// </summary>
    Task SaveValveStateAsync(ValveState valveState, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje wszystkie zmiany.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // Metody konfiguracji

    /// <summary>
    /// Pobiera parametry algorytmu z bazy danych.
    /// </summary>
    Task<HeatingParametersEntity?> GetHeatingParametersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje lub aktualizuje parametry algorytmu w bazie danych.
    /// </summary>
    Task SaveHeatingParametersAsync(HeatingParametersEntity parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pobiera wszystkie konfiguracje pokoi z bazy danych.
    /// </summary>
    Task<List<RoomConfiguration>> GetRoomConfigurationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pobiera konfigurację pokoju po nazwie.
    /// </summary>
    Task<RoomConfiguration?> GetRoomConfigurationAsync(string roomName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje lub aktualizuje konfigurację pokoju w bazie danych.
    /// </summary>
    Task SaveRoomConfigurationAsync(RoomConfiguration roomConfig, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pobiera konfigurację systemową z bazy danych.
    /// </summary>
    Task<SystemConfiguration?> GetSystemConfigurationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje lub aktualizuje konfigurację systemową w bazie danych.
    /// </summary>
    Task SaveSystemConfigurationAsync(SystemConfiguration systemConfig, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pobiera cache prognozy pogody po współrzędnych geograficznych.
    /// </summary>
    Task<ForecastDataEntity?> GetForecastDataCacheAsync(double latitude, double longitude, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje lub aktualizuje cache prognozy pogody w bazie danych.
    /// </summary>
    Task SaveForecastDataCacheAsync(ForecastDataEntity forecastData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pobiera wpisy z dziennika zmian konfiguracji (audit log).
    /// </summary>
    Task<List<ConfigurationChangeLog>> GetConfigurationChangeLogsAsync(string? entityType = null, string? entityId = null, DateTime? from = null, DateTime? to = null, int limit = 100, CancellationToken cancellationToken = default);
}
