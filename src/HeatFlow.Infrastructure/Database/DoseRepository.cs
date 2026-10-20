using HeatFlow.Domain;
using Microsoft.EntityFrameworkCore;

namespace HeatFlow.Infrastructure.Database;

/// <summary>
/// Implementacja repozytorium Fazy 5 „Dawka”.
/// </summary>
public class DoseRepository : IDoseRepository
{
    private readonly HeatFlowDbContext _context;

    public DoseRepository(HeatFlowDbContext context)
    {
        _context = context;
    }

    public async Task<DoseAutomatSwitch> GetAutomatSwitchAsync(CancellationToken cancellationToken = default)
    {
        // AsNoTracking: przełącznik zmienia API (inny proces) - zawsze świeży odczyt z bazy.
        return await _context.DoseAutomatSwitches.AsNoTracking().FirstOrDefaultAsync(cancellationToken)
               ?? new DoseAutomatSwitch { Id = 1, Enabled = false };
    }

    public async Task SetAutomatSwitchAsync(bool enabled, string changedBy, CancellationToken cancellationToken = default)
    {
        var existing = await _context.DoseAutomatSwitches.FirstOrDefaultAsync(cancellationToken);
        if (existing == null)
        {
            _context.DoseAutomatSwitches.Add(new DoseAutomatSwitch
            {
                Id = 1,
                Enabled = enabled,
                ChangedAtUtc = DateTime.UtcNow,
                ChangedBy = changedBy
            });
        }
        else if (existing.Enabled != enabled)
        {
            existing.Enabled = enabled;
            existing.ChangedAtUtc = DateTime.UtcNow;
            existing.ChangedBy = changedBy;
        }
        else
        {
            return;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<DoseState?> GetStateAsync(CancellationToken cancellationToken = default)
    {
        return await _context.DoseStates.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task SaveStateAsync(DoseState state, CancellationToken cancellationToken = default)
    {
        state.Id = 1;
        state.UpdatedAtUtc = DateTime.UtcNow;
        var existing = await _context.DoseStates.FirstOrDefaultAsync(cancellationToken);
        if (existing == null)
        {
            _context.DoseStates.Add(state);
        }
        else if (!ReferenceEquals(existing, state))
        {
            _context.Entry(existing).CurrentValues.SetValues(state);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddSampleAsync(DoseSample sample, CancellationToken cancellationToken = default)
    {
        _context.DoseSamples.Add(sample);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<DoseSample>> GetSamplesSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        return await _context.DoseSamples
            .AsNoTracking()
            .Where(s => s.TimestampUtc >= sinceUtc)
            .OrderBy(s => s.TimestampUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteSamplesOlderThanAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
    {
        await _context.DoseSamples
            .Where(s => s.TimestampUtc < olderThanUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task AddChangeLogAsync(IEnumerable<FeedChangeLog> entries, CancellationToken cancellationToken = default)
    {
        var list = entries.ToList();
        if (list.Count == 0) return;
        _context.FeedChangeLogs.AddRange(list);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<FeedChangeLog>> GetChangeLogAsync(int limit, CancellationToken cancellationToken = default)
    {
        return await _context.FeedChangeLogs
            .AsNoTracking()
            .OrderByDescending(e => e.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<FeedChangeLog> AddApplyRequestAsync(FeedChangeLog request, CancellationToken cancellationToken = default)
    {
        _context.FeedChangeLogs.Add(request);
        await _context.SaveChangesAsync(cancellationToken);
        return request;
    }

    public async Task<bool> AnyApplyRequestSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        return await _context.FeedChangeLogs.AsNoTracking()
            .AnyAsync(e => e.Parameter == "przycisk" && e.ReasonCode == "RECZNE_USTAW_SUGEROWANE"
                           && e.TimestampUtc >= sinceUtc, cancellationToken);
    }

    public async Task<FeedChangeLog?> GetPendingApplyRequestAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        // Tracking: status zmienia ta sama instancja w SetApplyRequestStatusAsync.
        return await _context.FeedChangeLogs
            .Where(e => e.Parameter == "przycisk" && e.Status == "ZLECENIE" && e.TimestampUtc >= sinceUtc)
            .OrderByDescending(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> ExpireApplyRequestsAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
    {
        return await _context.FeedChangeLogs
            .Where(e => e.Parameter == "przycisk" && e.Status == "ZLECENIE" && e.TimestampUtc < olderThanUtc)
            .ExecuteUpdateAsync(u => u
                .SetProperty(e => e.Status, "ZLECENIE_WYGASLO")
                .SetProperty(e => e.Message, "Zlecenie z przycisku wygasło: Console go nie obsłużył w ciągu 15 min (nic nie ustawiono)."),
                cancellationToken);
    }

    public async Task SetApplyRequestStatusAsync(FeedChangeLog request, string status, string message, CancellationToken cancellationToken = default)
    {
        request.Status = status;
        request.Message = message.Length > 1000 ? message[..999] + "…" : message;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
