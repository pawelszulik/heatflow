using HeatFlow.Domain;

namespace HeatFlow.Infrastructure.Database;

/// <summary>
/// Repozytorium do zarządzania logami trybu lato.
/// </summary>
public interface ISummerModeRepository
{
    /// <summary>
    /// Pobiera wpis logu dla podanej daty. Zwraca null jeśli brak wpisu.
    /// </summary>
    Task<SummerModeLog?> GetLogForDateAsync(DateTime date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje (tworzy lub aktualizuje) wpis logu dla danego dnia.
    /// </summary>
    Task SaveLogAsync(SummerModeLog log, CancellationToken cancellationToken = default);
}
