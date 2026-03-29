using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    /// <summary>
    /// Core contract for any object that can be interacted with in the world.
    /// Implement this interface on MonoBehaviours to enable detection, scoring, and interaction.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Fallback prompt text displayed when localization is not used.</summary>
        string InteractionPrompt { get; }

        /// <summary>Localization-ready prompt data (table name, key, fallback). Null if localization is disabled.</summary>
        InteractionPromptData? PromptData { get; }

        /// <summary>Whether this object is currently available for interaction.</summary>
        bool IsInteractable { get; }

        /// <summary>If true, the object triggers interaction automatically when detected (no input required).</summary>
        bool AutoInteract { get; }

        /// <summary>Whether an interaction is currently in progress on this object.</summary>
        bool IsInteracting { get; }

        /// <summary>Scoring priority. Higher values are preferred by the detection algorithm.</summary>
        int Priority { get; }

        /// <summary>World-space position used for distance and direction calculations. Cached per frame.</summary>
        Vector3 Position { get; }

        /// <summary>Maximum distance from the detector origin at which this object can be interacted with.</summary>
        float InteractionDistance { get; }

        /// <summary>Current state in the interaction lifecycle (Idle, Starting, InProgress, etc.).</summary>
        InteractionStateType CurrentState { get; }

        /// <summary>Channel flags for selective detection filtering.</summary>
        InteractionChannel Channel { get; }

        /// <summary>Pluggable conditions that must all be met before interaction is allowed.</summary>
        IReadOnlyList<IInteractionRequirement> Requirements { get; }

        /// <summary>
        /// Continuous interaction progress (0.0 = not started, 1.0 = complete).
        /// Updated during OnDoInteractAsync for timed/hold interactions.
        /// </summary>
        float InteractionProgress { get; }

        /// <summary>
        /// Available actions on this interactable (e.g., "Pick Up", "Examine", "Disassemble").
        /// Returns empty array if only the default single action is available.
        /// </summary>
        IReadOnlyList<InteractionAction> Actions { get; }

        /// <summary>The instigator currently performing the interaction, or null if idle.</summary>
        InstigatorHandle CurrentInstigator { get; }

        /// <summary>
        /// Duration in seconds the player must hold to complete the interaction.
        /// 0 = instant interaction (no hold required). Progress is reported automatically during the hold.
        /// </summary>
        float HoldDuration { get; }

        /// <summary>
        /// Maximum distance from the instigator allowed during an active interaction.
        /// If the instigator moves beyond this range, the interaction is cancelled automatically.
        /// 0 = no range limit (default).
        /// </summary>
        float MaxInteractionRange { get; }

        /// <summary>
        /// True if the interactable is occupied by an in-progress interaction and cannot accept a new one.
        /// Use this to show "busy" feedback in the UI.
        /// </summary>
        bool IsBusy { get; }

        /// <summary>Fired when the interaction state transitions to a new phase.</summary>
        event Action<IInteractable, InteractionStateType> OnStateChanged;

        /// <summary>
        /// Fired when <see cref="InteractionProgress"/> changes.
        /// Parameters: (source interactable, new progress value 0~1).
        /// </summary>
        event Action<IInteractable, float> OnProgressChanged;

        /// <summary>
        /// Fired when an interaction is cancelled for any reason (manual, out-of-range, interrupted, etc.).
        /// Parameters: (source interactable, cancel reason).
        /// </summary>
        event Action<IInteractable, InteractionCancelReason> OnInteractionCancelled;

        /// <summary>
        /// Attempt the default interaction asynchronously.
        /// Returns true if the interaction completed successfully.
        /// </summary>
        UniTask<bool> TryInteractAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Attempt a specific action by its <see cref="InteractionAction.ActionId"/>.
        /// Falls back to the default interaction if <paramref name="actionId"/> is null.
        /// </summary>
        UniTask<bool> TryInteractAsync(string actionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Attempt an interaction with a known instigator and optional action ID.
        /// The instigator is tracked for distance-based auto-cancellation and gameplay queries.
        /// </summary>
        /// <param name="instigator">The instigator initiating the interaction (e.g., player).</param>
        /// <param name="actionId">Optional action ID for multi-action interactables. Null for default action.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        UniTask<bool> TryInteractAsync(InstigatorHandle instigator, string actionId, CancellationToken cancellationToken = default);

        /// <summary>Check whether interaction is allowed, evaluating all <see cref="Requirements"/>.</summary>
        /// <param name="instigator">The instigator initiating the interaction (e.g., player).</param>
        bool CanInteract(InstigatorHandle instigator);

        /// <summary>Called by the detector when this object becomes the current interaction target.</summary>
        void OnFocus();

        /// <summary>Called by the detector when this object is no longer the current target.</summary>
        void OnDefocus();

        /// <summary>
        /// Forcefully cancel any in-progress interaction and reset state.
        /// </summary>
        /// <param name="reason">The reason for cancellation. Defaults to <see cref="InteractionCancelReason.Manual"/>.</param>
        void ForceEndInteraction(InteractionCancelReason reason = InteractionCancelReason.Manual);
    }
}