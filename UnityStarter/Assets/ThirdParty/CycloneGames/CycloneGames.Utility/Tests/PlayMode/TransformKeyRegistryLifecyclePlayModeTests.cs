using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;

using CycloneGames.Utility.Runtime;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.Utility.Tests.PlayMode
{
    /// <summary>
    /// PlayMode-only lifecycle coverage for <see cref="TransformKeyRegistry"/>. EditMode cannot exercise any of this:
    /// Unity delivers Awake/OnEnable/OnDisable only while the player loop is running, so the generation counter and
    /// the rebuild-on-enable path stay untouched there. Every case builds its own hierarchy in code and destroys it
    /// afterwards, so no scene asset or manual setup is required.
    /// </summary>
    /// <remarks>
    /// Entries are injected through reflection rather than <c>SerializedObject</c>, whose
    /// <c>GetArrayElementAtIndex</c> is O(n^2) and unusable at the multi-thousand-entry scale used by the cost cases.
    /// Injection mirrors the real authoring flow: the inspector writes the array, then the component builds. A test
    /// that instead relied on the Awake-time build would be measuring a registry whose entries arrived after Awake.
    /// </remarks>
    public sealed class TransformKeyRegistryLifecyclePlayModeTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly FieldInfo EntriesField =
            typeof(TransformKeyRegistry).GetField("Entries", PrivateInstance);

        private static readonly FieldInfo KeyPathModeField =
            typeof(TransformKeyRegistry).GetField("KeyPathMode", PrivateInstance);

        private static readonly FieldInfo AutoBuildField =
            typeof(TransformKeyRegistry).GetField("AutoBuildOnAwake", PrivateInstance);

        private static readonly FieldInfo EntryKeyField =
            typeof(TransformKeyRegistry.TransformKeyEntry).GetField("Key", PrivateInstance);

        private static readonly FieldInfo EntryTransformField =
            typeof(TransformKeyRegistry.TransformKeyEntry).GetField("Transform", PrivateInstance);

        [Test]
        public void ReflectionContract_AllInjectedMembersResolve()
        {
            // Later cases depend on these, so fail fast with a clear cause rather than a downstream NullReference.
            Assert.That(EntriesField, Is.Not.Null, "Entries field not found.");
            Assert.That(KeyPathModeField, Is.Not.Null, "KeyPathMode field not found.");
            Assert.That(AutoBuildField, Is.Not.Null, "AutoBuildOnAwake field not found.");
            Assert.That(EntryKeyField, Is.Not.Null, "TransformKeyEntry.Key field not found.");
            Assert.That(EntryTransformField, Is.Not.Null, "TransformKeyEntry.Transform field not found.");
        }

        [UnityTest]
        public IEnumerator BuiltRegistry_ServesLookupsAfterAFrame()
        {
            var root = new GameObject("Registry");
            try
            {
                TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
                Transform target = CreateChild(root.transform, "Target");
                InjectEntriesAndBuild(registry, new[] { "Target" }, new[] { target });

                yield return null;

                Assert.That(registry.IsBuilt, Is.True);
                Assert.That(registry.TryGetTransform("Target", out Transform resolved), Is.True);
                Assert.That(resolved, Is.SameAs(target));
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator OnDisable_InvalidatesAndStopsResolving()
        {
            var root = new GameObject("Registry");
            try
            {
                TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
                Transform target = CreateChild(root.transform, "Target");
                InjectEntriesAndBuild(registry, new[] { "Target" }, new[] { target });
                yield return null;

                Assert.That(registry.TryGetTransform("Target", out _), Is.True);

                uint beforeDisable = registry.Generation;
                registry.enabled = false;
                yield return null;

                Assert.That(registry.Generation, Is.GreaterThan(beforeDisable), "OnDisable must advance the generation.");
                Assert.That(registry.IsBuilt, Is.False, "OnDisable invalidates and nothing rebuilds while disabled.");
                Assert.That(
                    registry.TryGetTransform("Target", out _),
                    Is.False,
                    "A disabled registry must not serve its previous index; this is the contract pooled callers rely on.");
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator OnEnable_AfterDisable_RebuildsWhenAutoBuildIsOn()
        {
            var root = new GameObject("Registry");
            try
            {
                TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
                Transform target = CreateChild(root.transform, "Target");
                InjectEntriesAndBuild(registry, new[] { "Target" }, new[] { target });
                yield return null;

                registry.enabled = false;
                yield return null;
                registry.enabled = true;
                yield return null;

                Assert.That(registry.IsBuilt, Is.True, "Re-enabling must rebuild while AutoBuildOnAwake is on.");
                Assert.That(registry.TryGetTransform("Target", out Transform resolved), Is.True);
                Assert.That(resolved, Is.SameAs(target));
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator OnEnable_AfterDisable_LeavesIndexUnbuiltWhenAutoBuildIsOff()
        {
            var root = new GameObject("Registry");
            try
            {
                TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
                AutoBuildField.SetValue(registry, false);
                Transform target = CreateChild(root.transform, "Target");
                InjectEntriesAndBuild(registry, new[] { "Target" }, new[] { target });
                yield return null;

                Assert.That(registry.IsBuilt, Is.True);

                registry.enabled = false;
                yield return null;
                registry.enabled = true;
                yield return null;

                Assert.That(
                    registry.IsBuilt,
                    Is.False,
                    "With AutoBuildOnAwake off the rebuild is the caller's responsibility, so enable must not build.");
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator DisablingNestedRegistry_RemovesItFromTheParentSnapshot()
        {
            var root = new GameObject("RootRegistry");
            try
            {
                TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
                var nestedObject = new GameObject("Nested");
                nestedObject.transform.SetParent(root.transform, false);
                TransformKeyRegistry nestedRegistry = nestedObject.AddComponent<TransformKeyRegistry>();
                Transform nestedTarget = CreateChild(nestedObject.transform, "NestedTarget");
                InjectEntriesAndBuild(nestedRegistry, new[] { "NestedTarget" }, new[] { nestedTarget });
                yield return null;

                // The parent snapshotted its subtree during Awake, before the nested registry existed, so it must be
                // rebuilt before it can report the child.
                rootRegistry.BuildIndex();
                int baselineCount = rootRegistry.NestedRegistryCount;

                nestedRegistry.enabled = false;
                yield return null;

                // The snapshot is a cached array, so the count only reflects the inactive child after a rebuild.
                Assert.That(
                    rootRegistry.IsBuilt,
                    Is.False,
                    "Disabling the child invalidates the parent chain, so the parent's snapshot is now stale.");

                rootRegistry.BuildIndex();

                Assert.That(baselineCount, Is.EqualTo(1), "Baseline: the nested registry is included.");
                Assert.That(
                    rootRegistry.NestedRegistryCount,
                    Is.Zero,
                    "Once the parent rebuilds, an inactive nested registry must be gone from the snapshot.");
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator SetActiveToggleOnAncestor_InvalidatesAndRebuildsTheChildRegistry()
        {
            var root = new GameObject("Root");
            try
            {
                var child = new GameObject("Child");
                child.transform.SetParent(root.transform, false);
                TransformKeyRegistry registry = child.AddComponent<TransformKeyRegistry>();
                Transform target = CreateChild(child.transform, "Target");
                InjectEntriesAndBuild(registry, new[] { "Target" }, new[] { target });
                yield return null;

                Assert.That(registry.IsBuilt, Is.True);
                uint generationBefore = registry.Generation;

                root.SetActive(false);
                yield return null;

                // Deactivating an ancestor does reach the descendant: Unity treats the component as disabled for
                // callback purposes even though its own activeSelf never changed, so OnDisable runs and invalidates.
                Assert.That(
                    registry.Generation,
                    Is.GreaterThan(generationBefore),
                    "An ancestor toggle delivers OnDisable to the descendant component.");
                Assert.That(registry.IsBuilt, Is.False, "OnDisable invalidates, and nothing rebuilds while inactive.");

                uint generationWhileInactive = registry.Generation;

                root.SetActive(true);
                yield return null;

                // The inverse hop also arrives, so a registry with AutoBuildOnAwake on comes back ready to serve.
                Assert.That(
                    registry.Generation,
                    Is.GreaterThan(generationWhileInactive),
                    "Re-activating the ancestor delivers OnEnable and triggers the rebuild.");
                Assert.That(registry.IsBuilt, Is.True, "AutoBuildOnAwake rebuilds on re-enable.");

                Assert.That(registry.TryGetTransform("Target", out Transform resolved), Is.True);
                Assert.That(resolved, Is.SameAs(target));
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator ReparentingRegistry_InvalidatesAndRebuildsAgainstTheNewParent()
        {
            var root = new GameObject("Registry");
            var other = new GameObject("OtherParent");
            try
            {
                TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
                Transform target = CreateChild(root.transform, "Target");
                InjectEntriesAndBuild(registry, new[] { "Target" }, new[] { target });
                yield return null;

                Assert.That(registry.TryGetTransform("Target", out _), Is.True);

                root.transform.SetParent(other.transform, true);
                yield return null;

                // The transform callback invalidates and OnEnable is not re-run, so the caller owns the rebuild.
                Assert.That(registry.IsBuilt, Is.False, "Reparenting invalidates the index.");

                registry.BuildIndex();
                Assert.That(registry.TryGetTransform("Target", out Transform resolved), Is.True);
                Assert.That(resolved, Is.SameAs(target), "Keys stay relative to the registry root, so reparenting keeps them valid.");
            }
            finally
            {
                Object.Destroy(root);
                Object.Destroy(other);
            }
        }

        [UnityTest]
        public IEnumerator DisableEnableCycle_RebuildCostGrowsWithEntryCount()
        {
            const int samples = 8;
            var report = new StringBuilder();
            double smallCost = 0.0;
            double largeCost = 0.0;

            foreach (int count in new[] { 64, 4096 })
            {
                var root = new GameObject("Registry" + count.ToString(CultureInfo.InvariantCulture));
                try
                {
                    TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
                    Transform[] targets = new Transform[count];
                    string[] keys = new string[count];
                    for (int i = 0; i < count; i++)
                    {
                        targets[i] = CreateChild(root.transform, "N" + i.ToString(CultureInfo.InvariantCulture));
                        keys[i] = targets[i].name;
                    }

                    InjectEntriesAndBuild(registry, keys, targets);
                    yield return null;

                    double totalMs = 0.0;
                    for (int sample = 0; sample < samples; sample++)
                    {
                        registry.enabled = false;
                        yield return null;

                        var stopwatch = Stopwatch.StartNew();
                        registry.enabled = true;
                        yield return null;
                        stopwatch.Stop();
                        totalMs += stopwatch.Elapsed.TotalMilliseconds;
                    }

                    double perCycleMs = totalMs / samples;
                    report.Append(count.ToString(CultureInfo.InvariantCulture))
                          .Append(" entries=")
                          .Append(perCycleMs.ToString("F3", CultureInfo.InvariantCulture))
                          .Append("ms; ");

                    if (count == 64)
                    {
                        smallCost = perCycleMs;
                    }
                    else
                    {
                        largeCost = perCycleMs;
                    }
                }
                finally
                {
                    Object.Destroy(root);
                }
            }

            TestContext.WriteLine("Disable/enable cycle cost: " + report);

            Assert.That(largeCost, Is.GreaterThan(0.0), "A 4096-entry rebuild must take measurable time.");
            Assert.That(
                largeCost,
                Is.GreaterThan(smallCost),
                "A 64x larger index must cost measurably more to rebuild, otherwise the rebuild is not running.");
        }

        [UnityTest]
        public IEnumerator BurstLookups_AllocateNothingAndDoNotDegradeAcrossPulses()
        {
            var root = new GameObject("Registry");
            try
            {
                const int count = 4096;
                TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
                Transform[] targets = new Transform[count];
                string[] keys = new string[count];
                for (int i = 0; i < count; i++)
                {
                    targets[i] = CreateChild(root.transform, "N" + i.ToString(CultureInfo.InvariantCulture));
                    keys[i] = targets[i].name;
                }

                InjectEntriesAndBuild(registry, keys, targets);
                yield return null;

                const int iterations = 200_000;
                Transform sink;
                for (int i = 0; i < 20_000; i++)
                {
                    registry.TryGetTransform(keys[i % count], out sink);
                }

                long before = System.GC.GetAllocatedBytesForCurrentThread();
                var first = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    registry.TryGetTransform(keys[i % count], out sink);
                }

                first.Stop();
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

                var second = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    registry.TryGetTransform(keys[i % count], out sink);
                }

                second.Stop();

                double firstNs = first.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations;
                double secondNs = second.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations;
                TestContext.WriteLine(
                    "burst first=" + firstNs.ToString("F1", CultureInfo.InvariantCulture)
                    + "ns second=" + secondNs.ToString("F1", CultureInfo.InvariantCulture)
                    + "ns alloc=" + allocated.ToString(CultureInfo.InvariantCulture) + "B");

                Assert.That(allocated, Is.Zero, "The lookup path must stay allocation free under sustained load.");
                Assert.That(
                    secondNs,
                    Is.LessThan(firstNs * 2.0),
                    "The second pulse must not degrade; a widening gap points at hidden growth or cache thrash.");
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator DestroyingRegistry_ReleasesTransientBuffers()
        {
            var root = new GameObject("Registry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            Transform[] targets = new Transform[256];
            string[] keys = new string[256];
            for (int i = 0; i < targets.Length; i++)
            {
                targets[i] = CreateChild(root.transform, "N" + i.ToString(CultureInfo.InvariantCulture));
                keys[i] = targets[i].name;
            }

            InjectEntriesAndBuild(registry, keys, targets);
            yield return null;

            Assert.That(registry.IsBuilt, Is.True);

            // Destroying must not throw while the registry still holds a built index and its transient buffers.
            Object.Destroy(root);
            yield return null;
            yield return null;

            Assert.That(registry == null, Is.True, "Unity destroys the component, so the reference must compare null.");
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        /// <summary>
        /// Writes the private <c>Entries</c> backing array directly, then builds. This mirrors the authoring flow:
        /// the registry is populated by the inspector before it builds, so tests must not expect the Awake-time
        /// build to cover entries that arrive later.
        /// </summary>
        private static void InjectEntriesAndBuild(TransformKeyRegistry registry, string[] keys, Transform[] targets)
        {
            Assert.That(keys.Length, Is.EqualTo(targets.Length));

            var array = System.Array.CreateInstance(typeof(TransformKeyRegistry.TransformKeyEntry), keys.Length);
            for (int i = 0; i < keys.Length; i++)
            {
                object entry = System.Activator.CreateInstance(typeof(TransformKeyRegistry.TransformKeyEntry));
                EntryKeyField.SetValue(entry, keys[i]);
                EntryTransformField.SetValue(entry, targets[i]);
                array.SetValue(entry, i);
            }

            EntriesField.SetValue(registry, array);
            registry.BuildIndex();
        }
    }
}
