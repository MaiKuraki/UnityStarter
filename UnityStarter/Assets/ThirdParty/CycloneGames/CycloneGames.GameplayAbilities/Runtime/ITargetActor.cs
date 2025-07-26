using System;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Defines the contract for an object that performs targeting logic for a GameplayAbility.
    /// This decouples the ability from the specific method of targeting (e.g., raycast, sphere overlap, manual selection).
    /// TargetActors are typically transient and are created and destroyed by an AbilityTask like 'WaitTargetData'.
    /// </summary>
    public interface ITargetActor
    {
        /// <summary>
        /// Broadcasts when the actor has successfully acquired valid targeting data.
        /// The GameplayAbility's waiting task will be listening for this event.
        /// </summary>
        event Action<TargetData> OnTargetDataReady;

        /// <summary>
        /// Broadcasts when the targeting process is cancelled, either by user input or by failing to acquire a valid target.
        /// The GameplayAbility's waiting task will be listening for this event.
        /// </summary>
        event Action OnCanceled;

        /// <summary>
        /// Initializes the TargetActor, linking it to its owning ability and wiring up the result callbacks.
        /// This is the first method called on a new TargetActor instance.
        /// </summary>
        /// <param name="ability">The GameplayAbility that owns this targeting operation.</param>
        /// <param name="onTargetDataReady">The callback to invoke when targeting is successful.</param>
        /// <param name="onCancelled">The callback to invoke when targeting is cancelled.</param>
        void Configure(GameplayAbility ability, Action<TargetData> onTargetDataReady, Action onCancelled);

        /// <summary>
        /// Begins the targeting process. For instant targeting, this may produce data immediately.
        /// For user-driven targeting (like placing an AoE decal), this starts the update loop or input listening.
        /// </summary>
        void StartTargeting();

        /// <summary>
        /// Called externally (typically by the owning ability or an input handler) to confirm the current target selection.
        /// This should trigger the OnTargetDataReady event.
        /// </summary>
        void ConfirmTargeting();

        /// <summary>
        /// Called externally to cancel the targeting process.
        /// This should trigger the OnCanceled event.
        /// </summary>
        void CancelTargeting();

        /// <summary>
        /// Handles the complete cleanup and destruction of the target actor.
        /// If the actor is a MonoBehaviour, this should destroy the GameObject.
        /// If it's a pure C# object, it should clear delegates and prepare for garbage collection.
        /// </summary>
        void Destroy();
    }
}