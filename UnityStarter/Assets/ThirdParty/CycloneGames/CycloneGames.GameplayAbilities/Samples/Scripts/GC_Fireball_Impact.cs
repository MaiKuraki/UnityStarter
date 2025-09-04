using CycloneGames.GameplayAbilities.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    [CreateAssetMenu(fileName = "GC_Fireball_Impact", menuName = "CycloneGames/GameplayAbilitySystem/Samples/GameplayCues/Fireball Impact")]
    public class GC_Fireball_Impact : GameplayCueSO
    {
        [Header("Impact VFX")]
        public string ImpactVFXPrefab;
        public float VFXLifetime = 2.0f;

        [Header("Impact SFX")]
        public string ImpactSound;

        public override async UniTask OnExecutedAsync(GameplayCueParameters parameters, IGameObjectPoolManager poolManager)
        {
            if (parameters.TargetObject == null) return;

            // Play visual effect from pool
            if (!string.IsNullOrEmpty(ImpactVFXPrefab))
            {
                var vfxInstance = await poolManager.GetAsync(ImpactVFXPrefab, parameters.TargetObject.transform.position, Quaternion.identity);
                if (vfxInstance != null && VFXLifetime > 0)
                {
                    // Asynchronously release the object back to the pool after a delay.
                    ReturnToPoolAfterDelay(poolManager, vfxInstance, VFXLifetime).Forget();
                }
            }

            // Play sound effect
            if (!string.IsNullOrEmpty(ImpactSound))
            {
                var audioClip = await GameplayCueManager.Instance.ResourceLocator.LoadAssetAsync<AudioClip>(ImpactSound);
                if (audioClip)
                {
                    AudioSource.PlayClipAtPoint(audioClip, parameters.TargetObject.transform.position);
                    GameplayCueManager.Instance.ResourceLocator.ReleaseAsset(ImpactSound);
                }
            }
        }

        private async UniTaskVoid ReturnToPoolAfterDelay(IGameObjectPoolManager poolManager, GameObject instance, float delay)
        {
            await UniTask.Delay(System.TimeSpan.FromSeconds(delay), cancellationToken: instance.GetCancellationTokenOnDestroy()).SuppressCancellationThrow();
            poolManager.Release(instance);
        }
    }
}
