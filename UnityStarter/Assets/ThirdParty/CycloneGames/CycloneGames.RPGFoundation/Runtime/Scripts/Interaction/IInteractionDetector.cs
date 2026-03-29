using System.Collections.Generic;
using R3;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    /// <summary>
    /// Contract for a component that scans the environment, scores interactable candidates,
    /// and tracks the best (or player-cycled) interaction target.
    /// Attach the implementation to the player or camera.
    /// </summary>
    public interface IInteractionDetector
    {
        /// <summary>The current best interaction target, exposed as a reactive property for UI binding.</summary>
        ReadOnlyReactiveProperty<IInteractable> CurrentInteractable { get; }

        /// <summary>
        /// All valid candidates from the last detection scan, sorted by score (highest first).
        /// Zero-GC: the returned list is an internal buffer — do NOT cache the reference.
        /// Updated each detection cycle. Use for PUBG-style loot lists or target cycling.
        /// </summary>
        IReadOnlyList<InteractionCandidate> NearbyInteractables { get; }

        /// <summary>
        /// Fired after each detection scan with the updated nearby list.
        /// Use this instead of polling <see cref="NearbyInteractables"/> for event-driven UI.
        /// </summary>
        event System.Action<IReadOnlyList<InteractionCandidate>> OnNearbyInteractablesChanged;

        /// <summary>The active detection algorithm. Can be changed at runtime to switch between modes.</summary>
        DetectionMode DetectionMode { get; set; }

        /// <summary>Bitwise channel mask controlling which interactable categories are detected. Writable at runtime.</summary>
        InteractionChannel ChannelMask { get; set; }

        /// <summary>Trigger the default interaction on the current target via VitalRouter command.</summary>
        void TryInteract();

        /// <summary>
        /// Trigger a specific action by its <see cref="InteractionAction.ActionId"/> on the current target.
        /// </summary>
        void TryInteract(string actionId);

        /// <summary>
        /// Cycle to the next or previous candidate in the nearby list.
        /// Pass +1 for next, -1 for previous. Useful for gamepad target switching.
        /// </summary>
        void CycleTarget(int direction);

        /// <summary>
        /// Trigger the default interaction on ALL nearby candidates simultaneously.
        /// Useful for "pick up all nearby items" or batch interaction scenarios.
        /// </summary>
        void TryInteractAll();

        /// <summary>
        /// Trigger a specific action on ALL nearby candidates simultaneously.
        /// </summary>
        void TryInteractAll(string actionId);

        /// <summary>Enable or disable the detection scan loop at runtime.</summary>
        void SetDetectionEnabled(bool enabled);
    }
}