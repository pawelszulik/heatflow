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
}
