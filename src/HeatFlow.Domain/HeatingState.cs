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
    /// Stan pokoi z poprzedniego cyklu, po nazwie pokoju. Zasila histerezę w Fazie 1
    /// i dwell (anti-flap) w Fazie 2. Pusty przy pierwszym uruchomieniu - wtedy oba
    /// mechanizmy zachowują się jak przed ich wprowadzeniem.
    /// </summary>
    public Dictionary<string, RoomState> PreviousRoomStates { get; set; } = new();

    /// <summary>
    /// Klasyfikacja pokoju z poprzedniego cyklu albo null, jeśli nie ma historii.
    /// </summary>
    public DeficitClassification? PreviousClassification(string roomName)
    {
        return PreviousRoomStates.TryGetValue(roomName, out var previous)
            ? (DeficitClassification)previous.Classification
            : null;
    }

    /// <summary>
    /// Od kiedy pokój jest w obecnej klasyfikacji albo null, jeśli nie ma historii.
    /// </summary>
    public DateTime? ClassificationSince(string roomName)
    {
        return PreviousRoomStates.TryGetValue(roomName, out var previous)
            && previous.ClassificationSince > DateTime.MinValue
            ? previous.ClassificationSince
            : null;
    }

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
