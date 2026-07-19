using CycloneGames.GameplayAbilities.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Interface for AbilityTasks that need to be updated every frame.
    /// </summary>
    public interface IAbilityTaskTick
    {
        void Tick(float DeltaTime);
    }

    /// <summary>
    /// The base class for all asynchronous, latent actions within a GameplayAbility.
    /// Tasks are used for operations that occur over time, such as waiting for a delay,
    /// an event, or player input.
    /// </summary>
    public abstract class AbilityTask
    {
        private GASRuntimeMemory memoryOwner;
        private bool leaseActive;
        private bool leaseEverAcquired;
        private bool isEndingLease;
        private ulong leaseGeneration;
        /// <summary>
        /// A reference to the GameplayAbility that owns and created this task.
        /// </summary>
        public GameplayAbility Ability { get; protected set; }

        /// <summary>
        /// True if the task is currently running.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// True if the task was explicitly cancelled before it could complete naturally.
        /// </summary>
        public bool IsCancelled { get; private set; }

        public GASPredictionKey PredictionKey { get; private set; }

        public bool HasPredictionKey => PredictionKey.IsValid;

        internal ulong LeaseGeneration => leaseGeneration;

        /// <summary>
        /// Initializes the task with its owning ability and resets its state.
        /// Called when a task lease is created.
        /// </summary>
        public virtual void InitTask(GameplayAbility ability)
        {
            this.Ability = ability;
            IsActive = false;
            IsCancelled = false;
            PredictionKey = ability?.CurrentActivationInfo.PredictionKey ?? default;
        }

        internal void MarkLeaseAcquired(GASRuntimeMemory owner)
        {
            if (leaseActive || leaseEverAcquired)
            {
                throw new System.InvalidOperationException($"AbilityTask '{GetType().FullName}' cannot receive another lease.");
            }

            if (leaseGeneration == ulong.MaxValue)
            {
                throw new System.InvalidOperationException(
                    $"AbilityTask '{GetType().FullName}' lease generation is exhausted.");
            }

            memoryOwner = owner ?? throw new System.ArgumentNullException(nameof(owner));
            leaseGeneration++;
            isEndingLease = false;
            leaseEverAcquired = true;
            leaseActive = true;
        }

        internal bool TryReleaseLease()
        {
            if (!leaseActive)
            {
                return false;
            }

            leaseActive = false;
            return true;
        }

        internal bool IsCurrentLease(ulong expectedLeaseGeneration)
        {
            return leaseActive && leaseGeneration == expectedLeaseGeneration;
        }

        internal void EndTaskIfCurrentLease(ulong expectedLeaseGeneration)
        {
            if (leaseGeneration == expectedLeaseGeneration &&
                (leaseActive || IsActive || Ability != null))
            {
                EndTask();
            }
        }

        public bool IsBoundToPredictionKey(GASPredictionKey predictionKey)
        {
            return predictionKey.IsValid && PredictionKey.Equals(predictionKey);
        }

        public void CommitPrediction()
        {
            PredictionKey = default;
        }

        /// <summary>
        /// Starts the execution of the task's primary logic.
        /// </summary>
        public void Activate()
        {
            if (IsActive) return;
            ulong activationLeaseGeneration = leaseGeneration;
            IsActive = true;
            try
            {
                OnActivate();
            }
            catch
            {
                EndTaskIfCurrentLease(activationLeaseGeneration);
                throw;
            }
        }

        /// <summary>
        /// The main entry point for the task's logic. This must be implemented by subclasses.
        /// </summary>
        protected abstract void OnActivate();

        /// <summary>
        /// Called just before the task lease is released.
        /// Subclasses must override this to clean up their delegates and other state to prevent memory leaks.
        /// IMPORTANT: Always call base.OnDestroy() at the end of your override.
        /// </summary>
        protected virtual void OnDestroy()
        {
            // Clear the owner reference before the task becomes invalid.
            Ability = null;
            PredictionKey = default;
        }

        /// <summary>
        /// Marks the task as complete, notifies the parent ability, and releases the task lease.
        /// </summary>
        public void EndTask()
        {
            if (isEndingLease || (!IsActive && Ability == null))
            {
                return;
            }

            ulong endingLeaseGeneration = leaseGeneration;
            bool hadActiveLease = leaseActive;
            isEndingLease = true;
            IsActive = false;
            System.Exception failure = null;
            try
            {
                Ability?.OnTaskEnded(this);
            }
            catch (System.Exception exception)
            {
                failure = exception;
            }

            try
            {
                OnDestroy();
            }
            catch (System.Exception exception)
            {
                failure ??= exception;
            }
            finally
            {
                bool isSameLease = !hadActiveLease || IsCurrentLease(endingLeaseGeneration);
                if (isSameLease)
                {
                    // Enforce base reference cleanup even if an override fails to call base.OnDestroy().
                    Ability = null;
                    PredictionKey = default;
                    if (hadActiveLease)
                    {
                        memoryOwner?.ReleaseTask(this, releaseSucceeded: failure == null);
                    }
                    else
                    {
                        isEndingLease = false;
                    }
                }
            }

            if (failure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        /// <summary>
        /// Explicitly cancels the task, preventing its completion delegates from firing.
        /// </summary>
        public virtual void CancelTask()
        {
            if (IsActive || Ability != null)
            {
                IsCancelled = true;
                EndTask();
            }
        }
    }
}
