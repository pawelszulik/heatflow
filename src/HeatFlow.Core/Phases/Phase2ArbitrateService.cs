using HeatFlow.Core.Utils;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace HeatFlow.Core.Phases;

/// <summary>
/// Faza 2 - Arbitraż i priorytetyzacja pokoi.
/// Wybiera maksymalnie 5 pokoi do grzania na podstawie score.
/// </summary>
public class Phase2ArbitrateService : IPhaseService
{
    private readonly IHomeAssistantClient _haClient;
    private readonly IApplicationErrorLogger _errorLogger;
    private readonly ILogger<Phase2ArbitrateService> _logger;

    public int PhaseNumber => 2;

    public Phase2ArbitrateService(
        IHomeAssistantClient haClient,
        IApplicationErrorLogger errorLogger,
        ILogger<Phase2ArbitrateService> logger)
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
            var enabledRooms = state.GetEnabledRooms();

            if (enabledRooms.Count == 0)
            {
                // Wszystkie pokoje wyjęte z automatyki - nie ma czym sterować. To stan
                // konfiguracyjny, nie awaria, więc kończymy spokojnie zamiast rzucać wyjątkiem.
                _logger.LogWarning(
                    "Faza 2: żaden pokój nie jest objęty automatyką (automationDisabled na wszystkich) - nic nie robię");

                state.RoomsToHot = new List<Room>();
                state.RoomsToStay = new List<Room>();
                state.RoomsToDisable = new List<Room>();

                return PhaseResult.SuccessResult(PhaseNumber,
                    (long)(DateTime.UtcNow - startTime).TotalMilliseconds,
                    "Brak pokoi objętych automatyką - 0 zaworów otwartych");
            }

            // Pokoje na pełnej mocy - z dwell, żeby nie przerzucać zaworów co 5 minut.
            state.RoomsToHot = SelectRoomsToHeat(enabledRooms, parameters, state);

            // MinValvesOpen: piec nie może pracować z wszystkimi zaworami zamkniętymi,
            // więc zawsze utrzymujemy minimum otwartych obiegów (co najmniej jeden).
            var minOtwartych = Math.Min(
                Math.Max(1, parameters.MinValvesOpen),
                parameters.MaxValvesOpen);

            while (state.RoomsToHot.Count < minOtwartych)
            {
                // Pokój bezpieczeństwa: najzimniejszy z pozostałych.
                var safetyRoom = enabledRooms
                    .Except(state.RoomsToHot)
                    .OrderBy(r => r.TempActual)
                    .FirstOrDefault();

                if (safetyRoom is null)
                {
                    break; // mniej pokoi niż minimum - nie ma czego dobrać
                }

                safetyRoom.SetSafetyRoom();
                state.RoomsToHot.Add(safetyRoom);
            }

            // Balast przepływu: wolne sloty dostają pokoje Stay o NAJNIŻSZYM Score - celowo,
            // bo Stay ustawia nastawę na aktualną temperaturę pokoju, więc mało potrzebujący
            // pokój daje przepływ powrotny, prawie nie zabierając ciepła pokojom w Max.
            // To nie jest literówka - nie zmieniać na OrderByDescending.
            var wolneSloty = parameters.MaxValvesOpen - state.RoomsToHot.Count;
            state.RoomsToStay = wolneSloty <= 0
                ? new List<Room>()
                : enabledRooms
                    .Where(r => r.DeficitClassification == DeficitClassification.Stay)
                    .Except(state.RoomsToHot)
                    .OrderBy(r => r.Score)
                    .Take(wolneSloty)
                    .ToList();

            // INVARIANT: liczba otwartych zaworów (Hot + Stay) nigdy nie przekracza
            // MaxValvesOpen - przy większej liczbie otwartych obiegów spada wydajność pieca.
            var otwarte = state.RoomsToHot.Concat(state.RoomsToStay).ToList();

            foreach (var room in otwarte)
            {
                room.HeatingEnabled = true;
            }

            state.RoomsToDisable = enabledRooms.Except(otwarte).ToList();

            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            var przypietych = state.RoomsToHot.Count(r => TrzymaSlotPrzezDwell(r, parameters, state));
            _logger.LogInformation(
                "Faza 2 wykonana: pełna moc {Hot}, balast {Stay}, zamknięte {Disable}, limit {Limit}, utrzymane przez dwell {Dwell}",
                state.RoomsToHot.Count, state.RoomsToStay.Count, state.RoomsToDisable.Count,
                parameters.MaxValvesOpen, przypietych);

            var details = $"Wybrano {otwarte.Count} pokoi (pełna moc: {state.RoomsToHot.Count}, "
                + $"balast: {state.RoomsToStay.Count}, limit: {parameters.MaxValvesOpen}, dwell: {przypietych})";

            return PhaseResult.SuccessResult(PhaseNumber, duration, details);
        }
        catch (Exception ex)
        {
            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Błąd podczas wykonania Fazę 2");
            await _errorLogger.LogAsync(ex, PhaseNumber, nameof(Phase2ArbitrateService), null, "Error", "Console", cancellationToken);
            return PhaseResult.ErrorResult(PhaseNumber, ex.Message, duration);
        }
    }

    private List<Room> SelectRoomsToHeat(
        List<Room> rooms,
        HeatingParameters parameters,
        HeatingState state)
    {
        var kandydaci = rooms
            .Where(r => r.DeficitClassification == DeficitClassification.Max)
            .OrderByDescending(r => r.Score)
            .ToList();

        // Pokoje w realnym dołku wywłaszczają dwell - wychłodzenie ponad próg bezpieczeństwa
        // jest ważniejsze niż spokój głowic.
        var pilni = kandydaci
            .Where(r => r.TempDeficit >= parameters.HysteresisSafetyThreshold)
            .ToList();

        // Dwell: pokój, który w poprzednim cyklu grzał na pełnej mocy i nie minął jeszcze
        // MinDwellMinutes, trzyma swój slot nawet jeśli spadł mu Score. Bez tego głowica
        // przestawiana co 5 minut nigdy nie dojeżdża do zadanej pozycji, a pokój huśta się
        // między 5°C i maksimum - to jest źródło przeregulowań i spalonego węgla.
        var przypieci = rooms
            .Where(r => !pilni.Contains(r) && TrzymaSlotPrzezDwell(r, parameters, state))
            .OrderByDescending(r => r.Score)
            .ToList();

        var wybrani = pilni
            .Concat(przypieci)
            .Concat(kandydaci.Where(r => !pilni.Contains(r) && !przypieci.Contains(r)))
            .Take(parameters.MaxValvesOpen) // limit zaworów ZAWSZE wygrywa z dwell
            .ToList();

        // Pokój utrzymany przez dwell mógł już zostać sklasyfikowany niżej w Fazie 1 -
        // podnosimy go z powrotem, żeby Faza 3 wysłała pełną nastawę.
        foreach (var room in wybrani.Where(r => r.DeficitClassification != DeficitClassification.Max))
        {
            room.KeepHeating();
        }

        return wybrani;
    }

    /// <summary>
    /// Czy pokój utrzymuje przydzielony zawór na mocy dwell (anti-flap).
    /// Czas liczony w UTC, bo ClassificationSince zapisywany jest w UTC
    /// (HeatingState.CurrentTime jest czasem lokalnym - nie mieszać).
    /// </summary>
    private static bool TrzymaSlotPrzezDwell(Room room, HeatingParameters parameters, HeatingState state)
    {
        if (parameters.MinDwellMinutes <= 0)
        {
            return false;
        }

        if (state.PreviousClassification(room.Name) != DeficitClassification.Max)
        {
            return false;
        }

        var since = state.ClassificationSince(room.Name);
        if (since is null || (DateTime.UtcNow - since.Value).TotalMinutes >= parameters.MinDwellMinutes)
        {
            return false;
        }

        // Przegrzany pokój zwalnia slot od razu - trzymanie go na pełnej mocy to spalony węgiel.
        return room.TempDeficit > -parameters.Hysteresis;
    }
}
