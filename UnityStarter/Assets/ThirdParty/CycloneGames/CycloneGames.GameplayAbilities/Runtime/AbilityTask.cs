using System;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public interface IAbilityTaskTick
    {
        void Tick(float DeltaTime);
    }

    public abstract class AbilityTask
    {
        public GameplayAbility Ability { get; protected set; }
        public bool IsActive { get; private set; }
        public bool IsCancelled { get; private set; }
        public event Action<AbilityTask> OnFinish;

        public virtual void InitTask(GameplayAbility ability)
        {
            this.Ability = ability;
            // Reset state when reusing a task from the pool.
            IsActive = false;
            IsCancelled = false;
        }

        public void Activate()
        {
            if (IsActive) return;
            IsActive = true;
            OnActivate();
        }

        protected abstract void OnActivate();

        /// <summary>
        /// This should be overridden by child classes to clean up their specific state,
        /// especially delegates, before the task is returned to the pool.
        /// </summary>
        protected virtual void OnDestroy()
        {
            // Clean up base class delegates.
            OnFinish = null;
        }

        public void EndTask()
        {
            if (IsActive)
            {
                IsActive = false;
                Ability.OnTaskEnded(this);
                
                // Call the virtual OnDestroy for subclass cleanup.
                OnDestroy();

                // Return this task to the central pool for reuse.
                PoolManager.ReturnTask(this);
            }
        }

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