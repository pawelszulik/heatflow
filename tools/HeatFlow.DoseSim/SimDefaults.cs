using HeatFlow.Core.Dose;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Database;

namespace HeatFlow.DoseSim;

/// <summary>
/// Konfiguracja symulacji = to, co seed i migracja wpisują do bazy u Pawła: encje HA z
/// <see cref="ConfigurationSeed.DoseEntityDefaults"/> i 7 pokoi z czujnikami BMx280 do średniej w domu.
/// </summary>
public static class SimDefaults
{
    public static SystemConfiguration SystemConfiguration()
        => ConfigurationSeed.DoseEntityDefaults(new SystemConfiguration { Id = 1, SystemEnabled = true });

    public static DoseEntities Entities => DoseEntities.From(SystemConfiguration());

    /// <summary>Pokoje z UseForDoseIndoorMean - te same czujniki, co kolumny in_* w migawkach HMS.</summary>
    public static List<RoomConfiguration> IndoorRooms()
        => ConfigurationSeed.DoseIndoorMeanRooms
            .Select(name => new RoomConfiguration
            {
                Name = name,
                SensorTemperatureEntityId = $"sensor.{name}_czujnik_bm280_{name}_bmx280_temperature",
                UseForDoseIndoorMean = true
            })
            .ToList();

    /// <summary>Cele pokoi z bazy 2026-10-20 (RoomConfiguration.TempTarget, harmonogramów brak).</summary>
    public static readonly IReadOnlyDictionary<string, double> RoomTargetsToday = new Dictionary<string, double>
    {
        ["balkonowy"] = 25, ["garaz"] = 6, ["salon"] = 26, ["jadalnia"] = 25, ["hol"] = 25, ["edyta"] = 25,
        ["sypialnia"] = 25, ["grzes"] = 26, ["przejsciowy"] = 20, ["suszarnia"] = 21, ["kuchnia"] = 22,
        ["sien"] = 22, ["lazienka"] = 24, ["spizarnia"] = 19, ["toaleta"] = 25
    };

    /// <summary>
    /// Ile pokój odbiega od średniej domu ważonej stratami [K] - z RoomState 2026-10-20 15:34 UTC
    /// (ważona 23,79 °C; średnia 7 czujników BMx280 25,67 °C, czyli +1,88 K; garaż 14 °C przy celu 6).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, double> RoomOffsetsToday = new Dictionary<string, double>
    {
        ["balkonowy"] = 24.2 - 23.79, ["garaz"] = 14.0 - 23.79, ["salon"] = 26.11 - 23.79, ["jadalnia"] = 26.37 - 23.79,
        ["hol"] = 24.58 - 23.79, ["edyta"] = 25.0 - 23.79, ["sypialnia"] = 25.51 - 23.79, ["grzes"] = 25.2 - 23.79,
        ["przejsciowy"] = 25.26 - 23.79, ["suszarnia"] = 26.46 - 23.79, ["kuchnia"] = 25.9 - 23.79, ["sien"] = 22.3 - 23.79,
        ["lazienka"] = 23.8 - 23.79, ["spizarnia"] = 26.36 - 23.79, ["toaleta"] = 24.8 - 23.79
    };

    /// <summary>
    /// 15 pokoi jak w bazie: wagi HeatLossW z inwentaryzacji, cele <paramref name="targets"/> (domyślnie
    /// dzisiejsze), 7 czujników BMx280 z flagą UseForDoseIndoorMean.
    /// </summary>
    public static List<RoomConfiguration> AllRooms(IReadOnlyDictionary<string, double>? targets = null)
        => ConfigurationSeed.RoomHeatLossW.Select(kv => new RoomConfiguration
            {
                Name = kv.Key,
                HeatLossW = kv.Value,
                TempTarget = (targets ?? RoomTargetsToday).GetValueOrDefault(kv.Key, 22),
                SensorTemperatureEntityId = ConfigurationSeed.DoseIndoorMeanRooms.Contains(kv.Key)
                    ? $"sensor.{kv.Key}_czujnik_bm280_{kv.Key}_bmx280_temperature"
                    : $"sensor.{kv.Key}_temperatura",
                UseForDoseIndoorMean = ConfigurationSeed.DoseIndoorMeanRooms.Contains(kv.Key)
            })
            .ToList();
}
