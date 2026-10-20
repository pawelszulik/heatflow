namespace HeatFlow.Domain;

/// <summary>
/// Wyłącznik automatu dawki (switch.heatflow_dawka_automat w HA). Jeden wiersz (Id = 1).
/// Osobna tabela, a nie pole w <see cref="DoseState"/>: przełącza go API (Paweł z HA), a Faza 5
/// zapisuje go wyłącznie przy samowyłączeniu - dzięki temu przebieg Fazy 5 (który przy zapisie
/// do sterownika trwa ponad minutę) nie nadpisze przełączenia zrobionego w międzyczasie.
/// Domyślnie wyłączony: off = faza liczy i pokazuje, ale nic nie zapisuje do sterownika.
/// </summary>
public class DoseAutomatSwitch
{
    public int Id { get; set; } = 1;

    /// <summary>Czy Paweł chce, żeby automat zapisywał nastawy do sterownika.</summary>
    public bool Enabled { get; set; }

    /// <summary>Kiedy (UTC) przełącznik ostatnio zmienił stan.</summary>
    public DateTime ChangedAtUtc { get; set; }

    /// <summary>Kto przełączył: "HA" (API) albo "Faza5: powód" przy samowyłączeniu.</summary>
    public string? ChangedBy { get; set; }
}
