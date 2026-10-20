using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Core.Dose;

/// <summary>Powiadomienia Fazy 5 (zmiana nastaw, włączenie, wyłączenie, samowyłączenie).</summary>
public interface IDoseNotifier
{
    Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Discord przez usługę HA notify.homeintegrator - ten sam kanał (target) co automatyzacje
/// kotła w HA (vm100-homeassistant-grafana.md § 6). Błąd wysyłki nie przerywa fazy.
/// </summary>
public class HomeAssistantDiscordNotifier : IDoseNotifier
{
    private const string NotifyService = "homeintegrator";
    private const string DiscordTarget = "1458898223593689120";

    private readonly IHomeAssistantClient _haClient;
    private readonly ILogger<HomeAssistantDiscordNotifier> _logger;

    public HomeAssistantDiscordNotifier(IHomeAssistantClient haClient, ILogger<HomeAssistantDiscordNotifier> logger)
    {
        _haClient = haClient;
        _logger = logger;
    }

    public async Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var ok = await _haClient.CallServiceAsync("notify", NotifyService,
            new { target = DiscordTarget, title, message }, cancellationToken);
        if (!ok)
        {
            _logger.LogWarning("Faza 5: nie udało się wysłać powiadomienia na Discord: {Title}", title);
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
