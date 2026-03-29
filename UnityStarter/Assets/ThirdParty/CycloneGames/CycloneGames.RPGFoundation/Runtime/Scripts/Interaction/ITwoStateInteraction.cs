namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    /// <summary>
    /// Contract for interactables that toggle between two states (e.g., open/close, on/off).
    /// Implement alongside <see cref="IInteractable"/> for toggle-style interactions.
    /// </summary>
    public interface ITwoStateInteraction
    {
        /// <summary>Whether the interactable is currently in its activated (secondary) state.</summary>
        bool IsActivated { get; }

        /// <summary>Transition to the activated state.</summary>
        void ActivateState();

        /// <summary>Transition back to the deactivated (default) state.</summary>
        void DeactivateState();

        /// <summary>Switch to the opposite state: activate if deactivated, deactivate if activated.</summary>
        void ToggleState();
    }
}