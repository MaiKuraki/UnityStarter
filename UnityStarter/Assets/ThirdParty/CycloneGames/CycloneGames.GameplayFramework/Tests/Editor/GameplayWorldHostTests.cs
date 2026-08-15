using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.GameplayFramework.Runtime.Editor;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class GameplayWorldHostTests
    {
        [Test]
        public void Host_LiveAccessAndMutationRejectWorkerThreadBeforeUnityAccess()
        {
            var hostObject = new GameObject("GameplayWorldHost");
            GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
            var composition = new GameplayWorldComposition(
                new UnityActorLifetime(),
                new GameplayWorldTerminalCleanupRegistry(capacity: 2));
            Exception getterFailure = null;
            Exception configureFailure = null;
            Exception startFailure = null;
            var worker = new Thread(() =>
            {
                try
                {
                    _ = host.GameInstance;
                }
                catch (Exception exception)
                {
                    getterFailure = exception;
                }

                try
                {
                    host.Configure(composition);
                }
                catch (Exception exception)
                {
                    configureFailure = exception;
                }

                try
                {
                    host.StartWorldAsync().GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    startFailure = exception;
                }
            });

            try
            {
                worker.Start();
                worker.Join();

                Assert.IsInstanceOf<InvalidOperationException>(getterFailure);
                Assert.IsInstanceOf<InvalidOperationException>(configureFailure);
                Assert.IsInstanceOf<InvalidOperationException>(startFailure);
            }
            finally
            {
                Object.DestroyImmediate(hostObject);
            }
        }

        [Test]
        public void TerminalCleanupOwner_CannotBindFromWorkerBeforeUnityLifecycle()
        {
            var ownerObject = new GameObject("TerminalCleanupOwner");
            ownerObject.SetActive(false);
            GameplayWorldTerminalCleanupOwner owner =
                ownerObject.AddComponent<GameplayWorldTerminalCleanupOwner>();
            Exception workerFailure = null;
            var worker = new Thread(() =>
            {
                try
                {
                    _ = owner.PendingCount;
                }
                catch (Exception exception)
                {
                    workerFailure = exception;
                }
            });

            try
            {
                worker.Start();
                worker.Join();
                Assert.IsInstanceOf<InvalidOperationException>(workerFailure);

                ownerObject.SetActive(true);
                UnityLifecycleTestUtility.InvokeAwake(owner);
                UnityLifecycleTestUtility.InvokeOnEnable(owner);
                Assert.Zero(owner.PendingCount);
            }
            finally
            {
                Object.DestroyImmediate(ownerObject);
            }
        }

        [Test]
        public void Host_MissingTerminalCleanupOwnerFailsBeforeGameInstanceCreation()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            var hostObject = new GameObject("GameplayWorldHost");
            GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
            UnityLifecycleTestUtility.InvokeAwake(host);
            var serializedHost = new SerializedObject(host);
            serializedHost.FindProperty("worldSettings").objectReferenceValue =
                testWorld.Settings;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();

            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    host.StartWorldAsync().GetAwaiter().GetResult());
                Assert.IsNull(host.GameInstance);
                Assert.AreEqual(GameplayWorldHostState.Idle, host.State);
            }
            finally
            {
                Object.DestroyImmediate(hostObject);
            }
        }

        [Test]
        public void Host_StartAndStop_OwnsOneGameInstanceAndWorld()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            var hostObject = new GameObject("GameplayWorldHost");
            GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
            AssignWorldSettings(host, testWorld.Settings);

            try
            {
                World world = host.StartWorldAsync().GetAwaiter().GetResult();

                Assert.AreEqual(GameplayWorldHostState.Running, host.State);
                Assert.AreSame(world, host.CurrentWorld);
                Assert.IsNotNull(host.GameInstance);
                Assert.Greater(world.OwnedActorCount, 0);

                world.Dispose();
                Assert.AreEqual(GameplayWorldHostState.Stopped, host.State);
                Assert.IsNull(host.CurrentWorld);

                World replacementWorld = host.StartWorldAsync().GetAwaiter().GetResult();
                Assert.AreNotSame(world, replacementWorld);
                Assert.AreEqual(GameplayWorldHostState.Running, host.State);

                host.StopWorldAsync().GetAwaiter().GetResult();

                Assert.AreEqual(GameplayWorldHostState.Stopped, host.State);
                Assert.IsNull(host.CurrentWorld);
                Assert.IsNull(host.GameInstance);
            }
            finally
            {
                Object.DestroyImmediate(hostObject);
            }
        }

        [Test]
        public void Host_IncompleteStopRemainsRegisteredAndCanRetry()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            var hostObject = new GameObject("GameplayWorldHost");
            GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
            AssignWorldSettings(host, testWorld.Settings);
            var cleanupOwner = (GameplayWorldTerminalCleanupRegistry)host.TerminalCleanupOwner;
            var cameraArbiter = new FaultOnceCameraOutputLeaseArbiter();
            host.Configure(new GameplayWorldComposition(
                new UnityActorLifetime(),
                cleanupOwner,
                cameraOutputLeaseArbiter: cameraArbiter));

            try
            {
                host.StartWorldAsync().GetAwaiter().GetResult();
                GameInstance retainedInstance = host.GameInstance;

                Assert.Throws<WorldShutdownIncompleteException>(() =>
                    host.StopWorldAsync().GetAwaiter().GetResult());
                Assert.AreEqual(GameplayWorldHostState.Faulted, host.State);
                Assert.AreSame(retainedInstance, host.GameInstance);
                Assert.AreEqual(1, cleanupOwner.PendingCount);

                host.StopWorldAsync().GetAwaiter().GetResult();

                Assert.AreEqual(GameplayWorldHostState.Stopped, host.State);
                Assert.IsNull(host.GameInstance);
                Assert.Zero(cleanupOwner.PendingCount);
            }
            finally
            {
                Object.DestroyImmediate(hostObject);
            }
        }

        [Test]
        public void HostDestruction_TransfersIncompleteGameInstanceToApplicationOwner()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            var hostObject = new GameObject("GameplayWorldHost");
            GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
            AssignWorldSettings(host, testWorld.Settings);
            var cleanupOwner = (GameplayWorldTerminalCleanupRegistry)host.TerminalCleanupOwner;
            var cameraArbiter = new FaultOnceCameraOutputLeaseArbiter();
            host.Configure(new GameplayWorldComposition(
                new UnityActorLifetime(),
                cleanupOwner,
                cameraOutputLeaseArbiter: cameraArbiter));
            host.StartWorldAsync().GetAwaiter().GetResult();

            Object.DestroyImmediate(hostObject);

            Assert.AreEqual(1, cleanupOwner.PendingCount);
            bool cleanupComplete = cleanupOwner.TryCleanupAll();
            if (!cleanupComplete)
            {
                Assert.AreEqual(1, cleanupOwner.PendingCount,
                    "An incomplete application cleanup pass must retain the sole retry owner.");
                cleanupComplete = cleanupOwner.TryCleanupAll();
            }

            Assert.IsTrue(cleanupComplete,
                "The application owner must complete retained cleanup through explicit retry passes.");
            Assert.Zero(cleanupOwner.PendingCount);
        }

        [Test]
        public void Host_ExplicitComposition_AppliesImmutableWorldRuntimeLimits()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            var hostObject = new GameObject("GameplayWorldHost");
            GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
            AssignWorldSettings(host, testWorld.Settings);
            var limits = new WorldRuntimeLimits(
                maximumActorCount: 32,
                initialActorCapacity: 8,
                initialUpdateTickCapacity: 4,
                initialFixedUpdateTickCapacity: 2,
                initialLateUpdateTickCapacity: 2);
            host.Configure(new GameplayWorldComposition(
                new UnityActorLifetime(),
                host.TerminalCleanupOwner,
                runtimeLimits: limits));

            try
            {
                World world = host.StartWorldAsync().GetAwaiter().GetResult();

                Assert.IsTrue(host.HasExplicitComposition);
                Assert.AreSame(limits, host.Composition.RuntimeLimits);
                Assert.AreSame(limits, host.GameInstance.RuntimeLimits);
                Assert.AreSame(limits, world.RuntimeLimits);
                ActorAdmissionSnapshot snapshot = world.GetActorAdmissionSnapshot();
                Assert.AreEqual(32, snapshot.MaximumActorCount);
                Assert.GreaterOrEqual(snapshot.AllocatedActorCapacity, snapshot.ActorCount);
                Assert.Throws<System.InvalidOperationException>(() =>
                    host.Configure(GameplayWorldComposition.CreateDefault(
                        host.TerminalCleanupOwner)));
            }
            finally
            {
                if (host.IsRunning)
                {
                    host.StopWorldAsync().GetAwaiter().GetResult();
                }

                Object.DestroyImmediate(hostObject);
            }
        }

        [UnityTest]
        public IEnumerator Host_PreCanceledStart_RollsBackToStoppedAndCanRetry()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using GameplayTestWorld testWorld = GameplayTestWorld.Create();
                var hostObject = new GameObject("GameplayWorldHost");
                GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
                AssignWorldSettings(host, testWorld.Settings);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();

                try
                {
                    Exception cancellationFailure = await CaptureStartFailure(
                        host.StartWorldAsync(cancellation.Token));

                    Assert.IsInstanceOf<OperationCanceledException>(cancellationFailure);
                    Assert.AreEqual(GameplayWorldHostState.Stopped, host.State);
                    Assert.IsNull(host.GameInstance);
                    Assert.IsNull(host.CurrentWorld);

                    World retryWorld = await host.StartWorldAsync();
                    Assert.AreEqual(GameplayWorldHostState.Running, host.State);
                    Assert.AreSame(retryWorld, host.CurrentWorld);
                    await host.StopWorldAsync();
                }
                finally
                {
                    Object.DestroyImmediate(hostObject);
                }
            });
        }

        [UnityTest]
        public IEnumerator Host_StopDuringPendingStart_CancelsAndCompletesBothTransactions()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using GameplayTestWorld testWorld = GameplayTestWorld.Create();
                var resolver = new ControlledResolver(testWorld.Settings.GameModeClass)
                {
                    Behavior = ResolverBehavior.Pending,
                };
                ConfigureExternalGameMode(testWorld.Settings);
                var hostObject = new GameObject("GameplayWorldHost");
                GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
                AssignWorldSettings(host, testWorld.Settings);
                host.Configure(new GameplayWorldComposition(
                    new UnityActorLifetime(),
                    host.TerminalCleanupOwner,
                    referenceResolver: resolver));

                try
                {
                    UniTask<World> startTask = host.StartWorldAsync();
                    Assert.IsTrue(resolver.ResolveEntered);
                    Assert.AreEqual(GameplayWorldHostState.Starting, host.State);

                    UniTask stopTask = host.StopWorldAsync();
                    await stopTask;
                    Exception startFailure = await CaptureStartFailure(startTask);

                    Assert.IsInstanceOf<OperationCanceledException>(startFailure);
                    Assert.AreEqual(GameplayWorldHostState.Stopped, host.State);
                    Assert.IsNull(host.GameInstance);
                    Assert.IsNull(host.CurrentWorld);
                }
                finally
                {
                    Object.DestroyImmediate(hostObject);
                }
            });
        }

        [UnityTest]
        public IEnumerator Host_FaultedStart_CanRetryWithoutLeakingPreviousGameInstance()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using GameplayTestWorld testWorld = GameplayTestWorld.Create();
                var resolver = new ControlledResolver(testWorld.Settings.GameModeClass)
                {
                    Behavior = ResolverBehavior.Fault,
                };
                ConfigureExternalGameMode(testWorld.Settings);
                var hostObject = new GameObject("GameplayWorldHost");
                GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
                AssignWorldSettings(host, testWorld.Settings);
                host.Configure(new GameplayWorldComposition(
                    new UnityActorLifetime(),
                    host.TerminalCleanupOwner,
                    referenceResolver: resolver));

                try
                {
                    Exception firstFailure = await CaptureStartFailure(host.StartWorldAsync());
                    Assert.IsInstanceOf<InvalidOperationException>(firstFailure);
                    Assert.AreEqual(GameplayWorldHostState.Faulted, host.State);
                    Assert.IsNull(host.GameInstance);
                    Assert.IsNotEmpty(host.LastError);

                    resolver.Behavior = ResolverBehavior.Succeed;
                    World world = await host.StartWorldAsync();

                    Assert.AreEqual(2, resolver.ResolveCount);
                    Assert.AreEqual(GameplayWorldHostState.Running, host.State);
                    Assert.AreSame(world, host.CurrentWorld);
                    Assert.IsNull(host.LastError);
                    await host.StopWorldAsync();
                }
                finally
                {
                    Object.DestroyImmediate(hostObject);
                }
            });
        }

        [UnityTest]
        public IEnumerator Host_StartFailureWithIncompleteCleanupLeavesFaultedAndRetryable()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using GameplayTestWorld testWorld = GameplayTestWorld.Create();
                var hostObject = new GameObject("GameplayWorldHost");
                GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
                AssignWorldSettings(host, testWorld.Settings);
                var cleanupOwner =
                    (GameplayWorldTerminalCleanupRegistry)host.TerminalCleanupOwner;
                var cameraArbiter = new FaultOnceCameraOutputLeaseArbiter(
                    failedTerminalPassCount: 2);
                host.Configure(new GameplayWorldComposition(
                    new RejectingActorLifetime(),
                    cleanupOwner,
                    cameraOutputLeaseArbiter: cameraArbiter));

                try
                {
                    Exception failure = await CaptureStartFailure(host.StartWorldAsync());

                    Assert.IsInstanceOf<WorldShutdownIncompleteException>(failure);
                    Assert.AreEqual(GameplayWorldHostState.Faulted, host.State);
                    Assert.IsNotNull(host.GameInstance);
                    Assert.AreEqual(1, cleanupOwner.PendingCount);

                    await host.StopWorldAsync();

                    Assert.AreEqual(GameplayWorldHostState.Stopped, host.State);
                    Assert.IsNull(host.GameInstance);
                    Assert.Zero(cleanupOwner.PendingCount);
                }
                finally
                {
                    Object.DestroyImmediate(hostObject);
                }
            });
        }

        [UnityTest]
        public IEnumerator Host_AutomaticStartFailureRetainsLeaseOwnerForRegistryRetry()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using GameplayTestWorld testWorld = GameplayTestWorld.Create();
                ConfigureExternalGameMode(testWorld.Settings);
                var lease = new FaultingLease(failedDisposeCount: 2);
                var resolver = new ControlledResolver(testWorld.Settings.GameModeClass)
                {
                    Behavior = ResolverBehavior.Fault,
                    Lease = lease,
                };
                var cleanupOwner = new GameplayWorldTerminalCleanupRegistry(capacity: 2);
                var hostObject = new GameObject("GameplayWorldHost");
                GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
                AssignWorldSettings(host, testWorld.Settings);
                host.Configure(new GameplayWorldComposition(
                    new UnityActorLifetime(),
                    cleanupOwner,
                    referenceResolver: resolver));

                try
                {
                    typeof(GameplayWorldHost)
                        .GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(host, null);
                    await UniTask.WaitUntil(
                        () => host.State != GameplayWorldHostState.Starting);

                    Assert.AreEqual(GameplayWorldHostState.Faulted, host.State);
                    Assert.AreEqual(1, cleanupOwner.PendingCount);
                    Assert.AreEqual(2, lease.DisposeCount);

                    Assert.IsTrue(cleanupOwner.TryCleanupAll());
                    Assert.Zero(cleanupOwner.PendingCount);
                    Assert.AreEqual(3, lease.DisposeCount);

                    await host.StopWorldAsync();
                    Assert.AreEqual(GameplayWorldHostState.Stopped, host.State);
                    Assert.IsNull(host.GameInstance);
                }
                finally
                {
                    Object.DestroyImmediate(hostObject);
                }
            });
        }

        [UnityTest]
        public IEnumerator Host_ReentrantStartWhilePending_FailsWithoutReplacingActiveTransaction()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using GameplayTestWorld testWorld = GameplayTestWorld.Create();
                var resolver = new ControlledResolver(testWorld.Settings.GameModeClass)
                {
                    Behavior = ResolverBehavior.Pending,
                };
                ConfigureExternalGameMode(testWorld.Settings);
                var hostObject = new GameObject("GameplayWorldHost");
                GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
                AssignWorldSettings(host, testWorld.Settings);
                host.Configure(new GameplayWorldComposition(
                    new UnityActorLifetime(),
                    host.TerminalCleanupOwner,
                    referenceResolver: resolver));

                try
                {
                    UniTask<World> firstStart = host.StartWorldAsync();
                    GameInstance firstInstance = host.GameInstance;
                    Exception reentrantFailure = await CaptureStartFailure(host.StartWorldAsync());

                    Assert.IsInstanceOf<InvalidOperationException>(reentrantFailure);
                    Assert.AreSame(firstInstance, host.GameInstance);
                    Assert.AreEqual(GameplayWorldHostState.Starting, host.State);
                    Assert.AreEqual(1, resolver.ResolveCount);

                    await host.StopWorldAsync();
                    Exception firstFailure = await CaptureStartFailure(firstStart);
                    Assert.IsInstanceOf<OperationCanceledException>(firstFailure);
                    Assert.AreEqual(GameplayWorldHostState.Stopped, host.State);
                }
                finally
                {
                    Object.DestroyImmediate(hostObject);
                }
            });
        }

        [Test]
        public void WorldRuntimeLimits_CapInitialCapacityHintsToAdmissionLimit()
        {
            var limits = new WorldRuntimeLimits(maximumActorCount: 4);

            Assert.AreEqual(4, limits.InitialActorCapacity);
            Assert.AreEqual(4, limits.InitialUpdateTickCapacity);
            Assert.AreEqual(4, limits.InitialFixedUpdateTickCapacity);
            Assert.AreEqual(4, limits.InitialLateUpdateTickCapacity);
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new WorldRuntimeLimits(initialActorCapacity: -1));
        }

        [Test]
        public void WorldActorRegistration_IsAllocationFreeIndexedReadModel()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            World world = testWorld.World;
            int ownedCount = 0;

            for (int i = 0; i < world.ActorCount; i++)
            {
                Assert.IsTrue(world.TryGetActorRegistration(i, out WorldActorRegistration registration));
                Assert.IsNotNull(registration.Actor);
                if (registration.IsWorldOwned)
                {
                    ownedCount++;
                }
            }

            Assert.AreEqual(world.OwnedActorCount, ownedCount);
            Assert.IsFalse(world.TryGetActorRegistration(-1, out _));
            Assert.IsFalse(world.TryGetActorRegistration(world.ActorCount, out _));
        }

        [Test]
        public void EditorTools_ExposeHostInspectorAndConfigurationErrors()
        {
            var hostObject = new GameObject("GameplayWorldHost");
            GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
            WorldSettings settings = ScriptableObject.CreateInstance<WorldSettings>();
            UnityEditor.Editor hostEditor = null;
            try
            {
                hostEditor = UnityEditor.Editor.CreateEditor(host);
                Assert.IsInstanceOf<GameplayWorldHostEditor>(hostEditor);

                var issues = new List<GameplayFrameworkValidationIssue>();
                GameplayFrameworkProjectValidator.ValidateWorldSettings(settings, issues);

                Assert.AreEqual(4, issues.Count);
                Assert.IsTrue(issues.TrueForAll(
                    issue => issue.Severity == GameplayFrameworkValidationSeverity.Error));
            }
            finally
            {
                Object.DestroyImmediate(hostEditor);
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(hostObject);
            }
        }

        [Test]
        public void ProjectValidator_DifferentScenesOnlyReportMissingAuthoringOwners()
        {
            Scene firstScene = default;
            Scene secondScene = default;
            WorldSettings settings = null;
            GameObject firstObject = null;
            GameObject secondObject = null;

            try
            {
                firstScene = EditorSceneManager.NewPreviewScene();
                secondScene = EditorSceneManager.NewPreviewScene();
                settings = ScriptableObject.CreateInstance<WorldSettings>();
                firstObject = new GameObject("FirstHost");
                secondObject = new GameObject("SecondHost");
                SceneManager.MoveGameObjectToScene(firstObject, firstScene);
                SceneManager.MoveGameObjectToScene(secondObject, secondScene);
                GameplayWorldHost firstHost = firstObject.AddComponent<GameplayWorldHost>();
                GameplayWorldHost secondHost = secondObject.AddComponent<GameplayWorldHost>();
                AssignWorldSettings(firstHost, settings);
                AssignWorldSettings(secondHost, settings);
                var issues = new List<GameplayFrameworkValidationIssue>();

                GameplayFrameworkProjectValidator.ValidateHosts(
                    new[] { firstHost, secondHost },
                    issues);

                Assert.AreEqual(2, issues.Count);
                Assert.IsTrue(issues.TrueForAll(
                    issue => issue.Severity == GameplayFrameworkValidationSeverity.Warning &&
                             issue.Message.Contains("terminal cleanup owner")));
            }
            finally
            {
                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
                Object.DestroyImmediate(settings);
                if (secondScene.IsValid())
                {
                    EditorSceneManager.ClosePreviewScene(secondScene);
                }

                if (firstScene.IsValid())
                {
                    EditorSceneManager.ClosePreviewScene(firstScene);
                }
            }
        }

        [Test]
        public void ProjectValidator_ReportsEveryAutoStartHostSharingOneScene()
        {
            Scene scene = default;
            WorldSettings settings = null;
            GameObject firstObject = null;
            GameObject secondObject = null;

            try
            {
                scene = EditorSceneManager.NewPreviewScene();
                settings = ScriptableObject.CreateInstance<WorldSettings>();
                firstObject = new GameObject("FirstHost");
                secondObject = new GameObject("SecondHost");
                SceneManager.MoveGameObjectToScene(firstObject, scene);
                SceneManager.MoveGameObjectToScene(secondObject, scene);
                GameplayWorldHost firstHost = firstObject.AddComponent<GameplayWorldHost>();
                GameplayWorldHost secondHost = secondObject.AddComponent<GameplayWorldHost>();
                AssignWorldSettings(firstHost, settings);
                AssignWorldSettings(secondHost, settings);
                var issues = new List<GameplayFrameworkValidationIssue>();

                GameplayFrameworkProjectValidator.ValidateHosts(
                    new[] { firstHost, secondHost },
                    issues);

                Assert.AreEqual(4, issues.Count);
                Assert.AreEqual(2, issues.FindAll(
                    issue => issue.Severity == GameplayFrameworkValidationSeverity.Error &&
                             issue.Message.Contains("same Scene")).Count);
                Assert.AreEqual(2, issues.FindAll(
                    issue => issue.Severity == GameplayFrameworkValidationSeverity.Warning &&
                             issue.Message.Contains("terminal cleanup owner")).Count);
            }
            finally
            {
                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
                Object.DestroyImmediate(settings);
                if (scene.IsValid())
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
        }

        [Test]
        public void WorldDebuggerPage_ClampsRequestedPageAndBoundsVisibleRows()
        {
            ActorRegistrationPage firstPage = ActorRegistrationPage.Create(
                totalCount: 100,
                requestedPageIndex: -4,
                pageSize: 32);
            ActorRegistrationPage lastPage = ActorRegistrationPage.Create(
                totalCount: 100,
                requestedPageIndex: 99,
                pageSize: 32);

            Assert.AreEqual(0, firstPage.PageIndex);
            Assert.AreEqual(0, firstPage.StartIndex);
            Assert.AreEqual(32, firstPage.EndIndexExclusive);
            Assert.AreEqual(4, firstPage.PageCount);
            Assert.AreEqual(3, lastPage.PageIndex);
            Assert.AreEqual(96, lastPage.StartIndex);
            Assert.AreEqual(100, lastPage.EndIndexExclusive);
        }

        [Test]
        public void CameraDebugSampling_UsesActualMonotonicElapsedTime()
        {
            Assert.AreEqual(
                0.75f,
                CameraDebugSampling.GetElapsedSeconds(10d, 10.75d),
                0.0001f);
            Assert.AreEqual(0f, CameraDebugSampling.GetElapsedSeconds(10d, 9d));
            Assert.AreEqual(0f, CameraDebugSampling.GetElapsedSeconds(double.NaN, 11d));
        }

        private static void AssignWorldSettings(GameplayWorldHost host, WorldSettings settings)
        {
            UnityLifecycleTestUtility.InvokeAwake(host);
            host.ConfigureTerminalCleanupOwner(
                new GameplayWorldTerminalCleanupRegistry(capacity: 4));
            var serializedHost = new SerializedObject(host);
            serializedHost.FindProperty("worldSettings").objectReferenceValue = settings;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureExternalGameMode(WorldSettings settings)
        {
            var serializedSettings = new SerializedObject(settings);
            serializedSettings.FindProperty("gameModeSource").enumValueIndex =
                (int)WorldSettingsReferenceSource.PathLocation;
            serializedSettings.FindProperty("gameModeAssetLocation").stringValue =
                "tests/game-mode";
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static async UniTask<Exception> CaptureStartFailure(UniTask<World> startTask)
        {
            try
            {
                await startTask;
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private enum ResolverBehavior : byte
        {
            Pending = 0,
            Fault = 1,
            Succeed = 2,
        }

        private sealed class FaultOnceCameraOutputLeaseArbiter :
            ICameraOutputLeaseArbiter
        {
            private readonly CameraOutputLeaseArbiter inner =
                new CameraOutputLeaseArbiter();
            private int remainingFailedTerminalPasses;

            public FaultOnceCameraOutputLeaseArbiter(int failedTerminalPassCount = 1)
            {
                remainingFailedTerminalPasses = failedTerminalPassCount;
            }

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
                return inner.TryAcquire(
                    world,
                    owner,
                    output,
                    in resources,
                    out lease,
                    out error);
            }

            public void Release(
                World world,
                CameraManager owner,
                ICameraOutput output,
                in CameraOutputLease lease)
            {
                inner.Release(world, owner, output, in lease);
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
                bool innerReleased = inner.TryReleaseAll(world, in releasePass);
                if (remainingFailedTerminalPasses > 0)
                {
                    remainingFailedTerminalPasses--;
                    return false;
                }

                return innerReleased;
            }
        }

        private sealed class RejectingActorLifetime : IActorLifetime
        {
            public T Create<T>(T prefab) where T : Actor
            {
                throw new InvalidOperationException(
                    "Actor creation failure requested by the test.");
            }

            public void Release(Actor actor)
            {
            }
        }

        private sealed class ControlledResolver : IWorldSettingsReferenceResolver
        {
            private readonly GameMode gameMode;

            public ControlledResolver(GameMode gameMode)
            {
                this.gameMode = gameMode;
            }

            public ResolverBehavior Behavior { get; set; }
            public IDisposable Lease { get; set; }
            public bool ResolveEntered { get; private set; }
            public int ResolveCount { get; private set; }

            public bool Supports(WorldSettingsReferenceSource source)
            {
                return source == WorldSettingsReferenceSource.PathLocation;
            }

            public async UniTask<WorldSettingsAssetLoadResult<T>> ResolveAsync<T>(
                string location,
                IWorldSettingsLeaseRegistrar leaseRegistrar,
                CancellationToken cancellationToken) where T : UnityEngine.Object
            {
                ResolveEntered = true;
                ResolveCount++;
                leaseRegistrar.Register(Lease);
                if (Behavior == ResolverBehavior.Pending)
                {
                    await UniTask.WaitUntilCanceled(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (Behavior == ResolverBehavior.Fault)
                {
                    throw new InvalidOperationException("Resolver failure requested by the test.");
                }

                T asset = gameMode as T;
                return asset != null
                    ? new WorldSettingsAssetLoadResult<T>(true, asset, null)
                    : new WorldSettingsAssetLoadResult<T>(false, null, "Unexpected asset type.");
            }
        }

        private sealed class FaultingLease : IDisposable
        {
            private int remainingFailures;

            public FaultingLease(int failedDisposeCount)
            {
                remainingFailures = failedDisposeCount;
            }

            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
                if (remainingFailures > 0)
                {
                    remainingFailures--;
                    throw new InvalidOperationException(
                        "Lease cleanup failure requested by the test.");
                }
            }
        }

    }
}
