using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Logging;
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
        public void DerivedOnDestroyCallingBase_ReleasesLeaseForAnotherOutput()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            Object firstResource = CreateResource("DestroyedOutputFirst");
            Object secondResource = CreateResource("DestroyedOutputSecond");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("DestroyedOutputManagerPrefab"));
            DestroyAwareCameraOutput output =
                manager.gameObject.AddComponent<DestroyAwareCameraOutput>();
            UnityLifecycleTestUtility.InvokeAwake(output);
            output.SetResources(firstResource, secondResource);
            manager.SetCameraOutput(output, rebindImmediately: false);
            manager.InitializeFor(testWorld.World.PlayerControllers[0]);
            Assert.AreSame(output, manager.ActiveOutput);

            output.InvokeOnDestroyForTest();
            Object.DestroyImmediate(output);

            Assert.IsTrue(output.DestroyHookInvoked);
            Assert.IsNull(manager.ActiveOutput);
            CameraManager claimant = CreateManager(
                testWorld,
                "DestroyedOutputClaimant",
                firstResource,
                secondResource,
                out _);
            Assert.IsNotNull(claimant.ActiveOutput);
        }

        [Test]
        public void CameraManagerLiveApi_RejectsRetainedWorkerThreadAccessBeforeUnityLookup()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("ThreadGuardManagerPrefab"));
            manager.InitializeFor(testWorld.World.PlayerControllers[0]);
            Exception getterFailure = null;
            Exception lookupFailure = null;

            var worker = new Thread(() =>
            {
                try
                {
                    _ = manager.CurrentPose;
                }
                catch (Exception exception)
                {
                    getterFailure = exception;
                }

                try
                {
                    manager.TryResolveAndBindOutput();
                }
                catch (Exception exception)
                {
                    lookupFailure = exception;
                }
            });
            worker.Start();
            Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(5)));

            Assert.IsInstanceOf<InvalidOperationException>(getterFailure);
            Assert.IsInstanceOf<InvalidOperationException>(lookupFailure);
        }

        [Test]
        public void ActivationUsesLeasedSnapshot_WhenFutureDiscoveryWouldReturnDifferentResources()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            Object firstResource = CreateResource("LeasedFirst");
            Object leasedSecondResource = CreateResource("LeasedSecond");
            Object replacementResource = CreateResource("UnleasedReplacement");
            Object replacementPairResource = CreateResource("ReplacementPair");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("MutatingManagerPrefab"));
            var mutatingOutput = new MutatingCameraOutput(
                firstResource,
                leasedSecondResource,
                replacementResource);
            manager.SetCameraOutput(mutatingOutput, rebindImmediately: false);

            manager.InitializeFor(testWorld.World.PlayerControllers[0]);

            Assert.AreSame(mutatingOutput, manager.ActiveOutput);
            Assert.IsTrue(mutatingOutput.IsActive);
            CameraManager claimant = CreateManager(
                testWorld,
                "OriginalResourceClaimant",
                firstResource,
                leasedSecondResource,
                out _);
            Assert.IsNull(claimant.ActiveOutput,
                "Activation must retain the exact snapshot leased before backend mutation begins.");

            CameraManager replacementClaimant = CreateManager(
                testWorld,
                "ReplacementResourceClaimant",
                replacementResource,
                replacementPairResource,
                out _);
            Assert.IsNotNull(replacementClaimant.ActiveOutput,
                "Resources only returned by future discovery must remain unleased.");
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
            UnityLifecycleTestUtility.InvokeAwake(output);
            output.SetResources(firstResource, secondResource);
            manager.SetCameraOutput(output, rebindImmediately: false);
            testWorld.StartWorld();

            Assert.Throws<WorldShutdownIncompleteException>(() =>
                manager.InitializeFor(testWorld.World.PlayerControllers[0]));

            Assert.AreEqual(WorldLifecycleState.Stopping, testWorld.World.LifecycleState);
            Assert.IsNull(manager.World);
            Assert.IsNull(manager.ActiveOutput);
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(output.Owner);

            Assert.DoesNotThrow(testWorld.Instance.Dispose);
            Assert.AreEqual(WorldLifecycleState.Disposed, testWorld.World.LifecycleState);
        }

        [Test]
        public void ResourceDiscoveryException_DoesNotInvokeBackendDeactivationOrAcquireLease()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            Object resource = CreateResource("DiscoveryExceptionResource");
            Object secondResource = CreateResource("DiscoveryExceptionSecond");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("DiscoveryExceptionManagerPrefab"));
            var output = new FaultingRawCameraOutput(
                resource,
                RawOutputFailure.DiscoveryException);
            manager.SetCameraOutput(output, rebindImmediately: false);

            Assert.Throws<InvalidOperationException>(() =>
                manager.InitializeFor(testWorld.World.PlayerControllers[0]));

            Assert.Zero(output.DeactivateCount);
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(manager.ActiveOutput);

            CameraManager claimant = CreateManager(
                testWorld,
                "DiscoveryExceptionClaimant",
                resource,
                secondResource,
                out _);
            Assert.IsNotNull(claimant.ActiveOutput);
        }

        [Test]
        public void ActiveOutput_RebindUsesCommittedSnapshotWithoutRediscovery()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            Object resource = CreateResource("ActiveSnapshotResource");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("ActiveSnapshotManagerPrefab"));
            var output = new FaultingRawCameraOutput(resource, RawOutputFailure.None);
            manager.SetCameraOutput(output, rebindImmediately: false);
            manager.InitializeFor(testWorld.World.PlayerControllers[0]);
            output.Failure = RawOutputFailure.DiscoveryException;

            Assert.IsTrue(manager.TryResolveAndBindOutput());

            Assert.AreEqual(1, output.DiscoveryCount);
            Assert.Zero(output.DeactivateCount);
            Assert.IsTrue(output.IsActive);
            Assert.AreSame(output, manager.ActiveOutput);
        }

        [Test]
        public void InvalidResourceSnapshot_FailsBeforeActivationWithoutDeactivation()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            Object resource = CreateResource("InvalidSnapshotResource");
            Object secondResource = CreateResource("InvalidSnapshotSecond");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("InvalidSnapshotManagerPrefab"));
            var output = new FaultingRawCameraOutput(
                resource,
                RawOutputFailure.InvalidResourceSet);
            manager.SetCameraOutput(output, rebindImmediately: false);

            Assert.DoesNotThrow(() =>
                manager.InitializeFor(testWorld.World.PlayerControllers[0]));

            Assert.Zero(output.DeactivateCount);
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(manager.ActiveOutput);

            CameraManager claimant = CreateManager(
                testWorld,
                "InvalidSnapshotClaimant",
                resource,
                secondResource,
                out _);
            Assert.IsNotNull(claimant.ActiveOutput);
        }

        [Test]
        public void ArbiterAcquireExceptionAfterCommit_RollsBackLeaseAndPreparedOutput()
        {
            var arbiter = new FaultInjectingLeaseArbiter
            {
                ThrowAfterAcquire = true,
            };
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                discoverActiveSceneActors: false,
                cameraOutputLeaseArbiter: arbiter);
            Object firstResource = CreateResource("AcquireExceptionFirst");
            Object secondResource = CreateResource("AcquireExceptionSecond");
            CameraManager manager = CreateManager(
                testWorld,
                "AcquireExceptionManager",
                firstResource,
                secondResource,
                out CompositeCameraOutput output,
                initialize: false);

            Assert.Throws<InvalidOperationException>(() =>
                manager.InitializeFor(testWorld.World.PlayerControllers[0]));

            Assert.IsNull(manager.ActiveOutput);
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(output.Owner);
            Assert.IsTrue(manager.HasOutputLeaseFault);

            CameraManager claimant = CreateManager(
                testWorld,
                "AcquireExceptionClaimant",
                firstResource,
                secondResource,
                out _);
            Assert.IsNotNull(claimant.ActiveOutput,
                "The lease committed before the exception must be rolled back.");
        }

        [Test]
        public void ArbiterReleaseExceptionAfterCommit_EntersFaultStateWithoutLeakingLease()
        {
            var arbiter = new FaultInjectingLeaseArbiter();
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                discoverActiveSceneActors: false,
                cameraOutputLeaseArbiter: arbiter);
            Object firstResource = CreateResource("ReleaseExceptionFirst");
            Object secondResource = CreateResource("ReleaseExceptionSecond");
            CameraManager manager = CreateManager(
                testWorld,
                "ReleaseExceptionManager",
                firstResource,
                secondResource,
                out CompositeCameraOutput output);
            arbiter.ThrowAfterRelease = true;

            Assert.DoesNotThrow(() => manager.SetCameraOutput(null));

            Assert.IsNull(manager.ActiveOutput);
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(output.Owner);
            Assert.IsTrue(manager.HasOutputLeaseFault);

            CameraManager claimant = CreateManager(
                testWorld,
                "ReleaseExceptionClaimant",
                firstResource,
                secondResource,
                out _);
            Assert.IsNotNull(claimant.ActiveOutput,
                "A release exception after commit must not retain the lease.");
        }

        [Test]
        public void RawOutput_DeactivateExceptionRetainsLeaseAndFailsClosed()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                discoverActiveSceneActors: false);
            Object sharedResource = CreateResource("ThrowingDeactivateShared");
            Object secondResource = CreateResource("ThrowingDeactivateSecond");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("ThrowingDeactivateManagerPrefab"));
            var output = new FaultingRawCameraOutput(
                sharedResource,
                RawOutputFailure.None);
            manager.SetCameraOutput(output, rebindImmediately: false);
            manager.InitializeFor(testWorld.World.PlayerControllers[0]);
            output.ThrowDuringDeactivate = true;

            ILogWriter previousWriter = LogRuntime.Writer;
            Assert.IsTrue(LogRuntime.TryReplaceWriter(previousWriter, NullLogWriter.Instance));
            CameraManager claimant;
            try
            {
                Assert.DoesNotThrow(() => manager.SetCameraOutput(null));
                claimant = CreateManager(
                    testWorld,
                    "ThrowingDeactivateClaimant",
                    sharedResource,
                    secondResource,
                    out _);
            }
            finally
            {
                Assert.IsTrue(LogRuntime.TryReplaceWriter(NullLogWriter.Instance, previousWriter));
            }

            Assert.IsTrue(manager.HasOutputLeaseFault);
            Assert.IsNull(manager.ActiveOutput);
            Assert.IsTrue(output.IsActive);
            Assert.IsNull(claimant.ActiveOutput,
                "A backend that failed to deactivate must retain exclusive ownership until World cleanup.");

            output.ThrowDuringDeactivate = false;
            Assert.DoesNotThrow(testWorld.Instance.Dispose);
            Assert.AreEqual(2, output.DeactivateCount);
            Assert.IsFalse(output.IsActive);
        }

        [Test]
        public void ActivationCleanupOutOfMemory_QuarantinesLeaseAndBlocksDifferentRebind()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                discoverActiveSceneActors: false);
            Object quarantinedResource = CreateResource("ActivationCleanupOutOfMemory");
            Object claimantSpare = CreateResource("ActivationCleanupOutOfMemorySpare");
            Object replacementResource = CreateResource("ActivationCleanupReplacement");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("ActivationCleanupOutOfMemoryManagerPrefab"));
            var output = new FaultingRawCameraOutput(
                quarantinedResource,
                RawOutputFailure.None)
            {
                RejectActivationAfterMutation = true,
                ThrowOutOfMemoryDuringDeactivate = true,
            };
            manager.SetCameraOutput(output, rebindImmediately: false);

            ILogWriter previousWriter = LogRuntime.Writer;
            Assert.IsTrue(LogRuntime.TryReplaceWriter(previousWriter, NullLogWriter.Instance));
            CameraManager claimant;
            try
            {
                Assert.Throws<OutOfMemoryException>(() =>
                    manager.InitializeFor(testWorld.World.PlayerControllers[0]));

                var replacement = new FaultingRawCameraOutput(
                    replacementResource,
                    RawOutputFailure.None);
                manager.SetCameraOutput(replacement);
                Assert.IsFalse(manager.TryResolveAndBindOutput());
                Assert.Zero(replacement.DiscoveryCount,
                    "A quarantined manager must reject a different resource domain before discovery.");

                claimant = CreateManager(
                    testWorld,
                    "ActivationCleanupOutOfMemoryClaimant",
                    quarantinedResource,
                    claimantSpare,
                    out _);
            }
            finally
            {
                output.ThrowOutOfMemoryDuringDeactivate = false;
                Assert.IsTrue(LogRuntime.TryReplaceWriter(NullLogWriter.Instance, previousWriter));
            }

            Assert.IsTrue(manager.HasOutputLeaseFault);
            Assert.IsNull(manager.ActiveOutput);
            Assert.IsTrue(output.IsActive,
                "The partially activated backend remains quarantined until deactivation succeeds.");
            Assert.IsNull(claimant.ActiveOutput,
                "A catastrophic cleanup failure must retain the original lease fail-closed.");
        }

        [Test]
        public void TryReleaseAll_IsolatesDeactivationFailureAndContinuesWithLaterLeases()
        {
            var arbiter = new CameraOutputLeaseArbiter();
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                discoverActiveSceneActors: false,
                cameraOutputLeaseArbiter: arbiter);
            Object failedResource = CreateResource("ReleaseAllFailed");
            Object successfulFirst = CreateResource("ReleaseAllSuccessfulFirst");
            Object successfulSecond = CreateResource("ReleaseAllSuccessfulSecond");
            Object failedClaimantSpare = CreateResource("ReleaseAllFailedClaimantSpare");

            CameraManager failedManager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("ReleaseAllFailedManagerPrefab"));
            var failedOutput = new FaultingRawCameraOutput(
                failedResource,
                RawOutputFailure.None);
            failedManager.SetCameraOutput(failedOutput, rebindImmediately: false);
            failedManager.InitializeFor(testWorld.World.PlayerControllers[0]);
            failedOutput.ThrowDuringDeactivate = true;

            CameraManager successfulManager = CreateManager(
                testWorld,
                "ReleaseAllSuccessfulManager",
                successfulFirst,
                successfulSecond,
                out CompositeCameraOutput successfulOutput);

            ILogWriter previousWriter = LogRuntime.Writer;
            Assert.IsTrue(LogRuntime.TryReplaceWriter(previousWriter, NullLogWriter.Instance));
            bool allReleased;
            try
            {
                allReleased = TryReleaseAll(arbiter, testWorld.World);
            }
            finally
            {
                Assert.IsTrue(LogRuntime.TryReplaceWriter(NullLogWriter.Instance, previousWriter));
            }

            Assert.IsFalse(allReleased);
            Assert.AreEqual(1, failedOutput.DeactivateCount);
            Assert.IsTrue(failedOutput.IsActive);
            Assert.IsFalse(successfulOutput.IsActive,
                "A later independent lease must still be deactivated in the same terminal pass.");

            CameraManager failedClaimant = CreateManager(
                testWorld,
                "ReleaseAllFailedClaimant",
                failedResource,
                failedClaimantSpare,
                out _);
            CameraManager successfulClaimant = CreateManager(
                testWorld,
                "ReleaseAllSuccessfulClaimant",
                successfulFirst,
                successfulSecond,
                out _);

            Assert.IsNull(failedClaimant.ActiveOutput,
                "The failed lease must remain quarantined after the terminal pass.");
            Assert.IsNotNull(successfulClaimant.ActiveOutput,
                "A later successfully deactivated lease must be available to another manager.");

            failedOutput.ThrowDuringDeactivate = false;
            Assert.IsTrue(TryReleaseAll(arbiter, testWorld.World));
        }

        [Test]
        public void WorldShutdown_AttemptsEachOutputLeaseOncePerTerminalPass()
        {
            var arbiter = new CameraOutputLeaseArbiter();
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                discoverActiveSceneActors: false,
                cameraOutputLeaseArbiter: arbiter);
            Object resource = CreateResource("ManagerAttemptResource");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("ManagerAttemptPrefab"));
            var output = new FaultingRawCameraOutput(resource, RawOutputFailure.None);
            manager.SetCameraOutput(output, rebindImmediately: false);
            manager.InitializeFor(testWorld.World.PlayerControllers[0]);
            output.ThrowDuringDeactivate = true;

            ILogWriter previousWriter = LogRuntime.Writer;
            Assert.IsTrue(LogRuntime.TryReplaceWriter(previousWriter, NullLogWriter.Instance));
            try
            {
                Assert.Throws<WorldShutdownIncompleteException>(testWorld.Instance.Dispose);
            }
            finally
            {
                Assert.IsTrue(LogRuntime.TryReplaceWriter(NullLogWriter.Instance, previousWriter));
            }

            Assert.AreEqual(1, output.DeactivateCount,
                "One terminal pass must invoke a backend cleanup callback at most once per lease.");
            Assert.AreEqual(WorldLifecycleState.Stopping, testWorld.World.LifecycleState);
            Assert.AreSame(testWorld.World, testWorld.Instance.CurrentWorld);

            output.ThrowDuringDeactivate = false;
            Assert.DoesNotThrow(testWorld.Instance.Dispose);
            Assert.AreEqual(2, output.DeactivateCount,
                "A later explicit terminal pass must be allowed to retry the retained lease.");
            Assert.IsNull(testWorld.Instance.CurrentWorld);
        }

        [Test]
        public void TryReleaseAll_PropagatesOutOfMemoryWithoutReleasingLease()
        {
            var arbiter = new CameraOutputLeaseArbiter();
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                discoverActiveSceneActors: false,
                cameraOutputLeaseArbiter: arbiter);
            Object resource = CreateResource("ReleaseAllOutOfMemory");
            Object claimantSpare = CreateResource("ReleaseAllOutOfMemorySpare");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("ReleaseAllOutOfMemoryManagerPrefab"));
            var output = new FaultingRawCameraOutput(resource, RawOutputFailure.None);
            manager.SetCameraOutput(output, rebindImmediately: false);
            manager.InitializeFor(testWorld.World.PlayerControllers[0]);
            output.ThrowOutOfMemoryDuringDeactivate = true;

            Assert.Throws<OutOfMemoryException>(() => TryReleaseAll(arbiter, testWorld.World));

            CameraManager claimant = CreateManager(
                testWorld,
                "ReleaseAllOutOfMemoryClaimant",
                resource,
                claimantSpare,
                out _);
            Assert.IsNull(claimant.ActiveOutput,
                "An interrupted terminal pass must not expose the unclean backend resource.");

            output.ThrowOutOfMemoryDuringDeactivate = false;
            Assert.IsTrue(TryReleaseAll(arbiter, testWorld.World));
        }

        [Test]
        public void WorldShutdown_RetainsStoppingWorldUntilOutputCleanupCanRetry()
        {
            var arbiter = new CameraOutputLeaseArbiter();
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                discoverActiveSceneActors: false,
                cameraOutputLeaseArbiter: arbiter);
            Object resource = CreateResource("WorldShutdownOutOfMemory");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("WorldShutdownOutOfMemoryManagerPrefab"));
            var output = new FaultingRawCameraOutput(resource, RawOutputFailure.None);
            manager.SetCameraOutput(output, rebindImmediately: false);
            manager.InitializeFor(testWorld.World.PlayerControllers[0]);
            output.ThrowOutOfMemoryDuringDeactivate = true;
            World world = testWorld.World;
            GameInstance instance = testWorld.Instance;
            var cancellationFailure = new OutOfMemoryException(
                "World cancellation out-of-memory failure requested by the test.");
            using CancellationTokenRegistration cancellationRegistration =
                world.LifetimeToken.Register(() => throw cancellationFailure);

            ILogWriter previousWriter = LogRuntime.Writer;
            Assert.IsTrue(LogRuntime.TryReplaceWriter(previousWriter, NullLogWriter.Instance));
            OutOfMemoryException propagated;
            try
            {
                propagated = Assert.Throws<OutOfMemoryException>(instance.Dispose);
            }
            finally
            {
                Assert.IsTrue(LogRuntime.TryReplaceWriter(NullLogWriter.Instance, previousWriter));
            }

            Assert.AreEqual(WorldLifecycleState.Stopping, world.LifecycleState);
            Assert.IsTrue(instance.IsDisposed);
            Assert.AreSame(world, instance.CurrentWorld);
            Assert.AreSame(cancellationFailure, propagated,
                "Terminal cleanup must preserve the first observed out-of-memory failure.");
            Assert.AreEqual(1, output.DeactivateCount,
                "Camera cleanup must still run after a cancellation observer reports out-of-memory.");

            output.ThrowOutOfMemoryDuringDeactivate = false;
            Assert.DoesNotThrow(instance.Dispose);
            Assert.AreEqual(2, output.DeactivateCount);
            Assert.AreEqual(WorldLifecycleState.Disposed, world.LifecycleState);
            Assert.IsNull(instance.CurrentWorld);
        }

        [Test]
        public void FaultingTryReleaseAll_RetainsWorldForTerminalRetry()
        {
            var arbiter = new FaultingTryReleaseAllArbiter();
            using GameplayTestWorld testWorld = GameplayTestWorld.Create(
                discoverActiveSceneActors: false,
                cameraOutputLeaseArbiter: arbiter);
            World world = testWorld.StartWorld();
            GameInstance instance = testWorld.Instance;
            WorldDefinition definition = (WorldDefinition)world.Definition;
            var lifetimeToken = world.LifetimeToken;

            Assert.Throws<WorldShutdownIncompleteException>(instance.Dispose);

            Assert.AreEqual(1, arbiter.TryReleaseAllCount);
            Assert.AreEqual(WorldLifecycleState.Stopping, world.LifecycleState);
            Assert.IsFalse(definition.IsDisposed,
                "Definition ownership must remain available until camera cleanup completes.");
            Assert.IsTrue(lifetimeToken.IsCancellationRequested);
            Assert.AreEqual(lifetimeToken, world.LifetimeToken,
                "The cancelled lifetime token remains owned until all earlier cleanup stages complete.");
            Assert.AreSame(world, instance.CurrentWorld);
            Assert.IsTrue(instance.IsDisposed);

            Assert.DoesNotThrow(instance.Dispose);
            Assert.AreEqual(2, arbiter.TryReleaseAllCount);
            Assert.IsTrue(definition.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() =>
            {
                _ = world.LifetimeToken;
            });
            Assert.AreEqual(WorldLifecycleState.Disposed, world.LifecycleState);
            Assert.IsNull(instance.CurrentWorld);
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
        public void ActivationRejection_LoggingOutOfMemoryRunsCleanupBeforePropagation()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            Object resource = CreateResource("ActivationRejectionLoggingOutOfMemory");
            Object claimantSpare = CreateResource("ActivationRejectionLoggingOutOfMemorySpare");
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>(
                    "ActivationRejectionLoggingOutOfMemoryManagerPrefab"));
            var output = new FaultingRawCameraOutput(resource, RawOutputFailure.None)
            {
                RejectActivationAfterMutation = true,
            };
            manager.SetCameraOutput(output, rebindImmediately: false);

            ILogWriter previousWriter = LogRuntime.Writer;
            var throwingWriter = new OutOfMemoryLogWriter();
            Assert.IsTrue(LogRuntime.TryReplaceWriter(previousWriter, throwingWriter));
            try
            {
                Assert.Throws<OutOfMemoryException>(() =>
                    manager.InitializeFor(testWorld.World.PlayerControllers[0]));
            }
            finally
            {
                Assert.IsTrue(LogRuntime.TryReplaceWriter(throwingWriter, previousWriter));
            }

            Assert.IsFalse(output.IsActive);
            Assert.IsNull(manager.ActiveOutput);
            Assert.IsFalse(manager.HasOutputLeaseFault);

            CameraManager claimant = CreateManager(
                testWorld,
                "ActivationRejectionLoggingOutOfMemoryClaimant",
                resource,
                claimantSpare,
                out _);
            Assert.IsNotNull(claimant.ActiveOutput,
                "Logging failure must not strand a lease after backend cleanup succeeded.");
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
        public void DeactivationException_RetainsCompositeLeaseAndFailsClosed()
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
            Assert.IsNull(claimant.ActiveOutput,
                "A backend with unknown deactivation state must quarantine its leased resources.");

            output.ThrowDuringDeactivation = false;
            Assert.DoesNotThrow(testWorld.Instance.Dispose);
            Assert.IsFalse(output.IsActive);
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
            UnityLifecycleTestUtility.InvokeAwake(output);
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

        private static bool TryReleaseAll(
            CameraOutputLeaseArbiter arbiter,
            World world)
        {
            CameraOutputTerminalReleasePass releasePass =
                arbiter.BeginTerminalReleasePass(world);
            return arbiter.TryReleaseAll(world, in releasePass);
        }

        private sealed class CompositeCameraOutput : CameraOutputBehaviour
        {
            private Object firstResource;
            private Object secondResource;

            public bool FailActivation { get; set; }
            public bool ThrowDuringActivation { get; set; }
            public bool ThrowDuringDeactivation { get; set; }

            protected override Object OnGetOutputObject() => firstResource;

            public void SetResources(Object first, Object second)
            {
                ThrowIfLifecycleBound();
                firstResource = first;
                secondResource = second;
            }

            protected override bool OnTryGetResourceSet(
                out CameraOutputResourceSet resources,
                out string error)
            {
                return CameraOutputResourceSet.TryCreate(
                    2,
                    firstResource,
                    secondResource,
                    null,
                    null,
                    out resources,
                    out error);
            }

            protected override bool OnActivate(
                CameraManager newOwner,
                in CameraOutputResourceSet resources,
                out string error)
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

            protected override Object OnGetOutputObject() => firstResource;

            public void SetResources(Object first, Object second)
            {
                ThrowIfLifecycleBound();
                firstResource = first;
                secondResource = second;
            }

            protected override bool OnTryGetResourceSet(
                out CameraOutputResourceSet resources,
                out string error)
            {
                return CameraOutputResourceSet.TryCreate(
                    2,
                    firstResource,
                    secondResource,
                    null,
                    null,
                    out resources,
                    out error);
            }

            protected override bool OnActivate(
                CameraManager newOwner,
                in CameraOutputResourceSet resources,
                out string error)
            {
                newOwner.World.GameInstance.Dispose();
                error = null;
                return true;
            }

            protected override void OnApplyPose(in CameraPose pose)
            {
            }
        }

        private sealed class DestroyAwareCameraOutput : CameraOutputBehaviour
        {
            private Object firstResource;
            private Object secondResource;

            public bool DestroyHookInvoked { get; private set; }
            protected override Object OnGetOutputObject() => firstResource;

            public void SetResources(Object first, Object second)
            {
                ThrowIfLifecycleBound();
                firstResource = first;
                secondResource = second;
            }

            protected override bool OnTryGetResourceSet(
                out CameraOutputResourceSet resources,
                out string error)
            {
                return CameraOutputResourceSet.TryCreate(
                    2,
                    firstResource,
                    secondResource,
                    null,
                    null,
                    out resources,
                    out error);
            }

            protected override void OnApplyPose(in CameraPose pose)
            {
            }

            protected override void OnDestroy()
            {
                DestroyHookInvoked = true;
                base.OnDestroy();
            }

            public void InvokeOnDestroyForTest()
            {
                OnDestroy();
            }
        }

        public enum RawOutputFailure : byte
        {
            None,
            DiscoveryException,
            InvalidResourceSet,
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
            public CameraManager Owner { get; private set; }
            public Object OutputObject => resource;
            public int DiscoveryCount { get; private set; }
            public int DeactivateCount { get; private set; }
            public RawOutputFailure Failure { get; set; }
            public bool RejectActivationAfterMutation { get; set; }
            public bool ThrowDuringDeactivate { get; set; }
            public bool ThrowOutOfMemoryDuringDeactivate { get; set; }

            public bool TryGetResourceSet(
                out CameraOutputResourceSet resources,
                out string error)
            {
                DiscoveryCount++;
                if (Failure == RawOutputFailure.DiscoveryException)
                {
                    throw new InvalidOperationException(
                        "Resource discovery failure requested by the test.");
                }

                if (Failure == RawOutputFailure.InvalidResourceSet)
                {
                    resources = default;
                    error = null;
                    return true;
                }

                resources = new CameraOutputResourceSet(resource);
                error = null;
                return true;
            }

            public bool TryActivate(
                CameraManager owner,
                in CameraOutputResourceSet resources,
                out string error)
            {
                Owner = owner;
                IsActive = true;
                if (RejectActivationAfterMutation)
                {
                    error = "Activation rejection requested after backend mutation by the test.";
                    return false;
                }

                error = null;
                return true;
            }

            public void ApplyPose(in CameraPose pose)
            {
            }

            public void Deactivate(CameraManager owner)
            {
                DeactivateCount++;
                if (ThrowOutOfMemoryDuringDeactivate)
                {
                    throw new OutOfMemoryException(
                        "Out-of-memory failure requested by the test.");
                }
                if (ThrowDuringDeactivate)
                {
                    throw new InvalidOperationException(
                        "Deactivation failure requested by the test.");
                }

                IsActive = false;
                Owner = null;
            }
        }

        private sealed class FaultingTryReleaseAllArbiter : ICameraOutputLeaseArbiter
        {
            public int TryReleaseAllCount { get; private set; }
            private long nextReleasePassSequence;

            public CameraOutputTerminalReleasePass BeginTerminalReleasePass(World world)
            {
                nextReleasePassSequence++;
                return new CameraOutputTerminalReleasePass(this, nextReleasePassSequence);
            }

            public bool TryAcquire(
                World world,
                CameraManager owner,
                ICameraOutput output,
                in CameraOutputResourceSet resources,
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

            public bool TryBeginTerminalReleaseAttempt(
                World world,
                CameraManager owner,
                ICameraOutput output,
                in CameraOutputLease lease,
                in CameraOutputTerminalReleasePass releasePass)
            {
                return false;
            }

            public bool TryReleaseAll(
                World world,
                in CameraOutputTerminalReleasePass releasePass)
            {
                TryReleaseAllCount++;
                if (TryReleaseAllCount == 1)
                {
                    throw new InvalidOperationException(
                        "TryReleaseAll failure requested by the test.");
                }

                return true;
            }
        }

        private sealed class FaultInjectingLeaseArbiter : ICameraOutputLeaseArbiter
        {
            private readonly CameraOutputLeaseArbiter inner =
                new CameraOutputLeaseArbiter();

            public bool ThrowAfterAcquire { get; set; }
            public bool ThrowAfterRelease { get; set; }

            public CameraOutputTerminalReleasePass BeginTerminalReleasePass(World world)
            {
                return inner.BeginTerminalReleasePass(world);
            }

            public bool TryAcquire(
                World world,
                CameraManager owner,
                ICameraOutput output,
                in CameraOutputResourceSet resources,
                out CameraOutputLease lease,
                out string error)
            {
                bool acquired = inner.TryAcquire(
                    world,
                    owner,
                    output,
                    in resources,
                    out lease,
                    out error);
                if (acquired && ThrowAfterAcquire)
                {
                    ThrowAfterAcquire = false;
                    throw new InvalidOperationException(
                        "Acquire failure requested after lease commit by the test.");
                }

                return acquired;
            }

            public void Release(
                World world,
                CameraManager owner,
                ICameraOutput output,
                in CameraOutputLease lease)
            {
                inner.Release(world, owner, output, in lease);
                if (ThrowAfterRelease)
                {
                    ThrowAfterRelease = false;
                    throw new InvalidOperationException(
                        "Release failure requested after lease commit by the test.");
                }
            }

            public bool TryBeginTerminalReleaseAttempt(
                World world,
                CameraManager owner,
                ICameraOutput output,
                in CameraOutputLease lease,
                in CameraOutputTerminalReleasePass releasePass)
            {
                return inner.TryBeginTerminalReleaseAttempt(
                    world,
                    owner,
                    output,
                    in lease,
                    in releasePass);
            }

            public bool TryReleaseAll(
                World world,
                in CameraOutputTerminalReleasePass releasePass)
            {
                return inner.TryReleaseAll(world, in releasePass);
            }
        }

        private sealed class OutOfMemoryLogWriter : ILogWriter
        {
            private readonly OutOfMemoryException failure = new OutOfMemoryException(
                "Logging out-of-memory failure requested by the test.");

            public bool IsEnabled(LogSeverity severity, string category) => throw failure;

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => throw failure;

            public void Write(
                LogSeverity severity,
                string category,
                Action<StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => throw failure;

            public void Write<TState>(
                LogSeverity severity,
                string category,
                TState state,
                Action<TState, StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => throw failure;

            public void WriteException(
                LogSeverity severity,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => throw failure;
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

            public bool TryGetResourceSet(
                out CameraOutputResourceSet resources,
                out string error)
            {
                resources = new CameraOutputResourceSet(
                    firstResource,
                    useReplacement ? replacementResource : leasedSecondResource);
                error = null;
                return true;
            }

            public bool TryActivate(
                CameraManager owner,
                in CameraOutputResourceSet resources,
                out string error)
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
