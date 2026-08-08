namespace HeatFlow.Domain;

/// <summary>
/// Reprezentacja pokoju z wszystkimi parametrami i stanem.
/// </summary>
public class Room
{
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
    /// Harmonogram użytkowania pokoju.
    /// </summary>
    public Schedule UsageSchedule { get; set; } = new();
    
    /// <summary>
    /// Harmonogram grzania pokoju.
    /// </summary>
    public Schedule HeatingSchedule { get; set; } = new();
    
    /// <summary>
    /// Czy pokój jest wyłączony z automatyzacji.
    /// </summary>
    public bool AutomationDisabled { get; set; }
    
    // Stan aktualny (aktualizowane przez system)
    
    /// <summary>
    /// Aktualna temperatura pokoju.
    /// </summary>
    public double? TempActual { get; set; }
    
    /// <summary>
    /// Obliczony deficyt temperatury.
    /// </summary>
    public double TempDeficit { get; set; }
    
    /// <summary>
    /// Obliczony score pokoju (używany w arbitrażu).
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Encja Home Assistant dla czujnika temperatury pokoju (tylko do odczytu).
    /// Ustawiane przez OrchestrationService z RoomConfiguration.
    /// </summary>
    public string SensorTemperatureEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Encja Home Assistant dla zaworu termostatycznego pokoju (tylko do odczytu).
    /// Ustawiane przez OrchestrationService z RoomConfiguration.
    /// </summary>
    public string ValveEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Minimalna możliwa do ustawienia temperatura
    /// </summary>
    public double MinimalSetTemperature { get; set; } = 5d;

    /// <summary>
    /// Maksymalna możliwa do ustawienia temperatura
    /// </summary>
    public double MaximalSetTemperature { get; set; } = 35d;

    /// <summary>
    /// Klasyfikacja danego pokoju
    /// </summary>
    public DeficitClassification DeficitClassification { get; private set; }

    /// <summary>
    /// Temperatura do ustawienia na zaworze
    /// </summary>
    public int TemperatureToSet { get; private set; }


    /// <summary>
    /// Czy grzanie jest włączone dla tego pokoju
    /// </summary>
    public bool HeatingEnabled { get; set; }

    /// <summary>
    /// Zwraca docelową temperaturę na podstawie harmonogramu grzania.
    /// </summary>
    public double GetTargetTemperature(bool isHeatingActive)
    {
        if (HeatingSchedule.Weekday == "Brak" && HeatingSchedule.Weekend == "Brak")
        {
            return TempTarget;
        }

        if (isHeatingActive)
        {
            return TempTargetActive > 0 ? TempTargetActive : TempTarget;
        }
        else
        {
            return TempTargetInactive > 0 ? TempTargetInactive : TempTarget;
        }
    }

    public void ChangeTemperatureToSet()
    {
        switch (DeficitClassification)
        {
            case DeficitClassification.Disabled:
                TemperatureToSet = (int)MinimalSetTemperature;
                return;
            case DeficitClassification.Stay:
                TemperatureToSet = (int)(TempActual ?? TempTarget);
                return;
            case DeficitClassification.Max:
                TemperatureToSet = (int)MaximalSetTemperature;
                return;
            default:
                TemperatureToSet = (int)TempTarget;
                break;
        }
    }

    public void SetSafetyRoom()
    {
        DeficitClassification = DeficitClassification.Max;
        TemperatureToSet = (int)MaximalSetTemperature;
    }

    /// <summary>
    /// Utrzymuje pokój na pełnym grzaniu mimo spadku Score poniżej progu - dwell (anti-flap)
    /// z Fazy 2. To nie to samo co pokój bezpieczeństwa: powód jest inny i nie chcemy,
    /// żeby taki pokój raportował się jako awaryjny.
    /// </summary>
    public void KeepHeating()
    {
        DeficitClassification = DeficitClassification.Max;
        ChangeTemperatureToSet();
    }

    /// <summary>
    /// Klasyfikuje pokój na podstawie Score i progów z konfiguracji, z histerezą względem
    /// poprzedniej klasyfikacji. Histereza w °C (parametr Hysteresis) jest przeliczana na
    /// jednostki Score przez ScoreDeficitMultiplier, bo tym mnożnikiem deficyt wchodzi do Score.
    /// </summary>
    /// <param name="parameters">Parametry grzewcze (progi, histereza).</param>
    /// <param name="previous">
    /// Klasyfikacja z poprzedniego cyklu. Bez niej histereza nie ma się do czego odnieść
    /// i progi działają symetrycznie, jak przed wprowadzeniem histerezy.
    /// </param>
    public void ClassifyDeficit(HeatingParameters parameters, DeficitClassification? previous = null)
    {
        // Realne wychłodzenie łamie histerezę - pokój wchodzi w pełne grzanie natychmiast.
        if (TempDeficit >= parameters.HysteresisSafetyThreshold)
        {
            DeficitClassification = DeficitClassification.Max;
            return;
        }

        var histScore = parameters.Hysteresis * parameters.ScoreDeficitMultiplier;

        // Pokój już grzejący wypada z Max dopiero poniżej progu obniżonego o histerezę.
        var progMax = previous == DeficitClassification.Max
            ? parameters.ScoreThresholdMax - histScore
            : parameters.ScoreThresholdMax;

        // Ostro większe, tak jak przed parametryzacją progów (Score > 50) - granica bez zmian.
        if (Score > progMax)
        {
            DeficitClassification = DeficitClassification.Max;
            return;
        }

        // Analogicznie w drugą stronę: pokój zamknięty wraca do Stay dopiero powyżej
        // progu podniesionego o histerezę.
        var progDisabled = previous == DeficitClassification.Disabled
            ? parameters.ScoreThresholdDisabled + histScore
            : parameters.ScoreThresholdDisabled;

        DeficitClassification = Score < progDisabled
            ? DeficitClassification.Disabled
            : DeficitClassification.Stay;
    }
}
