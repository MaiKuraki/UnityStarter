using System;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public interface ITargetActor
    {
        event Action<TargetData> OnTargetDataReady;
        event Action OnCanceled;

        void Configure(GameplayAbility ability, Action<TargetData> onTargetDataReady, Action onCancelled);
        void StartTargeting();
        void ConfirmTargeting();
        void CancelTargeting();
        void Destroy();
    }
}