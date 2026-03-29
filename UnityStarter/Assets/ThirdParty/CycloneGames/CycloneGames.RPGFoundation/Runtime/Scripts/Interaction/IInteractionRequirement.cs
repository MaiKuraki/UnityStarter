namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    /// <summary>
    /// A pluggable precondition that must be satisfied before an interaction can proceed.
    /// Attach implementations as components alongside <see cref="IInteractable"/> to add
    /// requirements such as key possession, level thresholds, or quest state checks.
    /// </summary>
    public interface IInteractionRequirement
    {
        /// <summary>Evaluate whether the requirement is currently met.</summary>
        /// <param name="target">The interactable being checked.</param>
        /// <param name="instigator">The instigator attempting the interaction (e.g., player).</param>
        /// <returns>True if the requirement is satisfied and interaction may proceed.</returns>
        bool IsMet(IInteractable target, InstigatorHandle instigator);

        /// <summary>Human-readable reason displayed to the player when this requirement is not met.</summary>
        string FailureReason { get; }
    }
}
