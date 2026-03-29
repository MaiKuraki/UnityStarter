namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    /// <summary>
    /// Reason why an interaction was cancelled.
    /// Passed to <see cref="IInteractable.OnInteractionCancelled"/> for gameplay reactions and UI feedback.
    /// </summary>
    public enum InteractionCancelReason : byte
    {
        /// <summary>Player or code explicitly cancelled (e.g., pressed cancel button, called ForceEndInteraction).</summary>
        Manual = 0,

        /// <summary>The instigator moved beyond <see cref="IInteractable.MaxInteractionRange"/> during the interaction.</summary>
        OutOfRange = 1,

        /// <summary>External gameplay interruption (e.g., took damage, staggered, stunned).</summary>
        Interrupted = 2,

        /// <summary>The interaction exceeded its allowed time.</summary>
        Timeout = 3,

        /// <summary>The interactable object was destroyed during the interaction.</summary>
        TargetDestroyed = 4,

        /// <summary>The InteractionSystem was shut down or the scene was unloaded.</summary>
        SystemShutdown = 5
    }
}
