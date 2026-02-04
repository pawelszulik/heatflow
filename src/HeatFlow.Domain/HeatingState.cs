namespace HeatFlow.Domain;

/// <summary>
/// Stan systemu grzania w danym momencie.
/// </summary>
public class HeatingState
{
    public DateTime CurrentTime { get; set; }
    public bool IsWeekend { get; set; }
    public List<Room> Rooms { get; set; } = new();
    public List<Room> RoomsToDisable { get; set; } = new();
    public List<Room> RoomsToStay { get; set; } = new();
    public List<Room> RoomsToHot { get; set; } = new();
    public BoilerState? BoilerState { get; set; }
    
    /// <summary>
    /// Konfiguracja systemowa (encje HA, numer seryjny pieca).
    /// </summary>
    public SystemConfiguration? SystemConfiguration { get; set; }

    /// <summary>
    /// Znajduje pokój po nazwie.
    /// </summary>
    public Room? GetRoom(string name)
    {
        return Rooms.FirstOrDefault(r => r.Name == name);
    }

    /// <summary>
    /// Zwraca listę pokoi włączonych do automatyzacji.
    /// </summary>
    public List<Room> GetEnabledRooms()
    {
        return Rooms.Where(r => !r.AutomationDisabled).ToList();
    }
}
