using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Database;
using Microsoft.Extensions.Logging;

namespace HeatFlow.Application;

/// <summary>
/// Serwis do zapisywania stanów systemu do bazy danych.
/// </summary>
public class DataPersistenceService
{
    private readonly IHeatFlowRepository _repository;
    private readonly IApplicationErrorLogger _errorLogger;
    private readonly ILogger<DataPersistenceService> _logger;

    public DataPersistenceService(
        IHeatFlowRepository repository,
        IApplicationErrorLogger errorLogger,
        ILogger<DataPersistenceService> logger)
    {
        _repository = repository;
        _errorLogger = errorLogger;
        _logger = logger;
    }

    /// <summary>
    /// Zapisuje wyniki wykonania faz do bazy danych.
    /// </summary>
    public async Task SaveExecutionResultsAsync(
        List<PhaseResult> phaseResults,
        HeatingState state,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var executionTime = DateTime.UtcNow;
            var executionIds = new Dictionary<int, int>();

            // Zapisz historię wykonania dla każdej fazy
            foreach (var result in phaseResults)
            {
                var executionHistory = new ExecutionHistory
                {
                    ExecutionTime = executionTime,
                    Phase = result.PhaseNumber,
                    Status = result.Success ? "Success" : "Error",
                    DurationMs = result.DurationMs,
                    ErrorMessage = result.ErrorMessage,
                    Details = result.Details
                };

                var executionId = await _repository.SaveExecutionHistoryAsync(executionHistory, cancellationToken);
                executionIds[result.PhaseNumber] = executionId;
            }

            // Zapisz stany pokoi (z Fazę 1)
            var phase1ExecutionId = executionIds.GetValueOrDefault(1);
            if (phase1ExecutionId > 0)
            {
                foreach (var room in state.GetEnabledRooms())
                {
                    var roomState = new RoomState
                    {
                        ExecutionId = phase1ExecutionId,
                        RoomName = room.Name,
                        TempActual = (decimal)(room.TempActual ?? 0),
                        TempTarget = (decimal)room.GetTargetTemperature(
                            HeatFlow.Core.Utils.ScheduleHelper.IsTimeInRange(
                                state.CurrentTime,
                                room.HeatingSchedule,
                                state.IsWeekend)),
                        TempDeficit = (decimal)room.TempDeficit,
                        Classification = (int)room.DeficitClassification,
                        Score = (decimal)room.Score,
                        HeatingEnabled = room.HeatingEnabled,
                        RecordedAt = executionTime
                    };

                    await _repository.SaveRoomStateAsync(roomState, cancellationToken);
                }
            }

            // Zapisz stan pieca (z Fazę 4)
            var phase4ExecutionId = executionIds.GetValueOrDefault(4);
            if (phase4ExecutionId > 0 && state.BoilerState != null)
            {
                var boilerState = new BoilerStateEntity
                {
                    ExecutionId = phase4ExecutionId,
                    TempExternal = (decimal)state.BoilerState.TempExternal,
                    TempReturn = (decimal)state.BoilerState.TempReturn,
                    TempTarget = (decimal)state.BoilerState.TempTarget,
                    FeederTime = (decimal)state.BoilerState.FeederTime,
                    Mixer4DPosition = (decimal)state.BoilerState.Mixer4DPosition,
                    RoomsHeatedCount = state.BoilerState.RoomsHeatedCount,
                    ForecastMode = (int)state.BoilerState.ForecastMode,
                    RecordedAt = executionTime
                };

                await _repository.SaveBoilerStateAsync(boilerState, cancellationToken);
            }

            // Zapisz stany zaworów (z Fazy 3)
            var phase3Result = phaseResults.FirstOrDefault(r => r.PhaseNumber == 3);
            var phase3ExecutionId = executionIds.GetValueOrDefault(3);
            if (phase3ExecutionId > 0 && phase3Result?.ValveResults.Count > 0)
            {
                foreach (var valve in phase3Result.ValveResults)
                {
                    var valveState = new ValveState
                    {
                        ExecutionId = phase3ExecutionId,
                        RoomName = valve.RoomName,
                        ValveEntityId = valve.ValveEntityId,
                        TempSet = valve.TempSet,
                        TempActual = valve.TempActual,
                        Success = valve.Success,
                        RetryCount = valve.RetryCount,
                        RecordedAt = executionTime
                    };
                    await _repository.SaveValveStateAsync(valveState, cancellationToken);
                }
            }

            // Zapisz wszystkie zmiany
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Zapisano {Count} wyników wykonania do bazy danych", phaseResults.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas zapisywania wyników do bazy danych");
            await _errorLogger.LogAsync(ex, null, nameof(DataPersistenceService), "SaveExecutionResults", "Error", "Console", cancellationToken);
            // Nie rzucamy wyjątku - zapis do bazy nie powinien blokować działania systemu
        }
    }
}
