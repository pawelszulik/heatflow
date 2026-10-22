namespace HeatFlow.Domain;

/// <summary>
/// Ostatni stan przełącznika trybu lato/zima, tak jak go widziała Faza 4 - niezależny
/// od last_changed z Home Assistanta (ten resetuje się też przy krótkim zaniku integracji
/// kotła, np. unavailable → on, bez realnej zmiany stanu). Jeden wiersz (Id = 1).
/// </summary>
public class SummerModeSwitchState
{
    public int Id { get; set; }

    /// <summary>Ostatni stan przełącznika odczytany przez Fazę 4 (true = lato/on).</summary>
    public bool IsSummer { get; set; }

    /// <summary>Kiedy (UTC) Faza 4 po raz pierwszy zobaczyła ten stan.</summary>
    public DateTime SinceUtc { get; set; }

    /// <summary>
    /// Nastawa kotła [°C] sprzed włączenia lata przez Fazę 4; null = nic do przywrócenia.
    /// Przy powrocie na zimę (naszym albo ręcznym) wraca do kotła i jest czyszczona.
    /// </summary>
    public decimal? BoilerSetpointBeforeSummer { get; set; }
}
