using System;
using System.Collections.Generic;
using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class CameraOutputOwnershipTests
    {
        private readonly List<GameObject> resources = new List<GameObject>(8);

        [TearDown]
        public void TearDown()
        {
            for (int i = resources.Count - 1; i >= 0; i--)
            {
                GameObject resource = resources[i];
                if (resource != null)
                {
                    Object.DestroyImmediate(resource);
                }
            }

            resources.Clear();
        }

        [Test]
        public void CompositeLease_ConflictIsAtomicAndReleasesAllResources()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            Object firstResource = CreateResource("First");
            Object sharedResource = CreateResource("Shared");
            Object unclaimedResource = CreateResource("Unclaimed");
            Object fourthResource = CreateResource("Fourth");

            CameraManager first = CreateManager(
                testWorld,
                "FirstManager",
                firstResource,
                sharedResource,
                out _);
            CameraManager conflicting = CreateManager(
                testWorld,
                "ConflictingManager",
                sharedResource,
                unclaimedResource,
                out _);
            CameraManager independent = CreateManager(
                testWorld,
                "IndependentManager",
                unclaimedResource,
                fourthResource,
                out _);

            Assert.IsNotNull(first.ActiveOutput);
            Assert.IsNull(conflicting.ActiveOutput);
            Assert.IsNotNull(independent.ActiveOutput,
                "A failed composite acquisition must not retain a partial lease.");

            Assert.IsTrue(testWorld.World.DestroyActor(first));
            Assert.IsTrue(testWorld.World.DestroyActor(independent));
            Assert.IsTrue(conflicting.TryResolveAndBindOutput());
            Assert.IsNotNull(conflicting.ActiveOutput);
        }

        [Test]
        public void SharedArbiter_RejectsSameResourceAcrossParallelWorlds()
        {
            var arbiter = new CameraOutputLeaseArbiter();
            using GameplayTestWorld firstWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                discoverActiveSceneActors: false,
                cameraOutputLeaseArbiter: arbiter);
            using GameplayTestWorld secondWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                discoverActiveSceneActors: false,
                cameraOutputLeaseArbiter: arbiter);
            Object shared = CreateResource("SharedAcrossWorlds");
            Object firstUnique = CreateResource("FirstUnique");
            Object secondUnique = CreateResource("SecondUnique");

            CameraManager first = CreateManager(
                firstWorld,
                "FirstWorldManager",
                shared,
                firstUnique,
                out _);
            CameraManager second = CreateManager(
                secondWorld,
                "SecondWorldManager",
                shared,
                secondUnique,
                out _);

            Assert.IsNotNull(first.ActiveOutput);
            Assert.IsNull(second.ActiveOutput);

            Assert.IsTrue(firstWorld.World.DestroyActor(first));
            Assert.IsTrue(second.TryResolveAndBindOutput());
            Assert.IsNotNull(second.ActiveOutput);
        }

        [Test]
        public void ActivationResourceMutation_IsRejectedAndOriginalLeaseIsReleased()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            Object firstResource = CreateResource("LeasedFirst");
            Object leasedSecondResource = CreateResource("LeasedSecond");
            Object replacementResource = CreateResource("UnleasedReplacement");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("MutatingManagerPrefab"));
            var mutatingOutput = new MutatingCameraOutput(
                firstResource,
                leasedSecondResource,
                replacementResource);
            manager.SetCameraOutput(mutatingOutput, rebindImmediately: false);

            manager.InitializeFor(testWorld.World.PlayerControllers[0]);

            Assert.IsNull(manager.ActiveOutput);
            Assert.IsFalse(mutatingOutput.IsActive);
            CameraManager claimant = CreateManager(
                testWorld,
                "OriginalResourceClaimant",
                firstResource,
                leasedSecondResource,
                out _);
            Assert.IsNotNull(claimant.ActiveOutput);
        }

        [Test]
        public void ActivationWorldTeardown_DoesNotCommitStaleOutputOrLease()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create(localPlayerCount: 1);
            Object firstResource = CreateResource("TeardownFirst");
            Object secondResource = CreateResource("TeardownSecond");
            CameraManager manager =
                testWorld.CreateAuthoringActor<CameraManager>("TeardownCameraManager");
            WorldShutdownCameraOutput output =
                manager.gameObject.AddComponent<WorldShutdownCameraOutput>();
            output.SetResources(firstResource, secondResource);
            manager.SetCameraOutput(output, rebindImmediately: false);
            testWorld.StartWorld();

            Assert.Throws<InvalidOperationException>(() =>
                manager.InitializeFor(testWorld.World.PlayerControllers[0]));

            Assert.AreEqual(WorldLifecycleState.Disposed, testWorld.World.LifecycleState);
            Assert.IsNull(manager.World);
            Assert.IsNull(manager.ActiveOutput);
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(output.Owner);
        }

        [Test]
        public void RawOutput_PrepareException_ReleasesPreparedStateBeforeRethrow()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            Object resource = CreateResource("PrepareExceptionResource");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("PrepareExceptionManagerPrefab"));
            var output = new FaultingRawCameraOutput(
                resource,
                RawOutputFailure.Prepare);
            manager.SetCameraOutput(output, rebindImmediately: false);

            Assert.Throws<InvalidOperationException>(() =>
                manager.InitializeFor(testWorld.World.PlayerControllers[0]));

            Assert.AreEqual(1, output.DeactivateCount);
            Assert.IsFalse(output.IsPrepared);
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(manager.ActiveOutput);
        }

        [Test]
        public void ActiveRawOutput_ReprepareException_ReleasesOutputAndLeaseBeforeRethrow()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            Object resource = CreateResource("ActivePrepareExceptionResource");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("ActivePrepareExceptionManagerPrefab"));
            var output = new FaultingRawCameraOutput(resource, RawOutputFailure.None);
            manager.SetCameraOutput(output, rebindImmediately: false);
            manager.InitializeFor(testWorld.World.PlayerControllers[0]);
            output.Failure = RawOutputFailure.Prepare;

            Assert.Throws<InvalidOperationException>(() => manager.TryResolveAndBindOutput());

            Assert.AreEqual(1, output.DeactivateCount);
            Assert.IsFalse(output.IsPrepared);
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(manager.ActiveOutput);

            CameraManager claimant = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("ActivePrepareExceptionClaimantPrefab"));
            var claimantOutput = new FaultingRawCameraOutput(resource, RawOutputFailure.None);
            claimant.SetCameraOutput(claimantOutput, rebindImmediately: false);
            claimant.InitializeFor(testWorld.World.PlayerControllers[0]);
            Assert.AreSame(claimantOutput, claimant.ActiveOutput);
        }

        [Test]
        public void ActiveRawOutput_ReentrantPrepare_IsRejectedAndRollsBackOutputAndLease()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            Object resource = CreateResource("ActiveReentrantPrepareResource");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("ActiveReentrantPrepareManagerPrefab"));
            var output = new FaultingRawCameraOutput(resource, RawOutputFailure.None);
            manager.SetCameraOutput(output, rebindImmediately: false);
            manager.InitializeFor(testWorld.World.PlayerControllers[0]);
            output.ReenterOnPrepare = true;

            Assert.Throws<InvalidOperationException>(() => manager.TryResolveAndBindOutput());

            Assert.AreEqual(1, output.DeactivateCount);
            Assert.IsFalse(output.IsPrepared);
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(manager.ActiveOutput);

            CameraManager claimant = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("ActiveReentrantPrepareClaimantPrefab"));
            var claimantOutput = new FaultingRawCameraOutput(resource, RawOutputFailure.None);
            claimant.SetCameraOutput(claimantOutput, rebindImmediately: false);
            claimant.InitializeFor(testWorld.World.PlayerControllers[0]);
            Assert.AreSame(claimantOutput, claimant.ActiveOutput);
        }

        [Test]
        public void ActiveRawOutput_WorldTeardownDuringReprepare_ReleasesPostTeardownPreparedState()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            Object resource = CreateResource("ActivePrepareWorldTeardownResource");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("ActivePrepareWorldTeardownManagerPrefab"));
            var output = new FaultingRawCameraOutput(resource, RawOutputFailure.None);
            manager.SetCameraOutput(output, rebindImmediately: false);
            manager.InitializeFor(testWorld.World.PlayerControllers[0]);
            output.DisposeWorldOnPrepare = true;

            Assert.IsFalse(manager.TryResolveAndBindOutput());

            Assert.AreEqual(WorldLifecycleState.Disposed, testWorld.World.LifecycleState);
            Assert.AreEqual(2, output.DeactivateCount);
            Assert.IsFalse(output.IsPrepared);
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(manager.ActiveOutput);
        }

        [TestCase(RawOutputFailure.ResourceCount)]
        [TestCase(RawOutputFailure.PreparedResource)]
        public void RawOutput_ResourceInspectionException_ReleasesPreparedStateAndFailsClosed(
            RawOutputFailure failure)
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            Object resource = CreateResource(failure + "Resource");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>(failure + "ManagerPrefab"));
            var output = new FaultingRawCameraOutput(resource, failure);
            manager.SetCameraOutput(output, rebindImmediately: false);

            Assert.DoesNotThrow(() =>
                manager.InitializeFor(testWorld.World.PlayerControllers[0]));

            Assert.AreEqual(1, output.DeactivateCount);
            Assert.IsFalse(output.IsPrepared);
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(manager.ActiveOutput);
        }

        [Test]
        public void ThrowingArbiterReleaseAll_DoesNotInterruptWorldTerminalCleanup()
        {
            var arbiter = new ThrowingReleaseAllArbiter();
            using GameplayTestWorld testWorld = GameplayTestWorld.Create(
                discoverActiveSceneActors: false,
                cameraOutputLeaseArbiter: arbiter);
            World world = testWorld.StartWorld();
            GameInstance instance = testWorld.Instance;
            WorldDefinition definition = world.Definition;
            var lifetimeToken = world.LifetimeToken;

            Assert.DoesNotThrow(instance.Dispose);

            Assert.AreEqual(1, arbiter.ReleaseAllCount);
            Assert.AreEqual(WorldLifecycleState.Disposed, world.LifecycleState);
            Assert.IsTrue(definition.IsDisposed);
            Assert.IsTrue(lifetimeToken.IsCancellationRequested);
            Assert.Throws<ObjectDisposedException>(() =>
            {
                _ = world.LifetimeToken;
            });
            Assert.IsNull(instance.CurrentWorld);
            Assert.IsTrue(instance.IsDisposed);
        }

        [Test]
        public void ActivationRejection_ReleasesCompositeLeaseForAnotherOutput()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            Object firstResource = CreateResource("First");
            Object secondResource = CreateResource("Second");

            CameraManager rejected = CreateManager(
                testWorld,
                "RejectedManager",
                firstResource,
                secondResource,
                out CompositeCameraOutput rejectedOutput,
                initialize: false);
            rejectedOutput.FailActivation = true;
            rejected.InitializeFor(testWorld.World.PlayerControllers[0]);

            CameraManager claimant = CreateManager(
                testWorld,
                "ClaimantManager",
                firstResource,
                secondResource,
                out _);

            Assert.IsNull(rejected.ActiveOutput);
            Assert.IsNotNull(claimant.ActiveOutput);
        }

        [Test]
        public void ActivationException_ReleasesCompositeLeaseForAnotherOutput()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            Object firstResource = CreateResource("First");
            Object secondResource = CreateResource("Second");

            CameraManager throwing = CreateManager(
                testWorld,
                "ThrowingManager",
                firstResource,
                secondResource,
                out CompositeCameraOutput throwingOutput,
                initialize: false);
            throwingOutput.ThrowDuringActivation = true;
            Assert.Throws<InvalidOperationException>(() =>
                throwing.InitializeFor(testWorld.World.PlayerControllers[0]));

            CameraManager claimant = CreateManager(
                testWorld,
                "ClaimantManager",
                firstResource,
                secondResource,
                out _);

            Assert.IsNull(throwing.ActiveOutput);
            Assert.IsNotNull(claimant.ActiveOutput);
        }

        [Test]
        public void DeactivationException_DoesNotLeakCompositeLease()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            Object firstResource = CreateResource("First");
            Object secondResource = CreateResource("Second");
            CameraManager first = CreateManager(
                testWorld,
                "FirstManager",
                firstResource,
                secondResource,
                out CompositeCameraOutput output);
            output.ThrowDuringDeactivation = true;

            Assert.DoesNotThrow(() => testWorld.World.DestroyActor(first));
            CameraManager claimant = CreateManager(
                testWorld,
                "ClaimantManager",
                firstResource,
                secondResource,
                out _);
            Assert.IsNotNull(claimant.ActiveOutput);
        }

        [Test]
        public void DestroyedActiveResource_ReleasesRemainingCompositeLeaseOnRebind()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            GameObject destroyedResource = CreateResource("Destroyed");
            Object survivingResource = CreateResource("Surviving");
            Object independentResource = CreateResource("Independent");
            CameraManager first = CreateManager(
                testWorld,
                "FirstManager",
                destroyedResource,
                survivingResource,
                out _);

            Object.DestroyImmediate(destroyedResource);
            Assert.IsFalse(first.TryResolveAndBindOutput());
            Assert.IsNull(first.ActiveOutput);

            CameraManager claimant = CreateManager(
                testWorld,
                "ClaimantManager",
                survivingResource,
                independentResource,
                out _);
            Assert.IsNotNull(claimant.ActiveOutput);
        }

        [Test]
        public void WorldUnbind_ResetsCameraDirtyFlagAndCurrentPoseValue()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create(localPlayerCount: 1);
            CameraManager sceneManager =
                testWorld.CreateAuthoringActor<CameraManager>("SceneCameraManager");
            testWorld.StartWorld();
            sceneManager.InitializeFor(testWorld.World.PlayerControllers[0]);
            sceneManager.UpdateCamera(1f / 60f);
            sceneManager.NotifyCameraStateChanged();
            Assert.IsTrue(sceneManager.HasCurrentPose);
            Assert.IsTrue(sceneManager.CameraStateDirty);

            testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult();

            Assert.IsFalse(sceneManager.HasCurrentPose);
            Assert.IsFalse(sceneManager.CameraStateDirty);
            Assert.AreEqual(Vector3.zero, sceneManager.CurrentPose.Position);
            Assert.AreEqual(default(Quaternion), sceneManager.CurrentPose.Rotation);
            Assert.Zero(sceneManager.CurrentPose.Fov);
        }

        private CameraManager CreateManager(
            GameplayTestWorld testWorld,
            string name,
            Object firstResource,
            Object secondResource,
            out CompositeCameraOutput output,
            bool initialize = true)
        {
            CameraManager prefab = testWorld.CreateAuthoringActor<CameraManager>(name + "Prefab");
            CameraManager manager = testWorld.World.SpawnActor(prefab);
            output = manager.gameObject.AddComponent<CompositeCameraOutput>();
            output.SetResources(firstResource, secondResource);
            manager.SetCameraOutput(output, rebindImmediately: false);
            if (initialize)
            {
                manager.InitializeFor(testWorld.World.PlayerControllers[0]);
            }

            return manager;
        }

        private GameObject CreateResource(string name)
        {
            var resource = new GameObject(name);
            resources.Add(resource);
            return resource;
        }

        private sealed class CompositeCameraOutput : CameraOutputBehaviour
        {
            private Object firstResource;
            private Object secondResource;

            public bool FailActivation { get; set; }
            public bool ThrowDuringActivation { get; set; }
            public bool ThrowDuringDeactivation { get; set; }

            public override Object OutputObject => firstResource;

            public void SetResources(Object first, Object second)
            {
                ThrowIfPreparedOrActive();
                firstResource = first;
                secondResource = second;
            }

            protected override bool OnTryPrepare(out string error)
            {
                if (!TryAddPreparedResource(firstResource, out error))
                {
                    return false;
                }

                return TryAddPreparedResource(secondResource, out error);
            }

            protected override bool OnActivate(CameraManager newOwner, out string error)
            {
                if (ThrowDuringActivation)
                {
                    throw new InvalidOperationException("Activation failure requested by the test.");
                }

                error = FailActivation ? "Activation rejection requested by the test." : null;
                return !FailActivation;
            }

            protected override void OnApplyPose(in CameraPose pose)
            {
            }

            protected override void OnDeactivate()
            {
                if (ThrowDuringDeactivation)
                {
                    throw new InvalidOperationException("Deactivation failure requested by the test.");
                }
            }
        }

        private sealed class WorldShutdownCameraOutput : CameraOutputBehaviour
        {
            private Object firstResource;
            private Object secondResource;

            public override Object OutputObject => firstResource;

            public void SetResources(Object first, Object second)
            {
                ThrowIfPreparedOrActive();
                firstResource = first;
                secondResource = second;
            }

            protected override bool OnTryPrepare(out string error)
            {
                return TryAddPreparedResource(firstResource, out error) &&
                       TryAddPreparedResource(secondResource, out error);
            }

            protected override bool OnActivate(CameraManager newOwner, out string error)
            {
                newOwner.World.GameInstance.Dispose();
                error = null;
                return true;
            }

            protected override void OnApplyPose(in CameraPose pose)
            {
            }
        }

        public enum RawOutputFailure : byte
        {
            None,
            Prepare,
            ResourceCount,
            PreparedResource,
        }

        private sealed class FaultingRawCameraOutput : ICameraOutput
        {
            private readonly Object resource;
            public FaultingRawCameraOutput(Object resource, RawOutputFailure failure)
            {
                this.resource = resource;
                Failure = failure;
            }

            public string DisplayName => nameof(FaultingRawCameraOutput);
            public bool IsActive { get; private set; }
            public bool IsPrepared { get; private set; }
            public CameraManager Owner { get; private set; }
            public Object OutputObject => resource;
            public int DeactivateCount { get; private set; }
            public RawOutputFailure Failure { get; set; }
            public bool ReenterOnPrepare { get; set; }
            public bool DisposeWorldOnPrepare { get; set; }

            public int PreparedResourceCount
            {
                get
                {
                    if (Failure == RawOutputFailure.ResourceCount)
                    {
                        throw new InvalidOperationException(
                            "Resource-count failure requested by the test.");
                    }

                    return 1;
                }
            }

            public bool TryPrepare(out string error)
            {
                if (ReenterOnPrepare && Owner != null)
                {
                    Owner.SetCameraOutput(null);
                }

                World owningWorld = Owner?.World;
                if (DisposeWorldOnPrepare && owningWorld != null)
                {
                    owningWorld.GameInstance.Dispose();
                }

                IsPrepared = true;
                if (Failure == RawOutputFailure.Prepare)
                {
                    throw new InvalidOperationException(
                        "Preparation failure requested by the test.");
                }

                error = null;
                return true;
            }

            public Object GetPreparedResource(int index)
            {
                if (Failure == RawOutputFailure.PreparedResource)
                {
                    throw new InvalidOperationException(
                        "Prepared-resource failure requested by the test.");
                }

                if (index != 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return resource;
            }

            public bool TryActivate(CameraManager owner, out string error)
            {
                Owner = owner;
                IsActive = true;
                error = null;
                return true;
            }

            public void ApplyPose(in CameraPose pose)
            {
            }

            public void Deactivate(CameraManager owner)
            {
                DeactivateCount++;
                IsPrepared = false;
                IsActive = false;
                Owner = null;
            }
        }

        private sealed class ThrowingReleaseAllArbiter : ICameraOutputLeaseArbiter
        {
            public int ReleaseAllCount { get; private set; }

            public bool TryAcquire(
                World world,
                CameraManager owner,
                ICameraOutput output,
                out CameraOutputLease lease,
                out string error)
            {
                lease = default;
                error = "Acquisition is not used by this test.";
                return false;
            }

            public void Release(
                World world,
                CameraManager owner,
                ICameraOutput output,
                in CameraOutputLease lease)
            {
            }

            public void ReleaseAll(World world)
            {
                ReleaseAllCount++;
                throw new InvalidOperationException(
                    "ReleaseAll failure requested by the test.");
            }
        }

        private sealed class MutatingCameraOutput : ICameraOutput
        {
            private readonly Object firstResource;
            private readonly Object leasedSecondResource;
            private readonly Object replacementResource;
            private bool useReplacement;

            public MutatingCameraOutput(
                Object firstResource,
                Object leasedSecondResource,
                Object replacementResource)
            {
                this.firstResource = firstResource;
                this.leasedSecondResource = leasedSecondResource;
                this.replacementResource = replacementResource;
            }

            public string DisplayName => nameof(MutatingCameraOutput);
            public bool IsActive { get; private set; }
            public CameraManager Owner { get; private set; }
            public Object OutputObject => firstResource;
            public int PreparedResourceCount => 2;

            public bool TryPrepare(out string error)
            {
                error = null;
                return true;
            }

            public Object GetPreparedResource(int index)
            {
                switch (index)
                {
                    case 0: return firstResource;
                    case 1: return useReplacement ? replacementResource : leasedSecondResource;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }

            public bool TryActivate(CameraManager owner, out string error)
            {
                Owner = owner;
                IsActive = true;
                useReplacement = true;
                error = null;
                return true;
            }

            public void ApplyPose(in CameraPose pose)
            {
            }

            public void Deactivate(CameraManager owner)
            {
                IsActive = false;
                Owner = null;
                useReplacement = false;
            }
        }
    }
}
