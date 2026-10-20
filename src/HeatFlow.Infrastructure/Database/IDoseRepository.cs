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

    /// <summary>Dopisuje wpisy do dziennika decyzji (FeedChangeLog).</summary>
    Task AddChangeLogAsync(IEnumerable<FeedChangeLog> entries, CancellationToken cancellationToken = default);

    /// <summary>Ostatnie wpisy dziennika decyzji, od najnowszego.</summary>
    Task<List<FeedChangeLog>> GetChangeLogAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Ostatni wpis „wysłano na Discord nową sugestię” (Parameter = sugestia_discord); Controller* = zgłoszone wartości.</summary>
    Task<FeedChangeLog?> GetLastSuggestionNotificationAsync(CancellationToken cancellationToken = default);

    // --- Przycisk „Ustaw sugerowane”: zlecenie to wpis dziennika ze statusem ZLECENIE (bez nowej tabeli
    // i bez migracji). API je dopisuje, Console (jedyny z dostępem do mappera) obsługuje w najbliższym przebiegu.

    /// <summary>Dopisuje zlecenie z przycisku (status ZLECENIE) i zwraca wpis.</summary>
    Task<FeedChangeLog> AddApplyRequestAsync(FeedChangeLog request, CancellationToken cancellationToken = default);

    /// <summary>Czy w podanym oknie (UTC) było już zlecenie z przycisku - w jakimkolwiek statusie (ograniczenie częstości).</summary>
    Task<bool> AnyApplyRequestSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Najnowsze nieobsłużone zlecenie (status ZLECENIE) nie starsze niż podana chwila (UTC).</summary>
    Task<FeedChangeLog?> GetPendingApplyRequestAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Starsze nieobsłużone zlecenia dostają status ZLECENIE_WYGASLO. Zwraca ich liczbę.</summary>
    Task<int> ExpireApplyRequestsAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default);

    /// <summary>Zmienia status i opis zlecenia (np. na ZLECENIE_OBSL po obsłużeniu).</summary>
    Task SetApplyRequestStatusAsync(FeedChangeLog request, string status, string message, CancellationToken cancellationToken = default);
}
