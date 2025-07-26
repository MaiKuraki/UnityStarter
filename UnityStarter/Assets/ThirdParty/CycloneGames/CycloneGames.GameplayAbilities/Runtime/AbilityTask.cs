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

        /// <summary>
        /// Initializes the task with its owning ability and resets its state.
        /// Called when a task is retrieved from the pool.
        /// </summary>
        public virtual void InitTask(GameplayAbility ability)
        {
            this.Ability = ability;
            IsActive = false;
            IsCancelled = false;
        }

        /// <summary>
        /// Starts the execution of the task's primary logic.
        /// </summary>
        public void Activate()
        {
            if (IsActive) return;
            IsActive = true;
            OnActivate();
        }

        /// <summary>
        /// The main entry point for the task's logic. This must be implemented by subclasses.
        /// </summary>
        protected abstract void OnActivate();

        /// <summary>
        /// Called just before the task is returned to the pool.
        /// Subclasses must override this to clean up their delegates and other state to prevent memory leaks.
        /// </summary>
        protected virtual void OnDestroy()
        {
            // Clean up base class delegates.
            
        }

        /// <summary>
        /// Marks the task as complete, notifies the parent ability, and returns the task to the pool.
        /// </summary>
        public void EndTask()
        {
            if (IsActive)
            {
                IsActive = false;
                Ability.OnTaskEnded(this);

                OnDestroy();

                PoolManager.ReturnTask(this);
            }
        }

        /// <summary>
        /// Explicitly cancels the task, preventing its completion delegates from firing.
        /// </summary>
        public void CancelTask()
        {
            if (IsActive)
            {
                IsCancelled = true;
                EndTask();
            }
        }
    }
}
