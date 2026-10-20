using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Infrastructure.EsterownikMapper;

/// <summary>
/// Nastawy grzania w trybie Retorta-ręczny, odczytane bezpośrednio ze sterownika eCoal przez mapper.
/// </summary>
/// <param name="FeederWork">Czas pracy podajnika [s] - rejestr rrGPodOn (rr_g_pod_on).</param>
/// <param name="FeederPause">Czas postoju podajnika [s] - rejestr rrGPodOff (rr_g_pod_off).</param>
/// <param name="Blower">Moc dmuchawy w grzaniu [%] - rejestr rrGDiffuserMotorSpeed (rr_g_dm_speed).</param>
public record ControllerFeedSettings(int FeederWork, int FeederPause, int Blower);

/// <summary>
/// Odczyt nastaw kotła z eSterownikMappera. Potwierdzenie zapisu z HA czytamy stąd, nie ze stanu
/// encji number.* w HA - ta po number.set_value pokazuje wpisaną wartość od razu, niezależnie od
/// tego, czy doszła do sterownika (test 2026-09-16, plan-heatflow-algorytm.md).
/// </summary>
public interface IEsterownikMapperClient
{
    /// <summary>Null, gdy mapper nie odpowiedział albo nie zdołał odczytać kotła (jego 404).</summary>
    Task<ControllerFeedSettings?> GetFeedSettingsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// GET {BaseUrl}/readdata/GetAllValues. Pola w JSON mają nazwy właściwości modelu ECoalSync
/// w camelCase (sprawdzone na żywym mapperze 2026-10-20: rrGPodOn = 7, rrGPodOff = 160,
/// rrGDiffuserMotorSpeed = 35). GetField potrafi zwrócić 404 przy kolizji z pollingiem HA,
/// GetAllValues działa - dlatego tylko ten endpoint. Mapper trzyma odczyt 10 s.
/// </summary>
public class EsterownikMapperClient : IEsterownikMapperClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EsterownikMapperClient> _logger;

    public EsterownikMapperClient(HttpClient httpClient, ILogger<EsterownikMapperClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ControllerFeedSettings?> GetFeedSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_httpClient.BaseAddress == null)
        {
            // Brak EsterownikMapper:BaseUrl w appsettings - bez zgadywania adresu; Faza 5 nie potwierdzi
            // zapisu, więc automat się nie włączy (ostrzeżenie loguje też Program przy starcie).
            _logger.LogWarning("eSterownikMapper nie skonfigurowany (EsterownikMapper:BaseUrl) - brak odczytu nastaw kotła");
            return null;
        }

        try
        {
            using var response = await _httpClient.GetAsync("readdata/GetAllValues", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("eSterownikMapper GetAllValues: HTTP {Status}", (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return Parse(doc.RootElement);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "eSterownikMapper GetAllValues nie odpowiedział");
            return null;
        }
    }

    /// <summary>Wyciąga trzy rejestry z odpowiedzi GetAllValues; null, gdy któregoś brak.</summary>
    public static ControllerFeedSettings? Parse(JsonElement root)
    {
        var work = ReadInt(root, "rrGPodOn");
        var pause = ReadInt(root, "rrGPodOff");
        var blower = ReadInt(root, "rrGDiffuserMotorSpeed");
        if (work == null || pause == null || blower == null)
        {
            return null;
        }

        return new ControllerFeedSettings(work.Value, pause.Value, blower.Value);
    }

    private static int? ReadInt(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var prop in root.EnumerateObject())
        {
            if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return prop.Value.ValueKind switch
            {
                JsonValueKind.Number when prop.Value.TryGetDouble(out var d) => (int)Math.Round(d),
                JsonValueKind.String when double.TryParse(prop.Value.GetString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var d) => (int)Math.Round(d),
                _ => null
            };
        }

        return null;
    }
}
