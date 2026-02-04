namespace HeatFlow.Domain;

/// <summary>
/// Tryb prognozy pogody.
/// </summary>
public enum ForecastMode
{
    /// <summary>
    /// Tryb normalny - brak znaczących zmian pogody.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Tryb przygotowania - spadek temperatury, zwiększona aktywność grzania.
    /// </summary>
    PreHeating = 1,

    /// <summary>
    /// Tryb redukcji - wzrost temperatury, zmniejszona aktywność grzania.
    /// </summary>
    Reduction = 2
}
