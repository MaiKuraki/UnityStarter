using CycloneGames.GameplayAbilities.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace CycloneGames.GameplayAbilities.Sample
{
    [CreateAssetMenu(fileName = "GC_Fireball_Impact", menuName = "CycloneGames/GameplayAbilitySystem/Samples/GameplayCues/Fireball Impact")]
    public class GC_Fireball_Impact : GameplayCueSO
    {
        [Header("Impact VFX")]
        public AssetReferenceGameObject ImpactVFXPrefab;
        public float VFXLifetime = 2.0f;

        [Header("Impact SFX")]
        public AssetReferenceT<AudioClip> ImpactSound;

        public override async UniTask OnExecutedAsync(GameplayCueParameters parameters, IGameObjectPoolManager poolManager)
        {
            if (parameters.TargetObject == null) return;

            // Play visual effect from pool
            if (ImpactVFXPrefab != null && ImpactVFXPrefab.RuntimeKeyIsValid())
            {
                var vfxInstance = await poolManager.GetAsync(ImpactVFXPrefab, parameters.TargetObject.transform.position, Quaternion.identity);
                if (vfxInstance != null && VFXLifetime > 0)
                {
                    // Asynchronously release the object back to the pool after a delay.
                    ReturnToPoolAfterDelay(poolManager, vfxInstance, VFXLifetime).Forget();
                }
            }

            // Play sound effect
            if (ImpactSound != null && ImpactSound.RuntimeKeyIsValid())
            {
                AudioClip audioClip = null; // await Addressables.LoadAssetAsync<AudioClip>(ImpactSound);
                if (audioClip)
                {
                    AudioSource.PlayClipAtPoint(audioClip, parameters.TargetObject.transform.position);
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