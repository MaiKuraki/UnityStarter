using System;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public class AbilityTask_WaitDelay : AbilityTask, IAbilityTaskTick
    {
        public Action OnFinishDelay;
        private float timeRemaining;

        public static AbilityTask_WaitDelay WaitDelay(GameplayAbility ability, float duration)
        {
            var task = ability.NewAbilityTask<AbilityTask_WaitDelay>();
            task.timeRemaining = duration;
            return task;
        }

        protected override void OnActivate()
        {
            if (timeRemaining <= 0)
            {
                if (!IsCancelled)
                {
                    OnFinishDelay?.Invoke();
                }
                EndTask();
            }
        }

        public void Tick(float deltaTime)
        {
            if (!IsActive) return;

            timeRemaining -= deltaTime;
            if (timeRemaining <= 0)
            {
                if (!IsCancelled)
                {
                    OnFinishDelay?.Invoke();
                }
                EndTask();
            }
        }

        protected override void OnDestroy()
        {
            OnFinishDelay = null;
        }
    }
}