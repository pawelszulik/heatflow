using HeatFlow.Domain;
using Microsoft.EntityFrameworkCore;

namespace HeatFlow.Infrastructure.Database;

/// <summary>
/// Implementacja repozytorium do zapisu stanów systemu grzania.
/// </summary>
public class HeatFlowRepository : IHeatFlowRepository
{
    private readonly HeatFlowDbContext _context;

    public HeatFlowRepository(HeatFlowDbContext context)
    {
        _context = context;
    }

    public async Task<int> SaveExecutionHistoryAsync(ExecutionHistory executionHistory, CancellationToken cancellationToken = default)
    {
        _context.ExecutionHistories.Add(executionHistory);
        await _context.SaveChangesAsync(cancellationToken);
        return executionHistory.Id;
    }

    public async Task SaveRoomStateAsync(RoomState roomState, CancellationToken cancellationToken = default)
    {
        _context.RoomStates.Add(roomState);
    }

    public async Task SaveBoilerStateAsync(BoilerStateEntity boilerState, CancellationToken cancellationToken = default)
    {
        _context.BoilerStates.Add(boilerState);
    }

    public async Task SaveValveStateAsync(ValveState valveState, CancellationToken cancellationToken = default)
    {
        _context.ValveStates.Add(valveState);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    // Metody konfiguracji

    public async Task<HeatingParametersEntity?> GetHeatingParametersAsync(CancellationToken cancellationToken = default)
    {
        return await _context.HeatingParameters.FindAsync(new object[] { 1 }, cancellationToken);
    }

    public async Task SaveHeatingParametersAsync(HeatingParametersEntity parameters, CancellationToken cancellationToken = default)
    {
        var existing = await _context.HeatingParameters.FindAsync(new object[] { 1 }, cancellationToken);
        if (existing != null)
        {
            // Aktualizuj istniejącą encję wartościami z parametrów
            var heatingParams = parameters.ToHeatingParameters();
            existing.UpdateFrom(heatingParams);
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            parameters.Id = 1;
            parameters.UpdatedAt = DateTime.UtcNow;
            _context.HeatingParameters.Add(parameters);
        }
    }

    public async Task<List<RoomConfiguration>> GetRoomConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.RoomConfigurations.ToListAsync(cancellationToken);
    }

    public async Task<RoomConfiguration?> GetRoomConfigurationAsync(string roomName, CancellationToken cancellationToken = default)
    {
        return await _context.RoomConfigurations.FindAsync(new object[] { roomName }, cancellationToken);
    }

    public async Task SaveRoomConfigurationAsync(RoomConfiguration roomConfig, CancellationToken cancellationToken = default)
    {
        var existing = await _context.RoomConfigurations.FindAsync(new object[] { roomConfig.Name }, cancellationToken);
        if (existing != null)
        {
            _context.Entry(existing).CurrentValues.SetValues(roomConfig);
        }
        else
        {
            _context.RoomConfigurations.Add(roomConfig);
        }
    }

    public async Task<SystemConfiguration?> GetSystemConfigurationAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SystemConfigurations.FindAsync(new object[] { 1 }, cancellationToken);
    }

    public async Task SaveSystemConfigurationAsync(SystemConfiguration systemConfig, CancellationToken cancellationToken = default)
    {
        var existing = await _context.SystemConfigurations.FindAsync(new object[] { 1 }, cancellationToken);
        if (existing != null)
        {
            _context.Entry(existing).CurrentValues.SetValues(systemConfig);
        }
        else
        {
            systemConfig.Id = 1;
            _context.SystemConfigurations.Add(systemConfig);
        }
    }

    public async Task<ForecastDataEntity?> GetForecastDataCacheAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        var latDecimal = (decimal)latitude;
        var lonDecimal = (decimal)longitude;

        return await _context.ForecastDataCache.OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(
                f => f.Latitude == latDecimal && f.Longitude == lonDecimal,
                cancellationToken);
    }

    public async Task SaveForecastDataCacheAsync(ForecastDataEntity forecastData, CancellationToken cancellationToken = default)
    {
        var existing = await _context.ForecastDataCache
            .FirstOrDefaultAsync(
                f => f.Latitude == forecastData.Latitude && f.Longitude == forecastData.Longitude,
                cancellationToken);

        if (existing != null)
        {
            // Aktualizuj istniejący rekord
            existing.CurrentTemp = forecastData.CurrentTemp;
            existing.ForecastHoursJson = forecastData.ForecastHoursJson;
            existing.TempDropThreshold = forecastData.TempDropThreshold;
            existing.TempRiseThreshold = forecastData.TempRiseThreshold;
            existing.UpdatedAt = DateTime.UtcNow;
            // CreatedAt pozostaje bez zmian - ważność cache liczy się od daty utworzenia
        }
        else
        {
            // Dodaj nowy rekord
            forecastData.CreatedAt = DateTime.UtcNow;
            forecastData.UpdatedAt = DateTime.UtcNow;
            _context.ForecastDataCache.Add(forecastData);
        }
    }

    public async Task<List<ConfigurationChangeLog>> GetConfigurationChangeLogsAsync(string? entityType = null, string? entityId = null, DateTime? from = null, DateTime? to = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        IQueryable<ConfigurationChangeLog> query = _context.ConfigurationChangeLogs.AsNoTracking();
        if (!string.IsNullOrEmpty(entityType)) query = query.Where(x => x.EntityType == entityType);
        if (!string.IsNullOrEmpty(entityId)) query = query.Where(x => x.EntityId == entityId);
        if (from.HasValue) query = query.Where(x => x.Timestamp >= from.Value);
        if (to.HasValue) query = query.Where(x => x.Timestamp <= to.Value);
        return await query.OrderByDescending(x => x.Timestamp).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<List<ApplicationErrorLog>> GetErrorLogsAsync(DateTime? from = null, DateTime? to = null, int? phase = null, string? source = null, string? origin = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        var effectiveLimit = Math.Clamp(limit, 1, 500);
        IQueryable<ApplicationErrorLog> query = _context.ApplicationErrorLogs.AsNoTracking();
        if (from.HasValue) query = query.Where(x => x.OccurredAtUtc >= from.Value);
        if (to.HasValue) query = query.Where(x => x.OccurredAtUtc <= to.Value);
        if (phase.HasValue) query = query.Where(x => x.Phase == phase.Value);
        if (!string.IsNullOrEmpty(source)) query = query.Where(x => x.Source == source);
        if (!string.IsNullOrEmpty(origin)) query = query.Where(x => x.Origin == origin);
        return await query.OrderByDescending(x => x.OccurredAtUtc).Take(effectiveLimit).ToListAsync(cancellationToken);
    }
}
