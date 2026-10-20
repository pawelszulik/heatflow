namespace HeatFlow.Domain;

/// <summary>
/// Konfiguracja pojedynczego pokoju przechowywana w bazie danych.
/// </summary>
public class RoomConfiguration
{
    /// <summary>
    /// Nazwa pokoju (klucz główny).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Temperatura docelowa podstawowa.
    /// </summary>
    public double TempTarget { get; set; }

    /// <summary>
    /// Temperatura docelowa w godzinach grzania (gdy harmonogram grzania jest aktywny).
    /// </summary>
    public double TempTargetActive { get; set; }

    /// <summary>
    /// Temperatura docelowa poza godzinami grzania.
    /// </summary>
    public double TempTargetInactive { get; set; }

    /// <summary>
    /// Priorytet pokoju (1=najwyższy, 4=najniższy).
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Czy pokój jest wrażliwy (sypialnia, łazienka, pokój dzieci).
    /// </summary>
    public bool Sensitive { get; set; }

    /// <summary>
    /// Czy pokój jest wyłączony z automatyzacji.
    /// </summary>
    public bool AutomationDisabled { get; set; }

    /// <summary>
    /// Harmonogram użytkowania pokoju (serializowany jako string).
    /// Format: "weekday|weekend" lub "Brak"
    /// </summary>
    public string UsageSchedule { get; set; } = "Brak";

    /// <summary>
    /// Harmonogram grzania pokoju (serializowany jako string).
    /// Format: "weekday|weekend" lub "Brak"
    /// </summary>
    public string HeatingSchedule { get; set; } = "Brak";

    /// <summary>
    /// Encja Home Assistant dla czujnika temperatury pokoju.
    /// Przykład: "sensor.sypialnia_temperature" lub "climate.sypialnia"
    /// </summary>
    public string SensorTemperatureEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Encja Home Assistant dla zaworu termostatycznego pokoju.
    /// Przykład: "climate.sypialnia" lub "number.sypialnia_valve"
    /// </summary>
    public string ValveEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Czy czujnik tego pokoju (SensorTemperatureEntityId) wchodzi do średniej temperatury w domu,
    /// z której Faza 5 „Dawka” liczy bilans dobowy (korekta k). Model dawki był kalibrowany na 7 czujnikach
    /// BMx280 pod sufitem (kuchnia, salon, spiżarnia, suszarnia, edyta, hol, przejściowy) - zmiana zestawu
    /// zmienia poziom średniej, więc tylko świadomie. Głowice TRV nie nadają się (mierzą przy grzejniku).
    /// </summary>
    public bool UseForDoseIndoorMean { get; set; }

    /// <summary>
    /// Zapotrzebowanie pokoju na ciepło [W] z inwentaryzacji (plan-heatflow-algorytm.md, „Wagi pokoi
    /// z zapotrzebowania”). Waga celu pokoju w celu domu, z którego Faza 5 liczy dawkę:
    /// cel domu = Σ(waga · cel pokoju) / Σ wag. 0 = pokój nie wchodzi do celu domu.
    /// </summary>
    public double HeatLossW { get; set; }

    /// <summary>
    /// Czy faktyczna temperatura pokoju może podnieść jego wkład do celu domu ponad cel
    /// (HeatingParameters.DoseTargetUseActual). Wył. = pokój zawsze po celu - np. pomieszczenie ciepłe
    /// od cudzego ciepła (garaż przy celu 6 °C ma średnio ~16 °C, zawór zamknięty).
    /// </summary>
    public bool DoseUseActual { get; set; } = true;

    /// <summary>
    /// Tworzy kopię konfiguracji (snapshot) do porównań np. w audit logu.
    /// Zwraca null, gdy wywołane na null.
    /// </summary>
    public static RoomConfiguration? CreateSnapshot(RoomConfiguration? source)
    {
        if (source == null) return null;
        return new RoomConfiguration
        {
            Name = source.Name,
            TempTarget = source.TempTarget,
            TempTargetActive = source.TempTargetActive,
            TempTargetInactive = source.TempTargetInactive,
            Priority = source.Priority,
            Sensitive = source.Sensitive,
            AutomationDisabled = source.AutomationDisabled,
            UsageSchedule = source.UsageSchedule,
            HeatingSchedule = source.HeatingSchedule,
            SensorTemperatureEntityId = source.SensorTemperatureEntityId,
            ValveEntityId = source.ValveEntityId,
            UseForDoseIndoorMean = source.UseForDoseIndoorMean,
            HeatLossW = source.HeatLossW,
            DoseUseActual = source.DoseUseActual
        };
    }

    /// <summary>
    /// Konwertuje RoomConfiguration na obiekt Room z harmonogramami.
    /// </summary>
    public Room ToRoom()
    {
        return new Room
        {
            Name = Name,
            TempTarget = TempTarget,
            TempTargetActive = TempTargetActive,
            TempTargetInactive = TempTargetInactive,
            Priority = Priority,
            Sensitive = Sensitive,
            AutomationDisabled = AutomationDisabled,
            UsageSchedule = Schedule.FromString(UsageSchedule),
            HeatingSchedule = Schedule.FromString(HeatingSchedule)
        };
    }

    /// <summary>
    /// Tworzy RoomConfiguration z obiektu Room i encji HA.
    /// </summary>
    public static RoomConfiguration FromRoom(Room room, string sensorTemperatureEntityId, string valveEntityId)
    {
        return new RoomConfiguration
        {
            Name = room.Name,
            TempTarget = room.TempTarget,
            TempTargetActive = room.TempTargetActive,
            TempTargetInactive = room.TempTargetInactive,
            Priority = room.Priority,
            Sensitive = room.Sensitive,
            AutomationDisabled = room.AutomationDisabled,
            UsageSchedule = room.UsageSchedule.Weekday == "Brak" && room.UsageSchedule.Weekend == "Brak"
                ? "Brak"
                : $"{room.UsageSchedule.Weekday}|{room.UsageSchedule.Weekend}",
            HeatingSchedule = room.HeatingSchedule.Weekday == "Brak" && room.HeatingSchedule.Weekend == "Brak"
                ? "Brak"
                : $"{room.HeatingSchedule.Weekday}|{room.HeatingSchedule.Weekend}",
            SensorTemperatureEntityId = sensorTemperatureEntityId,
            ValveEntityId = valveEntityId
        };
    }
}
