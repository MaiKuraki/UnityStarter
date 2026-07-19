using System;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// AbilityTask that performs an action repeatedly on a fixed interval.
    /// UE5: UAbilityTask_Repeat.
    /// </summary>
    public class AbilityTask_Repeat : AbilityTask, IAbilityTaskTick
    {
        /// <summary>
        /// Fired on each repeat interval. Provides the current repetition count (1-based).
        /// Return false to stop repeating early.
        /// </summary>
        public Func<int, bool> OnPerformAction;

        /// <summary>
        /// Fired when all repetitions are complete or the action returned false.
        /// </summary>
        public Action OnFinished;
        public Action OnCancelled;

        private double interval;
        private int totalRepetitions;
        private int currentRepetition;
        private double timer;
        private bool terminalCallbackStarted;

        public override void InitTask(GameplayAbility ability)
        {
            base.InitTask(ability);
            terminalCallbackStarted = false;
        }

        /// <summary>
        /// Creates a Repeat task.
        /// </summary>
        /// <param name="ability">The owning ability.</param>
        /// <param name="interval">Time between each repetition in seconds.</param>
        /// <param name="totalRepetitions">Total number of times to repeat. Use -1 for infinite.</param>
        public static AbilityTask_Repeat Repeat(GameplayAbility ability, float interval, int totalRepetitions)
        {
            var task = ability.NewAbilityTask<AbilityTask_Repeat>();
            task.interval = System.Math.Max(0.001d, interval);
            task.totalRepetitions = totalRepetitions;
            task.currentRepetition = 0;
            task.timer = 0d;
            return task;
        }

        protected override void OnActivate()
        {
            if (totalRepetitions == 0)
            {
                CompleteRepeat();
            }
        }

        public void Tick(float deltaTime)
        {
            if (!IsActive || IsCancelled) return;

            timer += deltaTime;
            int executionBudget = Ability?.AbilitySystemComponent?.Limits.MaxAbilityTaskRepeatExecutionsPerTick
                ?? GASRuntimeLimits.Default.MaxAbilityTaskRepeatExecutionsPerTick;
            for (int executionCount = 0;
                 executionCount < executionBudget && timer >= interval && IsActive && !IsCancelled;
                 executionCount++)
            {
                timer -= interval;
                currentRepetition++;

                bool callbackReturned = false;
                bool shouldContinue = true;
                ulong callbackLeaseGeneration = LeaseGeneration;
                try
                {
                    shouldContinue = OnPerformAction?.Invoke(currentRepetition) ?? true;
                    callbackReturned = true;
                }
                finally
                {
                    if (!callbackReturned)
                    {
                        EndTaskIfCurrentLease(callbackLeaseGeneration);
                    }
                }

                if (!IsActive || IsCancelled)
                {
                    return;
                }

                if (!shouldContinue || (totalRepetitions > 0 && currentRepetition >= totalRepetitions))
                {
                    CompleteRepeat();
                    return;
                }
            }
        }

        public override void CancelTask()
        {
            if (!AbilityTaskTerminalCallbackGuard.TryBegin(
                    this,
                    ref terminalCallbackStarted,
                    out ulong leaseGeneration)) return;
            try
            {
                OnCancelled?.Invoke();
            }
            finally
            {
                if (IsCurrentLease(leaseGeneration))
                {
                    base.CancelTask();
                }
            }
        }

        private void CompleteRepeat()
        {
            if (IsCancelled ||
                !AbilityTaskTerminalCallbackGuard.TryBegin(
                    this,
                    ref terminalCallbackStarted,
                    out ulong leaseGeneration)) return;
            try
            {
                OnFinished?.Invoke();
            }
            finally
            {
                EndTaskIfCurrentLease(leaseGeneration);
            }
        }

        protected override void OnDestroy()
        {
            OnPerformAction = null;
            OnFinished = null;
            OnCancelled = null;
            base.OnDestroy();
        }
    }
}
