namespace HeatFlow.Domain;

/// <summary>
/// Dziennik operacji trybu lato - max jedna aktywacja i jedna dezaktywacja na dzień.
/// </summary>
public class SummerModeLog
{
    public int Id { get; set; }

    /// <summary>
    /// Data (bez czasu) - jeden rekord na dzień.
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Czy tryb lato został aktywowany dzisiaj.
    /// </summary>
    public bool WasActivated { get; set; }

    /// <summary>
    /// Czy tryb lato został dezaktywowany dzisiaj.
    /// </summary>
    public bool WasDeactivated { get; set; }

    /// <summary>
    /// Kiedy nastąpiła aktywacja (null jeśli nie aktywowano).
    /// </summary>
    public DateTime? ActivatedAt { get; set; }

    /// <summary>
    /// Kiedy nastąpiła dezaktywacja (null jeśli nie dezaktywowano).
    /// </summary>
    public DateTime? DeactivatedAt { get; set; }
}
