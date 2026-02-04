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
