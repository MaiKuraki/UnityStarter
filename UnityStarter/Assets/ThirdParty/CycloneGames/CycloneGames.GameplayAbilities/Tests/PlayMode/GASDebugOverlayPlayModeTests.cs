using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.GameplayAbilities.Tests.PlayMode
{
    public sealed class GASDebugOverlayPlayModeTests
    {
        private readonly List<AbilitySystemComponent> createdASCs = new List<AbilitySystemComponent>(40);
        private readonly List<GameObject> createdOwners = new List<GameObject>(4);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GASDebugOverlay.Cleanup();
            yield return null;
            createdASCs.Clear();
            createdOwners.Clear();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GASDebugOverlay.ClearTargets();
            GASDebugOverlay.SetEnabled(false);
            GASDebugOverlay.Cleanup();

            for (int i = 0; i < createdASCs.Count; i++)
            {
                AbilitySystemComponent asc = createdASCs[i];
                if (asc != null && !asc.IsDisposed)
                {
                    asc.Dispose();
                }
            }

            for (int i = 0; i < createdOwners.Count; i++)
            {
                GameObject owner = createdOwners[i];
                if (owner != null)
                {
                    Object.Destroy(owner);
                }
            }

            createdASCs.Clear();
            createdOwners.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator TryAddTarget_TwoDistinctTargetsCoexistWithoutChangingVisibility()
        {
            AbilitySystemComponent player = CreateASC();
            AbilitySystemComponent enemy = CreateASC();
            GameObject playerOwner = CreateOwner("Player");
            GameObject enemyOwner = CreateOwner("Enemy");

            Assert.That(GASDebugOverlay.TryAddTarget(player, playerOwner), Is.True);
            Assert.That(GASDebugOverlay.IsInitialized, Is.True);
            Assert.That(GASDebugOverlay.IsActive, Is.False);
            Assert.That(GASDebugOverlay.TryAddTarget(enemy, enemyOwner), Is.True);
            Assert.That(GASDebugOverlay.BoundTargetCount, Is.EqualTo(2));
            Assert.That(GASDebugOverlay.IsActive, Is.False);

            yield return null;
        }

        [UnityTest]
        public IEnumerator TryAddTarget_DuplicateAndCapacityRemainBounded()
        {
            AbilitySystemComponent first = CreateASC();
            Assert.That(GASDebugOverlay.TryAddTarget(first, displayName: "First"), Is.True);
            Assert.That(GASDebugOverlay.TryAddTarget(first, displayName: "Updated First"), Is.True);
            Assert.That(GASDebugOverlay.BoundTargetCount, Is.EqualTo(1));

            int capacity = GASDebugOverlay.TargetCapacity;
            Assert.That(capacity, Is.InRange(1, 32));
            for (int i = 1; i < capacity; i++)
            {
                Assert.That(GASDebugOverlay.TryAddTarget(CreateASC(), displayName: "Target " + i), Is.True);
            }

            Assert.That(GASDebugOverlay.BoundTargetCount, Is.EqualTo(capacity));
            Assert.That(GASDebugOverlay.TryAddTarget(first, displayName: "Updated At Capacity"), Is.True);
            Assert.That(GASDebugOverlay.TryAddTarget(CreateASC(), displayName: "Rejected"), Is.False);
            Assert.That(GASDebugOverlay.BoundTargetCount, Is.EqualTo(capacity));

            first.Dispose();
            Assert.That(GASDebugOverlay.TryAddTarget(CreateASC(), displayName: "Replacement"), Is.True);
            Assert.That(GASDebugOverlay.BoundTargetCount, Is.EqualTo(capacity));

            yield return null;
        }

        [UnityTest]
        public IEnumerator RemoveTargetAndClearTargets_AreIdempotentAndPreserveVisibility()
        {
            AbilitySystemComponent first = CreateASC();
            AbilitySystemComponent second = CreateASC();
            Assert.That(GASDebugOverlay.TryAddTarget(first), Is.True);
            Assert.That(GASDebugOverlay.TryAddTarget(second), Is.True);
            GASDebugOverlay.SetEnabled(true);

            Assert.That(GASDebugOverlay.RemoveTarget(first), Is.True);
            Assert.That(GASDebugOverlay.RemoveTarget(first), Is.False);
            Assert.That(GASDebugOverlay.BoundTargetCount, Is.EqualTo(1));
            Assert.That(GASDebugOverlay.IsActive, Is.True);

            GASDebugOverlay.ClearTargets();
            GASDebugOverlay.ClearTargets();
            Assert.That(GASDebugOverlay.BoundTargetCount, Is.Zero);
            Assert.That(GASDebugOverlay.IsInitialized, Is.True);
            Assert.That(GASDebugOverlay.IsActive, Is.True);

            yield return null;
        }

        [UnityTest]
        public IEnumerator IsTargetRegistered_TracksAddRemoveDisposalAndCleanup()
        {
            AbilitySystemComponent first = CreateASC();
            AbilitySystemComponent second = CreateASC();

            Assert.That(GASDebugOverlay.IsInitialized, Is.False);
            Assert.That(GASDebugOverlay.IsTargetRegistered(null), Is.False);
            Assert.That(GASDebugOverlay.IsTargetRegistered(first), Is.False);
            Assert.That(GASDebugOverlay.IsInitialized, Is.False);
            Assert.That(GASDebugOverlay.TryAddTarget(first), Is.True);
            Assert.That(GASDebugOverlay.IsTargetRegistered(first), Is.True);
            Assert.That(GASDebugOverlay.IsTargetRegistered(second), Is.False);

            Assert.That(GASDebugOverlay.RemoveTarget(first), Is.True);
            Assert.That(GASDebugOverlay.IsTargetRegistered(first), Is.False);

            Assert.That(GASDebugOverlay.TryAddTarget(second), Is.True);
            second.Dispose();
            Assert.That(GASDebugOverlay.IsTargetRegistered(second), Is.False);
            Assert.That(GASDebugOverlay.BoundTargetCount, Is.Zero);

            AbilitySystemComponent replacement = CreateASC();
            Assert.That(GASDebugOverlay.TryAddTarget(replacement), Is.True);
            GASDebugOverlay.Cleanup();
            Assert.That(GASDebugOverlay.IsTargetRegistered(replacement), Is.False);

            yield return null;
        }

        [UnityTest]
        public IEnumerator Toggle_ReplacesTargetSetAndKeepsSingleTargetSemantics()
        {
            AbilitySystemComponent player = CreateASC();
            AbilitySystemComponent enemy = CreateASC();
            Assert.That(GASDebugOverlay.TryAddTarget(player), Is.True);
            Assert.That(GASDebugOverlay.TryAddTarget(enemy), Is.True);

            Assert.That(GASDebugOverlay.Toggle(player), Is.True);
            Assert.That(GASDebugOverlay.BoundTargetCount, Is.EqualTo(1));
            Assert.That(GASDebugOverlay.RemoveTarget(enemy), Is.False);

            Assert.That(GASDebugOverlay.TryAddTarget(enemy), Is.True);
            Assert.That(GASDebugOverlay.Toggle(enemy), Is.False);
            Assert.That(GASDebugOverlay.BoundTargetCount, Is.EqualTo(1));
            Assert.That(GASDebugOverlay.RemoveTarget(player), Is.False);

            yield return null;
        }

        [UnityTest]
        public IEnumerator InvalidTargets_DoNotInitializeOrMutate()
        {
            Assert.That(GASDebugOverlay.TryAddTarget(null), Is.False);
            Assert.That(GASDebugOverlay.IsInitialized, Is.False);

            AbilitySystemComponent disposed = CreateASC();
            disposed.Dispose();
            Assert.That(GASDebugOverlay.TryAddTarget(disposed), Is.False);
            Assert.That(GASDebugOverlay.IsInitialized, Is.False);
            Assert.That(GASDebugOverlay.BoundTargetCount, Is.Zero);

            yield return null;
        }

        [UnityTest]
        public IEnumerator Cleanup_IsIdempotentAndReinitializable()
        {
            Assert.That(GASDebugOverlay.TryAddTarget(CreateASC()), Is.True);
            GASDebugOverlay.SetEnabled(true);
            GASDebugOverlay.Cleanup();

            Assert.That(GASDebugOverlay.IsInitialized, Is.False);
            Assert.That(GASDebugOverlay.IsActive, Is.False);
            Assert.That(GASDebugOverlay.BoundTargetCount, Is.Zero);
            yield return null;

            GASDebugOverlay.Cleanup();
            Assert.That(GASDebugOverlay.TryAddTarget(CreateASC()), Is.True);
            Assert.That(GASDebugOverlay.IsInitialized, Is.True);
            Assert.That(GASDebugOverlay.BoundTargetCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CleanupThenInitializeInSameFrame_OldOnDestroyDoesNotClearNewAuthority()
        {
            Assert.That(GASDebugOverlay.TryAddTarget(CreateASC(), displayName: "Old"), Is.True);
            GASDebugOverlay.Cleanup();
            Assert.That(GASDebugOverlay.TryAddTarget(CreateASC(), displayName: "New"), Is.True);

            yield return null;

            Assert.That(GASDebugOverlay.IsInitialized, Is.True);
            Assert.That(GASDebugOverlay.BoundTargetCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ExternalDestruction_ResetsLifecycleAndAllowsRecreation()
        {
            Assert.That(GASDebugOverlay.TryAddTarget(CreateASC()), Is.True);
            GASDebugOverlay overlay = Object.FindObjectOfType<GASDebugOverlay>();
            Assert.That(overlay, Is.Not.Null);

            Object.Destroy(overlay.gameObject);
            yield return null;

            Assert.That(GASDebugOverlay.IsInitialized, Is.False);
            Assert.That(GASDebugOverlay.IsActive, Is.False);
            Assert.That(GASDebugOverlay.BoundTargetCount, Is.Zero);
            Assert.That(GASDebugOverlay.TryAddTarget(CreateASC()), Is.True);
            Assert.That(GASDebugOverlay.IsInitialized, Is.True);
        }

        [Test]
        public void StaticLifecycle_HasSubsystemRegistrationReset()
        {
            MethodInfo resetMethod = typeof(GASDebugOverlay).GetMethod(
                "ResetStaticState",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(resetMethod, Is.Not.Null);
            Assert.That(
                resetMethod.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false),
                Has.Length.EqualTo(1));
        }

        private AbilitySystemComponent CreateASC()
        {
            var asc = new AbilitySystemComponent();
            createdASCs.Add(asc);
            return asc;
        }

        private GameObject CreateOwner(string name)
        {
            var owner = new GameObject(name);
            createdOwners.Add(owner);
            return owner;
        }
    }
}
