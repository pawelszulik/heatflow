using HeatFlow.Domain;

namespace HeatFlow.Core.Dose;

/// <summary>
/// Encje HA Fazy 5 „Dawka”, z <see cref="SystemConfiguration"/> w bazie (nie z kodu).
/// </summary>
public sealed record DoseEntities(
    string OutdoorTemp,
    string BoilerTemp,
    string BoilerSetpoint,
    string FlueTemp,
    string BlowerActual,
    string FeederCounter,
    string SummerSwitch,
    string ControllerMode,
    string ControllerClock,
    string FlueSafetyAutomation,
    string FeederWork,
    string FeederPause,
    string Blower)
{
    public static DoseEntities From(SystemConfiguration c) => new(
        c.DoseOutdoorTempEntityId,
        c.BoilerTempEntityId ?? string.Empty,
        c.DoseBoilerSetpointEntityId,
        c.DoseFlueTempEntityId,
        c.DoseBlowerActualEntityId,
        c.DoseFeederCounterEntityId,
        c.DoseSummerSwitchEntityId,
        c.DoseControllerModeEntityId,
        c.DoseControllerClockEntityId,
        c.DoseFlueSafetyAutomationEntityId,
        c.DoseFeederWorkEntityId,
        c.DoseFeederPauseEntityId,
        c.DoseBlowerEntityId);

    /// <summary>
    /// Błędy konfiguracji: brakujące encje, a dla trzech zapisywanych - nie number.*, powtórzone albo
    /// równe nastawie kotła. Automatyzacja #24 jest opcjonalna (bez niej tylko brak blokady po #24).
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        void Required(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) errors.Add($"brak encji {name} w SystemConfiguration");
        }

        Required(nameof(SystemConfiguration.DoseOutdoorTempEntityId), OutdoorTemp);
        Required(nameof(SystemConfiguration.BoilerTempEntityId), BoilerTemp);
        Required(nameof(SystemConfiguration.DoseBoilerSetpointEntityId), BoilerSetpoint);
        Required(nameof(SystemConfiguration.DoseFlueTempEntityId), FlueTemp);
        Required(nameof(SystemConfiguration.DoseBlowerActualEntityId), BlowerActual);
        Required(nameof(SystemConfiguration.DoseFeederCounterEntityId), FeederCounter);
        Required(nameof(SystemConfiguration.DoseSummerSwitchEntityId), SummerSwitch);
        Required(nameof(SystemConfiguration.DoseControllerModeEntityId), ControllerMode);
        Required(nameof(SystemConfiguration.DoseControllerClockEntityId), ControllerClock);

        var writable = new[]
        {
            (nameof(SystemConfiguration.DoseFeederWorkEntityId), FeederWork),
            (nameof(SystemConfiguration.DoseFeederPauseEntityId), FeederPause),
            (nameof(SystemConfiguration.DoseBlowerEntityId), Blower)
        };
        foreach (var (name, value) in writable)
        {
            Required(name, value);
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!value.StartsWith("number.", StringComparison.Ordinal))
                errors.Add($"{name} = {value} - zapis tylko do encji number.*");
            if (string.Equals(value, BoilerSetpoint, StringComparison.Ordinal))
                errors.Add($"{name} = {value} to nastawa kotła - Faza 5 jej nie zmienia");
        }
        if (writable.Select(w => w.Item2).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().Count()
            != writable.Count(w => !string.IsNullOrWhiteSpace(w.Item2)))
            errors.Add("encje czasu pracy, postoju i dmuchawy muszą być różne");

        return errors;
    }
}
