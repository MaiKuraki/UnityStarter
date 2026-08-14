using System.Collections;
using System.Text;
using System.Threading;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Logging;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class WorldLifecycleTests
    {
        [Test]
        public void StartWorld_CreatesAuthoritativeGameplayChainForLocalPlayer()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);

            Assert.AreEqual(WorldLifecycleState.Playing, testWorld.World.LifecycleState);
            Assert.IsTrue(testWorld.World.IsAuthority);
            Assert.IsNotNull(testWorld.World.GameMode);
            Assert.AreEqual(GameModeLifecycleState.Running, testWorld.World.GameMode.ModeState);
            Assert.AreEqual(1, testWorld.World.PlayerControllers.Count);

            PlayerController controller = testWorld.World.PlayerControllers[0];
            Assert.IsTrue(controller.IsLocalController);
            Assert.IsNotNull(controller.GetPlayerState());
            Assert.IsNotNull(controller.GetPawn());
            Assert.AreSame(controller, controller.GetPawn().Controller);
            Assert.AreSame(controller, testWorld.Instance.LocalPlayers[0].PlayerController);
        }

        [Test]
        public void ClientWorld_DoesNotCreateAuthoritativeGameModeOrPlayers()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                netMode: WorldNetMode.Client);

            Assert.IsFalse(testWorld.World.IsAuthority);
            Assert.IsNull(testWorld.World.GameMode);
            Assert.AreEqual(0, testWorld.World.PlayerControllers.Count);
            Assert.IsNull(testWorld.Instance.LocalPlayers[0].PlayerController);
        }

        [Test]
        public void ClientWorld_CanBindRegisteredReplicatedGameState()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 0,
                netMode: WorldNetMode.Client);
            GameState replicatedState = testWorld.CreateAuthoringActor<GameState>("ReplicatedGameState");

            testWorld.World.RegisterActor(replicatedState);
            testWorld.World.SetReplicatedGameState(replicatedState);

            Assert.AreSame(replicatedState, testWorld.World.GetGameState());
            Assert.AreSame(replicatedState, replicatedState.GetGameState());

            Assert.IsTrue(testWorld.World.DestroyActor(replicatedState));
            Assert.IsNull(testWorld.World.GetGameState());
        }

        [Test]
        public void ClientWorld_CanCommitInitializedReplicatedLocalPlayerController()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                netMode: WorldNetMode.Client);
            PlayerController controller =
                testWorld.CreateAuthoringActor<PlayerController>("ReplicatedPlayerController");
            PlayerState playerState =
                testWorld.CreateAuthoringActor<PlayerState>("ReplicatedPlayerState");
            LocalPlayer localPlayer = testWorld.Instance.LocalPlayers[0];

            testWorld.World.RegisterActor(controller);
            testWorld.World.RegisterActor(playerState);
            controller.InitializePlayer(testWorld.World, playerState, localPlayer);
            testWorld.World.CommitReplicatedPlayerController(controller, localPlayer);

            Assert.AreSame(controller, testWorld.World.GetFirstPlayerController());
            Assert.AreSame(controller, localPlayer.PlayerController);
            Assert.IsTrue(controller.IsLocalController);

            Assert.IsTrue(testWorld.World.DestroyActor(controller));
            Assert.IsNull(testWorld.World.GetFirstPlayerController());
            Assert.IsNull(localPlayer.PlayerController);
        }

        [Test]
        public void ClientWorld_ReplicatedLocalPlayerConflict_DoesNotPartiallyCommitController()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                netMode: WorldNetMode.Client);
            LocalPlayer localPlayer = testWorld.Instance.LocalPlayers[0];
            PlayerController firstController =
                testWorld.CreateAuthoringActor<PlayerController>("FirstReplicatedPlayerController");
            PlayerState firstPlayerState =
                testWorld.CreateAuthoringActor<PlayerState>("FirstReplicatedPlayerState");
            PlayerController conflictingController =
                testWorld.CreateAuthoringActor<PlayerController>("ConflictingReplicatedPlayerController");
            PlayerState conflictingPlayerState =
                testWorld.CreateAuthoringActor<PlayerState>("ConflictingReplicatedPlayerState");

            testWorld.World.RegisterActor(firstController);
            testWorld.World.RegisterActor(firstPlayerState);
            firstController.InitializePlayer(testWorld.World, firstPlayerState, localPlayer);
            testWorld.World.CommitReplicatedPlayerController(firstController, localPlayer);

            testWorld.World.RegisterActor(conflictingController);
            testWorld.World.RegisterActor(conflictingPlayerState);
            conflictingController.InitializePlayer(
                testWorld.World,
                conflictingPlayerState,
                localPlayer);

            Assert.Throws<System.InvalidOperationException>(() =>
                testWorld.World.CommitReplicatedPlayerController(conflictingController, localPlayer));

            Assert.AreEqual(1, testWorld.World.PlayerControllerCount);
            Assert.AreSame(firstController, testWorld.World.GetFirstPlayerController());
            Assert.AreSame(firstController, localPlayer.PlayerController);
            Assert.IsFalse(testWorld.World.ContainsPlayerController(conflictingController));
            Assert.IsTrue(testWorld.World.IsActorRegistered(conflictingController));
        }

        [Test]
        public void AuthorityWorld_RejectsReplicatedPlayerControllerCommit()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            PlayerController controller =
                testWorld.CreateAuthoringActor<PlayerController>("ReplicatedPlayerController");
            PlayerState playerState =
                testWorld.CreateAuthoringActor<PlayerState>("ReplicatedPlayerState");
            testWorld.World.RegisterActor(controller);
            testWorld.World.RegisterActor(playerState);
            controller.InitializePlayer(testWorld.World, playerState, null);

            Assert.Throws<System.InvalidOperationException>(() =>
                testWorld.World.CommitReplicatedPlayerController(controller));
        }

        [Test]
        public void AuthorityWorld_RejectsReplicatedGameStateBinding()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            GameState candidate = testWorld.CreateAuthoringActor<GameState>("ReplicatedGameState");
            testWorld.World.RegisterActor(candidate);

            Assert.Throws<System.InvalidOperationException>(() =>
                testWorld.World.SetReplicatedGameState(candidate));
        }

        [Test]
        public void DedicatedServer_DoesNotCreateLocalPlayerControllers()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                netMode: WorldNetMode.DedicatedServer);

            Assert.IsTrue(testWorld.World.IsAuthority);
            Assert.IsTrue(testWorld.World.IsDedicatedServer);
            Assert.IsNotNull(testWorld.World.GameMode);
            Assert.AreEqual(0, testWorld.World.PlayerControllers.Count);
        }

        [Test]
        public void StopThenStart_ReusesNonOwnedSceneActorsForANewWorldCycle()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            Actor sceneAuthoringActor = testWorld.Settings.PawnClass;

            testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult();
            Assert.AreEqual(ActorLifecycleState.Ended, sceneAuthoringActor.LifecycleState);

            World restartedWorld = testWorld.StartWorld();

            Assert.AreEqual(WorldLifecycleState.Playing, restartedWorld.LifecycleState);
            Assert.AreSame(restartedWorld, sceneAuthoringActor.World);
            Assert.AreEqual(ActorLifecycleState.Playing, sceneAuthoringActor.LifecycleState);
        }

        [Test]
        public void NonOwnedAIController_UnbindClearsWorldScopedPossessionAndRuntimeState()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            AIController controller =
                testWorld.CreateAuthoringActor<AIController>("SceneAIController");
            Pawn pawn = testWorld.CreateAuthoringActor<Pawn>("SceneAIPawn");
            PlayerState playerState =
                testWorld.CreateAuthoringActor<PlayerState>("SceneAIPlayerState");
            World world = testWorld.StartWorld();
            controller.Initialize(world, playerState);
            controller.Possess(pawn);

            Assert.IsTrue(controller.IsInitialized);
            Assert.IsTrue(controller.IsRunningAI());
            Assert.AreEqual(ActorTickPhase.Update, controller.TickPhase);
            Assert.IsTrue(controller.IsActorTickEnabled());
            Assert.AreEqual(1, world.GetTickActorCount(ActorTickPhase.Update));
            Assert.AreSame(pawn, controller.GetPawn());
            Assert.AreSame(controller, pawn.Controller);
            Assert.AreSame(pawn, playerState.GetPawn());

            testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult();

            Assert.IsFalse(controller.IsInitialized);
            Assert.IsFalse(controller.IsRunningAI());
            Assert.IsFalse(controller.IsActorTickEnabled());
            Assert.IsNull(controller.GetPawn());
            Assert.IsNull(controller.GetPlayerState());
            Assert.IsNull(pawn.Controller);
            Assert.IsNull(pawn.GetPlayerState());
            Assert.IsNull(playerState.GetPawn());

            World replacementWorld = testWorld.StartWorld();
            Assert.AreEqual(ActorTickPhase.Update, controller.TickPhase);
            Assert.IsFalse(controller.IsActorTickEnabled());
            Assert.Zero(replacementWorld.GetTickActorCount(ActorTickPhase.Update));
            Assert.IsFalse(controller.TryPossess(pawn, out _));
            controller.Initialize(replacementWorld, playerState);
            Assert.IsTrue(controller.TryPossess(pawn, out string error), error);
            Assert.IsTrue(controller.IsActorTickEnabled());
            Assert.AreEqual(1, replacementWorld.GetTickActorCount(ActorTickPhase.Update));
        }

        [Test]
        public void RegisteredInactiveActor_BeginsWhenEnabledInPlayingWorldExactlyOnce()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            CountingBeginPlayActor actor =
                testWorld.CreateAuthoringActor<CountingBeginPlayActor>("InactiveRegisteredActor");
            actor.gameObject.SetActive(false);

            testWorld.World.RegisterActor(actor);
            Assert.AreEqual(ActorLifecycleState.Initialized, actor.LifecycleState);
            Assert.AreEqual(0, actor.BeginPlayCount);

            actor.gameObject.SetActive(true);
            actor.NotifyEnabledForTest();
            actor.gameObject.SetActive(false);
            actor.gameObject.SetActive(true);
            actor.NotifyEnabledForTest();

            Assert.AreEqual(ActorLifecycleState.Playing, actor.LifecycleState);
            Assert.AreEqual(1, actor.BeginPlayCount);
        }

        [Test]
        public void DeferredActor_EarlyActivationDoesNotBypassFinishSpawningBarrier()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            CountingBeginPlayActor prefab =
                testWorld.CreateAuthoringActor<CountingBeginPlayActor>("DeferredActorPrefab");
            CountingBeginPlayActor actor = testWorld.World.SpawnActorDeferred(prefab);

            actor.gameObject.SetActive(true);
            Assert.AreEqual(0, actor.BeginPlayCount);

            testWorld.World.FinishSpawningActor(actor);

            Assert.AreEqual(ActorLifecycleState.Playing, actor.LifecycleState);
            Assert.AreEqual(1, actor.BeginPlayCount);
        }

        [Test]
        public void DestroyRegisteredLocalController_ClearsWorldAndLocalPlayerImmediately()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            PlayerController controller = testWorld.World.PlayerControllers[0];
            PlayerState playerState = controller.GetPlayerState();
            Pawn pawn = controller.GetPawn();

            Assert.IsTrue(testWorld.World.DestroyActor(controller));

            Assert.AreEqual(0, testWorld.World.PlayerControllers.Count);
            Assert.IsNull(testWorld.Instance.LocalPlayers[0].PlayerController);
            Assert.AreEqual(0, testWorld.World.GameMode.GetGameSession().PlayerCount);
            Assert.IsFalse(testWorld.World.IsActorRegistered(playerState));
            Assert.IsFalse(testWorld.World.IsActorRegistered(pawn));
        }

        [Test]
        public void DestroyCommittedPlayerState_LogsOutAndDestroysWholeParticipant()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            PlayerController controller = testWorld.World.PlayerControllers[0];
            PlayerState playerState = controller.GetPlayerState();
            Pawn pawn = controller.GetPawn();

            Assert.IsTrue(testWorld.World.DestroyActor(playerState));

            Assert.AreEqual(0, testWorld.World.PlayerControllers.Count);
            Assert.AreEqual(0, testWorld.World.GameMode.GetGameSession().PlayerCount);
            Assert.IsNull(testWorld.Instance.LocalPlayers[0].PlayerController);
            Assert.IsFalse(testWorld.World.IsActorRegistered(controller));
            Assert.IsFalse(testWorld.World.IsActorRegistered(pawn));
        }

        [Test]
        public void DestroyCallbackCommittedPlayerState_RemovesEntryBeforeParticipantCleanup()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                configure: world => world.SetReference(
                    "playerStateClass",
                    world.CreateAuthoringActor<DestroyablePlayerState>("DestroyablePlayerStatePrefab")));
            PlayerController controller = testWorld.World.PlayerControllers[0];
            DestroyablePlayerState playerState = controller.GetPlayerState<DestroyablePlayerState>();
            Pawn pawn = controller.GetPawn();

            Assert.DoesNotThrow(playerState.NotifyDestroyForTest);

            Assert.AreEqual(WorldLifecycleState.Playing, testWorld.World.LifecycleState);
            Assert.AreEqual(0, testWorld.World.PlayerControllers.Count);
            Assert.AreEqual(0, testWorld.World.GameMode.GetGameSession().PlayerCount);
            Assert.IsFalse(testWorld.World.IsActorRegistered(controller));
            Assert.IsFalse(testWorld.World.IsActorRegistered(pawn));
        }

        [Test]
        public void PossessionCallback_DestroyingIncomingPawnEmergencyDetachesAllRelationships()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            PlayerController controller = testWorld.World.PlayerControllers[0];
            PlayerState playerState = controller.GetPlayerState();
            Pawn replacementPrefab = testWorld.CreateAuthoringActor<Pawn>("ReplacementPawnPrefab");
            Pawn replacement = testWorld.World.SpawnActor(replacementPrefab);
            controller.OnPossessedPawnChanged += (_, currentPawn) =>
            {
                if (ReferenceEquals(currentPawn, replacement))
                {
                    Object.DestroyImmediate(replacement.gameObject);
                }
            };

            bool possessed = controller.TryPossess(replacement, out string error);

            Assert.IsFalse(possessed);
            StringAssert.Contains("invalidated", error);
            Assert.IsNull(controller.GetPawn());
            Assert.IsNull(playerState.GetPawn());
            Assert.IsNull(replacement.Controller);
        }

        [Test]
        public void DestroyInsideEndPlay_PreservesDestroyedStateAndNotifiesWorldUnboundOnce()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            DestroyInsideEndPlayActor prefab =
                testWorld.CreateAuthoringActor<DestroyInsideEndPlayActor>("DestroyInsideEndPlayPrefab");
            DestroyInsideEndPlayActor actor = testWorld.World.SpawnActor(prefab);

            Assert.DoesNotThrow(() => testWorld.World.DestroyActor(actor));

            Assert.AreEqual(ActorLifecycleState.Destroyed, actor.LifecycleState);
            Assert.AreEqual(1, actor.WorldUnboundCount);
        }

        [Test]
        public void ReentrantStopDuringEndPlay_FailsFastAndKeepsWorldOwnedUntilShutdownCompletes()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            ReentrantStopActor actor =
                testWorld.CreateAuthoringActor<ReentrantStopActor>("ReentrantStopActor");
            World world = testWorld.StartWorld();
            World observedDuringCallback = null;
            System.Exception reentrantStopException = null;
            System.Exception restartException = null;
            actor.Callback = () =>
            {
                try
                {
                    testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult();
                }
                catch (System.Exception exception)
                {
                    reentrantStopException = exception;
                }

                observedDuringCallback = testWorld.Instance.CurrentWorld;
                try
                {
                    testWorld.Instance.StartWorldAsync(testWorld.Settings).GetAwaiter().GetResult();
                }
                catch (System.Exception exception)
                {
                    restartException = exception;
                }
            };

            testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult();

            Assert.AreSame(world, observedDuringCallback);
            Assert.IsInstanceOf<System.InvalidOperationException>(reentrantStopException);
            Assert.IsInstanceOf<System.InvalidOperationException>(restartException);
            Assert.AreEqual(WorldLifecycleState.Disposed, world.LifecycleState);
            Assert.IsNull(testWorld.Instance.CurrentWorld);
        }

        [Test]
        public void ReentrantStopDuringGameModeCleanup_FailsFastWithoutCompletingEarly()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create(
                localPlayerCount: 1,
                configure: world => world.SetReference(
                    "gameModeClass",
                    world.CreateAuthoringActor<ReentrantStopGameMode>(
                        "ReentrantStopGameModePrefab")));
            World world = testWorld.StartWorld();
            var gameMode = (ReentrantStopGameMode)world.GameMode;

            testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult();

            Assert.IsInstanceOf<System.InvalidOperationException>(
                gameMode.ReentrantStopFailure);
            Assert.AreEqual(WorldLifecycleState.Disposed, world.LifecycleState);
            Assert.IsNull(testWorld.Instance.CurrentWorld);
        }

        [Test]
        public void DestroyedGameMode_RetainsParticipantCleanupUntilSessionReleaseSucceeds()
        {
            var session = new FailOnceUnregisterSession();
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                session: session,
                discoverActiveSceneActors: false);
            World world = testWorld.World;
            GameMode gameMode = world.GameMode;
            Assert.AreEqual(1, session.PlayerCount);

            GameObject gameModeObject = gameMode.gameObject;
            UnityLifecycleTestUtility.InvokeOnDestroy(gameMode);
            if (gameModeObject != null)
            {
                Object.DestroyImmediate(gameModeObject);
            }

            Assert.AreEqual(WorldLifecycleState.Stopping, world.LifecycleState);
            Assert.IsNull(world.GameMode);
            Assert.IsTrue(world.HasPendingGameplayCleanup);
            Assert.AreSame(world, testWorld.Instance.CurrentWorld);
            Assert.AreEqual(1, session.PlayerCount);
            Assert.AreEqual(1, session.UnregisterAttemptCount);

            testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult();

            Assert.AreEqual(WorldLifecycleState.Disposed, world.LifecycleState);
            Assert.IsFalse(world.HasPendingGameplayCleanup);
            Assert.IsNull(testWorld.Instance.CurrentWorld);
            Assert.Zero(session.PlayerCount);
            Assert.AreEqual(2, session.UnregisterAttemptCount);
        }

        [UnityTest]
        public IEnumerator DirectWorldShutdown_CancelsPendingLoginBeforeStartupCanCommit()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PendingLoginGameMode.ResetPendingLogin();
                using GameplayTestWorld testWorld = GameplayTestWorld.Create(
                    localPlayerCount: 1,
                    configure: world => world.SetReference(
                        "gameModeClass",
                        world.CreateAuthoringActor<PendingLoginGameMode>("PendingLoginGameModePrefab")));

                try
                {
                    UniTask<World> startTask = testWorld.Instance.StartWorldAsync(testWorld.Settings);
                    for (int attempt = 0;
                         attempt < 120 && !PendingLoginGameMode.LoginEntered;
                         attempt++)
                    {
                        await UniTask.Yield();
                    }

                    Assert.IsTrue(PendingLoginGameMode.LoginEntered);
                    World world = testWorld.Instance.CurrentWorld;
                    Assert.IsNotNull(world);
                    Assert.AreEqual(WorldLifecycleState.Initializing, world.LifecycleState);

                    await world.ShutdownAsync();
                    PendingLoginGameMode.CompletePendingLogin();

                    System.Exception startupFailure = null;
                    try
                    {
                        await startTask;
                    }
                    catch (System.Exception exception)
                    {
                        startupFailure = exception;
                    }

                    Assert.IsInstanceOf<System.OperationCanceledException>(startupFailure);
                    Assert.AreEqual(WorldLifecycleState.Disposed, world.LifecycleState);
                    Assert.IsNull(testWorld.Instance.CurrentWorld);
                }
                finally
                {
                    PendingLoginGameMode.ClearPendingLogin();
                }
            });
        }

        [Test]
        public void DestroyCameraManager_ReleasesUnityCameraOutput()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            CameraManager prefab = testWorld.CreateAuthoringActor<CameraManager>("CameraManagerPrefab");
            Camera prefabCamera = prefab.gameObject.AddComponent<Camera>();
            UnityCameraOutput prefabOutput = prefab.gameObject.AddComponent<UnityCameraOutput>();
            UnityLifecycleTestUtility.InvokeAwake(prefabOutput);
            prefabOutput.SetTargetCamera(prefabCamera);
            prefab.SetCameraOutput(prefabOutput, rebindImmediately: false);

            CameraManager manager = testWorld.World.SpawnActor(prefab);
            UnityLifecycleTestUtility.InvokeAwake(manager.GetComponent<UnityCameraOutput>());
            manager.InitializeFor(testWorld.World.PlayerControllers[0]);
            UnityCameraOutput activeOutput = manager.ActiveOutput as UnityCameraOutput;

            Assert.IsNotNull(activeOutput);
            Assert.IsTrue(activeOutput.IsActive);
            Assert.AreEqual(ActorTickPhase.LateUpdate, manager.TickPhase);
            Assert.IsTrue(manager.IsActorTickEnabled());
            Assert.AreEqual(1, testWorld.World.GetTickActorCount(ActorTickPhase.LateUpdate));
            Assert.IsTrue(testWorld.World.DestroyActor(manager));
            Assert.Zero(testWorld.World.GetTickActorCount(ActorTickPhase.LateUpdate));
        }

        [Test]
        public void UnityCameraOutput_AppliesPoseAndFieldOfView()
        {
            var managerObject = new GameObject("CameraManager");
            try
            {
                CameraManager manager = managerObject.AddComponent<CameraManager>();
                UnityLifecycleTestUtility.InvokeAwake(manager);
                Camera camera = managerObject.AddComponent<Camera>();
                UnityCameraOutput output = managerObject.AddComponent<UnityCameraOutput>();
                UnityLifecycleTestUtility.InvokeAwake(output);
                output.SetTargetCamera(camera);
                var pose = new CameraPose(
                    new Vector3(2f, 3f, 4f),
                    Quaternion.Euler(5f, 15f, 0f),
                    75f);

                Assert.IsTrue(output.TryGetResourceSet(
                    out CameraOutputResourceSet resources,
                    out string discoveryError), discoveryError);
                Assert.AreEqual(1, resources.Count);
                Assert.AreSame(camera, resources.GetResource(0));
                Assert.IsTrue(output.TryActivate(
                    manager,
                    in resources,
                    out string activationError), activationError);
                output.ApplyPose(in pose);

                Assert.AreEqual(pose.Position, camera.transform.position);
                Assert.Less(Quaternion.Angle(pose.Rotation, camera.transform.rotation), 0.001f);
                Assert.AreEqual(pose.Fov, camera.fieldOfView, 0.0001f);
                output.Deactivate(manager);
                Assert.IsFalse(output.IsActive);
            }
            finally
            {
                Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        public void CameraOutputOwnership_RejectsSharedResourceUntilOwnerReleasesIt()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            var cameraObject = new GameObject("SharedCamera");
            try
            {
                Camera sharedCamera = cameraObject.AddComponent<Camera>();
                CameraManager prefab = testWorld.CreateAuthoringActor<CameraManager>("CameraManagerPrefab");
                UnityCameraOutput prefabOutput = prefab.gameObject.AddComponent<UnityCameraOutput>();
                UnityLifecycleTestUtility.InvokeAwake(prefabOutput);
                prefabOutput.SetTargetCamera(sharedCamera);
                prefab.SetCameraOutput(prefabOutput, rebindImmediately: false);

                CameraManager first = testWorld.World.SpawnActor(prefab);
                CameraManager second = testWorld.World.SpawnActor(prefab);
                UnityLifecycleTestUtility.InvokeAwake(first.GetComponent<UnityCameraOutput>());
                UnityLifecycleTestUtility.InvokeAwake(second.GetComponent<UnityCameraOutput>());
                PlayerController controller = testWorld.World.PlayerControllers[0];
                first.InitializeFor(controller);
                second.InitializeFor(controller);

                Assert.IsNotNull(first.ActiveOutput);
                Assert.IsNull(second.ActiveOutput);
                Assert.IsTrue(testWorld.World.DestroyActor(first));
                Assert.IsTrue(second.TryResolveAndBindOutput());
                Assert.IsNotNull(second.ActiveOutput);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void DestroyedConfiguredCameraOutput_IsTreatedAsMissing()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            CameraManager prefab = testWorld.CreateAuthoringActor<CameraManager>("CameraManagerPrefab");
            Camera prefabCamera = prefab.gameObject.AddComponent<Camera>();
            UnityCameraOutput prefabOutput = prefab.gameObject.AddComponent<UnityCameraOutput>();
            UnityLifecycleTestUtility.InvokeAwake(prefabOutput);
            prefabOutput.SetTargetCamera(prefabCamera);
            prefab.SetCameraOutput(prefabOutput, rebindImmediately: false);

            CameraManager manager = testWorld.World.SpawnActor(prefab);
            UnityCameraOutput spawnedOutput = manager.GetComponent<UnityCameraOutput>();
            UnityLifecycleTestUtility.InvokeAwake(spawnedOutput);
            Object.DestroyImmediate(spawnedOutput);

            Assert.DoesNotThrow(() =>
                manager.InitializeFor(testWorld.World.PlayerControllers[0]));
            Assert.IsNull(manager.ConfiguredOutput);
            Assert.IsNull(manager.ActiveOutput);
        }

        [Test]
        public void CameraOutputActivation_ReentrantCompositionIsRejectedWithoutLeakingOwnership()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            CameraManager prefab = testWorld.CreateAuthoringActor<CameraManager>("CameraManagerPrefab");
            prefab.gameObject.AddComponent<Camera>();
            ReentrantCameraOutput prefabOutput = prefab.gameObject.AddComponent<ReentrantCameraOutput>();
            UnityLifecycleTestUtility.InvokeAwake(prefabOutput);
            prefab.SetCameraOutput(prefabOutput, rebindImmediately: false);

            CameraManager manager = testWorld.World.SpawnActor(prefab);
            ReentrantCameraOutput output = manager.GetComponent<ReentrantCameraOutput>();
            UnityLifecycleTestUtility.InvokeAwake(output);
            Assert.Throws<System.InvalidOperationException>(() =>
                manager.InitializeFor(testWorld.World.PlayerControllers[0]));
            Assert.IsNull(manager.ActiveOutput);
            Assert.IsFalse(output.IsActive);

            output.ReenterOnActivate = false;
            Assert.DoesNotThrow(() =>
                manager.InitializeFor(testWorld.World.PlayerControllers[0]));
            Assert.AreSame(output, manager.ActiveOutput);
            Assert.IsTrue(output.IsActive);
        }

        [Test]
        public void DestroyActiveGameMode_ShutsDownWholeWorldAndClearsGameInstance()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            World world = testWorld.World;

            Assert.IsTrue(world.DestroyActor(world.GameMode));

            Assert.AreEqual(WorldLifecycleState.Disposed, world.LifecycleState);
            Assert.IsNull(testWorld.Instance.CurrentWorld);
            Assert.AreEqual(0, world.PlayerControllers.Count);
        }

        [Test]
        public void CameraMode_SelfRemovalCommitsAfterEvaluation()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            PlayerController controller = testWorld.World.PlayerControllers[0];
            CameraManager prefab = testWorld.CreateAuthoringActor<CameraManager>("CameraManagerPrefab");
            CameraManager manager = testWorld.World.SpawnActor(prefab);
            manager.InitializeFor(controller);
            var mode = new SelfRemovingCameraMode();
            Assert.IsTrue(controller.TryPushCameraMode(mode));

            Assert.DoesNotThrow(() => manager.UpdateCamera(1f / 60f));

            Assert.IsTrue(mode.RemovalResult);
            Assert.AreEqual(0, controller.GetCameraContext().CameraModeCount);
        }

        [Test]
        public void NonOwnedCameraManager_ResetsAndCanInitializeInNextWorldCycle()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create(localPlayerCount: 1);
            CameraManager sceneManager = testWorld.CreateAuthoringActor<CameraManager>("SceneCameraManager");
            testWorld.StartWorld();
            sceneManager.InitializeFor(testWorld.World.PlayerControllers[0]);
            Assert.IsTrue(sceneManager.IsInitialized);
            Assert.AreEqual(ActorTickPhase.LateUpdate, sceneManager.TickPhase);
            Assert.IsTrue(sceneManager.IsActorTickEnabled());

            testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult();
            Assert.IsFalse(sceneManager.IsInitialized);
            Assert.IsFalse(sceneManager.IsActorTickEnabled());

            testWorld.StartWorld();
            Assert.IsFalse(sceneManager.IsActorTickEnabled());
            Assert.Zero(testWorld.World.GetTickActorCount(ActorTickPhase.LateUpdate));
            Assert.DoesNotThrow(() =>
                sceneManager.InitializeFor(testWorld.World.PlayerControllers[0]));
            Assert.IsTrue(sceneManager.IsInitialized);
            Assert.IsTrue(sceneManager.IsActorTickEnabled());
            Assert.AreEqual(1, testWorld.World.GetTickActorCount(ActorTickPhase.LateUpdate));
        }

        [Test]
        public void InactiveNonOwnedCameraManager_ReleasesOutputWithoutBeginPlay()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create(localPlayerCount: 1);
            CameraManager sceneManager = testWorld.CreateAuthoringActor<CameraManager>("InactiveSceneCameraManager");
            Camera sceneCamera = sceneManager.gameObject.AddComponent<Camera>();
            UnityCameraOutput output = sceneManager.gameObject.AddComponent<UnityCameraOutput>();
            UnityLifecycleTestUtility.InvokeAwake(output);
            output.SetTargetCamera(sceneCamera);
            sceneManager.SetCameraOutput(output, rebindImmediately: false);
            sceneManager.gameObject.SetActive(false);
            testWorld.StartWorld();
            sceneManager.InitializeFor(testWorld.World.PlayerControllers[0]);
            Assert.IsTrue(output.IsActive);

            testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult();

            Assert.IsFalse(output.IsActive);
            Assert.IsFalse(sceneManager.IsInitialized);
        }

        [Test]
        public void CameraManager_RejectsReentrantUpdateFromPostProcessor()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            CameraManager prefab = testWorld.CreateAuthoringActor<CameraManager>("CameraManagerPrefab");
            CameraManager manager = testWorld.World.SpawnActor(prefab);
            manager.InitializeFor(testWorld.World.PlayerControllers[0]);
            var processor = new ReentrantCameraPostProcessor(manager);
            manager.RegisterPostProcessor(processor);

            Assert.DoesNotThrow(() => manager.UpdateCamera(1f / 60f));

            Assert.AreEqual(1, processor.ProcessCount);
        }

        [Test]
        public void WorldShutdown_PreservesTickRegistryChangesMadeDuringCameraManagerUnbind()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                discoverActiveSceneActors: false);
            World world = testWorld.World;
            CameraManager prefab =
                testWorld.CreateAuthoringActor<CameraManager>("TickingCameraManagerPrefab");
            CameraManager manager = world.SpawnActor(prefab);
            manager.InitializeFor(world.PlayerControllers[0]);

            Assert.IsTrue(manager.IsActorTickEnabled());
            Assert.AreEqual(1, world.GetTickActorCount(ActorTickPhase.LateUpdate));

            Assert.DoesNotThrow(() =>
                testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult());

            Assert.AreEqual(WorldLifecycleState.Disposed, world.LifecycleState);
            Assert.Zero(world.ActorCount);
            Assert.Zero(world.GetTickActorCount(ActorTickPhase.LateUpdate));
            Assert.IsNull(testWorld.Instance.CurrentWorld);
        }

        [Test]
        public void SetViewTargetWithBlend_RejectedTargetDoesNotPublishBlendOverride()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create(
                localPlayerCount: 1,
                configure: world => world.SetReference(
                    "cameraManagerClass",
                    world.CreateAuthoringActor<CameraManager>("LocalCameraManagerPrefab")));
            testWorld.StartWorld();
            PlayerController controller = testWorld.World.PlayerControllers[0];
            CameraManager cameraManager = controller.GetCameraManager();
            Pawn foreignTarget = testWorld.CreateAuthoringActor<Pawn>("ForeignViewTarget");

            Assert.IsNotNull(cameraManager);
            Assert.IsFalse(cameraManager.HasPendingBlendDurationOverride);
            Assert.Throws<System.InvalidOperationException>(() =>
                controller.SetViewTargetWithBlend(foreignTarget, 1f));
            Assert.IsFalse(cameraManager.HasPendingBlendDurationOverride);
        }

        [Test]
        public void LoginRejection_RollsBackWorldAndClearsCurrentWorld()
        {
            int controllersBefore = Object.FindObjectsByType<PlayerController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length;
            using GameplayTestWorld testWorld = GameplayTestWorld.Create(localPlayerCount: 1);

            Assert.Throws<System.InvalidOperationException>(() =>
                testWorld.StartWorld(session: new RejectAllSession()));

            Assert.IsNull(testWorld.Instance.CurrentWorld);
            int controllersAfter = Object.FindObjectsByType<PlayerController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length;
            Assert.AreEqual(controllersBefore + 1, controllersAfter, "Only the authoring prefab object should remain.");
        }

        [Test]
        public void FailedStartup_DoesNotPublishUnpairedSessionEndNotification()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            testWorld.CreateAuthoringActor<ThrowingBeginPlayActor>("ThrowingBeginPlayActor");
            var session = new TrackingSession();

            Assert.Throws<System.InvalidOperationException>(() => testWorld.StartWorld(session: session));

            Assert.AreEqual(0, session.MatchStartedCount);
            Assert.AreEqual(0, session.MatchEndedCount);
            Assert.IsNull(testWorld.Instance.CurrentWorld);
        }

        [Test]
        public void SuccessfulWorld_PublishesPairedSessionStartAndEndNotifications()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            var session = new TrackingSession();

            testWorld.StartWorld(session: session);
            Assert.AreEqual(1, session.MatchStartedCount);
            Assert.AreEqual(0, session.MatchEndedCount);

            testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, session.MatchStartedCount);
            Assert.AreEqual(1, session.MatchEndedCount);
        }

        [Test]
        public void MatchStartCallback_SynchronousWorldDispose_PublishesPairedEndNotification()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            var session = new ReentrantMatchStartSession(
                () => testWorld.Instance.CurrentWorld.Dispose());

            Assert.Throws<System.InvalidOperationException>(() =>
                testWorld.StartWorld(session: session));

            Assert.AreEqual(1, session.MatchStartedCount);
            Assert.AreEqual(1, session.MatchEndedCount);
            Assert.IsNull(testWorld.Instance.CurrentWorld);
        }

        [Test]
        public void MatchStartCallback_Throws_PublishesPairedEndDuringStartupRollback()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            var session = new ThrowingMatchStartSession();

            Assert.Throws<System.InvalidOperationException>(() =>
                testWorld.StartWorld(session: session));

            Assert.AreEqual(1, session.MatchStartedCount);
            Assert.AreEqual(1, session.MatchEndedCount);
            Assert.IsNull(testWorld.Instance.CurrentWorld);
        }

        [Test]
        public void TravelToLevel_CompletesShutdownAndNavigationWithoutCancellationGap()
        {
            var sceneTransitionHandler = new RecordingSceneTransitionHandler();
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                sceneTransitionHandler: sceneTransitionHandler);
            World previousWorld = testWorld.World;

            previousWorld.GameMode.TravelToLevel("Stage02")
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(WorldLifecycleState.Disposed, previousWorld.LifecycleState);
            Assert.IsNull(testWorld.Instance.CurrentWorld);
            Assert.AreEqual("Stage02", sceneTransitionHandler.ChangedScene);
            Assert.IsFalse(sceneTransitionHandler.ChangeToken.CanBeCanceled);
        }

        [Test]
        public void RemoteLoginAndLogout_AreTransactional()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            GameMode gameMode = testWorld.World.GameMode;
            PlayerLoginResult result = gameMode.LoginAsync(new PlayerLoginRequest(
                    playerId: 100,
                    playerName: "RemotePlayer",
                    remoteAddress: "127.0.0.1"))
                .GetAwaiter()
                .GetResult();

            Assert.IsTrue(result.Succeeded, result.Error);
            Assert.IsFalse(result.PlayerController.IsLocalController);
            Assert.AreEqual(1, testWorld.World.PlayerControllers.Count);
            Assert.AreEqual(1, gameMode.GetGameSession().PlayerCount);

            Assert.IsTrue(gameMode.Logout(result.PlayerController));
            Assert.AreEqual(0, testWorld.World.PlayerControllers.Count);
            Assert.AreEqual(0, gameMode.GetGameSession().PlayerCount);
        }

        [Test]
        public void AdmissionException_IsLoggedAndReturnsBoundedFailure()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                session: new ThrowingAdmissionSession());
            var logWriter = new ExceptionRecordingLogWriter();
            ILogWriter previousWriter = LogRuntime.Writer;
            Assert.IsTrue(LogRuntime.TryReplaceWriter(previousWriter, logWriter));
            PlayerLoginResult result;
            try
            {
                result = testWorld.World.GameMode.LoginAsync(
                        new PlayerLoginRequest(150, "AdmissionFailure"))
                    .GetAwaiter()
                    .GetResult();
            }
            finally
            {
                Assert.IsTrue(LogRuntime.TryReplaceWriter(logWriter, previousWriter));
            }

            Assert.AreEqual(PlayerLoginStatus.Rejected, result.Status);
            Assert.AreEqual("Player login policy evaluation failed.", result.Error);
            StringAssert.DoesNotContain("sensitive admission failure", result.Error);
            Assert.IsInstanceOf<System.InvalidOperationException>(logWriter.LastException);
            Assert.AreEqual(
                "sensitive admission failure",
                logWriter.LastException.Message);
            Assert.AreEqual(0, testWorld.World.PlayerControllerCount);
            Assert.AreEqual(0, testWorld.World.GameMode.GetGameSession().PlayerCount);
        }

        [Test]
        public void Logout_FromWorkerThread_RejectsBeforeMutatingParticipantState()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            GameMode gameMode = testWorld.World.GameMode;
            PlayerController controller = testWorld.World.PlayerControllers[0];
            PlayerState playerState = controller.GetPlayerState();
            Pawn pawn = controller.GetPawn();
            System.Exception workerException = null;
            bool logoutResult = false;

            var worker = new Thread(() =>
            {
                try
                {
                    logoutResult = gameMode.Logout(controller);
                }
                catch (System.Exception exception)
                {
                    workerException = exception;
                }
            });

            worker.Start();
            Assert.IsTrue(worker.Join(5000), "Worker thread did not finish within the test timeout.");

            Assert.IsInstanceOf<System.InvalidOperationException>(workerException);
            Assert.IsFalse(logoutResult);
            Assert.AreEqual(1, testWorld.World.PlayerControllers.Count);
            Assert.AreEqual(1, gameMode.GetGameSession().PlayerCount);
            Assert.AreSame(controller, testWorld.World.PlayerControllers[0]);
            Assert.AreSame(playerState, controller.GetPlayerState());
            Assert.AreSame(pawn, controller.GetPawn());
        }

        [Test]
        public void RemoteLogin_CannotClaimTrustedLocalPlayerFlag()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();

            PlayerLoginResult result = testWorld.World.GameMode.LoginAsync(
                    new PlayerLoginRequest(101, "SpoofedLocal", isLocal: true))
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(PlayerLoginStatus.InvalidRequest, result.Status);
            Assert.AreEqual(0, testWorld.World.PlayerControllers.Count);
            Assert.AreEqual(0, testWorld.World.GameMode.GetGameSession().PlayerCount);
        }

        [Test]
        public void PostLoginFailure_RollsBackGameStateRosterAndSpawnedActors()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(configure: world =>
            {
                ThrowingPostLoginGameMode gameMode =
                    world.CreateAuthoringActor<ThrowingPostLoginGameMode>("ThrowingGameModePrefab");
                GameState gameState = world.CreateAuthoringActor<GameState>("GameStatePrefab");
                var serializedGameMode = new UnityEditor.SerializedObject(gameMode);
                serializedGameMode.FindProperty("gameStateClass").objectReferenceValue = gameState;
                serializedGameMode.ApplyModifiedPropertiesWithoutUndo();
                world.SetReference(
                    "gameModeClass",
                    gameMode);
            });

            var logWriter = new ExceptionRecordingLogWriter();
            ILogWriter previousWriter = LogRuntime.Writer;
            Assert.IsTrue(LogRuntime.TryReplaceWriter(previousWriter, logWriter));
            PlayerLoginResult result;
            try
            {
                result = testWorld.World.GameMode.LoginAsync(
                        new PlayerLoginRequest(200, "RejectedAfterCommit"))
                    .GetAwaiter()
                    .GetResult();
            }
            finally
            {
                Assert.IsTrue(LogRuntime.TryReplaceWriter(logWriter, previousWriter));
            }

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(PlayerLoginStatus.SpawnFailed, result.Status);
            Assert.AreEqual("Player login failed while preparing participant state.", result.Error);
            StringAssert.DoesNotContain("PostLogin failure requested by test.", result.Error);
            Assert.IsInstanceOf<System.InvalidOperationException>(logWriter.LastException);
            Assert.AreEqual("PostLogin failure requested by test.", logWriter.LastException.Message);
            Assert.AreEqual(0, testWorld.World.PlayerControllers.Count);
            Assert.AreEqual(0, testWorld.World.GameState.PlayerArray.Count);
            Assert.AreEqual(0, testWorld.World.GameMode.GetGameSession().PlayerCount);
        }

        [Test]
        public void PostLoginLogout_ReturnsFailureInsteadOfDestroyedSuccessController()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(configure: world =>
            {
                world.SetReference(
                    "gameModeClass",
                    world.CreateAuthoringActor<LogoutInPostLoginGameMode>("LogoutInPostLoginGameModePrefab"));
            });

            PlayerLoginResult result = testWorld.World.GameMode.LoginAsync(
                    new PlayerLoginRequest(201, "LoggedOutInCallback"))
                .GetAwaiter()
                .GetResult();

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.PlayerController);
            Assert.AreEqual(0, testWorld.World.PlayerControllers.Count);
            Assert.AreEqual(0, testWorld.World.GameMode.GetGameSession().PlayerCount);
        }

        [Test]
        public void GameSession_RosterIsDuplicateSafeAndTracksRegisteredCategory()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            var gameSession = new GameSession(maxPlayers: 2, maxSpectators: 1);

            PlayerController CreatePlayer(int playerId)
            {
                PlayerController controller = testWorld.World.SpawnActor(
                    testWorld.World.Definition.PlayerControllerClass);
                PlayerState state = testWorld.World.SpawnActor(
                    testWorld.World.Definition.PlayerStateClass);
                state.SetPlayerId(playerId);
                controller.InitializePlayer(testWorld.World, state, null);
                return controller;
            }

            PlayerController first = CreatePlayer(10);
            PlayerController second = CreatePlayer(11);
            PlayerController duplicateIdentity = CreatePlayer(10);

            Assert.IsTrue(gameSession.TryRegisterPlayer(first, spectator: false, out _));
            Assert.IsTrue(first.GetPlayerState().IsIdentityLocked);
            var competingSession = new GameSession(maxPlayers: 2, maxSpectators: 1);
            Assert.IsFalse(competingSession.TryRegisterPlayer(first, spectator: true, out _));
            Assert.IsFalse(first.GetPlayerState().IsSpectator());
            var noSpectatorSession = new GameSession(maxPlayers: 2, maxSpectators: 0);
            Assert.IsFalse(noSpectatorSession.TryRegisterPlayer(second, spectator: true, out _));
            Assert.IsFalse(second.GetPlayerState().IsSpectator());
            Assert.Throws<System.InvalidOperationException>(() => first.GetPlayerState().SetPlayerId(12));
            Assert.IsFalse(gameSession.TryRegisterPlayer(first, spectator: false, out _));
            Assert.IsFalse(gameSession.TryRegisterPlayer(duplicateIdentity, spectator: false, out _));
            Assert.IsTrue(gameSession.TryRegisterPlayer(second, spectator: false, out _));
            IGameSession sessionContract = gameSession;
            Assert.IsTrue(sessionContract.AtCapacity(spectator: false));
            Assert.IsTrue(gameSession.TrySetSpectatorStatus(first, spectator: true, out _));
            Assert.IsFalse(sessionContract.AtCapacity(spectator: false));
            Assert.IsTrue(sessionContract.AtCapacity(spectator: true));
            Assert.IsTrue(first.GetPlayerState().IsSpectator());
            Assert.IsFalse(first.GetPlayerState().TryRestoreSnapshot(
                new PlayerStateSnapshot(
                    first.GetPlayerState().GetPlayerName(),
                    first.GetPlayerState().GetPlayerId(),
                    isSpectator: false),
                out _));
            Assert.AreEqual(1, gameSession.PlayerCount);
            Assert.AreEqual(1, gameSession.SpectatorCount);
            Assert.IsTrue(gameSession.UnregisterPlayer(first));
            Assert.IsFalse(first.GetPlayerState().IsIdentityLocked);
            Assert.DoesNotThrow(() => first.GetPlayerState().SetPlayerId(12));
            Assert.IsFalse(gameSession.UnregisterPlayer(first));
            Assert.AreEqual(0, gameSession.SpectatorCount);
        }

        [Test]
        public void DestroyedPlayerState_IsRemovedFromGameStateByManagedIdentity()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                discoverActiveSceneActors: false,
                configure: world =>
                {
                    GameMode gameMode =
                        world.CreateAuthoringActor<GameMode>("GameModeWithGameStatePrefab");
                    GameState gameStatePrefab =
                        world.CreateAuthoringActor<GameState>("GameStatePrefab");
                    var serializedGameMode = new UnityEditor.SerializedObject(gameMode);
                    serializedGameMode.FindProperty("gameStateClass").objectReferenceValue =
                        gameStatePrefab;
                    serializedGameMode.ApplyModifiedPropertiesWithoutUndo();
                    world.SetReference("gameModeClass", gameMode);
                });
            GameState gameState = testWorld.World.GameState;
            IGameSession session = testWorld.World.GameMode.GetGameSession();
            PlayerController playerController = testWorld.World.GetPlayerController(0);
            PlayerState playerState = playerController.GetPlayerState();
            Assert.AreEqual(1, gameState.PlayerArray.Count);
            Assert.AreEqual(1, session.PlayerCount);

            GameObject playerStateObject = playerState.gameObject;
            UnityLifecycleTestUtility.InvokeOnDestroy(playerState);
            if (playerStateObject != null)
            {
                Object.DestroyImmediate(playerStateObject);
            }

            Assert.Zero(
                gameState.PlayerArray.Count,
                $"controllers={testWorld.World.PlayerControllerCount}, " +
                $"sessionPlayers={session.PlayerCount}, actors={testWorld.World.ActorCount}");
            Assert.Zero(testWorld.World.PlayerControllerCount);
            Assert.Zero(session.PlayerCount);
        }

        [Test]
        public void GameSession_WorkerThreadAccessIsRejectedBeforeRuntimeRosterAccess()
        {
            var session = new GameSession();
            System.Exception observed = null;
            var worker = new Thread(() =>
            {
                try
                {
                    session.ContainsPlayer(null);
                }
                catch (System.Exception exception)
                {
                    observed = exception;
                }
            });

            worker.Start();
            Assert.IsTrue(worker.Join(5000), "Worker thread did not finish within the test timeout.");
            Assert.IsInstanceOf<System.InvalidOperationException>(observed);
        }

        private class TestSessionDecorator : IGameSession
        {
            private readonly GameSession inner = new GameSession();

            public int MaxPlayers => inner.MaxPlayers;
            public int MaxSpectators => inner.MaxSpectators;
            public int PlayerCount => inner.PlayerCount;
            public int SpectatorCount => inner.SpectatorCount;
            public bool AtCapacity(bool spectator) => inner.AtCapacity(spectator);

            public virtual bool ApproveLogin(in PlayerLoginRequest request, out string errorMessage)
            {
                return inner.ApproveLogin(request, out errorMessage);
            }

            public bool TryRegisterPlayer(
                PlayerController playerController,
                bool spectator,
                out string errorMessage)
            {
                return inner.TryRegisterPlayer(playerController, spectator, out errorMessage);
            }

            public bool ContainsPlayer(PlayerController playerController)
            {
                return inner.ContainsPlayer(playerController);
            }

            public virtual bool UnregisterPlayer(PlayerController playerController)
            {
                return inner.UnregisterPlayer(playerController);
            }

            public bool TrySetSpectatorStatus(
                PlayerController playerController,
                bool spectator,
                out string errorMessage)
            {
                return inner.TrySetSpectatorStatus(playerController, spectator, out errorMessage);
            }

            public virtual void HandleMatchHasStarted() { }
            public virtual void HandleMatchHasEnded() { }
        }

        private sealed class RejectAllSession : TestSessionDecorator
        {
            public override bool ApproveLogin(in PlayerLoginRequest request, out string errorMessage)
            {
                errorMessage = "Rejected by test policy.";
                return false;
            }
        }

        private sealed class FailOnceUnregisterSession : TestSessionDecorator
        {
            private bool failNextUnregister = true;
            public int UnregisterAttemptCount { get; private set; }

            public override bool UnregisterPlayer(PlayerController playerController)
            {
                UnregisterAttemptCount++;
                if (failNextUnregister)
                {
                    failNextUnregister = false;
                    throw new System.InvalidOperationException(
                        "Session unregister failure requested by the test.");
                }

                return base.UnregisterPlayer(playerController);
            }
        }

        private sealed class ThrowingAdmissionSession : TestSessionDecorator
        {
            public override bool ApproveLogin(
                in PlayerLoginRequest request,
                out string errorMessage)
            {
                errorMessage = null;
                throw new System.InvalidOperationException("sensitive admission failure");
            }
        }

        private sealed class RecordingSceneTransitionHandler : ISceneTransitionHandler
        {
            public string ChangedScene { get; private set; }
            public CancellationToken ChangeToken { get; private set; }

            public UniTask ChangeScene(
                string sceneName,
                CancellationToken cancellationToken = default)
            {
                ChangedScene = sceneName;
                ChangeToken = cancellationToken;
                return UniTask.CompletedTask;
            }

            public UniTask PushScene(
                string sceneName,
                CancellationToken cancellationToken = default)
            {
                return UniTask.CompletedTask;
            }

            public UniTask PopScene(CancellationToken cancellationToken = default)
            {
                return UniTask.CompletedTask;
            }

            public UniTask ReplaceScene(
                string sceneName,
                CancellationToken cancellationToken = default)
            {
                return UniTask.CompletedTask;
            }
        }

        private sealed class TrackingSession : TestSessionDecorator
        {
            public int MatchStartedCount { get; private set; }
            public int MatchEndedCount { get; private set; }

            public override void HandleMatchHasStarted()
            {
                MatchStartedCount++;
            }

            public override void HandleMatchHasEnded()
            {
                MatchEndedCount++;
            }
        }

        private sealed class ThrowingBeginPlayActor : Actor
        {
            protected override void BeginPlay()
            {
                throw new System.InvalidOperationException("BeginPlay failure requested by test.");
            }
        }

        private sealed class DestroyablePlayerState : PlayerState
        {
            public void NotifyDestroyForTest()
            {
                base.OnDestroy();
            }
        }

        private sealed class ReentrantMatchStartSession : TestSessionDecorator
        {
            private readonly System.Action onMatchStarted;

            public ReentrantMatchStartSession(System.Action onMatchStarted)
            {
                this.onMatchStarted = onMatchStarted;
            }

            public int MatchStartedCount { get; private set; }
            public int MatchEndedCount { get; private set; }

            public override void HandleMatchHasStarted()
            {
                MatchStartedCount++;
                onMatchStarted();
            }

            public override void HandleMatchHasEnded()
            {
                MatchEndedCount++;
            }
        }

        private sealed class ThrowingMatchStartSession : TestSessionDecorator
        {
            public int MatchStartedCount { get; private set; }
            public int MatchEndedCount { get; private set; }

            public override void HandleMatchHasStarted()
            {
                MatchStartedCount++;
                throw new System.InvalidOperationException(
                    "Match-start callback failure requested by test.");
            }

            public override void HandleMatchHasEnded()
            {
                MatchEndedCount++;
            }
        }

        private sealed class PendingLoginGameMode : GameMode
        {
            private static UniTaskCompletionSource<PlayerLoginResult> pendingLogin;

            public static bool LoginEntered { get; private set; }

            public static void ResetPendingLogin()
            {
                pendingLogin = new UniTaskCompletionSource<PlayerLoginResult>();
                LoginEntered = false;
            }

            public static void CompletePendingLogin()
            {
                pendingLogin.TrySetResult(PlayerLoginResult.Failure(
                    PlayerLoginStatus.Rejected,
                    "Pending login completed by test."));
            }

            public static void ClearPendingLogin()
            {
                pendingLogin = null;
                LoginEntered = false;
            }

            protected override UniTask<PlayerLoginResult> LoginCoreAsync(
                PlayerLoginRequest request,
                LocalPlayer localPlayer,
                CancellationToken cancellationToken)
            {
                LoginEntered = true;
                return pendingLogin.Task;
            }
        }

        private sealed class ThrowingPostLoginGameMode : GameMode
        {
            protected override void HandleStartingNewPlayer(PlayerController newPlayer)
            {
                throw new System.InvalidOperationException("PostLogin failure requested by test.");
            }
        }

        private sealed class LogoutInPostLoginGameMode : GameMode
        {
            protected override void HandleStartingNewPlayer(PlayerController newPlayer)
            {
                Logout(newPlayer);
            }
        }

        private sealed class ReentrantStopGameMode : GameMode
        {
            public System.Exception ReentrantStopFailure { get; private set; }

            protected override void HandleLogout(PlayerController exiting)
            {
                try
                {
                    World.GameInstance.StopWorldAsync().GetAwaiter().GetResult();
                }
                catch (System.Exception exception)
                {
                    ReentrantStopFailure = exception;
                }
            }
        }

        private sealed class DestroyInsideEndPlayActor : Actor
        {
            public int WorldUnboundCount { get; private set; }

            protected override void EndPlay(EndPlayReason reason)
            {
                Object.DestroyImmediate(gameObject);
            }

            protected override void OnWorldUnbound(EndPlayReason reason)
            {
                WorldUnboundCount++;
            }
        }

        private sealed class ReentrantStopActor : Actor
        {
            public System.Action Callback { get; set; }

            protected override void EndPlay(EndPlayReason reason)
            {
                System.Action callback = Callback;
                Callback = null;
                callback?.Invoke();
            }
        }

        private sealed class CountingBeginPlayActor : Actor
        {
            public int BeginPlayCount { get; private set; }

            public void NotifyEnabledForTest()
            {
                base.OnEnable();
            }

            protected override void BeginPlay()
            {
                BeginPlayCount++;
            }
        }

        private sealed class SelfRemovingCameraMode : CameraMode
        {
            public bool RemovalResult { get; private set; }

            public override void Tick(CameraContext context, float deltaTime)
            {
                RemovalResult = context.RemoveCameraMode(this);
            }

            public override CameraPose Evaluate(
                CameraContext context,
                in CameraPose basePose,
                float deltaTime)
            {
                return basePose;
            }
        }

        private sealed class ReentrantCameraPostProcessor : ICameraPostProcessor
        {
            private readonly CameraManager cameraManager;

            public ReentrantCameraPostProcessor(CameraManager cameraManager)
            {
                this.cameraManager = cameraManager;
            }

            public int ProcessCount { get; private set; }

            public CameraPose Process(CameraPose desiredPose, CameraContext context, float deltaTime)
            {
                ProcessCount++;
                cameraManager.UpdateCamera(deltaTime);
                return desiredPose;
            }
        }

        private sealed class ExceptionRecordingLogWriter : ILogWriter
        {
            public System.Exception LastException { get; private set; }

            public bool IsEnabled(LogSeverity severity, string category)
            {
                return severity >= LogSeverity.Error && severity < LogSeverity.None;
            }

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") { }

            public void Write(
                LogSeverity severity,
                string category,
                System.Action<StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") { }

            public void Write<TState>(
                LogSeverity severity,
                string category,
                TState state,
                System.Action<TState, StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") { }

            public void WriteException(
                LogSeverity severity,
                string category,
                System.Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                if (IsEnabled(severity, category))
                {
                    LastException = exception;
                }
            }
        }

        private sealed class ReentrantCameraOutput : CameraOutputBehaviour
        {
            private Camera activeCamera;

            public bool ReenterOnActivate { get; set; } = true;
            protected override Object OnGetOutputObject() => activeCamera;

            protected override bool OnTryGetResourceSet(
                out CameraOutputResourceSet resources,
                out string error)
            {
                Camera resolvedCamera = GetComponent<Camera>();
                if (resolvedCamera == null)
                {
                    resources = default;
                    error = "A Camera is required.";
                    return false;
                }

                resources = new CameraOutputResourceSet(resolvedCamera);
                error = null;
                return true;
            }

            protected override bool OnActivate(
                CameraManager newOwner,
                in CameraOutputResourceSet resources,
                out string error)
            {
                activeCamera = resources.GetResource(0) as Camera;
                if (ReenterOnActivate)
                {
                    newOwner.SetCameraOutput(null);
                }

                error = null;
                return true;
            }

            protected override void OnApplyPose(in CameraPose pose)
            {
            }

            protected override void OnDeactivate()
            {
                activeCamera = null;
            }
        }
    }
}
