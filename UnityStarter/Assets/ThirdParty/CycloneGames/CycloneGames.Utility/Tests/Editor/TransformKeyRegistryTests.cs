using System;
using System.Collections.Generic;
using System.Globalization;

using CycloneGames.Utility.Runtime;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;

namespace CycloneGames.Utility.Tests.Editor
{
    /// <summary>
    /// EditMode coverage for <see cref="TransformKeyRegistry"/>.
    /// </summary>
    /// <remarks>
    /// Unity does not dispatch <c>Awake</c>, <c>OnEnable</c>, <c>OnDisable</c>,
    /// <c>OnTransformChildrenChanged</c> or <c>OnTransformParentChanged</c> for components created at runtime in
    /// EditMode. The only lifecycle callback that fires is <c>OnValidate</c>, from <c>AddComponent</c>. These tests
    /// therefore drive builds and invalidations explicitly rather than relying on those callbacks.
    /// </remarks>
    public sealed class TransformKeyRegistryTests
    {
        private const int LinearSearchThreshold = 16;

        private readonly List<GameObject> _roots = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _roots.Count - 1; i >= 0; i--)
            {
                if (_roots[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_roots[i]);
                }
            }

            _roots.Clear();
        }

        [Test]
        public void BuildIndex_FirstDuplicateWinsAcrossLinearSearchThreshold()
        {
            GameObject root = CreateRoot("Registry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            var entries = new EntrySpec[18];
            Transform first = CreateChild(root.transform, "First");
            entries[0] = new EntrySpec("Shared", first);
            for (int i = 1; i < 17; i++)
            {
                entries[i] = new EntrySpec(string.Concat("Key", i), CreateChild(root.transform, string.Concat("Child", i)));
            }

            Transform duplicate = CreateChild(root.transform, "Duplicate");
            entries[17] = new EntrySpec("Shared", duplicate);
            SetEntries(registry, entries);

            registry.BuildIndex();

            Assert.That(registry.EntryCount, Is.EqualTo(17));
            Assert.That(registry.DuplicateKeyCount, Is.EqualTo(1));
            Assert.That(registry.InvalidEntryCount, Is.Zero);
            Assert.That(registry.TryGetTransform("Shared", out Transform resolved), Is.True);
            Assert.That(resolved, Is.SameAs(first));
            Assert.That(
                registry.TryGetTransform(TransformKeyRegistry.ComputeStableHash("Shared"), out Transform hashResolved),
                Is.True);
            Assert.That(hashResolved, Is.SameAs(first));
        }

        [Test]
        public void BuildIndex_ReportsInvalidEntriesWithoutRetainingThem()
        {
            GameObject root = CreateRoot("Registry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            Transform target = CreateChild(root.transform, "Target");
            SetEntries(
                registry,
                new EntrySpec(string.Empty, target),
                new EntrySpec("Missing", null),
                new EntrySpec("Valid", null),
                new EntrySpec("Valid", target));

            registry.BuildIndex();

            Assert.That(registry.EntryCount, Is.EqualTo(1));
            Assert.That(registry.InvalidEntryCount, Is.EqualTo(3));
            Assert.That(registry.DuplicateKeyCount, Is.Zero);
            Assert.That(registry.TryGetTransform("Valid", out Transform resolved), Is.True);
            Assert.That(resolved, Is.SameAs(target));
            Assert.That(registry.TryGetTransform("Missing", out _), Is.False);
            Assert.That(
                registry.TryGetTransform(TransformKeyRegistry.ComputeStableHash("Unknown"), out Transform missing),
                Is.False);
            Assert.That(missing, Is.Null);
        }

        [Test]
        public void NestedRegistryLookup_UsesFlattenedDepthFirstCache()
        {
            GameObject root = CreateRoot("RootRegistry");
            TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
            GameObject nestedObject = new GameObject("NestedRegistry");
            nestedObject.transform.SetParent(root.transform, false);
            TransformKeyRegistry nestedRegistry = nestedObject.AddComponent<TransformKeyRegistry>();
            Transform target = CreateChild(nestedObject.transform, "NestedTarget");
            SetEntries(nestedRegistry, new EntrySpec("Nested.Target", target));

            nestedRegistry.BuildIndex();
            rootRegistry.BuildIndex();

            Assert.That(rootRegistry.TryGetTransform("Nested.Target", out Transform resolved), Is.True);
            Assert.That(resolved, Is.SameAs(target));
        }

        [Test]
        public void NestedRegistryLookup_ExcludesDisabledAndInactiveRegistries()
        {
            GameObject root = CreateRoot("RootRegistry");
            TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
            GameObject nestedObject = new GameObject("NestedRegistry");
            nestedObject.transform.SetParent(root.transform, false);
            TransformKeyRegistry nestedRegistry = nestedObject.AddComponent<TransformKeyRegistry>();
            Transform target = CreateChild(nestedObject.transform, "NestedTarget");
            SetEntries(nestedRegistry, new EntrySpec("Nested.Target", target));

            // A disabled registry cannot serve a parent lookup, so its ancestors' caches must exclude it.
            nestedRegistry.enabled = false;
            rootRegistry.BuildIndex();
            Assert.That(rootRegistry.NestedRegistryCount, Is.Zero);
            Assert.That(rootRegistry.TryGetTransform("Nested.Target", out _), Is.False);

            // Re-enabling restores discoverability once the registry is built. Builds are always explicit: the
            // component contract never rebuilds implicitly, and EditMode does not raise OnEnable to do it either.
            nestedRegistry.enabled = true;
            nestedRegistry.BuildIndex();
            rootRegistry.BuildIndex();
            Assert.That(rootRegistry.NestedRegistryCount, Is.EqualTo(1));
            Assert.That(rootRegistry.TryGetTransform("Nested.Target", out Transform enabledResult), Is.True);
            Assert.That(enabledResult, Is.SameAs(target));

            // Deactivating the GameObject hides the registry the same way a disabled component does.
            nestedObject.SetActive(false);
            rootRegistry.BuildIndex();
            Assert.That(rootRegistry.NestedRegistryCount, Is.Zero);
            Assert.That(rootRegistry.TryGetTransform("Nested.Target", out _), Is.False);
        }

        [Test]
        public void NestedRegistryLookup_SkipsNestedRegistryThatIsNotBuilt()
        {
            GameObject root = CreateRoot("RootRegistry");
            TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
            GameObject nestedObject = new GameObject("NestedRegistry");
            nestedObject.transform.SetParent(root.transform, false);
            TransformKeyRegistry nestedRegistry = nestedObject.AddComponent<TransformKeyRegistry>();
            SetEntries(nestedRegistry, new EntrySpec("Nested.Target", CreateChild(nestedObject.transform, "NestedTarget")));

            rootRegistry.BuildIndex();
            Assert.That(rootRegistry.NestedRegistryCount, Is.EqualTo(1));
            Assert.That(nestedRegistry.IsBuilt, Is.False);

            // The parent discovers the nested registry but must not build it on the query path.
            Assert.That(rootRegistry.TryGetTransform("Nested.Target", out _), Is.False);
            Assert.That(nestedRegistry.IsBuilt, Is.False);

            rootRegistry.BuildHierarchy();
            Assert.That(rootRegistry.TryGetTransform("Nested.Target", out _), Is.True);
        }

        [Test]
        public void ComputeStableHash_IsStableAcrossCallsAndBackends()
        {
            // Golden values pin the hash contract: if Fnv1a64 or the sentinel remap changes, this fails loudly
            // instead of silently invalidating every serialized key ever produced.
            Assert.That(TransformKeyRegistry.ComputeStableHash("abc"), Is.EqualTo(0xE71FA2190541574BUL));
            Assert.That(TransformKeyRegistry.ComputeStableHash("Key.00000"), Is.EqualTo(ComputeExpectedOrdinalHash("Key.00000")));
            Assert.That(TransformKeyRegistry.ComputeStableHash("abc"), Is.EqualTo(TransformKeyRegistry.ComputeStableHash("abc")));
            Assert.That(TransformKeyRegistry.ComputeStableHash("abc"), Is.Not.EqualTo(TransformKeyRegistry.ComputeStableHash("abd")));
        }

        [Test]
        public void ComputeStableHash_ReservedSentinelForNullOrEmpty()
        {
            Assert.That(TransformKeyRegistry.ComputeStableHash(null), Is.Zero);
            Assert.That(TransformKeyRegistry.ComputeStableHash(string.Empty), Is.Zero);
        }

        [Test]
        public void StringAndHashLookups_AgreeWhenKeyExistsInLocalAndNested()
        {
            GameObject root = CreateRoot("RootRegistry");
            TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
            GameObject nestedObject = new GameObject("NestedRegistry");
            nestedObject.transform.SetParent(root.transform, false);
            TransformKeyRegistry nestedRegistry = nestedObject.AddComponent<TransformKeyRegistry>();

            Transform local = CreateChild(root.transform, "Local");
            Transform nested = CreateChild(nestedObject.transform, "Nested");
            SetEntries(rootRegistry, new EntrySpec("Shared.Key", local));
            SetEntries(nestedRegistry, new EntrySpec("Shared.Key", nested));

            nestedRegistry.BuildIndex();
            rootRegistry.BuildIndex();

            ulong hash = TransformKeyRegistry.ComputeStableHash("Shared.Key");

            Assert.That(rootRegistry.TryGetTransform("Shared.Key", out Transform stringResult), Is.True);
            Assert.That(rootRegistry.TryGetTransform(hash, out Transform hashResult), Is.True);
            Assert.That(stringResult, Is.SameAs(local));
            Assert.That(hashResult, Is.SameAs(stringResult), "String and hash lookups must agree on the same input domain.");
        }

        [Test]
        public void HashLookup_RejectsCollisionBetweenDistinctKeys()
        {
            GameObject root = CreateRoot("Registry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            Transform first = CreateChild(root.transform, "First");
            Transform second = CreateChild(root.transform, "Second");
            SetEntries(registry, new EntrySpec("alpha", first), new EntrySpec("beta", second));
            registry.BuildIndex();

            // A hash no authored key produces must not resolve to a guess.
            ulong foreignHash = TransformKeyRegistry.ComputeStableHash("gamma");
            Assert.That(registry.TryGetTransform(foreignHash, out Transform resolved), Is.False);
            Assert.That(resolved, Is.Null);

            Assert.That(registry.TryGetTransform(TransformKeyRegistry.ComputeStableHash("alpha"), out Transform alpha), Is.True);
            Assert.That(alpha, Is.SameAs(first));
        }

        [Test]
        public void Lookup_ReturnsFalseUntilIndexIsExplicitlyBuilt()
        {
            GameObject root = CreateRoot("Registry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            Transform target = CreateChild(root.transform, "Target");
            SetEntries(registry, new EntrySpec("Target", target));

            // SetEntries calls Invalidate; no lifecycle callback builds the index in EditMode.
            Assert.That(registry.IsBuilt, Is.False);
            Assert.That(registry.TryGetTransform("Target", out Transform unbuilt), Is.False);
            Assert.That(unbuilt, Is.Null);
            Assert.That(
                registry.TryGetTransform(TransformKeyRegistry.ComputeStableHash("Target"), out Transform unbuiltHash),
                Is.False);
            Assert.That(unbuiltHash, Is.Null);

            registry.BuildIndex();
            Assert.That(registry.TryGetTransform("Target", out Transform built), Is.True);
            Assert.That(built, Is.SameAs(target));
        }

        [Test]
        public void Invalidate_DoesNotRebuildImplicitlyAndAdvancesGeneration()
        {
            GameObject root = CreateRoot("Registry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            Transform target = CreateChild(root.transform, "Target");
            SetEntries(registry, new EntrySpec("Target", target));
            registry.BuildIndex();

            uint generationAfterBuild = registry.Generation;
            Assert.That(registry.TryGetTransform("Target", out _), Is.True);

            registry.Invalidate();
            Assert.That(registry.IsBuilt, Is.False);
            Assert.That(registry.Generation, Is.EqualTo(generationAfterBuild + 1));

            // A lookup must not silently rebuild; it fails and leaves the registry unbuilt.
            Assert.That(registry.TryGetTransform("Target", out _), Is.False);
            Assert.That(registry.IsBuilt, Is.False);

            registry.BuildIndex();
            Assert.That(registry.TryGetTransform("Target", out Transform rebuilt), Is.True);
            Assert.That(rebuilt, Is.SameAs(target));
        }

        [Test]
        public void Invalidate_IsIdempotentWhileAlreadyUnbuilt()
        {
            GameObject root = CreateRoot("Registry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            SetEntries(registry, new EntrySpec("Target", CreateChild(root.transform, "Target")));
            registry.BuildIndex();
            registry.Invalidate();

            uint generation = registry.Generation;
            registry.Invalidate();
            registry.Invalidate();

            Assert.That(registry.Generation, Is.EqualTo(generation));
        }

        [Test]
        public void Invalidate_PropagatesToAncestorRegistries()
        {
            GameObject root = CreateRoot("RootRegistry");
            TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
            GameObject nestedObject = new GameObject("NestedRegistry");
            nestedObject.transform.SetParent(root.transform, false);
            TransformKeyRegistry nestedRegistry = nestedObject.AddComponent<TransformKeyRegistry>();

            rootRegistry.BuildIndex();
            Assert.That(rootRegistry.IsBuilt, Is.True);

            // Editing a nested registry's authored entries must stale every ancestor that caches it.
            SetEntries(nestedRegistry, new EntrySpec("Nested.Target", CreateChild(nestedObject.transform, "NestedTarget")));
            Assert.That(nestedRegistry.IsBuilt, Is.False);

            rootRegistry.BuildIndex();
            Assert.That(rootRegistry.IsBuilt, Is.True);
            Assert.That(rootRegistry.NestedRegistryCount, Is.EqualTo(1));
        }

        [Test]
        public void Lookup_DoesNotAllocateAfterBuild()
        {
            GameObject root = CreateRoot("Registry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            var entries = new EntrySpec[64];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = new EntrySpec(string.Concat("Key.", i), CreateChild(root.transform, string.Concat("Child", i)));
            }

            SetEntries(registry, entries);
            registry.BuildIndex();

            ulong hash = TransformKeyRegistry.ComputeStableHash("Key.63");
            for (int i = 0; i < 256; i++)
            {
                registry.TryGetTransform("Key.63", out _);
                registry.TryGetTransform(hash, out _);
                registry.TryGetLocalTransform("Key.63", out _);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4_096; i++)
            {
                registry.TryGetTransform("Key.0", out Transform _);
                registry.TryGetTransform(hash, out Transform _);
                registry.TryGetLocalTransform("Key.32", out Transform _);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, "Built-index lookups must not allocate managed memory.");
        }

        [Test]
        public void GetTransformOrFind_OnlyFallsBackWhenEnabled()
        {
            GameObject root = CreateRoot("Registry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            Transform direct = CreateChild(root.transform, "Direct");
            SetEntries(registry, new EntrySpec("Direct", direct));
            registry.BuildIndex();

            Assert.That(registry.GetTransformOrFind("Direct"), Is.SameAs(direct));
            Assert.That(registry.GetTransformOrFind("Missing"), Is.Null);
            Assert.That(registry.GetTransformOrFind(string.Empty), Is.Null);
            Assert.That(registry.GetTransformOrFind(null), Is.Null);

            SetFindFallback(registry, true);
            registry.Invalidate();
            registry.BuildIndex();
            Assert.That(registry.GetTransformOrFind("Direct"), Is.SameAs(direct));
            Assert.That(registry.GetTransformOrFind("Missing"), Is.Null);
        }

        [Test]
        public void TryGetTransform_RejectsNullOrEmptyAndZeroHash()
        {
            GameObject root = CreateRoot("Registry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            SetEntries(registry, new EntrySpec("Target", CreateChild(root.transform, "Target")));
            registry.BuildIndex();

            Assert.That(registry.TryGetTransform((string)null, out _), Is.False);
            Assert.That(registry.TryGetTransform(string.Empty, out _), Is.False);
            Assert.That(registry.TryGetTransform(0UL, out _), Is.False);
            Assert.That(registry.TryGetLocalTransform((string)null, out _), Is.False);
            Assert.That(registry.TryGetLocalTransform(0UL, out _), Is.False);
        }

        [Test]
        public void EmptyRegistry_HandlesNullAndEmptySources()
        {
            GameObject root = CreateRoot("Registry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();

            SetEntries(registry, Array.Empty<EntrySpec>());
            registry.BuildIndex();
            Assert.That(registry.EntryCount, Is.Zero);
            Assert.That(registry.SourceEntryCount, Is.Zero);
            Assert.That(registry.IsBuilt, Is.True);
            Assert.That(registry.TryGetTransform("Any", out _), Is.False);
        }

        [Test]
        public void ThresholdBoundaries_ResolveAtFifteenSixteenAndSeventeenEntries()
        {
            for (int count = LinearSearchThreshold - 1; count <= LinearSearchThreshold + 1; count++)
            {
                GameObject root = CreateRoot(string.Concat("Registry", count));
                TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
                var entries = new EntrySpec[count];
                for (int i = 0; i < count; i++)
                {
                    entries[i] = new EntrySpec(string.Concat("Boundary.", i), CreateChild(root.transform, string.Concat("C", i)));
                }

                SetEntries(registry, entries);
                registry.BuildIndex();

                Assert.That(registry.EntryCount, Is.EqualTo(count), "Entry count mismatch at {0} entries.", count);
                Assert.That(registry.TryGetTransform("Boundary.0", out Transform first), Is.True);
                Assert.That(first, Is.SameAs(entries[0].Value));
                Assert.That(registry.TryGetTransform(string.Concat("Boundary.", count - 1), out Transform last), Is.True);
                Assert.That(last, Is.SameAs(entries[count - 1].Value));
                Assert.That(registry.TryGetTransform("Absent", out _), Is.False);
            }
        }

        [Test]
        public void BuildHierarchy_BuildsEveryNestedRegistry()
        {
            GameObject root = CreateRoot("RootRegistry");
            TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
            var nestedObject = new GameObject("NestedRegistry");
            nestedObject.transform.SetParent(root.transform, false);
            TransformKeyRegistry nestedRegistry = nestedObject.AddComponent<TransformKeyRegistry>();
            Transform target = CreateChild(nestedObject.transform, "NestedTarget");
            SetEntries(nestedRegistry, new EntrySpec("Nested.Target", target));
            SetEntries(rootRegistry, Array.Empty<EntrySpec>());

            Assert.That(nestedRegistry.IsBuilt, Is.False);
            rootRegistry.BuildHierarchy();

            Assert.That(rootRegistry.IsBuilt, Is.True);
            Assert.That(nestedRegistry.IsBuilt, Is.True);
            Assert.That(rootRegistry.TryGetTransform("Nested.Target", out Transform resolved), Is.True);
            Assert.That(resolved, Is.SameAs(target));
            Assert.That(rootRegistry.NestedRegistryCount, Is.EqualTo(1));
        }

        [Test]
        public void NestedRegistries_AreStoredWithoutNullOrInactiveHoles()
        {
            GameObject root = CreateRoot("RootRegistry");
            TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
            GameObject activeObject = new GameObject("Active");
            activeObject.transform.SetParent(root.transform, false);
            activeObject.AddComponent<TransformKeyRegistry>();

            GameObject disabledObject = new GameObject("Disabled");
            disabledObject.transform.SetParent(root.transform, false);
            TransformKeyRegistry disabled = disabledObject.AddComponent<TransformKeyRegistry>();
            disabled.enabled = false;

            // Dead components leave destroyed-object handles behind; the build must filter them rather than let the
            // query loop discover them. Enumerating via NestedRegistryCount proves the array is dense.
            GameObject doomedObject = new GameObject("Doomed");
            doomedObject.transform.SetParent(root.transform, false);
            doomedObject.AddComponent<TransformKeyRegistry>();
            UnityEngine.Object.DestroyImmediate(doomedObject);

            rootRegistry.BuildIndex();

            Assert.That(rootRegistry.NestedRegistryCount, Is.EqualTo(1));
        }

        [Test]
        public void NestedRegistryLookup_ResolvesPathModeAcrossIntermediateNodes()
        {
            GameObject root = CreateRoot("RootRegistry");
            TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
            SetEntries(rootRegistry, Array.Empty<EntrySpec>());

            // Intermediate nodes without a registry must not block the walk to a deeper one.
            Transform cursor = root.transform;
            for (int i = 0; i < 3; i++)
            {
                var node = new GameObject(string.Concat("Plain", i));
                node.transform.SetParent(cursor, false);
                cursor = node.transform;
            }

            var nestedObject = new GameObject("BoneRegistry");
            nestedObject.transform.SetParent(cursor, false);
            TransformKeyRegistry nestedRegistry = nestedObject.AddComponent<TransformKeyRegistry>();
            Transform target = CreateChild(nestedObject.transform, "Hand");
            SetEntries(nestedRegistry, new EntrySpec("Hand", target));

            nestedRegistry.BuildIndex();
            rootRegistry.BuildIndex();

            Assert.That(rootRegistry.NestedRegistryCount, Is.EqualTo(1));
            Assert.That(rootRegistry.TryGetTransform("Hand", out Transform resolved), Is.True);
            Assert.That(resolved, Is.SameAs(target));
        }

        [Test]
        public void LeafThenPathMode_DisambiguatesRepeatedLeafNamesAcrossNestedRegistries()
        {
            // A rig where two subtrees both own a node called "Hand": a plain leaf key cannot address them, which is
            // exactly the multi-level case the path mode exists for.
            GameObject root = CreateRoot("RigRegistry");
            TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(rootRegistry, RegistryKeyPathMode.LeafThenPath);
            SetEntries(rootRegistry, Array.Empty<EntrySpec>());

            GameObject leftObject = new GameObject("LeftArm");
            leftObject.transform.SetParent(root.transform, false);
            TransformKeyRegistry leftRegistry = leftObject.AddComponent<TransformKeyRegistry>();
            GameObject rightObject = new GameObject("RightArm");
            rightObject.transform.SetParent(root.transform, false);
            TransformKeyRegistry rightRegistry = rightObject.AddComponent<TransformKeyRegistry>();

            Transform leftHand = CreateChild(leftObject.transform, "Hand");
            Transform rightHand = CreateChild(rightObject.transform, "Hand");

            SetKeyPathMode(leftRegistry, RegistryKeyPathMode.LeafThenPath);
            SetKeyPathMode(rightRegistry, RegistryKeyPathMode.LeafThenPath);
            SetEntries(leftRegistry, new EntrySpec("Hand", leftHand));
            SetEntries(rightRegistry, new EntrySpec("Hand", rightHand));

            leftRegistry.BuildHierarchy();
            rightRegistry.BuildHierarchy();
            rootRegistry.BuildHierarchy();

            Assert.That(leftRegistry.TryGetTransform("Hand", out Transform leftResolved), Is.True);
            Assert.That(leftResolved, Is.SameAs(leftHand));

            // Root-level addressing reaches each arm through its registry-name prefix, which is the whole point:
            // the repeated "Hand" leaf is now reachable unambiguously.
            Assert.That(rootRegistry.TryGetTransform("LeftArm/Hand", out Transform leftFromRoot), Is.True);
            Assert.That(leftFromRoot, Is.SameAs(leftHand));
            Assert.That(rootRegistry.TryGetTransform("RightArm/Hand", out Transform rightFromRoot), Is.True);
            Assert.That(rightFromRoot, Is.SameAs(rightHand));

            // A bare leaf at the root still resolves to the first matching descendant, preserving the
            // deterministic first-wins contract instead of becoming ambiguous.
            Assert.That(rootRegistry.TryGetTransform("Hand", out Transform bare), Is.True);
            Assert.That(bare, Is.SameAs(leftHand));
        }

        [Test]
        public void LeafThenPathMode_RegistersBothGrandchildrenWhenSiblingsShareAGrandchildName()
        {
            // A single flat registry, no nested registries: two differently-named children ("LeftArm", "RightArm")
            // each owning a node called "Hand". The short key collides, so the qualified alias is the only form that
            // can keep the second grandchild addressable -- it must not be discarded along with the duplicate primary.
            GameObject root = CreateRoot("RigRegistry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(registry, RegistryKeyPathMode.LeafThenPath);

            Transform leftArm = CreateChild(root.transform, "LeftArm");
            Transform rightArm = CreateChild(root.transform, "RightArm");
            Transform leftHand = CreateChild(leftArm, "Hand");
            Transform rightHand = CreateChild(rightArm, "Hand");

            SetEntries(
                registry,
                new EntrySpec("LeftArm", leftArm),
                new EntrySpec("Hand", leftHand),
                new EntrySpec("RightArm", rightArm),
                new EntrySpec("Hand", rightHand));

            registry.BuildIndex();

            // Four authored nodes are all reachable: two arms plus three key forms that cover both hands.
            Assert.That(registry.SourceEntryCount, Is.EqualTo(4));
            Assert.That(registry.InvalidEntryCount, Is.EqualTo(0));
            Assert.That(registry.UnindexedEntryCount, Is.EqualTo(0));
            Assert.That(registry.DuplicateKeyCount, Is.EqualTo(1));
            Assert.That(registry.EntryCount, Is.EqualTo(5));

            Assert.That(registry.TryGetTransform("LeftArm/Hand", out Transform leftResolved), Is.True);
            Assert.That(leftResolved, Is.SameAs(leftHand));

            // The regression: the right grandchild shares the bare name, so only its path can address it.
            Assert.That(registry.TryGetTransform("RightArm/Hand", out Transform rightResolved), Is.True);
            Assert.That(rightResolved, Is.SameAs(rightHand));

            // The bare key stays on the first authored hand, and the arms keep resolving under their own names.
            Assert.That(registry.TryGetTransform("Hand", out Transform bare), Is.True);
            Assert.That(bare, Is.SameAs(leftHand));
            Assert.That(registry.TryGetTransform("RightArm", out Transform resolvedRightArm), Is.True);
            Assert.That(resolvedRightArm, Is.SameAs(rightArm));
        }

        [Test]
        public void PathMode_DuplicateSiblingNamesKeepDistinctKeysAndReportFullyShadowedEntries()
        {
            // Two identically-named siblings under one parent is legal in Unity (runtime-built hierarchies
            // produce them), and their path prefixes are identical by construction. The registry must keep
            // entries whose authored keys differ addressable through their qualified forms, while an entry
            // whose every form collides is reported as unindexed instead of silently disappearing. This also
            // exercises the reference-based prefix walk for a node whose nearest cached ancestor is a
            // same-named sibling of one of its own ancestors.
            GameObject root = CreateRoot("RigRegistry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(registry, RegistryKeyPathMode.LeafThenPath);

            Transform arm = CreateChild(root.transform, "Arm");
            Transform firstHand = CreateChild(arm, "Hand");
            Transform secondHand = CreateChild(arm, "Hand");
            Transform firstSocket = CreateChild(firstHand, "Socket");
            Transform secondSocket = CreateChild(secondHand, "Socket");

            SetEntries(
                registry,
                new EntrySpec("Grip", firstSocket),
                new EntrySpec("GripAlternate", secondSocket),
                new EntrySpec("Hand", firstHand),
                new EntrySpec("Hand", secondHand));

            registry.BuildIndex();

            // Distinct authored keys stay addressable even though both sockets sit under identically-named
            // parents: the bare key and the qualified path both reach the first socket.
            Assert.That(registry.TryGetTransform("Grip", out Transform gripResolved), Is.True);
            Assert.That(gripResolved, Is.SameAs(firstSocket));
            Assert.That(registry.TryGetTransform("Arm/Hand/Grip", out Transform qualifiedGrip), Is.True);
            Assert.That(qualifiedGrip, Is.SameAs(firstSocket));

            // The second socket is only reachable through its own qualified form; the first-Hand prefix and
            // the duplicate-Hand prefix compose the same string, so the key is what separates them.
            Assert.That(registry.TryGetTransform("Arm/Hand/GripAlternate", out Transform alternateResolved), Is.True);
            Assert.That(alternateResolved, Is.SameAs(secondSocket));

            // Both hands claim the same bare key and the same qualified path, so the second hand is reachable
            // through no form at all: one duplicate primary plus one unindexed entry.
            Assert.That(registry.DuplicateKeyCount, Is.EqualTo(1));
            Assert.That(registry.UnindexedEntryCount, Is.EqualTo(1));
            Assert.That(registry.InvalidEntryCount, Is.EqualTo(0));
            Assert.That(registry.TryGetTransform("Hand", out Transform handResolved), Is.True);
            Assert.That(handResolved, Is.SameAs(firstHand));
            Assert.That(registry.TryGetTransform("Arm/Hand", out Transform armHand), Is.True);
            Assert.That(armHand, Is.SameAs(firstHand));
        }

        [Test]
        public void HashLookup_ResolvesRepeatedLeafNameThroughItsQualifiedAlias()
        {
            // The precomputed-hash overload must agree with the string overload on every form the index stores.
            GameObject root = CreateRoot("RigRegistry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(registry, RegistryKeyPathMode.LeafThenPath);

            Transform leftArm = CreateChild(root.transform, "LeftArm");
            Transform rightArm = CreateChild(root.transform, "RightArm");
            Transform leftHand = CreateChild(leftArm, "Hand");
            Transform rightHand = CreateChild(rightArm, "Hand");

            SetEntries(
                registry,
                new EntrySpec("Hand", leftHand),
                new EntrySpec("Hand", rightHand));
            registry.BuildIndex();

            // The right hand's bare key lost to the first authored entry, so the bare hash is unambiguous and
            // keeps the first-wins contract. Its qualified alias is what makes the right hand reachable.
            Assert.That(registry.TryGetTransform(TransformKeyRegistry.ComputeStableHash("Hand"), out Transform bare), Is.True);
            Assert.That(bare, Is.SameAs(leftHand));
            Assert.That(registry.TryGetTransform(TransformKeyRegistry.ComputeStableHash("LeftArm/Hand"), out Transform leftByPath), Is.True);
            Assert.That(leftByPath, Is.SameAs(leftHand));
            Assert.That(registry.TryGetTransform(TransformKeyRegistry.ComputeStableHash("RightArm/Hand"), out Transform rightByPath), Is.True);
            Assert.That(rightByPath, Is.SameAs(rightHand));

            // A leaf and its own qualified alias share one Transform, so the hash overload must resolve them
            // rather than report the two distinct keys as an ambiguity.
            SetEntries(
                registry,
                new EntrySpec("Grip", leftHand),
                new EntrySpec("Hand", rightHand));
            registry.BuildIndex();

            Assert.That(registry.TryGetTransform(TransformKeyRegistry.ComputeStableHash("Hand"), out Transform resolvedBare), Is.True);
            Assert.That(resolvedBare, Is.SameAs(rightHand));
            Assert.That(registry.TryGetTransform(TransformKeyRegistry.ComputeStableHash("RightArm/Hand"), out Transform resolvedAlias), Is.True);
            Assert.That(resolvedAlias, Is.SameAs(rightHand));
        }

        [Test]
        public void UnindexedEntryCount_ReportsRowsShadowedInEveryForm()
        {
            // Authored twice under the same key with no path to distinguish them: both forms are taken by the first
            // row, so the second contributes nothing. The counter is what surfaces the conflict in the inspector.
            GameObject root = CreateRoot("RigRegistry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            Transform first = CreateChild(root.transform, "Socket");
            Transform second = CreateChild(root.transform, "SocketCopy");

            SetEntries(
                registry,
                new EntrySpec("Socket", first),
                new EntrySpec("Socket", second));

            registry.BuildIndex();

            Assert.That(registry.EntryCount, Is.EqualTo(1));
            Assert.That(registry.DuplicateKeyCount, Is.EqualTo(1));
            Assert.That(registry.UnindexedEntryCount, Is.EqualTo(1));
        }

        [Test]
        public void HashLookup_TreatsTwoKeysOnOneTransformAsResolvableNotAmbiguous()
        {
            // Force two distinct keys onto one hash by splitting one authored key with the separator: the index
            // then holds "Alias/Grip" (primary, path only) and the same node is reached by both forms. Distinct
            // keys that agree on the Transform are alias forms, not a hash collision, so the hash overload must
            // resolve them instead of refusing.
            GameObject root = CreateRoot("RigRegistry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(registry, RegistryKeyPathMode.LeafThenPath);

            Transform mount = CreateChild(root.transform, "Mount");
            Transform grip = CreateChild(mount, "Grip");

            SetEntries(registry, new EntrySpec("Mount/Grip", grip));
            registry.BuildIndex();

            // Both the authored key and its qualified path address the same Transform.
            Assert.That(registry.TryGetLocalTransform("Mount/Grip", out Transform byAuthored), Is.True);
            Assert.That(byAuthored, Is.SameAs(grip));
            Assert.That(registry.TryGetLocalTransform("Mount/Mount/Grip", out Transform byQualified), Is.True);
            Assert.That(byQualified, Is.SameAs(grip));

            // Both resolve by hash as well, and both agree on the target.
            Assert.That(registry.TryGetTransform(TransformKeyRegistry.ComputeStableHash("Mount/Grip"), out Transform hashedAuthored), Is.True);
            Assert.That(hashedAuthored, Is.SameAs(grip));
            Assert.That(registry.TryGetTransform(TransformKeyRegistry.ComputeStableHash("Mount/Mount/Grip"), out Transform hashedQualified), Is.True);
            Assert.That(hashedQualified, Is.SameAs(grip));
        }

        [Test]
        public void PathOnlyMode_IndexesOnlyQualifiedPaths()
        {
            GameObject root = CreateRoot("RootRegistry");
            TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(rootRegistry, RegistryKeyPathMode.PathOnly);
            SetEntries(rootRegistry, Array.Empty<EntrySpec>());

            GameObject nestedObject = new GameObject("WeaponMount");
            nestedObject.transform.SetParent(root.transform, false);
            TransformKeyRegistry nestedRegistry = nestedObject.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(nestedRegistry, RegistryKeyPathMode.PathOnly);
            Transform muzzle = CreateChild(nestedObject.transform, "Muzzle");
            SetEntries(nestedRegistry, new EntrySpec("Muzzle", muzzle));

            nestedRegistry.BuildHierarchy();
            rootRegistry.BuildHierarchy();

            // A direct child keeps the bare key even in path mode: there is no prefix to qualify it with.
            Assert.That(nestedRegistry.TryGetTransform("Muzzle", out Transform localPath), Is.True);
            Assert.That(localPath, Is.SameAs(muzzle));
            // The ancestor addresses it through the registry chain.
            Assert.That(rootRegistry.TryGetTransform("WeaponMount/Muzzle", out Transform fromRoot), Is.True);
            Assert.That(fromRoot, Is.SameAs(muzzle));
            Assert.That(rootRegistry.TryGetTransform("Absent", out _), Is.False);
        }

        [Test]
        public void PathOnlyMode_QualifiesDeepEntryAndKeepsAuthoredKeyAsLastSegment()
        {
            GameObject root = CreateRoot("RootRegistry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(registry, RegistryKeyPathMode.PathOnly);
            Transform deep = CreateChild(CreateChild(root.transform, "Arm"), "Palm");
            // The authored key is semantic and must survive: only the prefix comes from the hierarchy.
            SetEntries(registry, new EntrySpec("GripSocket", deep));
            registry.BuildIndex();

            Assert.That(registry.TryGetTransform("Arm/GripSocket", out Transform resolved), Is.True);
            Assert.That(resolved, Is.SameAs(deep));
            Assert.That(registry.TryGetTransform("GripSocket", out _), Is.False);
            Assert.That(registry.TryGetTransform("Arm/Palm", out _), Is.False);
        }

        [Test]
        public void PathMode_ComposesNestedRegistryChainPrefix()
        {
            GameObject root = CreateRoot("StageRoot");
            TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(rootRegistry, RegistryKeyPathMode.PathOnly);
            SetEntries(rootRegistry, Array.Empty<EntrySpec>());

            var middleObject = new GameObject("ParticleSystem");
            middleObject.transform.SetParent(root.transform, false);
            TransformKeyRegistry middleRegistry = middleObject.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(middleRegistry, RegistryKeyPathMode.PathOnly);

            var leafObject = new GameObject("Trails");
            leafObject.transform.SetParent(middleObject.transform, false);
            TransformKeyRegistry leafRegistry = leafObject.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(leafRegistry, RegistryKeyPathMode.PathOnly);
            Transform emitter = CreateChild(leafObject.transform, "Emitter");
            SetEntries(leafRegistry, new EntrySpec("Emitter", emitter));

            leafRegistry.BuildHierarchy();
            middleRegistry.BuildHierarchy();
            rootRegistry.BuildHierarchy();

            // Each level resolves by its own relative form: a leaf keeps the bare key, a middle level prefixes its
            // own child registry, and the root carries the whole chain.
            Assert.That(leafRegistry.TryGetTransform("Emitter", out Transform fromLeaf), Is.True);
            Assert.That(fromLeaf, Is.SameAs(emitter));
            Assert.That(middleRegistry.TryGetTransform("Trails/Emitter", out Transform fromMiddle), Is.True);
            Assert.That(fromMiddle, Is.SameAs(emitter));
            Assert.That(rootRegistry.TryGetTransform("ParticleSystem/Trails/Emitter", out Transform fromRoot), Is.True);
            Assert.That(fromRoot, Is.SameAs(emitter));
            Assert.That(rootRegistry.NestedRegistryCount, Is.EqualTo(2));
        }

        [Test]
        public void PathMode_LeavesInheritAncestorPrefixWithoutBeingBuiltByTheParent()
        {
            GameObject root = CreateRoot("StageRoot");
            TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(rootRegistry, RegistryKeyPathMode.PathOnly);
            SetEntries(rootRegistry, Array.Empty<EntrySpec>());

            var nestedObject = new GameObject("WeaponMount");
            nestedObject.transform.SetParent(root.transform, false);
            TransformKeyRegistry nestedRegistry = nestedObject.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(nestedRegistry, RegistryKeyPathMode.PathOnly);
            Transform muzzle = CreateChild(nestedObject.transform, "Muzzle");
            SetEntries(nestedRegistry, new EntrySpec("Muzzle", muzzle));

            rootRegistry.BuildIndex();

            // The parent records the hop but must not build the child on the query path.
            Assert.That(nestedRegistry.IsBuilt, Is.False);
            Assert.That(rootRegistry.TryGetTransform("WeaponMount/Muzzle", out _), Is.False);
            Assert.That(nestedRegistry.IsBuilt, Is.False);

            rootRegistry.BuildHierarchy();
            Assert.That(rootRegistry.TryGetTransform("WeaponMount/Muzzle", out Transform resolved), Is.True);
            Assert.That(resolved, Is.SameAs(muzzle));
        }

        [Test]
        public void PathMode_LookupStillDoesNotAllocateAfterBuild()
        {
            GameObject root = CreateRoot("RootRegistry");
            TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(rootRegistry, RegistryKeyPathMode.LeafThenPath);
            SetEntries(rootRegistry, Array.Empty<EntrySpec>());

            GameObject nestedObject = new GameObject("NestedRegistry");
            nestedObject.transform.SetParent(root.transform, false);
            TransformKeyRegistry nestedRegistry = nestedObject.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(nestedRegistry, RegistryKeyPathMode.LeafThenPath);
            var entries = new EntrySpec[32];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = new EntrySpec(string.Concat("Node", i), CreateChild(nestedObject.transform, string.Concat("Node", i)));
            }

            SetEntries(nestedRegistry, entries);
            nestedRegistry.BuildHierarchy();
            rootRegistry.BuildHierarchy();

            for (int i = 0; i < 256; i++)
            {
                rootRegistry.TryGetTransform("Node0", out _);
                rootRegistry.TryGetTransform("NestedRegistry/Node0", out _);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4_096; i++)
            {
                // The leaf probe, the chain-qualified hit, and a miss that walks every descendant must all stay
                // allocation-free; the strip helper must not allocate a trimmed string on the miss path.
                rootRegistry.TryGetTransform("Node0", out Transform _);
                rootRegistry.TryGetTransform("NestedRegistry/Node15", out Transform _);
                rootRegistry.TryGetTransform("Absent", out Transform _);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, "Path-mode lookups must not allocate managed memory after a build.");
        }

        [Test]
        public void ComposeKey_MatchesWhatTheIndexStores()
        {
            GameObject root = CreateRoot("RootRegistry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(registry, RegistryKeyPathMode.LeafThenPath);
            Transform deep = CreateChild(CreateChild(root.transform, "Arm"), "Palm");
            SetEntries(registry, new EntrySpec("Hand", deep));
            registry.BuildIndex();

            // LeafThenPath keeps the authored key as the primary form and adds the qualified path alongside it, so
            // the semantic name survives whatever the GameObject is called.
            string composed = registry.ComposeKey("Hand", deep);
            Assert.That(composed, Is.EqualTo("Hand"));
            Assert.That(registry.TryGetLocalTransform(composed, out Transform viaComposed), Is.True);
            Assert.That(viaComposed, Is.SameAs(deep));

            // Both the short name and the disambiguating path resolve to the same node.
            Assert.That(registry.TryGetTransform("Hand", out Transform viaLeaf), Is.True);
            Assert.That(viaLeaf, Is.SameAs(deep));
            string qualified = string.Concat("Arm", TransformKeyRegistry.KeyPathSeparator, "Hand");
            Assert.That(registry.TryGetTransform(qualified, out Transform viaPath), Is.True);
            Assert.That(viaPath, Is.SameAs(deep));
            Assert.That(registry.EntryCount, Is.EqualTo(2), "Leaf-then-path indexes the key plus its qualified alias.");
        }

        [Test]
        public void DeepHierarchy_DoesNotOverflowAndResolvesEveryLevel()
        {
            const int depth = 512;
            GameObject root = CreateRoot("RootRegistry");
            TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
            SetEntries(rootRegistry, Array.Empty<EntrySpec>());

            Transform cursor = root.transform;
            TransformKeyRegistry deepest = null;
            for (int i = 0; i < depth; i++)
            {
                var node = new GameObject(string.Concat("Level", i));
                node.transform.SetParent(cursor, false);
                cursor = node.transform;
                if (i == depth - 1)
                {
                    deepest = node.AddComponent<TransformKeyRegistry>();
                }
            }

            Transform target = CreateChild(cursor, "DeepTarget");
            SetEntries(deepest, new EntrySpec("Deep.Target", target));

            // The flattened DFS must stay off the call stack so 512 levels resolve without a StackOverflow.
            rootRegistry.BuildHierarchy();
            Assert.That(rootRegistry.TryGetTransform("Deep.Target", out Transform resolved), Is.True);
            Assert.That(resolved, Is.SameAs(target));

            UnityEngine.Object.DestroyImmediate(cursor.gameObject);
        }

        [Test]
        public void PathMode_DeepChainBuildStaysLinearInEntryCount()
        {
            // Regression: composing the prefix walked the ancestor chain once per entry, which made a deep chain
            // quadratic (512 levels cost ~125 ms). The memo must keep the growth near linear, so doubling the
            // depth must not quadruple the build.
            const int shallowDepth = 128;
            const int deepDepth = 256;
            double shallowMs = MeasurePathModeChainBuild(shallowDepth);
            double deepMs = MeasurePathModeChainBuild(deepDepth);

            // Linear would be ~2x; the old quadratic behaviour produced ~4x. Allow generous headroom for a noisy
            // shared CI machine while still failing loudly if the memo is removed.
            double growth = deepMs / Math.Max(shallowMs, 0.0001);
            Assert.That(
                growth < 3.0,
                Is.True,
                string.Concat(
                    "Path-mode build grew ",
                    growth.ToString("F2", CultureInfo.InvariantCulture),
                    "x for a 2x depth increase (",
                    shallowMs.ToString("F2", CultureInfo.InvariantCulture),
                    "ms -> ",
                    deepMs.ToString("F2", CultureInfo.InvariantCulture),
                    "ms); the prefix memo is likely gone."));
        }
        [Test]
        public void PathMode_DeepChainResolvesEveryEntryByPath()
        {
            // The memo changes where the prefix is computed, not what it is: every level must still address by its
            // full path, and the shallow levels must not inherit a stale prefix from a previous build.
            const int depth = 64;
            GameObject root = CreateRoot("RootRegistry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            SetKeyPathMode(registry, RegistryKeyPathMode.PathOnly);

            var chain = new List<Transform>();
            var keys = new List<string>();
            var expectedPaths = new List<string>();
            Transform cursor = root.transform;
            var pathBuilder = new System.Text.StringBuilder();
            for (int i = 0; i < depth; i++)
            {
                var node = new GameObject(string.Concat("L", i));
                node.transform.SetParent(cursor, false);
                cursor = node.transform;
                chain.Add(cursor);
                keys.Add(node.name);

                // A direct child of the registry root has a bare name as its qualified path; deeper nodes
                // accumulate the ancestor chain.
                if (i == 0)
                {
                    pathBuilder.Append(node.name);
                }
                else
                {
                    pathBuilder.Append(TransformKeyRegistry.KeyPathSeparator).Append(node.name);
                }

                expectedPaths.Add(pathBuilder.ToString());
            }

            SetEntries(registry, BuildSpecs(keys, chain));
            registry.BuildIndex();

            // PathOnly stores only qualified paths, one per authored entry.
            Assert.That(registry.EntryCount, Is.EqualTo(depth));

            // Rebuild once more: the cache must not leak a prefix between builds.
            registry.Invalidate();
            registry.BuildIndex();
            Assert.That(registry.EntryCount, Is.EqualTo(depth));

            // Every level resolves to its own node, which is what proves the prefix is per-node and not shared.
            for (int i = 0; i < depth; i++)
            {
                Assert.That(
                    registry.TryGetTransform(expectedPaths[i], out Transform resolved),
                    Is.True,
                    string.Concat("Path '", expectedPaths[i], "' did not resolve."));
                Assert.That(resolved, Is.SameAs(chain[i]));
            }
        }

        private double MeasurePathModeChainBuild(int depth)
        {
            GameObject root = CreateRoot("BenchRegistry");
            try
            {
                TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
                SetKeyPathMode(registry, RegistryKeyPathMode.LeafThenPath);

                var chain = new List<Transform>();
                var keys = new List<string>();
                Transform cursor = root.transform;
                for (int i = 0; i < depth; i++)
                {
                    var node = new GameObject(string.Concat("L", i));
                    node.transform.SetParent(cursor, false);
                    cursor = node.transform;
                    chain.Add(cursor);
                    keys.Add(node.name);
                }

                SetEntries(registry, BuildSpecs(keys, chain));

                // Warm the transient buffers so the measured build is the steady-state one.
                registry.BuildIndex();
                registry.Invalidate();

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                registry.BuildIndex();
                stopwatch.Stop();
                return stopwatch.Elapsed.TotalMilliseconds;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static EntrySpec[] BuildSpecs(List<string> keys, List<Transform> transforms)
        {
            var specs = new EntrySpec[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                specs[i] = new EntrySpec(keys[i], transforms[i]);
            }

            return specs;
        }

        private static ulong ComputeExpectedOrdinalHash(string text)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            unchecked
            {
                ulong hash = offsetBasis;
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= prime;
                }

                return hash == 0UL ? 1UL : hash;
            }
        }

        private GameObject CreateRoot(string name)
        {
            var root = new GameObject(name);
            _roots.Add(root);
            return root;
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void SetEntries(TransformKeyRegistry registry, params EntrySpec[] values)
        {
            using (var serializedRegistry = new SerializedObject(registry))
            {
                serializedRegistry.Update();
                SerializedProperty entries = serializedRegistry.FindProperty("Entries");
                entries.arraySize = values.Length;
                for (int i = 0; i < values.Length; i++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("Key").stringValue = values[i].Key;
                    entry.FindPropertyRelative("Transform").objectReferenceValue = values[i].Value;
                }

                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
            }

            registry.Invalidate();
        }

        private static void SetFindFallback(TransformKeyRegistry registry, bool enabled)
        {
            using (var serializedRegistry = new SerializedObject(registry))
            {
                serializedRegistry.Update();
                serializedRegistry.FindProperty("UseTransformFindFallback").boolValue = enabled;
                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void SetKeyPathMode(TransformKeyRegistry registry, RegistryKeyPathMode mode)
        {
            using (var serializedRegistry = new SerializedObject(registry))
            {
                serializedRegistry.Update();
                serializedRegistry.FindProperty("KeyPathMode").enumValueIndex = (int)mode;
                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
            }

            registry.Invalidate();
        }

        private readonly struct EntrySpec
        {
            public readonly string Key;
            public readonly Transform Value;

            public EntrySpec(string key, Transform value)
            {
                Key = key;
                Value = value;
            }
        }
    }
}
