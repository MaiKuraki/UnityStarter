using System;
using System.Collections;
using System.Threading;

using CycloneGames.Logging;
using CycloneGames.Utility.Runtime;
using CycloneGames.Utility.Tests.PlayMode.Support;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.Utility.Tests.PlayMode
{
    [TestFixture]
    public sealed class MonoSingletonPlayModeTests
    {
        [UnityTest]
        public IEnumerator QueryApis_DoNotSearchOrCreate()
        {
            Assert.That(QueryOnlySingleton.HasInstance, Is.False);
            Assert.That(QueryOnlySingleton.TryGetInstance(out QueryOnlySingleton singleton), Is.False);
            Assert.That(singleton, Is.Null);

            yield return null;

            Assert.That(QueryOnlySingleton.HasInstance, Is.False);
        }

        [UnityTest]
        public IEnumerator Instance_CreatesCachesAndReleasesDedicatedOwner()
        {
            AutoCreatedSingleton first = AutoCreatedSingleton.Instance;
            try
            {
                Assert.That(first, Is.Not.Null);
                Assert.That(first.gameObject.name, Is.EqualTo("AutoCreatedSingleton (Singleton)"));
                Assert.That(AutoCreatedSingleton.Instance, Is.SameAs(first));
                Assert.That(AutoCreatedSingleton.HasInstance, Is.True);
                Assert.That(AutoCreatedSingleton.TryGetInstance(out AutoCreatedSingleton queried), Is.True);
                Assert.That(queried, Is.SameAs(first));
            }
            finally
            {
                if (first != null)
                {
                    UnityEngine.Object.Destroy(first.gameObject);
                }
            }

            yield return null;
            Assert.That(AutoCreatedSingleton.HasInstance, Is.False);
        }

        [UnityTest]
        public IEnumerator Instance_FindsOneInactiveSceneAuthoredComponent()
        {
            var owner = new GameObject("Inactive Scene Singleton");
            owner.SetActive(false);
            InactiveSceneSingleton authored = owner.AddComponent<InactiveSceneSingleton>();
            try
            {
                Assert.That(InactiveSceneSingleton.HasInstance, Is.False);
                Assert.That(InactiveSceneSingleton.Instance, Is.SameAs(authored));
                Assert.That(InactiveSceneSingleton.HasInstance, Is.True);
            }
            finally
            {
                UnityEngine.Object.Destroy(owner);
            }

            yield return null;
            Assert.That(InactiveSceneSingleton.HasInstance, Is.False);
        }

        [UnityTest]
        public IEnumerator Instance_WithMultipleInactiveCandidates_FailsWithoutSelectingAuthority()
        {
            var firstOwner = new GameObject("Ambiguous Singleton A");
            var secondOwner = new GameObject("Ambiguous Singleton B");
            firstOwner.SetActive(false);
            secondOwner.SetActive(false);
            firstOwner.AddComponent<AmbiguousSingleton>();
            secondOwner.AddComponent<AmbiguousSingleton>();
            try
            {
                Assert.Throws<InvalidOperationException>(() => _ = AmbiguousSingleton.Instance);
                Assert.That(AmbiguousSingleton.HasInstance, Is.False);
            }
            finally
            {
                UnityEngine.Object.Destroy(firstOwner);
                UnityEngine.Object.Destroy(secondOwner);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Duplicate_DestroysOnlyComponentAndPreservesGameObjectAndSiblings()
        {
            var firstOwner = new GameObject("Singleton Authority");
            var duplicateOwner = new GameObject("Singleton Duplicate");
            DuplicateSingleton authority = firstOwner.AddComponent<DuplicateSingleton>();
            DuplicateSibling sibling = duplicateOwner.AddComponent<DuplicateSibling>();
            DuplicateSingleton duplicate = null;
            try
            {
                RecordedLogEntry[] entries;
                using (var logs = new RecordingLogWriterScope("CycloneGames.Utility"))
                {
                    duplicate = duplicateOwner.AddComponent<DuplicateSingleton>();
                    Assert.That(DuplicateSingleton.Instance, Is.SameAs(authority));
                    Assert.That(duplicate.enabled, Is.False);
                    yield return null;

                    Assert.That(duplicateOwner, Is.Not.Null);
                    Assert.That(sibling, Is.Not.Null);
                    Assert.That(duplicateOwner.GetComponent<DuplicateSibling>(), Is.SameAs(sibling));
                    Assert.That(duplicateOwner.GetComponent<DuplicateSingleton>(), Is.Null);
                    entries = logs.Snapshot();
                }

                Assert.That(entries, Has.Length.EqualTo(1));
                Assert.That(entries[0].Severity, Is.EqualTo(LogSeverity.Error));
                Assert.That(entries[0].Category, Is.EqualTo("CycloneGames.Utility"));
                Assert.That(
                    entries[0].Message,
                    Is.EqualTo(
                        $"Duplicate {typeof(DuplicateSingleton).FullName} component was disabled and will be destroyed. " +
                        "Its GameObject and sibling components are preserved."));
                Assert.That(entries[0].Exception, Is.Null);
            }
            finally
            {
                UnityEngine.Object.Destroy(firstOwner);
                UnityEngine.Object.Destroy(duplicateOwner);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator GlobalComponent_OnChild_ReportsErrorAndKeepsSceneLifetime()
        {
            var root = new GameObject("Scene Root");
            var child = new GameObject("Configured Global Singleton Child");
            child.transform.SetParent(root.transform, false);
            NonRootGlobalSingleton singleton = null;
            try
            {
                RecordedLogEntry[] entries;
                using (var logs = new RecordingLogWriterScope("CycloneGames.Utility"))
                {
                    singleton = child.AddComponent<NonRootGlobalSingleton>();
                    Assert.That(NonRootGlobalSingleton.Instance, Is.SameAs(singleton));
                    Assert.That(singleton.transform.parent, Is.SameAs(root.transform));
                    Assert.That(singleton.gameObject.scene, Is.EqualTo(root.scene));
                    entries = logs.Snapshot();
                }

                Assert.That(entries, Has.Length.EqualTo(1));
                Assert.That(entries[0].Severity, Is.EqualTo(LogSeverity.Error));
                Assert.That(entries[0].Category, Is.EqualTo("CycloneGames.Utility"));
                Assert.That(
                    entries[0].Message,
                    Is.EqualTo(
                        $"Global {typeof(NonRootGlobalSingleton).FullName} must be attached to a root GameObject. " +
                        "The configured component will keep Scene lifetime."));
                Assert.That(entries[0].Exception, Is.Null);
            }
            finally
            {
                UnityEngine.Object.Destroy(root);
            }

            yield return null;
            Assert.That(NonRootGlobalSingleton.HasInstance, Is.False);
        }

        [UnityTest]
        public IEnumerator Instance_FromWorkerThread_FailsBeforeUsingUnityApis()
        {
            Exception observed = null;
            var worker = new Thread(() =>
            {
                try
                {
                    _ = WorkerThreadSingleton.Instance;
                }
                catch (Exception exception)
                {
                    observed = exception;
                }
            });

            worker.Start();
            Assert.That(worker.Join(5000), Is.True, "Worker thread did not terminate.");
            Assert.That(observed, Is.TypeOf<InvalidOperationException>());
            Assert.That(WorkerThreadSingleton.HasInstance, Is.False);
            yield return null;
        }

        private sealed class QueryOnlySingleton : MonoSingleton<QueryOnlySingleton>
        {
            protected override bool IsGlobal => false;
        }

        private sealed class AutoCreatedSingleton : MonoSingleton<AutoCreatedSingleton>
        {
        }

        private sealed class InactiveSceneSingleton : MonoSingleton<InactiveSceneSingleton>
        {
            protected override bool IsGlobal => false;
        }

        private sealed class AmbiguousSingleton : MonoSingleton<AmbiguousSingleton>
        {
            protected override bool IsGlobal => false;
        }

        private sealed class DuplicateSingleton : MonoSingleton<DuplicateSingleton>
        {
            protected override bool IsGlobal => false;
        }

        private sealed class WorkerThreadSingleton : MonoSingleton<WorkerThreadSingleton>
        {
            protected override bool IsGlobal => false;
        }

        private sealed class NonRootGlobalSingleton : MonoSingleton<NonRootGlobalSingleton>
        {
        }

        private sealed class DuplicateSibling : MonoBehaviour
        {
        }
    }
}
