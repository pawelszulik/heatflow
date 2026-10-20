using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Core.Dose;

/// <summary>Powiadomienia Fazy 5 (zmiana nastaw, włączenie, wyłączenie, samowyłączenie).</summary>
public interface IDoseNotifier
{
    Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Powiadomienie przez usługę HA notify.&lt;DoseNotifyService&gt; z celem DoseNotifyTarget - oba z
/// SystemConfiguration (u nas homeintegrator = Discord, ten sam kanał co alarmy kotła w HA).
/// Brak konfiguracji = ostrzeżenie w logu, nic nie wysyła. Błąd wysyłki nie przerywa fazy.
/// </summary>
public class HomeAssistantDiscordNotifier : IDoseNotifier
{
    private readonly IHomeAssistantClient _haClient;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<HomeAssistantDiscordNotifier> _logger;

    public HomeAssistantDiscordNotifier(IHomeAssistantClient haClient, IConfigurationService configurationService,
        ILogger<HomeAssistantDiscordNotifier> logger)
    {
        _haClient = haClient;
        _configurationService = configurationService;
        _logger = logger;
    }

    public async Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var config = await _configurationService.GetSystemConfigurationAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(config.DoseNotifyService) || string.IsNullOrWhiteSpace(config.DoseNotifyTarget))
        {
            _logger.LogWarning("Faza 5: brak DoseNotifyService/DoseNotifyTarget w SystemConfiguration - nie wysyłam: {Title}", title);
            return;
        }

        var ok = await _haClient.CallServiceAsync("notify", config.DoseNotifyService,
            new { target = config.DoseNotifyTarget, title, message }, cancellationToken);
        if (!ok)
        {
            _logger.LogWarning("Faza 5: nie udało się wysłać powiadomienia: {Title}", title);
        }
    }
}

/// <summary>
/// Czasy potwierdzania zapisu w mapperze. Mapper trzyma odczyt 10 s, a zapis dmuchawy z HA
/// był w sterowniku po 25 s (test 2026-09-16) - stąd pierwsze sprawdzenie po 20 s i limit 60 s.
/// ekoPiec ma limiter zapisów, więc przy braku potwierdzenia jedna ponowna próba po 15 s
/// (jak automatyzacja #24). Testy ustawiają zera.
/// </summary>
public class DoseWriteOptions
{
    public TimeSpan ConfirmFirstCheck { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan ConfirmPollInterval { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ConfirmTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(15);
}
