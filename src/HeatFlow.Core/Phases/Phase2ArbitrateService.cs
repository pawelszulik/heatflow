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
            
            // Wybierz pokoje do grzania
            var selectedRooms = SelectRoomsToHeat(enabledRooms, parameters);

            state.RoomsToHot = selectedRooms;

            if (selectedRooms.Count == 0)
            {
                // Dodaj pokój bezpieczeństwa (najwyższy priorytet z pozostałych)
                var safetyRoom = enabledRooms.OrderByDescending(r => r.Score).First();
                safetyRoom.SetSafetyRoom();
                state.RoomsToHot.Add(safetyRoom);
                selectedRooms.Add(safetyRoom);
            }

            // Dodaj te Stay, jeśli jest miejsce
            if (selectedRooms.Count < parameters.MaxValvesOpen)
            {
                var rowsToAdd = enabledRooms.Where(r => r.DeficitClassification == DeficitClassification.Stay).OrderBy(x => x.Score).Take(parameters.MaxValvesOpen - selectedRooms.Count);
                state.RoomsToStay = rowsToAdd.ToList(); 
                selectedRooms.AddRange(state.RoomsToStay);
            }

            foreach (Room selectedRoom in selectedRooms)
            {
                selectedRoom.HeatingEnabled = true;
            }

            state.RoomsToDisable = enabledRooms.Except(selectedRooms).ToList();

            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("Faza 2 wykonana: wybrano {Count} pokoi do grzania", selectedRooms.Count);

            return PhaseResult.SuccessResult(PhaseNumber, duration, $"Wybrano {selectedRooms.Count} pokoi");
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
        HeatingParameters parameters)
    {
        var candidateRooms = rooms
            .Where(r => r.DeficitClassification == DeficitClassification.Max).OrderByDescending(x => x.Score)
            .ToList();

        var selected = candidateRooms.Take(parameters.MaxValvesOpen).ToList();
        
        return selected;
    }
}
