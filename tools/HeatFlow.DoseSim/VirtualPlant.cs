namespace HeatFlow.DoseSim;

/// <summary>
/// Wirtualny kocioł + dom do pętli zamkniętej. Energia liczona w „sekundach pracy podajnika”
/// (jak model z analizy § 3-4), czasy w minutach.
///
/// Dom: pojemność C = 1549 s/K, strata = <see cref="TrueFactor"/> · (255 · (Tin − Tz) − 934) s/dobę -
/// TrueFactor ≠ 1 to „prawdziwy dom” inny niż model, który k ma wyuczyć.
/// Pokoje (głowice + Fazy 1-3) biorą z wody tyle, ile trzeba, żeby Tin wracała do celu w ~6 h;
/// zawór 4D miesza w dół, więc dopóki kocioł ma ponad ~53 °C, odbiór nie zależy od jego temperatury -
/// temperatura kotła wynika z bilansu ogień − odbiór. Poniżej ~50 °C 4D przymyka (ochrona powrotu)
/// i dom dostaje mniej, niż chce.
/// Kocioł: ogień idzie za dawką z opóźnieniem 15 min i stałą 45 min (§ 7: rusza po 15-30 min,
/// po podtrzymaniu spaliny 69 → 121 °C w 45 min, ustala się po 1,5-2 h); woda 200 l ≈ 9,6 s/K (0,23 kWh/K); podtrzymanie powyżej nastawy,
/// powrót do grzania poniżej nastawy − histereza (tryb ręczny, instrukcja § 4.1.3); w podtrzymaniu
/// podajnik 7 s co 27 min ×2 ≈ 373 s/dobę. Spaliny rosną z ogniem i dmuchawą.
/// To nie jest model do liczenia kilogramów - tylko do sprawdzenia, jak regulator zachowuje się
/// w pętli (stabilność, uczenie k, bezpieczniki).
/// </summary>
public sealed class VirtualPlant
{
    public const double HouseCapacity = 1549.0;
    public const double WaterCapacity = 9.6;
    public const double StandbyFeedPerDay = 373.0;
    private const double MixerFloor = 50.0;
    private const double MixerGain = 5000.0;
    private const double RoomResponseDays = 0.25;
    private const double BoilerLossPerDay = 100.0;
    private const int FireDelayMinutes = 15;
    private const double FireTauMinutes = 45.0;

    private readonly Queue<double> _feedPipeline = new();

    public VirtualPlant(double trueFactor, double initialFeedPerDay, double indoorGoal = 24.0)
    {
        TrueFactor = trueFactor;
        IndoorGoal = indoorGoal;
        Fire = initialFeedPerDay;
        for (var i = 0; i < FireDelayMinutes; i++) _feedPipeline.Enqueue(initialFeedPerDay);
    }

    public double TrueFactor { get; }
    public double IndoorGoal { get; }
    public double Indoor { get; set; } = 24.0;
    public double Boiler { get; private set; } = 64.0;
    public double Setpoint { get; set; } = 67.0;
    public double Hysteresis { get; set; } = 2.0;
    public bool Heating { get; private set; } = true;
    /// <summary>Moc ognia w s podajnika / dobę.</summary>
    public double Fire { get; private set; }
    public double FeederCounterSec { get; private set; }
    public double Flue { get; private set; } = 120;
    public double BlowerActual { get; private set; }
    public double? FlueOverride { get; set; }
    /// <summary>Ile s podajnika dom faktycznie stracił (do porównania z k).</summary>
    public double LossTotal { get; private set; }

    public double LossPerDay(double outdoor) => TrueFactor * (255.0 * (Indoor - outdoor) - 934.0);

    /// <summary>Jedna minuta.</summary>
    public void Step(double outdoor, int work, int pause, int blower)
    {
        var feed = Heating ? (double)work / (work + pause) * 86400.0 : StandbyFeedPerDay;
        FeederCounterSec += feed / 1440.0;

        _feedPipeline.Enqueue(feed);
        var delayed = _feedPipeline.Dequeue();
        Fire += (delayed - Fire) / FireTauMinutes;

        var loss = LossPerDay(outdoor);
        LossTotal += loss / 1440.0;
        var demand = Math.Max(0, loss + HouseCapacity * (IndoorGoal - Indoor) / RoomResponseDays);
        var available = MixerGain * Math.Max(0, Boiler - MixerFloor);
        var delivered = Math.Min(demand, available);

        Boiler += (Fire - delivered - BoilerLossPerDay) / 1440.0 / WaterCapacity;
        Indoor += (delivered - loss) / 1440.0 / HouseCapacity;

        if (Heating && Boiler >= Setpoint) Heating = false;
        else if (!Heating && Boiler < Setpoint - Hysteresis) Heating = true;

        BlowerActual = Heating ? blower : 0;
        Flue = FlueOverride ?? (Heating ? 70 + Fire * 0.01 + (blower - 33) * 2.0 : 60 + Fire * 0.004);
    }
}
