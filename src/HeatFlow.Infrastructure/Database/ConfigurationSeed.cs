using HeatFlow.Domain;
using Microsoft.EntityFrameworkCore;

namespace HeatFlow.Infrastructure.Database;

/// <summary>
/// Seed danych domyślnych dla konfiguracji systemu.
/// Wartości pochodzą z plików inputs/*.yaml (wartości initial).
/// </summary>
public static class ConfigurationSeed
{
    /// <summary>
    /// Wypełnia bazę danych wartościami domyślnymi jeśli jest pusta.
    /// </summary>
    public static async Task SeedAsync(HeatFlowDbContext context, CancellationToken cancellationToken = default)
    {
        // Sprawdź czy już są dane
        var hasParameters = await context.HeatingParameters.AnyAsync(cancellationToken);
        var hasSystemConfig = await context.SystemConfigurations.AnyAsync(cancellationToken);
        var hasRooms = await context.RoomConfigurations.AnyAsync(cancellationToken);

        if (hasParameters && hasSystemConfig && hasRooms)
        {
            return; // Baza już ma dane
        }

        // Seed parametrów algorytmu
        if (!hasParameters)
        {
            var parameters = CreateDefaultHeatingParameters();
            context.HeatingParameters.Add(parameters);
        }

        // Seed konfiguracji systemowej
        if (!hasSystemConfig)
        {
            var systemConfig = CreateDefaultSystemConfiguration();
            context.SystemConfigurations.Add(systemConfig);
        }

        // Seed konfiguracji pokoi (tylko jeśli nie ma żadnych)
        if (!hasRooms)
        {
            var rooms = CreateDefaultRoomConfigurations();
            context.RoomConfigurations.AddRange(rooms);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static HeatingParametersEntity CreateDefaultHeatingParameters()
    {
        return new HeatingParametersEntity
        {
            Id = 1,
            UpdatedAt = DateTime.UtcNow,

            // Progi deficytów
            DeficitHighP1 = 1.0,
            DeficitHighP2 = 2.0,
            DeficitHighP3 = 3.0,

            // Wartości bazowe
            DeficitHighP1Base = 1.0,
            DeficitHighP2Base = 2.0,
            DeficitHighP3Base = 3.0,
            BufferPreparationBase = 0.8,

            // Bufor przygotowania
            BufferPreparation = 0.8,
            BufferHeatingTime = 60,

            // Parametry prognozy
            ForecastTempDropThreshold = 5.0,
            ForecastTempRiseThreshold = 3.0,
            ForecastHoursCount = 8,
            ForecastPreHeatingP1Multiplier = 0.8,
            ForecastPreHeatingP2Multiplier = 0.9,
            ForecastPreHeatingP3Multiplier = 0.9,
            ForecastPreHeatingBufferMultiplier = 1.2,
            ForecastReductionP1Multiplier = 1.2,
            ForecastReductionP2Multiplier = 1.2,
            ForecastReductionP3Multiplier = 1.2,
            ForecastReductionBufferMultiplier = 0.8,

            // Parametry arbitrażu
            MaxValvesOpen = 5,
            MinValvesOpen = 1,
            UsageSoonMinutes = 30,
            ScorePriorityMultiplier = 100,
            ScoreDeficitMultiplier = 10,
            ScoreSensitiveBonus = 50,
            ScoreUsageSoonBonus = 20,
            ScoreHeatingScheduleBonus = 50,
            ScoreThresholdMax = 50.0,
            ScoreThresholdDisabled = 0.0,
            MinDwellMinutes = 20,

            // Parametry trybu lato (Faza 4)
            SummerModeWarmDayTemp = 20.0,
            SummerModeForecastHours = 6,
            SummerModeBoilerSetpoint = 64,

            // Parametry zaworów
            ValveTolerance = 0.1,
            ValveRetryCount = 3,
            ValveRetryDelay = 1.0,

            // Parametry pieca
            MinReturnTemp = 45.0,
            BoilerNominalTemp = 70.0,

            // Parametry bezpieczeństwa
            Hysteresis = 0.5,
            HysteresisSafetyThreshold = 2.0,
            TempValidationMin = 0.0,
            TempValidationMax = 40.0
        };
    }

    /// <summary>Pokoje, których czujniki (7× BMx280 pod sufitem) były podstawą modelu dawki (analiza § 2).</summary>
    public static readonly string[] DoseIndoorMeanRooms =
        { "kuchnia", "salon", "spizarnia", "suszarnia", "edyta", "hol", "przejsciowy" };

    /// <summary>
    /// Zapotrzebowanie pokoi na ciepło [W] z inwentaryzacji (plan-heatflow-algorytm.md, „Wagi pokoi
    /// z zapotrzebowania”) - wagi celów pokoi w celu domu dla dawki (Faza 5).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, double> RoomHeatLossW = new Dictionary<string, double>
    {
        ["balkonowy"] = 1344, ["garaz"] = 965, ["salon"] = 626, ["jadalnia"] = 625, ["hol"] = 622,
        ["edyta"] = 604, ["sypialnia"] = 584, ["grzes"] = 500, ["przejsciowy"] = 484, ["suszarnia"] = 448,
        ["kuchnia"] = 430, ["sien"] = 340, ["lazienka"] = 293, ["spizarnia"] = 159, ["toaleta"] = 159
    };

    /// <summary>
    /// Encje HA Fazy 5 „Dawka” u nas (integracja ekoPiec „kociol_*”, czujnik ogrodowy, automatyzacja #24)
    /// i kanał powiadomień. Te same wartości wpisuje migracja AddDosePhase do istniejącego wiersza.
    /// </summary>
    public static SystemConfiguration DoseEntityDefaults(SystemConfiguration c)
    {
        c.BoilerTempEntityId ??= "sensor.kociol_temperatura_kotla";
        c.DoseOutdoorTempEntityId = "sensor.temperatura_ogrod_temperature";
        c.DoseOutdoorTemp2EntityId = "sensor.temperatura_przodek_temperature";
        c.DoseBoilerSetpointEntityId = "number.kociol_temperatura_zadana_kotla";
        c.DoseFlueTempEntityId = "sensor.kociol_temperatura_spalin";
        c.DoseBlowerActualEntityId = "sensor.kociol_aktualna_moc_dmuchawy";
        c.DoseFeederCounterEntityId = "sensor.kociol_czas_pracy_podajnika_2";
        c.DoseSummerSwitchEntityId = "switch.kociol_tryb_zima_lato";
        c.DoseControllerModeEntityId = "switch.kociol_tryb_pracy";
        c.DoseControllerClockEntityId = "sensor.kociol_aktualna_data";
        c.DoseFlueSafetyAutomationEntityId = "automation.kociol_bezpiecznik_spalin_dmuchawa_na_minimum";
        c.DoseFeederWorkEntityId = "number.kociol_czas_pracy_podajnika";
        c.DoseFeederPauseEntityId = "number.kociol_czas_postoju_podajnika";
        c.DoseBlowerEntityId = "number.kociol_moc_dmuchawy";
        c.DoseNotifyService = "homeintegrator";
        c.DoseNotifyTarget = "1458898223593689120";
        return c;
    }

    private static SystemConfiguration CreateDefaultSystemConfiguration()
    {
        return DoseEntityDefaults(new SystemConfiguration
        {
            Id = 1,
            RoomsList = "sypialnia,lazienka,edyta,przejsciowy,hol,balkonowy,grzes,garaz,kuchnia,salon,jadalnia,suszarnia,spizarnia,sien,toaleta",
            EkoPiecDeviceSn = "ABC", // Użytkownik musi dostosować do swojego pieca
            TempReturnEntityId = "sensor.kociol_temperatura_powrotu",
            Mixer4DPositionEntityId = "sensor.kociol_pozycja_zaworu_4d",
            BoilerTempEntityId = "sensor.kociol_temperatura_kotla",
            SystemEnabled = true,
            Latitude = 50.13050002204031, // Użytkownik musi ustawić współrzędne geograficzne
            Longitude = 18.641279186369157 // Użytkownik musi ustawić współrzędne geograficzne
        });
    }

    private static List<RoomConfiguration> CreateDefaultRoomConfigurations()
    {
        // Wartości pochodzą z inputs/*.yaml (wartości initial)
        var rooms = new List<RoomConfiguration>
        {
            new RoomConfiguration { Name = "sypialnia", TempTarget = 27.0, TempTargetActive = 23.0, TempTargetInactive = 21.0, Priority = 1, Sensitive = true, AutomationDisabled = false, UsageSchedule = "21:00-04:00|21:00-04:00", HeatingSchedule = "21:00-04:00|21:00-04:00", SensorTemperatureEntityId = "sensor.sypialnia_czujnik_bm280_sypialnia_bmx280_temperature", ValveEntityId = "climate.avatto_zigbee_smart_trv_2" },
            new RoomConfiguration { Name = "lazienka", TempTarget = 24.0, TempTargetActive = 24.0, TempTargetInactive = 21.0, Priority = 1, Sensitive = true, AutomationDisabled = false, UsageSchedule = "18:00-22:00|19:00-21:00", HeatingSchedule = "06:30-07:30,18:00-22:00|08:00-09:00,19:00-21:00", SensorTemperatureEntityId = "sensor.lazienka_czujnik_dht_lazienka_temperature", ValveEntityId = "climate.zigbee_smart_trv" },
            new RoomConfiguration { Name = "edyta", TempTarget = 25.0, TempTargetActive = 25.0, TempTargetInactive = 22.0, Priority = 1, Sensitive = true, AutomationDisabled = false, UsageSchedule = "00:00-06:00,15:00-23:59|00:00-23:59", HeatingSchedule = "00:00-06:00,15:00-23:59|00:00-23:59", SensorTemperatureEntityId = "sensor.edyta_czujnik_bm280_edyta_bmx280_temperature", ValveEntityId = "climate.avatto_zigbee_smart_trv_3" },
            new RoomConfiguration { Name = "przejsciowy", TempTarget = 24.0, TempTargetActive = 21.0, TempTargetInactive = 20.0, Priority = 3, Sensitive = false, AutomationDisabled = false, UsageSchedule = "08:00-20:00|18:00-23:00", HeatingSchedule = "18:00-23:00|18:00-23:00", SensorTemperatureEntityId = "sensor.przejsciowy_czujnik_bm280_przejsciowy_bmx280_temperature", ValveEntityId = "climate.avatto_zigbee_smart_trv" },
            new RoomConfiguration { Name = "hol", TempTarget = 23.0, TempTargetActive = 23.0, TempTargetInactive = 20.0, Priority = 2, Sensitive = false, AutomationDisabled = false, UsageSchedule = "06:00-09:00,12:00-14:00,17:00-20:00|08:00-22:00", HeatingSchedule = "10:00-14:00|10:00-14:00", SensorTemperatureEntityId = "sensor.hol_czujnik_bm280_hol_bmx280_temperature", ValveEntityId = "climate.smart_radiator_thermostat_controller" },
            new RoomConfiguration { Name = "balkonowy", TempTarget = 17.0, TempTargetActive = 20.0, TempTargetInactive = 18.0, Priority = 4, Sensitive = false, AutomationDisabled = false, UsageSchedule = "Brak", HeatingSchedule = "Brak", SensorTemperatureEntityId = "climate.zigbee_smart_trv_3", ValveEntityId = "climate.zigbee_smart_trv_3" },
            new RoomConfiguration { Name = "grzes", TempTarget = 18.0, TempTargetActive = 25.0, TempTargetInactive = 22.0, Priority = 3, Sensitive = false, AutomationDisabled = false, UsageSchedule = "00:00-06:00,15:00-23:59|00:00-23:59", HeatingSchedule = "00:00-06:00,15:00-23:59|00:00-23:59", SensorTemperatureEntityId = "climate.avatto_zigbee_smart_trv_2_2", ValveEntityId = "climate.avatto_zigbee_smart_trv_2_2" },
            new RoomConfiguration { Name = "garaz", TempTarget = 7.0, TempTargetActive = 15.0, TempTargetInactive = 12.0, Priority = 4, Sensitive = false, AutomationDisabled = false, UsageSchedule = "Brak", HeatingSchedule = "Brak", SensorTemperatureEntityId = "climate.avatto_zigbee_smart_trv_6", ValveEntityId = "climate.avatto_zigbee_smart_trv_6" },
            new RoomConfiguration { Name = "kuchnia", TempTarget = 24.0, TempTargetActive = 21.0, TempTargetInactive = 20.0, Priority = 3, Sensitive = false, AutomationDisabled = false, UsageSchedule = "Brak", HeatingSchedule = "Brak", SensorTemperatureEntityId = "sensor.kuchnia_czujnik_bm280_kuchnia_bmx280_temperature", ValveEntityId = "climate.zigbee_smart_trv_4" },
            new RoomConfiguration { Name = "salon", TempTarget = 26.0, TempTargetActive = 23.0, TempTargetInactive = 20.0, Priority = 2, Sensitive = false, AutomationDisabled = false, UsageSchedule = "06:00-09:00,12:00-14:00,17:00-20:00|08:00-22:00", HeatingSchedule = "10:00-14:00|10:00-14:00", SensorTemperatureEntityId = "sensor.salon_czujnik_bm280_salon_bmx280_temperature", ValveEntityId = "climate.smart_radiator_thermostat_controller_2" },
            new RoomConfiguration { Name = "jadalnia", TempTarget = 18.0, TempTargetActive = 22.0, TempTargetInactive = 20.0, Priority = 2, Sensitive = false, AutomationDisabled = false, UsageSchedule = "07:00-08:00,15:00-17:00,18:00-19:00|07:00-08:00,12:00-15:00,18:00-19:00", HeatingSchedule = "07:00-08:00,15:00-17:00,18:00-19:00|07:00-08:00,12:00-15:00,18:00-19:00", SensorTemperatureEntityId = "climate.smart_radiator_thermostat_controller_3", ValveEntityId = "climate.smart_radiator_thermostat_controller_3" },
            new RoomConfiguration { Name = "suszarnia", TempTarget = 24.0, TempTargetActive = 19.0, TempTargetInactive = 17.0, Priority = 4, Sensitive = false, AutomationDisabled = false, UsageSchedule = "Brak", HeatingSchedule = "Brak", SensorTemperatureEntityId = "sensor.suszarnia_czujnik_bm280_suszarnia_bmx280_temperature", ValveEntityId = "climate.zhi_neng_san_re_qi_heng_wen_kong_zhi_qi" },
            new RoomConfiguration { Name = "spizarnia", TempTarget = 25.0, TempTargetActive = 19.0, TempTargetInactive = 17.0, Priority = 4, Sensitive = false, AutomationDisabled = false, UsageSchedule = "Brak", HeatingSchedule = "Brak", SensorTemperatureEntityId = "sensor.spizarnia_czujnik_bm280_spizarnia_bmx280_temperature", ValveEntityId = "climate.avatto_zigbee_smart_trv_4" },
            new RoomConfiguration { Name = "sien", TempTarget = 18.0, TempTargetActive = 17.0, TempTargetInactive = 15.0, Priority = 4, Sensitive = false, AutomationDisabled = false, UsageSchedule = "Brak", HeatingSchedule = "Brak", SensorTemperatureEntityId = "climate.zigbee_smart_trv_2", ValveEntityId = "climate.zigbee_smart_trv_2" },
            new RoomConfiguration { Name = "toaleta", TempTarget = 19.0, TempTargetActive = 22.0, TempTargetInactive = 20.0, Priority = 3, Sensitive = false, AutomationDisabled = false, UsageSchedule = "07:00-08:00,15:00-17:00,18:00-19:00|07:00-08:00,12:00-15:00,18:00-19:00", HeatingSchedule = "07:00-08:00,15:00-17:00,18:00-19:00|07:00-08:00,12:00-15:00,18:00-19:00", SensorTemperatureEntityId = "climate.zigbee_smart_trv_5", ValveEntityId = "climate.zigbee_smart_trv_5" }
        };
        foreach (var room in rooms)
        {
            room.UseForDoseIndoorMean = DoseIndoorMeanRooms.Contains(room.Name);
            room.HeatLossW = RoomHeatLossW.GetValueOrDefault(room.Name);
        }
        return rooms;
    }
}
