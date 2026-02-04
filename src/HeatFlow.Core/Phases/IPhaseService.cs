using HeatFlow.Domain;

namespace HeatFlow.Core.Phases;

/// <summary>
/// Interfejs serwisu fazy algorytmu sterowania grzaniem.
/// </summary>
public interface IPhaseService
{
    /// <summary>
    /// Numer fazy (0-5).
    /// </summary>
    int PhaseNumber { get; }

    /// <summary>
    /// Wykonuje fazę algorytmu.
    /// </summary>
    /// <param name="state">Stan systemu grzania.</param>
    /// <param name="parameters">Parametry algorytmu.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    /// <returns>Wynik wykonania fazy.</returns>
    Task<PhaseResult> ExecuteAsync(
        HeatingState state,
        HeatingParameters parameters,
        CancellationToken cancellationToken = default);
}
