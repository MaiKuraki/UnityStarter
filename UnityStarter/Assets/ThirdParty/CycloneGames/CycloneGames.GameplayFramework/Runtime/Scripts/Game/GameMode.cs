using System;
using System.Collections.Generic;
using System.Threading;
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
        [SerializeField] private bool bStartPlayersAsSpectators;
        [SerializeField] private GameModeConfig gameModeConfig;
        [SerializeField] private GameState gameStateClass;
        [SerializeField, Min(0)] private int maxPlayers = 16;
        [SerializeField, Min(0)] private int maxSpectators = 4;

        private IGameSession gameSession;
        private GameModeLifecycleState modeState;
        private bool ownsDefaultSession;
        private bool matchStartNotified;

        public bool StartPlayersAsSpectators
        {
            get => bStartPlayersAsSpectators;
            set => bStartPlayersAsSpectators = value;
        }

        public GameModeLifecycleState ModeState => modeState;
        public IGameSession GetGameSession() => gameSession;
        public GameModeConfig GetGameModeConfig() => gameModeConfig;
        public GameState GetGameState() => World?.GameState;

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
            ownsDefaultSession = session == null;
            gameModeConfig?.ApplyTo(this);
            modeState = GameModeLifecycleState.Initialized;
        }

        public virtual void SetGameModeConfig(GameModeConfig config)
        {
            World?.AssertOwnerThread();
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
            SetRequiredMatchState(GameState.EMatchState.WaitingToStart);

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

            SetRequiredMatchState(GameState.EMatchState.InProgress);
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

        public virtual UniTask<PlayerLoginResult> LoginAsync(
            PlayerLoginRequest request,
            LocalPlayer localPlayer = null,
            CancellationToken cancellationToken = default)
        {
            World?.AssertOwnerThread();
            if (modeState != GameModeLifecycleState.Starting &&
                modeState != GameModeLifecycleState.Running)
            {
                return UniTask.FromResult(PlayerLoginResult.Failure(
                    PlayerLoginStatus.WorldNotAcceptingPlayers,
                    $"GameMode is in state '{modeState}'."));
            }

            if (World == null || !World.IsAuthority)
            {
                return UniTask.FromResult(PlayerLoginResult.Failure(
                    PlayerLoginStatus.NotAuthoritative,
                    "Only an authoritative World can accept players."));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return UniTask.FromResult(PlayerLoginResult.Failure(
                    PlayerLoginStatus.Cancelled,
                    "Login was cancelled."));
            }

            if (!request.TryValidate(out string validationError))
            {
                return UniTask.FromResult(PlayerLoginResult.Failure(
                    PlayerLoginStatus.InvalidRequest,
                    validationError));
            }

            bool hasLocalPlayer = localPlayer != null;
            if (request.IsLocal != hasLocalPlayer ||
                hasLocalPlayer && !IsOwnedLocalPlayer(localPlayer))
            {
                return UniTask.FromResult(PlayerLoginResult.Failure(
                    PlayerLoginStatus.InvalidRequest,
                    "Local login identity does not match the GameInstance LocalPlayer slot."));
            }

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

            PlayerController playerController = null;
            PlayerState playerState = null;
            CameraManager cameraManager = null;
            SpectatorPawn spectatorPawn = null;
            Pawn spawnedPawn = null;
            bool sessionRegistered = false;
            bool worldCommitted = false;
            bool gameStateCommitted = false;
            bool transactionCommitted = false;

            try
            {
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

                if (!gameSession.TryRegisterPlayer(playerController, request.IsSpectator, out string rosterError))
                {
                    return UniTask.FromResult(PlayerLoginResult.Failure(PlayerLoginStatus.AtCapacity, rosterError));
                }

                sessionRegistered = true;
                World.CommitPlayerController(playerController, localPlayer);
                worldCommitted = true;

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

                    gameStateCommitted = true;
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
                return UniTask.FromResult(PlayerLoginResult.Failure(
                    PlayerLoginStatus.SpawnFailed,
                    exception.Message));
            }
            finally
            {
                if (!transactionCommitted)
                {
                    RollbackLogin(
                        playerController,
                        playerState,
                        cameraManager,
                        spectatorPawn,
                        spawnedPawn,
                        sessionRegistered,
                        worldCommitted,
                        gameStateCommitted);
                }
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

        public virtual void PostLogin(PlayerController newPlayer)
        {
            HandleStartingNewPlayer(newPlayer);
        }

        protected virtual void HandleStartingNewPlayer(PlayerController newPlayer) { }

        public bool Logout(PlayerController exiting)
        {
            World?.AssertOwnerThread();
            return LogoutInternal(exiting, destroyController: true);
        }

        internal bool HandleDestroyingPlayerController(PlayerController exiting)
        {
            return LogoutInternal(exiting, destroyController: false);
        }

        private bool LogoutInternal(PlayerController exiting, bool destroyController)
        {
            if (ReferenceEquals(exiting, null) || World == null || !World.ContainsPlayerController(exiting))
            {
                return false;
            }

            Pawn pawn = exiting.GetPawn();
            PlayerState playerState = exiting.GetPlayerState();
            CameraManager cameraManager = exiting.GetCameraManager();
            SpectatorPawn spectatorPawn = exiting.GetSpectatorPawn();

            try
            {
                exiting.UnPossess();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, exiting);
            }

            RemoveParticipantState(exiting, playerState);

            try
            {
                HandleLogout(exiting);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, exiting);
            }

            DestroyIfRegistered(pawn);
            if (destroyController)
            {
                DestroyIfRegistered(exiting);
            }
            DestroyIfRegistered(playerState);
            DestroyIfRegistered(cameraManager);
            if (!ReferenceEquals(spectatorPawn, pawn))
            {
                DestroyIfRegistered(spectatorPawn);
            }

            return true;
        }

        protected virtual void HandleLogout(PlayerController exiting) { }

        internal void HandleExternallyDestroyedPlayerController(PlayerController exiting)
        {
            if (ReferenceEquals(exiting, null))
            {
                return;
            }

            RemoveParticipantState(exiting, exiting.GetPlayerState());
        }

        public virtual bool RestartPlayer(PlayerController player, string portal = "")
        {
            World?.AssertOwnerThread();
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
                    if (player != null && ReferenceEquals(player.GetPawn(), spawnedPawn))
                    {
                        try
                        {
                            player.UnPossess();
                        }
                        catch (Exception exception)
                        {
                            Debug.LogException(exception, player);
                        }
                    }

                    DestroyIfRegistered(spawnedPawn);
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

            HandleMatchHasStarted();
            matchStartNotified = true;
        }

        protected virtual void HandleMatchHasStarted()
        {
            gameSession?.HandleMatchHasStarted();
        }

        protected virtual void HandleMatchHasEnded()
        {
            gameSession?.HandleMatchHasEnded();
        }

        public virtual async UniTask TravelToLevel(
            string levelName,
            CancellationToken cancellationToken = default)
        {
            World?.AssertOwnerThread();
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

            await instance.StopWorldAsync(EndPlayReason.Travel, cancellationToken);
            try
            {
                await handler.ChangeScene(levelName, cancellationToken);
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
            while (World != null && World.PlayerControllers.Count > 0)
            {
                PlayerController controller = World.PlayerControllers[World.PlayerControllers.Count - 1];
                Logout(controller);
            }

            FinishShutdown();
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
            while (World != null && World.PlayerControllers.Count > 0)
            {
                Logout(World.PlayerControllers[World.PlayerControllers.Count - 1]);
            }

            FinishShutdown();
        }

        private void InitializeGameState()
        {
            GameState state = null;
            if (gameStateClass != null)
            {
                state = World.SpawnActor(gameStateClass);
            }
            else
            {
                World.TryGetActor(out state);
            }

            if (state != null)
            {
                World.SetGameState(state);
            }
        }

        private void SetRequiredMatchState(GameState.EMatchState matchState)
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

        private void RollbackLogin(
            PlayerController playerController,
            PlayerState playerState,
            CameraManager cameraManager,
            SpectatorPawn spectatorPawn,
            Pawn spawnedPawn,
            bool sessionRegistered,
            bool worldCommitted,
            bool gameStateCommitted)
        {
            if (playerController != null && playerController.GetPawn() != null)
            {
                try
                {
                    playerController.UnPossess();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, playerController);
                }
            }

            if (sessionRegistered)
            {
                try
                {
                    gameSession?.UnregisterPlayer(playerController);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, playerController);
                }
            }

            if (worldCommitted)
            {
                try
                {
                    World.RemovePlayerController(playerController);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, playerController);
                }
            }

            if (gameStateCommitted)
            {
                try
                {
                    GetGameState()?.RemovePlayerState(playerState);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, playerState);
                }
            }

            DestroyIfRegistered(spawnedPawn);
            DestroyIfRegistered(playerController);
            DestroyIfRegistered(playerState);
            DestroyIfRegistered(cameraManager);
            DestroyIfRegistered(spectatorPawn);
        }

        private void DestroyIfRegistered(Actor actor)
        {
            if (actor != null && World != null && World.IsActorRegistered(actor))
            {
                try
                {
                    World.DestroyActor(actor);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, actor);
                }
            }
        }

        private void RemoveParticipantState(PlayerController playerController, PlayerState playerState)
        {
            try
            {
                gameSession?.UnregisterPlayer(playerController);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, playerController);
            }

            try
            {
                GetGameState()?.RemovePlayerState(playerState);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, playerState);
            }

            World?.RemovePlayerController(playerController);
        }

        private void FinishShutdown()
        {
            try
            {
                if (matchStartNotified)
                {
                    matchStartNotified = false;
                    GetGameState()?.TrySetMatchState(GameState.EMatchState.WaitingPostMatch, out _);
                    HandleMatchHasEnded();
                }
            }
            finally
            {
                modeState = GameModeLifecycleState.Stopped;
                if (ownsDefaultSession)
                {
                    gameSession = null;
                }
            }
        }

        protected override void OnDestroy()
        {
            if (modeState != GameModeLifecycleState.Stopped &&
                modeState != GameModeLifecycleState.Uninitialized)
            {
                try
                {
                    ShutdownImmediate(EndPlayReason.Destroyed);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }

            base.OnDestroy();
        }
    }
}
