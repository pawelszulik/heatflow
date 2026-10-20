namespace HeatFlow.Domain;

/// <summary>
/// Migawka kotła i temperatur co przebieg HeatFlow.Console (co 5 min) - pamięć Fazy 5 do
/// średniej Tz z 2 h, szybkiego trymu (2 h / 6 h), bezpieczników (1 h) i dobowego bilansu
/// do korekty k (licznik podajnika, średnia temperatura w domu). Trzymane kilka dni.
/// </summary>
public class DoseSample
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }

    /// <summary>sensor.temperatura_ogrod_temperature [°C].</summary>
    public double? OutdoorTemp { get; set; }

    /// <summary>Średnia z 7 czujników BMx280 (te same, co w modelu § 4) [°C]; null przy &lt; 5 odczytach.</summary>
    public double? IndoorTemp { get; set; }

    public double? BoilerTemp { get; set; }
    public double? BoilerSetpoint { get; set; }
    public double? FlueTemp { get; set; }

    /// <summary>sensor.kociol_aktualna_moc_dmuchawy [%] - &gt; 0 = kocioł grzeje.</summary>
    public double? BlowerActual { get; set; }

    /// <summary>sensor.kociol_czas_pracy_podajnika_2 w sekundach (licznik od zasypu, zeruje się).</summary>
    public int? FeederCounterSec { get; set; }

    /// <summary>switch.kociol_tryb_zima_lato = on.</summary>
    public bool? SummerMode { get; set; }
}
