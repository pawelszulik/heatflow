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

    /// <summary>
    /// Pobiera ostatni znany Fazie 4 stan przełącznika. Null przy pierwszym uruchomieniu
    /// (nowa baza albo świeżo po migracji) - wtedy nie ma z czym porównywać, więc brak karencji.
    /// </summary>
    Task<SummerModeSwitchState?> GetSwitchStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje stan przełącznika widziany przez Fazę 4 wraz z chwilą (UTC), od której ten
    /// stan obowiązuje. Wołający ustawia SinceUtc: na "teraz" przy zmianie, bez zmian gdy
    /// stan jest taki sam jak poprzednio zapisany.
    /// </summary>
    Task SaveSwitchStateAsync(SummerModeSwitchState state, CancellationToken cancellationToken = default);

    /// <summary>Nastawa kotła zapamiętana przed latem; null = brak.</summary>
    Task<double?> GetBoilerSetpointBeforeSummerAsync(CancellationToken cancellationToken = default);

    /// <summary>Zapamiętuje (albo czyści, gdy null) nastawę kotła sprzed lata. Nie rusza reszty stanu.</summary>
    Task SaveBoilerSetpointBeforeSummerAsync(double? value, CancellationToken cancellationToken = default);
}
