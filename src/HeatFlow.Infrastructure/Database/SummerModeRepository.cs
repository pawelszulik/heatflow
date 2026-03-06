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
}
