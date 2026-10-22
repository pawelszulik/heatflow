using System.Globalization;
using HeatFlow.Core.Dose;
using HeatFlow.Infrastructure.EsterownikMapper;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Core.Phases;

/// <summary>Nastawa kotła dla Fazy 4: odczyt, zapis z potwierdzeniem w sterowniku, powiadomienie.</summary>
public interface ISummerBoilerSetpoint
{
    /// <summary>Aktualna nastawa [°C] z HA; null, gdy encja jest nieczytelna.</summary>
    Task<double?> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>number.set_value i potwierdzenie w mapperze (jedna ponowna próba). True = sterownik ma wartość.</summary>
    Task<bool> WriteAsync(int value, CancellationToken cancellationToken = default);

    Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Jedyne miejsce, w którym HeatFlow zmienia nastawę kotła (number.kociol_temperatura_zadana_kotla):
/// Faza 4 przy przejściu lato/zima (decyzja Pawła 2026-10-22). Potwierdzenie ze sterownika przez
/// mapper (desiredKettledrumTemperature), nie ze stanu encji HA - tak jak przy Fazie 5.
/// </summary>
public class SummerBoilerSetpoint : ISummerBoilerSetpoint
{
    public const string EntityId = "number.kociol_temperatura_zadana_kotla";
    private const int HardMin = 50;
    private const int HardMax = 75;

    private readonly IHomeAssistantClient _haClient;
    private readonly IEsterownikMapperClient _mapper;
    private readonly IDoseNotifier _notifier;
    private readonly DoseWriteOptions _options;
    private readonly ILogger<SummerBoilerSetpoint> _logger;

    public SummerBoilerSetpoint(IHomeAssistantClient haClient, IEsterownikMapperClient mapper,
        IDoseNotifier notifier, DoseWriteOptions options, ILogger<SummerBoilerSetpoint> logger)
    {
        _haClient = haClient;
        _mapper = mapper;
        _notifier = notifier;
        _options = options;
        _logger = logger;
    }

    public async Task<double?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var state = await _haClient.GetStateAsync(EntityId, cancellationToken);
        return double.TryParse(state?.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public async Task<bool> WriteAsync(int value, CancellationToken cancellationToken = default)
    {
        if (value is < HardMin or > HardMax)
        {
            _logger.LogError("Faza 4: odmowa zapisu nastawy kotła {Value} - poza {Min}-{Max}", value, HardMin, HardMax);
            return false;
        }

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var sent = await _haClient.SetNumberValueAsync(EntityId, value, cancellationToken);
            _logger.LogInformation("Faza 4: number.set_value {Entity} = {Value} (próba {Attempt}): HA {Result}",
                EntityId, value, attempt, sent ? "ok" : "błąd");

            if (sent && await ConfirmAsync(value, cancellationToken))
            {
                return true;
            }

            if (attempt == 1)
            {
                await Task.Delay(_options.RetryDelay, cancellationToken);
            }
        }

        return false;
    }

    public Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default)
        => _notifier.NotifyAsync(title, message, cancellationToken);

    private async Task<bool> ConfirmAsync(int value, CancellationToken cancellationToken)
    {
        await Task.Delay(_options.ConfirmFirstCheck, cancellationToken);
        var checks = _options.ConfirmPollInterval > TimeSpan.Zero
            ? 1 + (int)Math.Max(0, (_options.ConfirmTimeout - _options.ConfirmFirstCheck) / _options.ConfirmPollInterval)
            : 1;

        for (var i = 0; i < checks; i++)
        {
            if (i > 0)
            {
                await Task.Delay(_options.ConfirmPollInterval, cancellationToken);
            }

            if (await _mapper.GetBoilerSetpointAsync(cancellationToken) == value)
            {
                return true;
            }
        }

        return false;
    }
}
