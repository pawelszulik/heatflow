using HeatFlow.Domain;

namespace HeatFlow.Domain;

/// <summary>
/// Stan pieca i zaworu 4D.
/// </summary>
public class BoilerState
{
    /// <summary>
    /// Temperatura powrotu wody.
    /// </summary>
    public double TempReturn { get; set; }
    
    /// <summary>
    /// Pozycja zaworu 4D (%).
    /// </summary>
    public double Mixer4DPosition { get; set; }
    
    /// <summary>
    /// Temperatura zewnętrzna.
    /// </summary>
    public double TempExternal { get; set; }
    
    /// <summary>
    /// Liczba grzanych pokoi.
    /// </summary>
    public int RoomsHeatedCount { get; set; }
    
    /// <summary>
    /// Tryb prognozy pogody.
    /// </summary>
    public ForecastMode ForecastMode { get; set; }
    
    /// <summary>
    /// Temperatura zadana pieca (obliczona i ustawiona).
    /// </summary>
    public double TempTarget { get; set; }
    
    /// <summary>
    /// Czas podajnika (obliczony i ustawiony).
    /// </summary>
    public double FeederTime { get; set; }
}
