using HeatFlow.Domain;
using HeatFlow.Infrastructure.EsterownikMapper;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Core.Dose;

/// <summary>Trzy nastawy kotła, które Faza 5 może zmieniać. Nic innego - nigdy.</summary>
public enum DoseRegister
{
    /// <summary>number.kociol_czas_pracy_podajnika → rrGPodOn.</summary>
    Work,
    /// <summary>number.kociol_czas_postoju_podajnika → rrGPodOff.</summary>
    Pause,
    /// <summary>number.kociol_moc_dmuchawy → rrGDiffuserMotorSpeed.</summary>
    Blower
}

/// <summary>Zapis jednego rejestru: z ilu na ile i co powiedział mapper (OK / BRAK / ROZJAZD).</summary>
public record DoseRegisterWrite(DoseRegister Register, int From, int To, string Confirmation, int? Seen);

/// <summary>Wynik zapisu nastaw: czy wszystko potwierdził mapper, co sterownik ma na końcu, opis, zapisy po kolei.</summary>
public record DoseWriteResult(bool Confirmed, ControllerFeedSettings? Last, string Report)
{
    public IReadOnlyList<DoseRegisterWrite> Writes { get; init; } = Array.Empty<DoseRegisterWrite>();
}

/// <summary>
/// Zapis nastaw do sterownika przez HA (number.set_value) z potwierdzeniem w eSterownikMapperze
/// (GetAllValues), nie ze stanu encji HA. Pisze po jednym rejestrze i czeka na potwierdzenie,
/// zanim ruszy następny - ekoPiec ma limiter zapisów. Wartości poza twardymi granicami
/// (<see cref="DoseLimits"/>) nie są wysyłane nigdy.
/// </summary>
public class DoseControllerWriter
{
    public const string WorkEntityId = "number.kociol_czas_pracy_podajnika";
    public const string PauseEntityId = "number.kociol_czas_postoju_podajnika";
    public const string BlowerEntityId = "number.kociol_moc_dmuchawy";

    private readonly IHomeAssistantClient _haClient;
    private readonly IEsterownikMapperClient _mapper;
    private readonly DoseWriteOptions _options;
    private readonly ILogger<DoseControllerWriter> _logger;

    public DoseControllerWriter(
        IHomeAssistantClient haClient,
        IEsterownikMapperClient mapper,
        DoseWriteOptions options,
        ILogger<DoseControllerWriter> logger)
    {
        _haClient = haClient;
        _mapper = mapper;
        _options = options;
        _logger = logger;
    }

    public static string EntityId(DoseRegister register) => register switch
    {
        DoseRegister.Work => WorkEntityId,
        DoseRegister.Pause => PauseEntityId,
        _ => BlowerEntityId
    };

    public static int Value(ControllerFeedSettings settings, DoseRegister register) => register switch
    {
        DoseRegister.Work => settings.FeederWork,
        DoseRegister.Pause => settings.FeederPause,
        _ => settings.Blower
    };

    public static bool WithinHardLimits(DoseRegister register, int value) => register switch
    {
        DoseRegister.Work => value is >= DoseLimits.WorkMin and <= DoseLimits.WorkMax,
        DoseRegister.Pause => value is >= DoseLimits.PauseMin and <= DoseLimits.PauseMax,
        _ => value is >= DoseLimits.BlowerMin and <= DoseLimits.BlowerMax
    };

    /// <summary>
    /// Zapisuje te z trzech nastaw, które różnią się od <paramref name="current"/>. Przerywa na
    /// pierwszym niepotwierdzonym zapisie.
    /// </summary>
    public async Task<DoseWriteResult> WriteSettingsAsync(
        ControllerFeedSettings current, int work, int pause, int blower, CancellationToken cancellationToken = default)
    {
        var report = new List<string>();
        var writes = new List<DoseRegisterWrite>();
        var last = current;
        foreach (var (register, value) in new[] { (DoseRegister.Work, work), (DoseRegister.Pause, pause), (DoseRegister.Blower, blower) })
        {
            var from = Value(last, register);
            if (from == value)
            {
                continue;
            }

            var result = await WriteAsync(register, value, cancellationToken);
            report.Add(result.Report);
            writes.Add(result.Writes.Count > 0 ? result.Writes[0] with { From = from } : new DoseRegisterWrite(register, from, value, "BRAK", null));
            last = result.Last ?? last;
            if (!result.Confirmed)
            {
                return new DoseWriteResult(false, result.Last, string.Join("; ", report)) { Writes = writes };
            }
        }

        return new DoseWriteResult(true, last, report.Count == 0 ? "bez zmian" : string.Join("; ", report)) { Writes = writes };
    }

    /// <summary>Zapis jednego rejestru: set_value → potwierdzenie w mapperze; jedna ponowna próba.</summary>
    public async Task<DoseWriteResult> WriteAsync(DoseRegister register, int value, CancellationToken cancellationToken = default)
    {
        var entityId = EntityId(register);
        if (!WithinHardLimits(register, value))
        {
            _logger.LogError("Faza 5: odmowa zapisu {Entity} = {Value} - poza twardymi granicami", entityId, value);
            return new DoseWriteResult(false, null, $"{entityId} = {value} poza twardymi granicami - nie wysłano")
            {
                Writes = new[] { new DoseRegisterWrite(register, value, value, "ODMOWA", null) }
            };
        }

        ControllerFeedSettings? lastSeen = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var sent = await _haClient.SetNumberValueAsync(entityId, value, cancellationToken);
            _logger.LogInformation("Faza 5: number.set_value {Entity} = {Value} (próba {Attempt}): HA {Result}",
                entityId, value, attempt, sent ? "ok" : "błąd");

            if (sent)
            {
                (var confirmed, lastSeen) = await ConfirmAsync(register, value, cancellationToken);
                if (confirmed)
                {
                    return new DoseWriteResult(true, lastSeen, $"{entityId} = {value} potwierdzone w mapperze")
                    {
                        Writes = new[] { new DoseRegisterWrite(register, value, value, "OK", value) }
                    };
                }
            }

            if (attempt == 1)
            {
                await Task.Delay(_options.RetryDelay, cancellationToken);
            }
        }

        var seen = lastSeen == null ? "mapper nie odpowiada" : $"sterownik ma {Value(lastSeen, register)}";
        return new DoseWriteResult(false, lastSeen, $"{entityId} = {value} NIEPOTWIERDZONE ({seen})")
        {
            Writes = new[]
            {
                new DoseRegisterWrite(register, value, value, lastSeen == null ? "BRAK" : "ROZJAZD",
                    lastSeen == null ? null : Value(lastSeen, register))
            }
        };
    }

    private async Task<(bool Confirmed, ControllerFeedSettings? Last)> ConfirmAsync(
        DoseRegister register, int value, CancellationToken cancellationToken)
    {
        await Task.Delay(_options.ConfirmFirstCheck, cancellationToken);
        var checks = _options.ConfirmPollInterval > TimeSpan.Zero
            ? 1 + (int)Math.Max(0, (_options.ConfirmTimeout - _options.ConfirmFirstCheck) / _options.ConfirmPollInterval)
            : 1;

        ControllerFeedSettings? last = null;
        for (var i = 0; i < checks; i++)
        {
            if (i > 0)
            {
                await Task.Delay(_options.ConfirmPollInterval, cancellationToken);
            }

            last = await _mapper.GetFeedSettingsAsync(cancellationToken) ?? last;
            if (last != null && Value(last, register) == value)
            {
                return (true, last);
            }
        }

        return (false, last);
    }
}
