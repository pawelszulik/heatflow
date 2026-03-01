namespace HeatFlow.Core.Phases;

public record ValveResult(
    string RoomName,
    string ValveEntityId,
    decimal TempSet,
    decimal? TempActual,
    bool Success,
    int RetryCount
);
