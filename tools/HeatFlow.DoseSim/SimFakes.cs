using System.Globalization;
using HeatFlow.Core.Dose;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.Database;
using HeatFlow.Infrastructure.EsterownikMapper;
using HeatFlow.Infrastructure.HomeAssistant;

namespace HeatFlow.DoseSim;

/// <summary>Zegar symulacji; strefa jak w domu (Europe/Warsaw) - od niej zależy „północ” korekty k.</summary>
public sealed class SimClock : TimeProvider
{
    public DateTime UtcNow { get; set; }
    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(UtcNow, DateTimeKind.Utc));
    public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
}

/// <summary>
/// Sterownik eCoal + eSterownikMapper w jednym: number.set_value z HA zmienia rejestr, mapper go
/// zwraca. Usterki: <see cref="DropWrites"/> (HA mówi ok, sterownik nie przyjmuje), <see cref="Down"/>.
/// Każdy przyjęty zapis trafia do <see cref="History"/> - do sprawdzania twardych granic.
/// </summary>
public sealed class SimController : IEsterownikMapperClient
{
    public SimController(ControllerFeedSettings initial) => Settings = initial;

    public ControllerFeedSettings Settings { get; private set; }
    public bool DropWrites { get; set; }
    /// <summary>Tyle kolejnych zapisów sterownik „zgubi” (HA odpowie ok) - potem przyjmuje normalnie.</summary>
    public int DropNextWrites { get; set; }
    public int DroppedWrites { get; private set; }
    public bool Down { get; set; }
    public List<(DateTime Utc, string Entity, int Value)> History { get; } = new();
    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    /// <summary>Encje, które sterownik „ma” (zapis do innej = wyjątek).</summary>
    public DoseEntities Entities { get; set; } = SimDefaults.Entities;

    public Task<ControllerFeedSettings?> GetFeedSettingsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Down ? null : Settings);

    public bool Write(string entity, int value)
    {
        if (DropWrites) { DroppedWrites++; return true; }
        if (DropNextWrites > 0) { DropNextWrites--; DroppedWrites++; return true; }
        if (entity == Entities.FeederWork) Settings = Settings with { FeederWork = value };
        else if (entity == Entities.FeederPause) Settings = Settings with { FeederPause = value };
        else if (entity == Entities.Blower) Settings = Settings with { Blower = value };
        else throw new InvalidOperationException($"Faza 5 próbowała zapisać niedozwoloną encję {entity}");
        History.Add((Now(), entity, value));
        return true;
    }
}

/// <summary>Home Assistant w pamięci: stany encji, number.set_value idzie do <see cref="SimController"/>.</summary>
public sealed class SimHomeAssistant : IHomeAssistantClient
{
    private readonly SimController _controller;
    public SimHomeAssistant(SimController controller) => _controller = controller;

    public Dictionary<string, EntityState> States { get; } = new();
    public List<(string Domain, string Service)> Calls { get; } = new();

    public void Set(string entityId, string? value, DateTime updatedUtc)
    {
        States[entityId] = new EntityState
        {
            EntityId = entityId,
            State = value ?? "unavailable",
            LastUpdated = DateTime.SpecifyKind(updatedUtc, DateTimeKind.Utc),
            LastChanged = DateTime.SpecifyKind(updatedUtc, DateTimeKind.Utc)
        };
    }

    public void Set(string entityId, double? value, DateTime updatedUtc)
        => Set(entityId, value?.ToString(CultureInfo.InvariantCulture), updatedUtc);

    public Task<EntityState?> GetStateAsync(string entityId, CancellationToken cancellationToken = default)
        => Task.FromResult(States.GetValueOrDefault(entityId));

    public Task<string?> GetStateValueAsync(string entityId, CancellationToken cancellationToken = default)
        => Task.FromResult(States.GetValueOrDefault(entityId)?.State);

    public Task<double?> GetStateDoubleAsync(string entityId, CancellationToken cancellationToken = default)
        => Task.FromResult(double.TryParse(States.GetValueOrDefault(entityId)?.State, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var v) ? v : (double?)null);

    public Task<bool?> GetStateBoolAsync(string entityId, CancellationToken cancellationToken = default)
        => Task.FromResult(HomeAssistantClient.ParseBoolState(States.GetValueOrDefault(entityId)?.State));

    public async Task<int?> GetStateIntAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var v = await GetStateDoubleAsync(entityId, cancellationToken);
        return v.HasValue ? (int)Math.Round(v.Value) : null;
    }

    public Task<bool> SetNumberValueAsync(string entityId, double value, CancellationToken cancellationToken = default)
    {
        Calls.Add(("number", "set_value"));
        return Task.FromResult(_controller.Write(entityId, (int)Math.Round(value)));
    }

    public Task<bool> SetInputNumberValueAsync(string entityId, double value, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Faza 5 nie pisze input_number");

    public Task<bool> SetBooleanValueAsync(string entityId, bool value, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Faza 5 nie pisze input_boolean");

    public Task<bool> SetClimateTemperatureAsync(string entityId, double temperature, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Faza 5 nie pisze climate");

    public Task<bool> CallServiceAsync(string domain, string service, object? serviceData = null, CancellationToken cancellationToken = default)
    {
        Calls.Add((domain, service));
        return Task.FromResult(true);
    }

    public Task<bool> EntityExistsAsync(string entityId, CancellationToken cancellationToken = default)
        => Task.FromResult(States.ContainsKey(entityId));
}

/// <summary>Repozytorium Fazy 5 w pamięci (z dziennikiem decyzji).</summary>
public sealed class InMemoryDoseRepository : IDoseRepository
{
    public DoseAutomatSwitch Switch { get; } = new() { Id = 1 };
    public DoseState? State { get; set; }
    public List<DoseSample> Samples { get; } = new();
    public List<FeedChangeLog> Log { get; } = new();

    public Task<DoseAutomatSwitch> GetAutomatSwitchAsync(CancellationToken ct = default)
        => Task.FromResult(new DoseAutomatSwitch { Id = 1, Enabled = Switch.Enabled, ChangedBy = Switch.ChangedBy, ChangedAtUtc = Switch.ChangedAtUtc });

    public Task SetAutomatSwitchAsync(bool enabled, string changedBy, CancellationToken ct = default)
    {
        Switch.Enabled = enabled;
        Switch.ChangedBy = changedBy;
        return Task.CompletedTask;
    }

    public Task<DoseState?> GetStateAsync(CancellationToken ct = default) => Task.FromResult(State);

    public Task SaveStateAsync(DoseState state, CancellationToken ct = default)
    {
        State = state;
        return Task.CompletedTask;
    }

    public Task AddSampleAsync(DoseSample sample, CancellationToken ct = default)
    {
        Samples.Add(sample);
        return Task.CompletedTask;
    }

    public Task<List<DoseSample>> GetSamplesSinceAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        // Migawki są dopisywane rosnąco - wyszukiwanie binarne zamiast pełnego przejścia.
        var lo = 0;
        var hi = Samples.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (Samples[mid].TimestampUtc < sinceUtc) lo = mid + 1; else hi = mid;
        }
        return Task.FromResult(Samples.GetRange(lo, Samples.Count - lo));
    }

    public Task DeleteSamplesOlderThanAsync(DateTime olderThanUtc, CancellationToken ct = default)
    {
        var n = 0;
        while (n < Samples.Count && Samples[n].TimestampUtc < olderThanUtc) n++;
        if (n > 0) Samples.RemoveRange(0, n);
        return Task.CompletedTask;
    }

    public Task AddChangeLogAsync(IEnumerable<FeedChangeLog> entries, CancellationToken ct = default)
    {
        Log.AddRange(entries);
        return Task.CompletedTask;
    }

    public Task<List<FeedChangeLog>> GetChangeLogAsync(int limit, CancellationToken ct = default)
        => Task.FromResult(Log.AsEnumerable().Reverse().Take(limit).ToList());

    public Task<FeedChangeLog?> GetLastSuggestionNotificationAsync(CancellationToken ct = default)
        => Task.FromResult(Log.LastOrDefault(e => e.Parameter == "sugestia_discord"));

    public Task<FeedChangeLog> AddApplyRequestAsync(FeedChangeLog request, CancellationToken ct = default)
    {
        Log.Add(request);
        return Task.FromResult(request);
    }

    public Task<bool> AnyApplyRequestSinceAsync(DateTime sinceUtc, CancellationToken ct = default)
        => Task.FromResult(Log.Any(e => e.Parameter == "przycisk" && e.ReasonCode == "RECZNE_USTAW_SUGEROWANE" && e.TimestampUtc >= sinceUtc));

    public Task<FeedChangeLog?> GetPendingApplyRequestAsync(DateTime sinceUtc, CancellationToken ct = default)
        => Task.FromResult(Log.LastOrDefault(e => e.Parameter == "przycisk" && e.Status == "ZLECENIE" && e.TimestampUtc >= sinceUtc));

    public Task<int> ExpireApplyRequestsAsync(DateTime olderThanUtc, CancellationToken ct = default)
    {
        var old = Log.Where(e => e.Parameter == "przycisk" && e.Status == "ZLECENIE" && e.TimestampUtc < olderThanUtc).ToList();
        foreach (var e in old) e.Status = "ZLECENIE_WYGASLO";
        return Task.FromResult(old.Count);
    }

    public Task SetApplyRequestStatusAsync(FeedChangeLog request, string status, string message, CancellationToken ct = default)
    {
        request.Status = status;
        request.Message = message;
        return Task.CompletedTask;
    }
}

/// <summary>Parametry w pamięci - korekta k zapisuje tu nowe k.</summary>
public sealed class SimConfigurationService : IConfigurationService
{
    public SimConfigurationService(HeatingParameters parameters) => Parameters = parameters;
    public HeatingParameters Parameters { get; private set; }
    public List<(DateTime Utc, double K)> KHistory { get; } = new();
    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    public Task<HeatingParameters> GetHeatingParametersAsync(CancellationToken cancellationToken = default) => Task.FromResult(Parameters);

    public Task SaveHeatingParametersAsync(HeatingParameters parameters, CancellationToken cancellationToken = default)
    {
        KHistory.Add((Now(), parameters.FeedCorrectionK));
        Parameters.FeedCorrectionK = parameters.FeedCorrectionK;
        return Task.CompletedTask;
    }

    public Task UpdateHeatingParametersAsync(HeatingParameters parameters, CancellationToken cancellationToken = default)
        => SaveHeatingParametersAsync(parameters, cancellationToken);

    public SystemConfiguration System { get; set; } = SimDefaults.SystemConfiguration();
    public List<RoomConfiguration> Rooms { get; set; } = SimDefaults.IndoorRooms();

    public Task<List<RoomConfiguration>> GetAllRoomsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Rooms);
    public Task<RoomConfiguration?> GetRoomAsync(string roomName, CancellationToken cancellationToken = default)
        => Task.FromResult(Rooms.FirstOrDefault(r => r.Name == roomName));
    public Task SaveRoomAsync(RoomConfiguration roomConfig, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<SystemConfiguration> GetSystemConfigurationAsync(CancellationToken cancellationToken = default) => Task.FromResult(System);
    public Task SaveSystemConfigurationAsync(SystemConfiguration systemConfig, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class NullAuditService : IConfigurationAuditService
{
    public Task LogRoomChangesAsync(string roomName, RoomConfiguration? oldValue, RoomConfiguration newValue, string? source = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task LogHeatingParametersChangesAsync(HeatingParameters? oldValue, HeatingParameters newValue, string? source = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task LogSystemConfigurationChangesAsync(SystemConfiguration? oldValue, SystemConfiguration newValue, string? source = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Błędy fazy w symulacji - zbierane, test/raport ma je pokazać, nie połknąć.</summary>
public sealed class CollectingErrorLogger : IApplicationErrorLogger
{
    public List<string> Errors { get; } = new();

    public Task LogAsync(Exception? ex, int? phase, string? source, object? context = null, string severity = "Error", string? origin = null, CancellationToken cancellationToken = default)
    {
        Errors.Add($"{source}: {ex}");
        return Task.CompletedTask;
    }

    public Task LogAsync(string message, int? phase, string? source, object? context = null, string severity = "Error", string? origin = null, CancellationToken cancellationToken = default)
    {
        Errors.Add($"{source}: {message}");
        return Task.CompletedTask;
    }
}

/// <summary>Discord w pamięci.</summary>
public sealed class CapturingNotifier : IDoseNotifier
{
    public List<(DateTime Utc, string Title, string Message)> Messages { get; } = new();
    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    public Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        Messages.Add((Now(), title, message));
        return Task.CompletedTask;
    }
}
