namespace HeatFlow.Domain;

/// <summary>
/// Klasyfikacja deficytu temperatury pokoju.
/// </summary>
public enum DeficitClassification
{
    /// <summary>
    /// Nie sprecyzowane.
    /// </summary>
    None = 0,

    /// <summary>
    /// Wyłączone grzanie.
    /// </summary>
    Disabled = 1,

    /// <summary>
    /// Średni deficyt - ustawiamy temp taką jaka jest aktualnie na grzejniki.
    /// </summary>
    Stay = 2,

    /// <summary>
    /// max deficyt - wymaga natychmiastowego grzania.
    /// </summary>
    Max = 3
}
