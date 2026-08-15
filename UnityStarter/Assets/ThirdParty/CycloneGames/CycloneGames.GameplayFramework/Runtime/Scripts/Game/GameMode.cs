using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.Logging;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public enum GameModeLifecycleState : byte
    {
        Uninitialized = 0,
        Initialized = 1,
        Starting = 2,
        Running = 3,
        Stopping = 4,
        Stopped = 5,
    }

    /// <summary>
    /// Authoritative world rules and participant orchestration. Client worlds do not create a
    /// GameMode. GameMode does not own global services and all spawned objects are World-owned.
    /// </summary>
    public class GameMode : Actor
    {
        private static readonly LogChannel Log = GameplayFrameworkLog.Channel;
        private const string UnexpectedAdmissionFailureMessage =
            "Player login policy evaluation failed.";
        private const string UnexpectedLoginFailureMessage =
            "Player login failed while preparing participant state.";

        [SerializeField] private bool bStartPlayersAsSpectators;
        [SerializeField] private GameModeConfig gameModeConfig;
        [SerializeField] private GameState gameStateClass;
        [SerializeField, Min(0)] private int maxPlayers = 16;
        [SerializeField, Min(0)] private int maxSpectators = 4;

        private IGameSession gameSession;
        private World terminalWorldOwner;
        private GameModeLifecycleState modeState;
        private bool ownsDefaultSession;
        private bool matchStartNotified;
        private bool isLoginTransactionActive;

        public bool StartPlayersAsSpectators
        {
            get
            {
                AssertActorOwnerThread();
                return bStartPlayersAsSpectators;
            }
            set
            {
                AssertActorOwnerThread();
                bStartPlayersAsSpectators = value;
            }
        }

        public GameModeLifecycleState ModeState
        {
            get
            {
                AssertActorOwnerThread();
                return modeState;
            }
        }

        public IGameSession GetGameSession()
        {
            AssertActorOwnerThread();
            return gameSession;
        }

        public GameModeConfig GetGameModeConfig()
        {
            AssertActorOwnerThread();
            return gameModeConfig;
        }

        public virtual void Initialize(World targetWorld, IGameSession session = null)
        {
            if (targetWorld == null)
            {
                throw new ArgumentNullException(nameof(targetWorld));
            }

            targetWorld.AssertOwnerThread();

            if (!ReferenceEquals(World, targetWorld))
            {
                throw new InvalidOperationException("GameMode must be registered with its World before initialization.");
            }

            if (!targetWorld.IsAuthority)
            {
                throw new InvalidOperationException("GameMode can only exist in an authoritative World.");
            }

            if (modeState != GameModeLifecycleState.Uninitialized)
            {
                throw new InvalidOperationException("GameMode is already initialized.");
            }

            if (maxPlayers < 0 || maxSpectators < 0)
            {
                throw new InvalidOperationException("GameMode capacity cannot be negative.");
            }

            gameSession = session ?? new GameSession(maxPlayers, maxSpectators);
            terminalWorldOwner = targetWorld;
            ownsDefaultSession = session == null;
            targetWorld.BindTerminalGameSession(this, gameSession);
            gameModeConfig?.ApplyTo(this);
            modeState = GameModeLifecycleState.Initialized;
        }

        public virtual void SetGameModeConfig(GameModeConfig config)
        {
            AssertActorOwnerThread();
            gameModeConfig = config;
            config?.ApplyTo(this);
        }

        internal async UniTask StartPlayAsync(
            IReadOnlyList<LocalPlayer> localPlayers,
            CancellationToken cancellationToken)
        {
            if (modeState != GameModeLifecycleState.Initialized)
            {
                throw new InvalidOperationException($"Cannot start GameMode from state '{modeState}'.");
            }

            modeState = GameModeLifecycleState.Starting;
            InitializeGameState();
            SetRequiredMatchState(MatchState.WaitingToStart);

            if (!World.IsDedicatedServer && localPlayers != null)
            {
                for (int i = 0; i < localPlayers.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    LocalPlayer localPlayer = localPlayers[i];
                    PlayerLoginRequest request = CreateLocalPlayerLoginRequest(localPlayer);
                    PlayerLoginResult result = await LoginAsync(request, localPlayer, cancellationToken);
                    await UniTask.SwitchToMainThread();
                    World?.AssertOwnerThread();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!result.Succeeded)
                    {
                        if (result.Status == PlayerLoginStatus.Cancelled)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        throw new InvalidOperationException(
                            $"LocalPlayer {localPlayer.Index} login failed ({result.Status}): {result.Error}");
                    }
                }
            }

            SetRequiredMatchState(MatchState.InProgress);
            modeState = GameModeLifecycleState.Running;
        }

        protected virtual PlayerLoginRequest CreateLocalPlayerLoginRequest(LocalPlayer localPlayer)
        {
            if (localPlayer == null)
            {
                throw new ArgumentNullException(nameof(localPlayer));
            }

            return new PlayerLoginRequest(
                playerId: localPlayer.Index + 1,
                playerName: $"LocalPlayer{localPlayer.Index + 1}",
                isSpectator: bStartPlayersAsSpectators,
                isLocal: true);
        }

        public async UniTask<PlayerLoginResult> LoginAsync(
            PlayerLoginRequest request,
            LocalPlayer localPlayer = null,
            CancellationToken cancellationToken = default)
        {
            AssertActorOwnerThread();
            if (modeState != GameModeLifecycleState.Starting &&
                modeState != GameModeLifecycleState.Running)
            {
                return PlayerLoginResult.Failure(
                    PlayerLoginStatus.WorldNotAcceptingPlayers,
                    $"GameMode is in state '{modeState}'.");
            }

            if (World == null || !World.IsAuthority)
            {
                return PlayerLoginResult.Failure(
                    PlayerLoginStatus.NotAuthoritative,
                    "Only an authoritative World can accept players.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return PlayerLoginResult.Failure(
                    PlayerLoginStatus.Cancelled,
                    "Login was cancelled.");
            }

            if (!request.TryValidate(out string validationError))
            {
                return PlayerLoginResult.Failure(
                    PlayerLoginStatus.InvalidRequest,
                    validationError);
            }

            bool hasLocalPlayer = localPlayer != null;
            if (request.IsLocal != hasLocalPlayer ||
                hasLocalPlayer && !IsOwnedLocalPlayer(localPlayer))
            {
                return PlayerLoginResult.Failure(
                    PlayerLoginStatus.InvalidRequest,
                    "Local login identity does not match the GameInstance LocalPlayer slot.");
            }

            if (isLoginTransactionActive)
            {
                return PlayerLoginResult.Failure(
                    PlayerLoginStatus.Rejected,
                    "A Player login transaction is already active for this GameMode.");
            }

            isLoginTransactionActive = true;
            try
            {
                return await LoginCoreAsync(request, localPlayer, cancellationToken);
            }
            finally
            {
                isLoginTransactionActive = false;
            }
        }

        /// <summary>
        /// Executes the staged participant admission transaction. Override this to provide
        /// asynchronous admission (for example a networked login handshake). The non-virtual
        /// <see cref="LoginAsync"/> wrapper owns the transaction-serialization guard and
        /// releases it only after the returned task completes.
        /// </summary>
        protected virtual UniTask<PlayerLoginResult> LoginCoreAsync(
            PlayerLoginRequest request,
            LocalPlayer localPlayer,
            CancellationToken cancellationToken)
        {
            PlayerController playerController = null;
            PlayerState playerState = null;
            CameraManager cameraManager = null;
            SpectatorPawn spectatorPawn = null;
            Pawn spawnedPawn = null;
            bool transactionCommitted = false;
            bool admissionCompleted = false;
            Exception unexpectedLoginException = null;
            OutOfMemoryException terminalOutOfMemory = null;

            try
            {
                if (!PreLogin(in request, out string admissionError))
                {
                    PlayerLoginStatus status = request.IsSpectator
                        ? gameSession.SpectatorCount >= gameSession.MaxSpectators
                            ? PlayerLoginStatus.AtCapacity
                            : PlayerLoginStatus.Rejected
                        : gameSession.PlayerCount >= gameSession.MaxPlayers
                            ? PlayerLoginStatus.AtCapacity
                            : PlayerLoginStatus.Rejected;

                    return UniTask.FromResult(PlayerLoginResult.Failure(status, admissionError));
                }

                admissionCompleted = true;
                cancellationToken.ThrowIfCancellationRequested();
                playerController = World.SpawnActorDeferred(World.Definition.PlayerControllerClass);
                playerState = World.SpawnActorDeferred(World.Definition.PlayerStateClass);
                playerState.SetPlayerId(request.PlayerId);
                playerState.SetPlayerName(request.PlayerName);
                playerState.SetIsSpectator(request.IsSpectator);

                if (localPlayer != null && World.Definition.CameraManagerClass != null)
                {
                    cameraManager = World.SpawnActorDeferred(World.Definition.CameraManagerClass);
                }

                if (request.IsSpectator && World.Definition.SpectatorPawnClass != null)
                {
                    spectatorPawn = World.SpawnActorDeferred(World.Definition.SpectatorPawnClass);
                }

                playerController.InitializePlayer(
                    World,
                    playerState,
                    localPlayer,
                    cameraManager,
                    spectatorPawn);

                // Reserve the World roster slot before GameSession takes ownership. Once the
                // session commit succeeds, publishing the Controller cannot grow the list.
                World.PreparePlayerControllerCommit(playerController, localPlayer);

                if (!gameSession.TryRegisterPlayer(playerController, request.IsSpectator, out string rosterError))
                {
                    return UniTask.FromResult(PlayerLoginResult.Failure(PlayerLoginStatus.AtCapacity, rosterError));
                }

                World.CommitPlayerController(playerController, localPlayer);

                if (request.IsSpectator)
                {
                    if (spectatorPawn != null)
                    {
                        playerController.Possess(spectatorPawn);
                    }
                }
                else if (!TryRestartPlayer(playerController, string.Empty, out spawnedPawn, out string spawnError))
                {
                    return UniTask.FromResult(PlayerLoginResult.Failure(PlayerLoginStatus.SpawnFailed, spawnError));
                }

                GameState currentGameState = GetGameState();
                if (currentGameState != null)
                {
                    if (!currentGameState.AddPlayerState(playerState))
                    {
                        throw new InvalidOperationException("PlayerState could not be committed to GameState.");
                    }

                }

                World.FinishSpawningActor(playerState);
                if (cameraManager != null) World.FinishSpawningActor(cameraManager);
                if (spectatorPawn != null) World.FinishSpawningActor(spectatorPawn);
                if (spawnedPawn != null) World.FinishSpawningActor(spawnedPawn);
                World.FinishSpawningActor(playerController);
                PostLogin(playerController);
                if (!World.ContainsPlayerController(playerController) ||
                    !World.IsActorRegistered(playerController) ||
                    !gameSession.ContainsPlayer(playerController))
                {
                    return UniTask.FromResult(PlayerLoginResult.Failure(
                        PlayerLoginStatus.Rejected,
                        "PostLogin ended the participant before login completion."));
                }

                transactionCommitted = true;
                return UniTask.FromResult(PlayerLoginResult.Success(playerController));
            }
            catch (OperationCanceledException)
            {
                return UniTask.FromResult(PlayerLoginResult.Failure(
                    PlayerLoginStatus.Cancelled,
                    "Login was cancelled."));
            }
            catch (Exception exception)
            {
                if (TryCaptureTerminalOutOfMemory(ref terminalOutOfMemory, exception))
                {
                    throw;
                }

                unexpectedLoginException = exception;
                return UniTask.FromResult(PlayerLoginResult.Failure(
                    admissionCompleted
                        ? PlayerLoginStatus.SpawnFailed
                        : PlayerLoginStatus.Rejected,
                    admissionCompleted
                        ? UnexpectedLoginFailureMessage
                        : UnexpectedAdmissionFailureMessage));
            }
            finally
            {
                bool rollbackCompleted = transactionCommitted;
                if (!transactionCommitted)
                {
                    try
                    {
                        rollbackCompleted = RollbackLogin(
                            playerController,
                            playerState,
                            cameraManager,
                            spectatorPawn,
                            spawnedPawn);
                    }
                    catch (Exception rollbackException)
                    {
                        LogTerminalException(
                            rollbackException,
                            "Player login rollback encountered an unexpected failure; cleanup cannot continue safely.",
                            ref terminalOutOfMemory);
                    }
                }

                if (unexpectedLoginException != null)
                {
                    string failureContext = admissionCompleted
                        ? rollbackCompleted
                            ? "Player login transaction failed after staged participant state was rolled back."
                            : "Player login transaction failed and participant rollback did not complete."
                        : "Player login policy evaluation failed before participant state was staged.";
                    LogTerminalException(
                        unexpectedLoginException,
                        failureContext,
                        ref terminalOutOfMemory);
                }

                ThrowTerminalOutOfMemory(terminalOutOfMemory);
            }
        }

        protected virtual bool PreLogin(in PlayerLoginRequest request, out string errorMessage)
        {
            return gameSession.ApproveLogin(in request, out errorMessage);
        }

        private bool IsOwnedLocalPlayer(LocalPlayer localPlayer)
        {
            IReadOnlyList<LocalPlayer> localPlayers = World.GameInstance.LocalPlayers;
            int index = localPlayer.Index;
            return index >= 0 &&
                   index < localPlayers.Count &&
                   ReferenceEquals(localPlayers[index], localPlayer);
        }

        public void PostLogin(PlayerController newPlayer)
        {
            AssertActorOwnerThread();
            HandleStartingNewPlayer(newPlayer);
        }

        protected virtual void HandleStartingNewPlayer(PlayerController newPlayer) { }

        public bool Logout(PlayerController exiting)
        {
            AssertActorOwnerThread();
            OutOfMemoryException terminalOutOfMemory = null;
            bool removed = LogoutInternal(
                exiting,
                ReferenceEquals(exiting, null) ? null : exiting.GetPlayerState(),
                destroyController: true,
                ref terminalOutOfMemory);
            ThrowTerminalOutOfMemory(terminalOutOfMemory);
            return removed;
        }

        internal bool HandleDestroyingPlayerController(PlayerController exiting)
        {
            OutOfMemoryException terminalOutOfMemory = null;
            bool removed = LogoutInternal(
                exiting,
                ReferenceEquals(exiting, null) ? null : exiting.GetPlayerState(),
                destroyController: false,
                ref terminalOutOfMemory);
            ThrowTerminalOutOfMemory(terminalOutOfMemory);
            return removed;
        }

        private bool LogoutInternal(
            PlayerController exiting,
            PlayerState playerState,
            bool destroyController,
            ref OutOfMemoryException terminalOutOfMemory)
        {
            World cleanupWorld = terminalWorldOwner;
            if (ReferenceEquals(exiting, null) ||
                cleanupWorld == null ||
                !cleanupWorld.ContainsPlayerController(exiting))
            {
                return false;
            }

            Pawn pawn = exiting.GetPawn();
            CameraManager cameraManager = exiting.GetCameraManager();
            SpectatorPawn spectatorPawn = exiting.GetSpectatorPawn();

            try
            {
                exiting.UnPossess();
            }
            catch (Exception exception)
            {
                LogTerminalException(
                    exception,
                    "PlayerController failed to release possession during logout; cleanup will continue.",
                    ref terminalOutOfMemory);
            }

            if (exiting.GetPawn() != null)
            {
                return false;
            }

            if (!RemoveParticipantState(
                    exiting,
                    playerState,
                    ref terminalOutOfMemory))
            {
                return false;
            }

            try
            {
                HandleLogout(exiting);
            }
            catch (Exception exception)
            {
                LogTerminalException(
                    exception,
                    "GameMode logout extension failed; cleanup will continue.",
                    ref terminalOutOfMemory);
            }

            bool cleanupComplete =
                DestroyIfRegistered(pawn, ref terminalOutOfMemory);
            if (destroyController)
            {
                cleanupComplete &=
                    DestroyIfRegistered(exiting, ref terminalOutOfMemory);
            }
            cleanupComplete &=
                DestroyIfRegistered(playerState, ref terminalOutOfMemory);
            cleanupComplete &=
                DestroyIfRegistered(cameraManager, ref terminalOutOfMemory);
            if (!ReferenceEquals(spectatorPawn, pawn))
            {
                cleanupComplete &=
                    DestroyIfRegistered(spectatorPawn, ref terminalOutOfMemory);
            }

            return cleanupComplete;
        }

        protected virtual void HandleLogout(PlayerController exiting) { }

        internal void HandleExternallyDestroyedPlayerController(PlayerController exiting)
        {
            if (ReferenceEquals(exiting, null))
            {
                return;
            }

            OutOfMemoryException terminalOutOfMemory = null;
            bool cleanupComplete = RemoveParticipantState(
                exiting,
                exiting.GetPlayerState(),
                ref terminalOutOfMemory);
            ThrowTerminalOutOfMemory(terminalOutOfMemory);
            if (!cleanupComplete)
            {
                throw new InvalidOperationException(
                    "Externally destroyed PlayerController cleanup retained participant ownership.");
            }
        }

        internal void HandleExternallyDestroyedPlayerState(
            PlayerController exiting,
            PlayerState destroyedPlayerState)
        {
            if (ReferenceEquals(exiting, null) || ReferenceEquals(destroyedPlayerState, null))
            {
                return;
            }

            OutOfMemoryException terminalOutOfMemory = null;
            bool cleanupComplete = LogoutInternal(
                exiting,
                destroyedPlayerState,
                destroyController: true,
                ref terminalOutOfMemory);
            ThrowTerminalOutOfMemory(terminalOutOfMemory);
            if (!cleanupComplete)
            {
                throw new InvalidOperationException(
                    "Externally destroyed PlayerState cleanup retained participant ownership.");
            }
        }

        public virtual bool RestartPlayer(PlayerController player, string portal = "")
        {
            AssertActorOwnerThread();
            Pawn spawnedPawn = null;
            bool committed = false;
            try
            {
                bool restarted = TryRestartPlayer(player, portal, out spawnedPawn, out _);
                if (!restarted)
                {
                    return false;
                }

                if (spawnedPawn != null)
                {
                    World.FinishSpawningActor(spawnedPawn);
                }

                committed = true;
                return true;
            }
            finally
            {
                if (!committed && spawnedPawn != null)
                {
                    OutOfMemoryException terminalOutOfMemory = null;
                    if (player != null && ReferenceEquals(player.GetPawn(), spawnedPawn))
                    {
                        try
                        {
                            player.UnPossess();
                        }
                        catch (Exception exception)
                        {
                            LogTerminalException(
                                exception,
                                "PlayerController failed to release possession during restart rollback.",
                                ref terminalOutOfMemory);
                        }
                    }

                    DestroyIfRegistered(spawnedPawn, ref terminalOutOfMemory);
                    ThrowTerminalOutOfMemory(terminalOutOfMemory);
                }
            }
        }

        protected virtual PlayerStart FindPlayerStart(Controller player, string incomingName = "")
        {
            IReadOnlyList<PlayerStart> starts = World.PlayerStarts;
            if (!string.IsNullOrEmpty(incomingName))
            {
                for (int i = 0; i < starts.Count; i++)
                {
                    PlayerStart start = starts[i];
                    if (start != null && string.Equals(start.GetName(), incomingName, StringComparison.Ordinal))
                    {
                        player.SetStartSpot(start);
                        return start;
                    }
                }
            }

            PlayerStart chosen = ChoosePlayerStart(player, starts);
            player.SetStartSpot(chosen);
            return chosen;
        }

        protected virtual PlayerStart ChoosePlayerStart(
            Controller player,
            IReadOnlyList<PlayerStart> availableStarts)
        {
            return availableStarts != null && availableStarts.Count > 0 ? availableStarts[0] : null;
        }

        protected virtual Pawn GetDefaultPawnPrefabForController(Controller controller)
        {
            return controller.GetDefaultPawnPrefab();
        }

        protected virtual Pawn SpawnDefaultPawnAtTransform(Controller controller, Transform spawnTransform)
        {
            Pawn prefab = GetDefaultPawnPrefabForController(controller);
            if (prefab == null)
            {
                return null;
            }

            Pawn pawn = World.SpawnActorDeferred(prefab);
            Vector3 position = spawnTransform != null ? spawnTransform.position : Vector3.zero;
            Quaternion rotation = spawnTransform != null ? spawnTransform.rotation : Quaternion.identity;
            TeleportPawn(pawn, position, rotation);
            pawn.NotifyInitialRotation(rotation);
            return pawn;
        }

        protected virtual void TeleportPawn(Pawn pawn, Vector3 position, Quaternion rotation)
        {
            if (pawn == null)
            {
                throw new ArgumentNullException(nameof(pawn));
            }

            CharacterController characterController = pawn.GetComponent<CharacterController>();
            if (characterController != null)
            {
                bool wasEnabled = characterController.enabled;
                if (wasEnabled) characterController.enabled = false;
                pawn.transform.SetPositionAndRotation(position, rotation);
                if (wasEnabled) characterController.enabled = true;
                return;
            }

            Rigidbody rigidbody = pawn.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                if (rigidbody.isKinematic)
                {
                    rigidbody.position = position;
                    rigidbody.rotation = rotation;
                }
                else
                {
#if UNITY_6000_0_OR_NEWER
                    rigidbody.linearVelocity = Vector3.zero;
#else
                    rigidbody.velocity = Vector3.zero;
#endif
                    rigidbody.angularVelocity = Vector3.zero;
                    rigidbody.position = position;
                    rigidbody.rotation = rotation;
                }

                return;
            }

            pawn.transform.SetPositionAndRotation(position, rotation);
        }

        public PlayerController GetPlayerController(int index = 0)
        {
            AssertActorOwnerThread();
            IReadOnlyList<PlayerController> controllers = World?.PlayerControllers;
            return controllers != null && index >= 0 && index < controllers.Count
                ? controllers[index]
                : null;
        }

        internal void NotifyWorldStarted()
        {
            if (modeState != GameModeLifecycleState.Running || matchStartNotified)
            {
                return;
            }

            // Enter the notified state before invoking extension code. A callback may
            // synchronously stop the World or throw after publishing external side effects;
            // both paths must observe a committed start and emit the paired end notification.
            matchStartNotified = true;
            HandleMatchHasStarted();
        }

        protected virtual void HandleMatchHasStarted()
        {
            gameSession?.HandleMatchHasStarted();
        }

        protected virtual void HandleMatchHasEnded()
        {
            gameSession?.HandleMatchHasEnded();
        }

        /// <summary>
        /// Commits non-cancellable World shutdown and destination navigation as one travel
        /// operation. Callers decide whether to begin travel before invoking this method.
        /// </summary>
        public virtual async UniTask TravelToLevel(string levelName)
        {
            AssertActorOwnerThread();
            if (string.IsNullOrWhiteSpace(levelName))
            {
                throw new ArgumentException("Level name is required.", nameof(levelName));
            }

            ISceneTransitionHandler handler = World?.SceneTransitionHandler;
            GameInstance instance = World?.GameInstance;
            if (handler == null || instance == null)
            {
                throw new InvalidOperationException("No scene transition handler is configured.");
            }

            await instance.StopWorldAsync(EndPlayReason.Travel);
            try
            {
                await handler.ChangeScene(levelName, CancellationToken.None);
            }
            finally
            {
                await UniTask.SwitchToMainThread();
            }
        }

        internal UniTask ShutdownAsync(EndPlayReason reason)
        {
            if (modeState == GameModeLifecycleState.Stopped ||
                modeState == GameModeLifecycleState.Uninitialized)
            {
                return UniTask.CompletedTask;
            }

            modeState = GameModeLifecycleState.Stopping;
            OutOfMemoryException terminalOutOfMemory = null;
            World cleanupWorld = terminalWorldOwner;
            while (cleanupWorld != null && cleanupWorld.PlayerControllers.Count > 0)
            {
                int controllerCount = cleanupWorld.PlayerControllers.Count;
                PlayerController controller =
                    cleanupWorld.PlayerControllers[cleanupWorld.PlayerControllers.Count - 1];
                LogoutInternal(
                    controller,
                    controller.GetPlayerState(),
                    destroyController: true,
                    ref terminalOutOfMemory);
                if (cleanupWorld.PlayerControllers.Count >= controllerCount)
                {
                    break;
                }
            }

            if (cleanupWorld == null || cleanupWorld.PlayerControllers.Count == 0)
            {
                FinishShutdown(ref terminalOutOfMemory);
            }
            ThrowTerminalOutOfMemory(terminalOutOfMemory);
            return UniTask.CompletedTask;
        }

        internal void ShutdownImmediate(EndPlayReason reason)
        {
            if (modeState == GameModeLifecycleState.Stopped ||
                modeState == GameModeLifecycleState.Uninitialized)
            {
                return;
            }

            modeState = GameModeLifecycleState.Stopping;
            OutOfMemoryException terminalOutOfMemory = null;
            World cleanupWorld = terminalWorldOwner;
            while (cleanupWorld != null && cleanupWorld.PlayerControllers.Count > 0)
            {
                int controllerCount = cleanupWorld.PlayerControllers.Count;
                PlayerController controller = cleanupWorld.PlayerControllers[controllerCount - 1];
                LogoutInternal(
                    controller,
                    controller.GetPlayerState(),
                    destroyController: true,
                    ref terminalOutOfMemory);
                if (cleanupWorld.PlayerControllers.Count >= controllerCount)
                {
                    break;
                }
            }

            if (cleanupWorld == null || cleanupWorld.PlayerControllers.Count == 0)
            {
                FinishShutdown(ref terminalOutOfMemory);
            }
            ThrowTerminalOutOfMemory(terminalOutOfMemory);
        }

        private void InitializeGameState()
        {
            if (gameStateClass != null)
            {
                GameState state = World.SpawnActor(gameStateClass);
                World.SetGameState(state);
            }
        }

        private void SetRequiredMatchState(MatchState matchState)
        {
            GameState state = GetGameState();
            if (state != null && !state.TrySetMatchState(matchState, out string error))
            {
                throw new InvalidOperationException(error);
            }
        }

        private bool TryRestartPlayer(
            PlayerController player,
            string portal,
            out Pawn spawnedPawn,
            out string error)
        {
            spawnedPawn = null;
            if (player == null || !ReferenceEquals(player.World, World))
            {
                error = "PlayerController must belong to this World.";
                return false;
            }

            if (player.GetPlayerState()?.IsSpectator() == true)
            {
                error = "Spectators do not spawn the default Pawn.";
                return false;
            }

            PlayerStart start = FindPlayerStart(player, portal);
            Pawn pawn = player.GetPawn();
            if (pawn == null)
            {
                pawn = SpawnDefaultPawnAtTransform(player, start != null ? start.transform : null);
                spawnedPawn = pawn;
            }

            if (pawn == null)
            {
                player.FailedToSpawnPawn();
                error = "Default Pawn could not be spawned.";
                return false;
            }

            if (start != null && spawnedPawn == null)
            {
                TeleportPawn(pawn, start.transform.position, start.transform.rotation);
            }

            player.Possess(pawn);
            player.SetControlRotation(start != null ? start.transform.rotation : pawn.GetActorRotation());
            error = null;
            return true;
        }

        private bool RollbackLogin(
            PlayerController playerController,
            PlayerState playerState,
            CameraManager cameraManager,
            SpectatorPawn spectatorPawn,
            Pawn spawnedPawn)
        {
            OutOfMemoryException terminalOutOfMemory = null;
            if (!ReferenceEquals(playerController, null) && playerController.GetPawn() != null)
            {
                try
                {
                    playerController.UnPossess();
                }
                catch (Exception exception)
                {
                    LogTerminalException(
                        exception,
                        "PlayerController failed to release possession during login rollback.",
                        ref terminalOutOfMemory);
                }

                if (playerController.GetPawn() != null)
                {
                    RetainParticipantCleanupOwner(
                        playerController,
                        ref terminalOutOfMemory);
                    ThrowTerminalOutOfMemory(terminalOutOfMemory);
                    return false;
                }
            }

            if (!RemoveParticipantState(
                    playerController,
                    playerState,
                    ref terminalOutOfMemory))
            {
                RetainParticipantCleanupOwner(
                    playerController,
                    ref terminalOutOfMemory);
                ThrowTerminalOutOfMemory(terminalOutOfMemory);
                return false;
            }

            DestroyIfRegistered(spawnedPawn, ref terminalOutOfMemory);
            DestroyIfRegistered(playerController, ref terminalOutOfMemory);
            DestroyIfRegistered(playerState, ref terminalOutOfMemory);
            DestroyIfRegistered(cameraManager, ref terminalOutOfMemory);
            DestroyIfRegistered(spectatorPawn, ref terminalOutOfMemory);

            bool cleanupComplete =
                (ReferenceEquals(playerController, null) || !terminalWorldOwner.IsActorRegistered(playerController)) &&
                (ReferenceEquals(playerState, null) || !terminalWorldOwner.IsActorRegistered(playerState)) &&
                (ReferenceEquals(cameraManager, null) || !terminalWorldOwner.IsActorRegistered(cameraManager)) &&
                (ReferenceEquals(spectatorPawn, null) || !terminalWorldOwner.IsActorRegistered(spectatorPawn)) &&
                (ReferenceEquals(spawnedPawn, null) || !terminalWorldOwner.IsActorRegistered(spawnedPawn));
            if (!cleanupComplete)
            {
                RetainParticipantCleanupOwner(
                    playerController,
                    ref terminalOutOfMemory);
            }

            ThrowTerminalOutOfMemory(terminalOutOfMemory);
            return cleanupComplete;
        }

        private void RetainParticipantCleanupOwner(
            PlayerController playerController,
            ref OutOfMemoryException terminalOutOfMemory)
        {
            if (ReferenceEquals(playerController, null) ||
                terminalWorldOwner == null ||
                !terminalWorldOwner.IsActorRegistered(playerController) ||
                terminalWorldOwner.ContainsPlayerController(playerController))
            {
                return;
            }

            try
            {
                terminalWorldOwner.CommitPlayerController(playerController, localPlayer: null);
            }
            catch (Exception exception)
            {
                LogTerminalException(
                    exception,
                    "World failed to retain an incomplete login participant for cleanup retry.",
                    ref terminalOutOfMemory);
            }
        }

        private void DestroyIfRegistered(Actor actor)
        {
            OutOfMemoryException terminalOutOfMemory = null;
            DestroyIfRegistered(actor, ref terminalOutOfMemory);
            ThrowTerminalOutOfMemory(terminalOutOfMemory);
        }

        private bool DestroyIfRegistered(
            Actor actor,
            ref OutOfMemoryException terminalOutOfMemory)
        {
            World cleanupWorld = terminalWorldOwner;
            if (ReferenceEquals(actor, null) ||
                cleanupWorld == null ||
                !cleanupWorld.IsActorRegistered(actor))
            {
                return true;
            }

            try
            {
                return cleanupWorld.DestroyActor(actor) || !cleanupWorld.IsActorRegistered(actor);
            }
            catch (Exception exception)
            {
                LogTerminalException(
                    exception,
                    "World failed to destroy a registered Actor during participant cleanup.",
                    ref terminalOutOfMemory);
                return !cleanupWorld.IsActorRegistered(actor);
            }
        }

        private bool RemoveParticipantState(
            PlayerController playerController,
            PlayerState playerState,
            ref OutOfMemoryException terminalOutOfMemory)
        {
            try
            {
                World cleanupWorld = terminalWorldOwner;
                return cleanupWorld != null &&
                       cleanupWorld.TryReleaseParticipantOwnership(
                           playerController,
                           playerState,
                           gameSession);
            }
            catch (Exception exception)
            {
                LogTerminalException(
                    exception,
                    "Participant session, GameState, or World roster cleanup failed during logout.",
                    ref terminalOutOfMemory);
                return false;
            }
        }

        private void FinishShutdown(ref OutOfMemoryException terminalOutOfMemory)
        {
            try
            {
                if (matchStartNotified)
                {
                    matchStartNotified = false;
                    GetGameState()?.TrySetMatchState(MatchState.WaitingPostMatch, out _);
                    HandleMatchHasEnded();
                }
            }
            catch (Exception exception)
            {
                LogTerminalException(
                    exception,
                    "GameMode match-end notification failed; shutdown will continue.",
                    ref terminalOutOfMemory);
            }
            finally
            {
                modeState = GameModeLifecycleState.Stopped;
                terminalWorldOwner = null;
                if (ownsDefaultSession)
                {
                    gameSession = null;
                }
            }
        }

        private static void LogTerminalException(
            Exception exception,
            string message,
            ref OutOfMemoryException terminalOutOfMemory)
        {
            if (TryCaptureTerminalOutOfMemory(ref terminalOutOfMemory, exception))
            {
                return;
            }

            try
            {
                Log.Error(exception, message);
            }
            catch (Exception loggingException)
            {
                TryCaptureTerminalOutOfMemory(
                    ref terminalOutOfMemory,
                    loggingException);
            }
        }

        private static bool TryCaptureTerminalOutOfMemory(
            ref OutOfMemoryException terminalOutOfMemory,
            Exception exception)
        {
            OutOfMemoryException captured = FindOutOfMemory(exception);
            if (captured == null)
            {
                return false;
            }

            if (terminalOutOfMemory == null)
            {
                terminalOutOfMemory = captured;
            }

            return true;
        }

        private static OutOfMemoryException FindOutOfMemory(Exception exception)
        {
            if (exception is OutOfMemoryException outOfMemoryException)
            {
                return outOfMemoryException;
            }

            if (exception is AggregateException aggregateException)
            {
                for (int index = 0; index < aggregateException.InnerExceptions.Count; index++)
                {
                    OutOfMemoryException nested = FindOutOfMemory(
                        aggregateException.InnerExceptions[index]);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private static void ThrowTerminalOutOfMemory(
            OutOfMemoryException terminalOutOfMemory)
        {
            if (terminalOutOfMemory != null)
            {
                throw terminalOutOfMemory;
            }
        }

        protected override void OnDestroy()
        {
            World destructionWorld = terminalWorldOwner;
            bool destructionStageEntered = false;
            OutOfMemoryException terminalOutOfMemory = null;
            try
            {
                if (destructionWorld != null)
                {
                    destructionWorld.EnterGameModeDestructionStage(this);
                    destructionStageEntered = true;
                }

                if (modeState != GameModeLifecycleState.Stopped &&
                    modeState != GameModeLifecycleState.Uninitialized)
                {
                    try
                    {
                        ShutdownImmediate(EndPlayReason.Destroyed);
                    }
                    catch (Exception exception)
                    {
                        LogTerminalException(
                            exception,
                            "GameMode immediate shutdown failed during destruction.",
                            ref terminalOutOfMemory);
                    }
                }

                Exception baseFailure = null;
                try
                {
                    base.OnDestroy();
                }
                catch (Exception exception)
                {
                    if (!TryCaptureTerminalOutOfMemory(
                            ref terminalOutOfMemory,
                            exception))
                    {
                        baseFailure = exception;
                    }
                }

                ThrowTerminalOutOfMemory(terminalOutOfMemory);
                if (baseFailure != null)
                {
                    throw baseFailure;
                }
            }
            finally
            {
                if (destructionStageEntered)
                {
                    destructionWorld.ExitGameModeDestructionStage(this);
                }
            }
        }
    }
}
