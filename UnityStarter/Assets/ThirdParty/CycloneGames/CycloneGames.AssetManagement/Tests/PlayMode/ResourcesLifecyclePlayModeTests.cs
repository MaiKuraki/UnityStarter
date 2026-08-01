using System.Collections;

using Cysharp.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

using AssetRuntime = CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Tests.PlayMode
{
    public sealed class ResourcesLifecyclePlayModeTests
    {
        [UnityTest]
        public IEnumerator InstanceRelease_WaitsUntilUnityDestroysTheObject()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var instance = new GameObject("ResourcesLifecyclePlayModeTests");
                var tails = new AssetRuntime.AssetOperationTailTracker();
                AssetRuntime.ResourcesInstantiateHandle handle =
                    AssetRuntime.ResourcesInstantiateHandle.Create(
                        AssetRuntime.AssetRuntimeGuard.NextHandleId(),
                        instance,
                        onDisposed: null,
                        tails);

                try
                {
                    UniTask release = handle.DisposeInternal();

                    Assert.That(release.Status, Is.EqualTo(UniTaskStatus.Pending));
                    Assert.That(handle.Instance, Is.SameAs(instance));
                    Assert.That(tails.PendingCount, Is.EqualTo(1));

                    await release;

                    Assert.That(instance == null, Is.True);
                    Assert.That(handle.Instance == null, Is.True);
                    Assert.That(handle.RefCount, Is.Zero);
                    Assert.That(tails.PendingCount, Is.Zero);
                }
                finally
                {
                    if (instance != null)
                    {
                        Object.Destroy(instance);
                        await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                    }
                }
            });
        }
    }
}
