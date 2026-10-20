using HeatFlow.Domain;

namespace HeatFlow.Infrastructure.Database;

/// <summary>
/// Repozytorium Fazy 5 „Dawka”: wyłącznik automatu, stan fazy między przebiegami i migawki kotła.
/// </summary>
public interface IDoseRepository
{
    /// <summary>Wyłącznik automatu. Brak wiersza = wyłączony (stan po wdrożeniu).</summary>
    Task<DoseAutomatSwitch> GetAutomatSwitchAsync(CancellationToken cancellationToken = default);

    /// <summary>Ustawia wyłącznik automatu (zapisuje tylko przy zmianie stanu).</summary>
    Task SetAutomatSwitchAsync(bool enabled, string changedBy, CancellationToken cancellationToken = default);

    /// <summary>Stan Fazy 5. Null przy pierwszym uruchomieniu.</summary>
    Task<DoseState?> GetStateAsync(CancellationToken cancellationToken = default);

    Task SaveStateAsync(DoseState state, CancellationToken cancellationToken = default);

    Task AddSampleAsync(DoseSample sample, CancellationToken cancellationToken = default);

    /// <summary>Migawki od podanej chwili (UTC), rosnąco po czasie.</summary>
    Task<List<DoseSample>> GetSamplesSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Kasuje migawki starsze niż podana chwila (UTC).</summary>
    Task DeleteSamplesOlderThanAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default);
}
