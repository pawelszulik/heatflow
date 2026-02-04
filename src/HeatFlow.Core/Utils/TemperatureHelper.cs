namespace HeatFlow.Core.Utils;

/// <summary>
/// Pomocnicze metody do obsługi temperatur i obliczeń deficytów.
/// </summary>
public static class TemperatureHelper
{
    /// <summary>
    /// Waliduje i koryguje temperaturę do zakresu.
    /// </summary>
    public static double ValidateTemperature(double temp, double minTemp = 0.0, double maxTemp = 50.0)
    {
        if (temp < minTemp)
        {
            return minTemp;
        }
        if (temp > maxTemp)
        {
            return maxTemp;
        }
        return temp;
    }

    /// <summary>
    /// Oblicza deficyt temperatury.
    /// </summary>
    public static double CalculateDeficit(double tempTarget, double tempActual)
    {
        return tempTarget - tempActual;
    }

    /// <summary>
    /// Oblicza deficyt z buforem przygotowania.
    /// </summary>
    public static double CalculateDeficitWithBuffer(double deficitBase, double bufferPrep, bool usageSoon)
    {
        //if (usageSoon)
        //{
        //    return deficitBase + bufferPrep;
        //}
        return deficitBase;
    }
}
