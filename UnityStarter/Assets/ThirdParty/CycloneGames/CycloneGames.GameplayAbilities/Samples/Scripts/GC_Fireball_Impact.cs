using System;
using System.Threading;

using CycloneGames.GameplayAbilities.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    [CreateAssetMenu(fileName = "GC_Fireball_Impact", menuName = "CycloneGames/GameplayAbilities/Samples/GameplayCues/Fireball Impact")]
    public class GC_Fireball_Impact : GameplayCueSO
    {
        [Header("Impact VFX")]
        public string ImpactVFXPrefab;
        public float VFXLifetime = 2.0f;

        [Header("Impact SFX")]
        public string ImpactSound;

        public override async UniTask OnExecutedAsync(
            GameplayCueParameters parameters,
            IGameObjectPoolManager poolManager,
            CancellationToken cancellationToken = default)
        {
            GameObject targetObject = parameters.TargetObject;
            if (targetObject == null) return;

            Vector3 impactPosition = targetObject.transform.position;
            CancellationToken targetLifetime = targetObject.GetCancellationTokenOnDestroy();
            using var cueLifetimeSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                targetLifetime);
            CancellationToken cueLifetime = cueLifetimeSource.Token;

            try
            {
                if (!string.IsNullOrEmpty(ImpactVFXPrefab))
                {
                    GameObjectLease vfxLease = await poolManager.GetAsync(
                        ImpactVFXPrefab,
                        impactPosition,
                        Quaternion.identity,
                        cancellationToken: cueLifetime);
                    if (vfxLease.IsValid)
                    {
                        if (VFXLifetime > 0f)
                        {
                            ReturnToPoolAfterDelay(poolManager, vfxLease, VFXLifetime).Forget();
                        }
                        else
                        {
                            poolManager.Release(vfxLease);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(ImpactSound))
                {
                    IResourceHandle<AudioClip> audioClipHandle = null;
                    try
                    {
                        audioClipHandle = await poolManager.ResourceLocator.LoadAssetAsync<AudioClip>(
                            ImpactSound,
                            cancellationToken: cueLifetime);
                        await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cueLifetime);

                        if (audioClipHandle?.Asset != null)
                        {
                            PlayAudioAndReleaseHandle(audioClipHandle, impactPosition).Forget();
                            audioClipHandle = null;
                        }
                    }
                    finally
                    {
                        audioClipHandle?.Dispose();
                    }
                }
            }
            catch (OperationCanceledException) when (cueLifetime.IsCancellationRequested)
            {
                // The cue target no longer owns presentation work.
            }
            catch (ObjectDisposedException)
            {
                // Gameplay Cue shutdown already canceled and reclaimed presentation resources.
            }
        }

        private static async UniTaskVoid ReturnToPoolAfterDelay(
            IGameObjectPoolManager poolManager,
            GameObjectLease lease,
            float delay)
        {
            GameObject instance = lease.Instance;
            bool canceled = await UniTask.Delay(
                    TimeSpan.FromSeconds(delay),
                    cancellationToken: instance.GetCancellationTokenOnDestroy())
                .SuppressCancellationThrow();

            if (canceled || instance == null)
            {
                return;
            }

            try
            {
                poolManager.Release(lease);
            }
            catch (ObjectDisposedException)
            {
                // Pool shutdown already destroyed every outstanding lease.
            }
        }

        private static async UniTaskVoid PlayAudioAndReleaseHandle(
            IResourceHandle<AudioClip> audioClipHandle,
            Vector3 position)
        {
            try
            {
                AudioClip clip = audioClipHandle.Asset;
                if (clip == null) return;

                AudioSource.PlayClipAtPoint(clip, position);
                await UniTask.Delay(TimeSpan.FromSeconds(Math.Max(0.01f, clip.length)), ignoreTimeScale: true);
            }
            finally
            {
                audioClipHandle.Dispose();
            }
        }
    }
}
