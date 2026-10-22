using HeatFlow.Domain;
using Microsoft.EntityFrameworkCore;

namespace HeatFlow.Infrastructure.Database;

/// <summary>
/// Implementacja repozytorium logów trybu lato.
/// </summary>
public class SummerModeRepository : ISummerModeRepository
{
    private readonly HeatFlowDbContext _context;

    public SummerModeRepository(HeatFlowDbContext context)
    {
        _context = context;
    }

    public async Task<SummerModeLog?> GetLogForDateAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var dateOnly = date.Date;
        return await _context.SummerModeLogs
            .FirstOrDefaultAsync(x => x.Date == dateOnly, cancellationToken);
    }

    public async Task SaveLogAsync(SummerModeLog log, CancellationToken cancellationToken = default)
    {
        log.Date = log.Date.Date;
        var existing = await _context.SummerModeLogs
            .FirstOrDefaultAsync(x => x.Date == log.Date, cancellationToken);

        if (existing != null)
        {
            existing.WasActivated = log.WasActivated;
            existing.WasDeactivated = log.WasDeactivated;
            existing.ActivatedAt = log.ActivatedAt;
            existing.DeactivatedAt = log.DeactivatedAt;
        }
        else
        {
            _context.SummerModeLogs.Add(log);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<SummerModeSwitchState?> GetSwitchStateAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SummerModeSwitchStates.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<double?> GetBoilerSetpointBeforeSummerAsync(CancellationToken cancellationToken = default)
    {
        var row = await _context.SummerModeSwitchStates.FirstOrDefaultAsync(cancellationToken);
        return row?.BoilerSetpointBeforeSummer is { } v ? (double)v : null;
    }

    public async Task SaveBoilerSetpointBeforeSummerAsync(double? value, CancellationToken cancellationToken = default)
    {
        var row = await _context.SummerModeSwitchStates.FirstOrDefaultAsync(cancellationToken);
        if (row == null)
        {
            if (value == null) return;
            // Faza 4 zapisuje stan przełącznika przed aktywacją; wiersz zawsze już jest.
            row = new SummerModeSwitchState { Id = 1, SinceUtc = DateTime.UtcNow };
            _context.SummerModeSwitchStates.Add(row);
        }

        row.BoilerSetpointBeforeSummer = value.HasValue ? (decimal)value.Value : null;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveSwitchStateAsync(SummerModeSwitchState state, CancellationToken cancellationToken = default)
    {
        var existing = await _context.SummerModeSwitchStates.FirstOrDefaultAsync(cancellationToken);

        if (existing != null)
        {
            existing.IsSummer = state.IsSummer;
            existing.SinceUtc = state.SinceUtc;
        }
        else
        {
            state.Id = 1;
            _context.SummerModeSwitchStates.Add(state);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
