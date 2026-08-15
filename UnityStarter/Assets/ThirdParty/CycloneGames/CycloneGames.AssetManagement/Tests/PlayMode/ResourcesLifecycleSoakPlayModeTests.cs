using System.Collections;

using Cysharp.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

using AssetRuntime = CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Tests.PlayMode
{
    /// <summary>
    /// Bounded repeated-lifecycle coverage for the deferred instance-destroy path. Long-running (8-24h) soak
    /// validation with real providers remains a CI/lab exercise; this test guards the per-cycle invariants so a
    /// leak or dangling tail fails fast in any PlayMode run.
    /// </summary>
    public sealed class ResourcesLifecycleSoakPlayModeTests
    {
        private const int CycleCount = 200;

        [UnityTest]
        public IEnumerator RepeatedInstanceCycles_LeaveNoLeakedTailsOrInstances()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var tails = new AssetRuntime.AssetOperationTailTracker();

                for (int i = 0; i < CycleCount; i++)
                {
                    var instance = new GameObject("ResourcesLifecycleSoakPlayModeTests");
                    AssetRuntime.ResourcesInstantiateHandle handle =
                        AssetRuntime.ResourcesInstantiateHandle.Create(
                            AssetRuntime.AssetRuntimeGuard.NextHandleId(),
                            instance,
                            onDisposed: null,
                            tails);

                    await handle.DisposeInternal();

                    Assert.That(instance == null, Is.True);
                    Assert.That(handle.RefCount, Is.Zero);
                    Assert.That(tails.PendingCount, Is.Zero);
                }
            });
        }
    }
}
