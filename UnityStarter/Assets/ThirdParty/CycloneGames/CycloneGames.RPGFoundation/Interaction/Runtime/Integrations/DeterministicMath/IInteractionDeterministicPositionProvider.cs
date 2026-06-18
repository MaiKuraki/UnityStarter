using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath
{
    /// <summary>
    /// Provides the fixed-point interaction position used by deterministic authority validation.
    /// Implementations should read from the same deterministic simulation state that drives lockstep,
    /// rollback, replay, or server-side authoritative movement.
    /// </summary>
    public interface IInteractionDeterministicPositionProvider
    {
        bool TryGetDeterministicInteractionPosition(out FPVector3 position);
    }
}
