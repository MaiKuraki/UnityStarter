using System.Collections;
using System.Threading;
using CycloneGames.GameplayFramework.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Unity.Cinemachine;
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
        public void ReentrantStopDuringEndPlay_KeepsStoppingWorldOwnedUntilShutdownCompletes()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            ReentrantStopActor actor =
                testWorld.CreateAuthoringActor<ReentrantStopActor>("ReentrantStopActor");
            World world = testWorld.StartWorld();
            World observedDuringCallback = null;
            System.Exception restartException = null;
            actor.Callback = () =>
            {
                testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult();
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
            Assert.IsInstanceOf<System.InvalidOperationException>(restartException);
            Assert.AreEqual(WorldLifecycleState.Disposed, world.LifecycleState);
            Assert.IsNull(testWorld.Instance.CurrentWorld);
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
        public void DestroyCameraManager_RestoresCinemachineBrainUpdateMethod()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            var brainObject = new GameObject("GameplayFrameworkTestBrain");
            try
            {
                brainObject.AddComponent<Camera>();
                CinemachineBrain brain = brainObject.AddComponent<CinemachineBrain>();
                CinemachineBrain.UpdateMethods initialMethod = brain.UpdateMethod;
                CameraManager prefab = testWorld.CreateAuthoringActor<CameraManager>("CameraManagerPrefab");
                CameraManager manager = testWorld.World.SpawnActor(prefab);
                manager.SetBootstrapBrain(brain, rebindImmediately: false);
                manager.InitializeFor(testWorld.World.PlayerControllers[0]);

                Assert.AreEqual(CinemachineBrain.UpdateMethods.ManualUpdate, brain.UpdateMethod);
                Assert.AreEqual(ActorTickPhase.LateUpdate, manager.TickPhase);
                Assert.IsTrue(manager.IsActorTickEnabled());
                Assert.AreEqual(1, testWorld.World.GetTickActorCount(ActorTickPhase.LateUpdate));
                Assert.IsTrue(testWorld.World.DestroyActor(manager));
                Assert.AreEqual(initialMethod, brain.UpdateMethod);
                Assert.Zero(testWorld.World.GetTickActorCount(ActorTickPhase.LateUpdate));
            }
            finally
            {
                Object.DestroyImmediate(brainObject);
            }
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
        public void CameraMode_CannotMutateStackDuringEvaluation()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            PlayerController controller = testWorld.World.PlayerControllers[0];
            CameraManager prefab = testWorld.CreateAuthoringActor<CameraManager>("CameraManagerPrefab");
            CameraManager manager = testWorld.World.SpawnActor(prefab);
            manager.InitializeFor(controller);
            var mode = new SelfRemovingCameraMode();
            Assert.IsTrue(controller.PushCameraMode(mode));

            Assert.DoesNotThrow(() => manager.UpdateCamera(1f / 60f));

            Assert.IsFalse(mode.RemovalResult);
            Assert.AreEqual(1, controller.GetCameraContext().CameraModeCount);
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
        public void InactiveNonOwnedCameraManager_ReleasesBrainWithoutBeginPlay()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create(localPlayerCount: 1);
            CameraManager sceneManager = testWorld.CreateAuthoringActor<CameraManager>("InactiveSceneCameraManager");
            sceneManager.gameObject.SetActive(false);
            var brainObject = new GameObject("InactiveManagerTestBrain");
            try
            {
                brainObject.AddComponent<Camera>();
                CinemachineBrain brain = brainObject.AddComponent<CinemachineBrain>();
                CinemachineBrain.UpdateMethods initialMethod = brain.UpdateMethod;
                sceneManager.SetBootstrapBrain(brain, rebindImmediately: false);
                testWorld.StartWorld();
                sceneManager.InitializeFor(testWorld.World.PlayerControllers[0]);
                Assert.AreEqual(CinemachineBrain.UpdateMethods.ManualUpdate, brain.UpdateMethod);

                testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult();

                Assert.AreEqual(initialMethod, brain.UpdateMethod);
                Assert.IsFalse(sceneManager.IsInitialized);
            }
            finally
            {
                Object.DestroyImmediate(brainObject);
            }
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
                world.SetReference(
                    "gameModeClass",
                    world.CreateAuthoringActor<ThrowingPostLoginGameMode>("ThrowingGameModePrefab"));
                world.CreateAuthoringActor<GameState>("SceneGameState");
            });

            PlayerLoginResult result = testWorld.World.GameMode.LoginAsync(
                    new PlayerLoginRequest(200, "RejectedAfterCommit"))
                .GetAwaiter()
                .GetResult();

            Assert.IsFalse(result.Succeeded);
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
            Assert.IsTrue(gameSession.TrySetSpectatorStatus(first, spectator: true, out _));
            Assert.IsTrue(first.GetPlayerState().IsSpectator());
            Assert.IsFalse(first.GetPlayerState().TryRestoreSnapshot(
                new PlayerStateSnapshot
                {
                    PlayerId = first.GetPlayerState().GetPlayerId(),
                    PlayerName = first.GetPlayerState().GetPlayerName(),
                    IsSpectator = false,
                },
                out _));
            Assert.AreEqual(1, gameSession.PlayerCount);
            Assert.AreEqual(1, gameSession.SpectatorCount);
            Assert.IsTrue(gameSession.UnregisterPlayer(first));
            Assert.IsFalse(first.GetPlayerState().IsIdentityLocked);
            Assert.DoesNotThrow(() => first.GetPlayerState().SetPlayerId(12));
            Assert.IsFalse(gameSession.UnregisterPlayer(first));
            Assert.AreEqual(0, gameSession.SpectatorCount);
        }

        private sealed class RejectAllSession : GameSession
        {
            public override bool ApproveLogin(in PlayerLoginRequest request, out string errorMessage)
            {
                errorMessage = "Rejected by test policy.";
                return false;
            }
        }

        private sealed class TrackingSession : GameSession
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
                Object.DestroyImmediate(gameObject);
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

            public override UniTask<PlayerLoginResult> LoginAsync(
                PlayerLoginRequest request,
                LocalPlayer localPlayer = null,
                CancellationToken cancellationToken = default)
            {
                LoginEntered = true;
                return pendingLogin.Task;
            }
        }

        private sealed class ThrowingPostLoginGameMode : GameMode
        {
            public override void PostLogin(PlayerController newPlayer)
            {
                throw new System.InvalidOperationException("PostLogin failure requested by test.");
            }
        }

        private sealed class LogoutInPostLoginGameMode : GameMode
        {
            public override void PostLogin(PlayerController newPlayer)
            {
                Logout(newPlayer);
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
    }
}
