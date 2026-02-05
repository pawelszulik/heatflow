using HeatFlow.Domain;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Core.Phases;

/// <summary>
/// Faza 4 - Sterowanie piecem ekopiec i monitoring zaworu 4D.
/// Oblicza temperaturę pieca z kompensacją mrozu i moduluje moc podajnika.
/// </summary>
public class Phase4BoilerService : IPhaseService
{
    private readonly IHomeAssistantClient _haClient;
    private readonly IApplicationErrorLogger _errorLogger;
    private readonly ILogger<Phase4BoilerService> _logger;

    public int PhaseNumber => 4;

    public Phase4BoilerService(
        IHomeAssistantClient haClient,
        IApplicationErrorLogger errorLogger,
        ILogger<Phase4BoilerService> logger)
    {
        _haClient = haClient;
        _errorLogger = errorLogger;
        _logger = logger;
    }

    public async Task<PhaseResult> ExecuteAsync(
        HeatingState state,
        HeatingParameters parameters,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            // Pobierz numer seryjny pieca z konfiguracji systemowej
            if (state.SystemConfiguration == null || string.IsNullOrWhiteSpace(state.SystemConfiguration.EkoPiecDeviceSn))
            {
                return PhaseResult.ErrorResult(PhaseNumber, "Brak numeru seryjnego pieca w konfiguracji");
            }

            var deviceSn = state.SystemConfiguration.EkoPiecDeviceSn;

            // Oblicz liczbę grzanych pokoi
            var roomsHeatedCount = state.GetEnabledRooms().Count(r => r.HeatingEnabled);
            state.BoilerState ??= new BoilerState();
            state.BoilerState.RoomsHeatedCount = roomsHeatedCount;
            state.BoilerState.ForecastMode = await GetForecastModeAsync(cancellationToken);

            // Oblicz temperaturę pieca
            var boilerTemp = CalculateBoilerTemperature(
                state.BoilerState.TempExternal,
                parameters.BoilerNominalTemp,
                parameters.FrostCompensationFactor);

            // Oblicz czas podajnika z modulacją mocy
            var currentFeederTime = await GetCurrentFeederTimeAsync(deviceSn, state.SystemConfiguration, cancellationToken);
            var feederTime = CalculateFeederTime(
                roomsHeatedCount,
                state.BoilerState.ForecastMode,
                parameters,
                currentFeederTime);

            // Ustaw piec z retry
            var boilerSuccess = await SetBoilerTemperatureAsync(
                deviceSn,
                boilerTemp,
                parameters,
                state.SystemConfiguration,
                cancellationToken);

            var feederSuccess = await SetFeederTimeAsync(
                deviceSn,
                feederTime,
                parameters,
                state.SystemConfiguration,
                cancellationToken);

            // Zapisz obliczone wartości do stanu pieca
            state.BoilerState.TempTarget = boilerTemp;
            state.BoilerState.FeederTime = feederTime;

            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            var details = $"Temp pieca: {boilerTemp}°C, Czas podajnika: {feederTime}s, Pokoi: {roomsHeatedCount}";

            _logger.LogInformation("Faza 4 wykonana: temp {Temp}°C, podajnik {Time}s", boilerTemp, feederTime);

            return PhaseResult.SuccessResult(PhaseNumber, duration, details);
        }
        catch (Exception ex)
        {
            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Błąd podczas wykonania Fazę 4");
            await _errorLogger.LogAsync(ex, PhaseNumber, nameof(Phase4BoilerService), null, "Error", "Console", cancellationToken);
            return PhaseResult.ErrorResult(PhaseNumber, ex.Message, duration);
        }
    }

    private double CalculateBoilerTemperature(
        double tempExternal,
        double nominalTemp,
        double frostCompensation)
    {
        if (tempExternal < 0)
        {
            var frostCompensationValue = Math.Abs(tempExternal) * frostCompensation;
            return Math.Round(nominalTemp + frostCompensationValue, 0);
        }

        return Math.Round(nominalTemp, 0);
    }

    private double CalculateFeederTime(
        int roomsCount,
        ForecastMode forecastMode,
        HeatingParameters parameters,
        double? currentFeederTime)
    {
        var feederBaseTime = currentFeederTime ?? parameters.FeederTimeDefault;

        double feederModulation;
        if (roomsCount >= parameters.FeederBoostThreshold)
        {
            feederModulation = parameters.FeederBoostMultiplier;
        }
        else if (roomsCount <= parameters.FeederEconomyThreshold)
        {
            feederModulation = parameters.FeederEconomyMultiplier;
        }
        else
        {
            feederModulation = parameters.FeederNormalMultiplier;
        }

        return Math.Round(feederBaseTime * feederModulation, 0);
    }

    private async Task<bool> SetBoilerTemperatureAsync(
        string deviceSn,
        double targetTemp,
        HeatingParameters parameters,
        SystemConfiguration? systemConfig,
        CancellationToken cancellationToken)
    {
        // Użyj encji z konfiguracji jeśli jest podana, w przeciwnym razie zbuduj automatycznie
        var entityId = systemConfig?.BoilerTempEntityId ?? $"number.ekopiec_{deviceSn}_kot_tzad";
        
        // Sprawdź aktualną wartość
        var currentTemp = await _haClient.GetStateDoubleAsync(entityId, cancellationToken);
        if (currentTemp.HasValue && Math.Abs(currentTemp.Value - targetTemp) <= parameters.BoilerTempTolerance)
        {
            return true;
        }

        // Retry z weryfikacją
        for (int i = 0; i < parameters.BoilerRetryCount; i++)
        {
            var success = await _haClient.SetNumberValueAsync(entityId, targetTemp, cancellationToken);
            if (success)
            {
                await Task.Delay((int)(parameters.BoilerRetryDelay * 1000), cancellationToken);
                
                var verified = await _haClient.GetStateDoubleAsync(entityId, cancellationToken);
                if (verified.HasValue && Math.Abs(verified.Value - targetTemp) <= parameters.BoilerTempTolerance)
                {
                    return true;
                }
            }

            if (i < parameters.BoilerRetryCount - 1)
            {
                await Task.Delay((int)(parameters.BoilerRetryDelay * 1000), cancellationToken);
            }
        }

        return false;
    }

    private async Task<bool> SetFeederTimeAsync(
        string deviceSn,
        double targetTime,
        HeatingParameters parameters,
        SystemConfiguration? systemConfig,
        CancellationToken cancellationToken)
    {
        // Użyj encji z konfiguracji jeśli jest podana, w przeciwnym razie zbuduj automatycznie
        var entityId = systemConfig?.FeederTimeEntityId ?? $"number.ekopiec_{deviceSn}_p_pod_on";
        
        // Sprawdź aktualną wartość
        var currentTime = await _haClient.GetStateDoubleAsync(entityId, cancellationToken);
        if (currentTime.HasValue && Math.Abs(currentTime.Value - targetTime) <= parameters.FeederTimeTolerance)
        {
            return true;
        }

        // Retry z weryfikacją
        for (int i = 0; i < parameters.BoilerRetryCount; i++)
        {
            var success = await _haClient.SetNumberValueAsync(entityId, targetTime, cancellationToken);
            if (success)
            {
                await Task.Delay((int)(parameters.BoilerRetryDelay * 1000), cancellationToken);
                
                var verified = await _haClient.GetStateDoubleAsync(entityId, cancellationToken);
                if (verified.HasValue && Math.Abs(verified.Value - targetTime) <= parameters.FeederTimeTolerance)
                {
                    return true;
                }
            }

            if (i < parameters.BoilerRetryCount - 1)
            {
                await Task.Delay((int)(parameters.BoilerRetryDelay * 1000), cancellationToken);
            }
        }

        return false;
    }

    private async Task<ForecastMode> GetForecastModeAsync(CancellationToken cancellationToken)
    {
        var modeValue = await _haClient.GetStateIntAsync("input_number.forecast_mode", cancellationToken);
        if (modeValue.HasValue && Enum.IsDefined(typeof(ForecastMode), modeValue.Value))
        {
            return (ForecastMode)modeValue.Value;
        }
        return ForecastMode.Normal;
    }

    private async Task<double?> GetCurrentFeederTimeAsync(string deviceSn, SystemConfiguration? systemConfig, CancellationToken cancellationToken)
    {
        var entityId = systemConfig?.FeederTimeEntityId ?? $"number.ekopiec_{deviceSn}_p_pod_on";
        return await _haClient.GetStateDoubleAsync(entityId, cancellationToken);
    }
}
