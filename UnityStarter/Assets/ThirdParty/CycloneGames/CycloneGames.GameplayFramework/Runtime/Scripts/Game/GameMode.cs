using System.Threading;
using CycloneGames.Logger;
using CycloneGames.Factory.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public interface IGameMode
    {
        UniTask LaunchGameModeAsync(CancellationToken cancellationToken = default);
    }

    public class GameMode : Actor, IGameMode
    {
        private const string DEBUG_FLAG = "<color=cyan>[GameMode]</color>";
        private IUnityObjectSpawner objectSpawner;
        private IWorldSettings worldSettings;
        private IGameSession gameSession;
        private ISceneTransitionHandler sceneTransitionHandler;

        [SerializeField] private bool bStartPlayersAsSpectators;
        [SerializeField] private GameModeConfig gameModeConfig;
        
        public bool StartPlayersAsSpectators { get => bStartPlayersAsSpectators; set => bStartPlayersAsSpectators = value; }

        public IGameSession GetGameSession() => gameSession;
        public void SetGameSession(IGameSession session) => gameSession = session;

        public ISceneTransitionHandler GetSceneTransitionHandler() => sceneTransitionHandler;
        public void SetSceneTransitionHandler(ISceneTransitionHandler handler) => sceneTransitionHandler = handler;

        public virtual void Initialize(IUnityObjectSpawner objectSpawner, IWorldSettings worldSettings)
        {
            this.objectSpawner = objectSpawner;
            this.worldSettings = worldSettings;
        }

        /// <summary>
        /// Set game mode configuration from a ScriptableObject asset.
        /// Can be called during initialization to configure game rules.
        /// </summary>
        public virtual void SetGameModeConfig(GameModeConfig config)
        {
            if (config != null)
            {
                gameModeConfig = config;
                config.ApplyTo(this);
            }
        }

        public GameModeConfig GetGameModeConfig() => gameModeConfig;

        #region Player Start Management
        protected virtual void InitNewPlayer(PlayerController NewPlayerController, string Portal = "")
        {
            if (NewPlayerController == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid PlayerController");
                return;
            }

            if (NewPlayerController.GetPlayerState() == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid PlayerState");
                return;
            }

            UpdatePlayerStartSpot(NewPlayerController, Portal);
        }

        protected virtual bool UpdatePlayerStartSpot(PlayerController Player, string Portal = "")
        {
            Actor StartSpot = FindPlayerStart(Player, Portal);
            if (StartSpot != null)
            {
                Quaternion StartRotation = Quaternion.Euler(0, StartSpot.GetYaw(), 0);
                Player.SetInitialLocationAndRotation(StartSpot.transform.position, StartRotation);
                Player.SetStartSpot(StartSpot);
                return true;
            }
            return false;
        }

        protected virtual Actor FindPlayerStart(Controller Player, string IncomingName = "")
        {
            var playerStarts = PlayerStart.GetAllPlayerStarts();

            if (playerStarts == null || playerStarts.Count == 0)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} No PlayerStart found in the scene");
                return null;
            }

            if (!string.IsNullOrEmpty(IncomingName))
            {
                for (int i = 0; i < playerStarts.Count; i++)
                {
                    if (string.Equals(playerStarts[i].GetName(), IncomingName, System.StringComparison.Ordinal))
                    {
                        Player.SetStartSpot(playerStarts[i]);
                        return playerStarts[i];
                    }
                }
            }

            Actor chosen = ChoosePlayerStart(Player);
            if (chosen != null)
            {
                Player.SetStartSpot(chosen);
            }
            return chosen;
        }

        /// <summary>
        /// Override to implement custom player start selection logic (random, round-robin, team-based, etc.)
        /// </summary>
        protected virtual Actor ChoosePlayerStart(Controller Player)
        {
            var playerStarts = PlayerStart.GetAllPlayerStarts();
            return playerStarts.Count > 0 ? playerStarts[0] : null;
        }

        protected virtual bool ShouldSpawnAtStartSpot(PlayerController Player)
        {
            return Player.GetStartSpot() != null;
        }
        #endregion

        #region Restart Player
        public virtual void RestartPlayer(PlayerController NewPlayer, string Portal = "")
        {
            if (NewPlayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid Player Controller");
                return;
            }

            Actor StartSpot = FindPlayerStart(NewPlayer, Portal);
            if (StartSpot == null)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} No PlayerStart found, spawning at origin");
                RestartPlayerAtLocation(NewPlayer, Vector3.zero);
                return;
            }

            RestartPlayerAtPlayerStart(NewPlayer, StartSpot);
        }

        protected virtual void RestartPlayerAtPlayerStart(PlayerController NewPlayer, Actor StartSpot)
        {
            if (NewPlayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid Player Controller");
                return;
            }
            if (StartSpot == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid Player Start");
                return;
            }

            Quaternion SpawnRotation = StartSpot.transform.rotation;
            Pawn pawnToPossess = NewPlayer.GetPawn();
            if (NewPlayer.GetPawn() != null)
            {
                SpawnRotation = NewPlayer.GetPawn().transform.rotation;
            }
            else if (GetDefaultPawnPrefabForController(NewPlayer) != null)
            {
                Pawn NewPawn = SpawnDefaultPawnAtPlayerStart(NewPlayer, StartSpot);
                if (NewPawn != null)
                {
                    pawnToPossess = NewPawn;
                }
            }

            if (pawnToPossess == null)
            {
                NewPlayer.FailedToSpawnPawn();
                CLogger.LogError($"{DEBUG_FLAG} Failed to restart player at PlayerStart");
            }
            else
            {
                FinishRestartPlayer(NewPlayer, pawnToPossess, SpawnRotation);
            }
        }

        protected virtual void RestartPlayerAtTransform(PlayerController NewPlayer, Transform SpawnTransform)
        {
            if (NewPlayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid Player Controller");
                return;
            }

            Quaternion SpawnRotation = SpawnTransform != null ? SpawnTransform.rotation : Quaternion.identity;
            Pawn pawnToPossess = NewPlayer.GetPawn();
            if (NewPlayer.GetPawn() != null)
            {
                SpawnRotation = NewPlayer.GetPawn().transform.rotation;
            }
            else if (GetDefaultPawnPrefabForController(NewPlayer) != null)
            {
                Pawn NewPawn = SpawnDefaultPawnAtTransform(NewPlayer, SpawnTransform);
                if (NewPawn != null)
                {
                    pawnToPossess = NewPawn;
                }
            }

            if (pawnToPossess == null)
            {
                NewPlayer.FailedToSpawnPawn();
                CLogger.LogError($"{DEBUG_FLAG} Failed to restart player at Transform");
            }
            else
            {
                FinishRestartPlayer(NewPlayer, pawnToPossess, SpawnRotation);
            }
        }

        protected virtual void RestartPlayerAtLocation(PlayerController NewPlayer, Vector3 NewLocation)
        {
            if (NewPlayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid Player Controller");
                return;
            }

            Quaternion SpawnRotation = Quaternion.identity;
            Pawn pawnToPossess = NewPlayer.GetPawn();
            if (NewPlayer.GetPawn() != null)
            {
                SpawnRotation = NewPlayer.GetPawn().transform.rotation;
            }
            else if (GetDefaultPawnPrefabForController(NewPlayer) != null)
            {
                Pawn NewPawn = SpawnDefaultPawnAtLocation(NewPlayer, NewLocation);
                if (NewPawn != null)
                {
                    pawnToPossess = NewPawn;
                }
            }

            if (pawnToPossess == null)
            {
                NewPlayer.FailedToSpawnPawn();
                CLogger.LogError($"{DEBUG_FLAG} Failed to restart player at Location");
            }
            else
            {
                FinishRestartPlayer(NewPlayer, pawnToPossess, SpawnRotation);
            }
        }

        protected virtual void FinishRestartPlayer(Controller NewPlayer, Pawn PawnToPossess, Quaternion StartRotation)
        {
            NewPlayer.Possess(PawnToPossess);

            if (NewPlayer.GetPawn() == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid Player Pawn after possess");
            }
            else
            {
                NewPlayer.SetControlRotation(StartRotation);
            }
        }
        #endregion

        #region Spawn Pawn
        protected virtual Pawn SpawnDefaultPawnAtPlayerStart(Controller NewPlayer, Actor StartSpot)
        {
            return SpawnDefaultPawnAtTransform(NewPlayer, StartSpot.transform);
        }

        protected virtual Pawn SpawnDefaultPawnAtTransform(Controller NewPlayer, Transform SpawnTransform)
        {
            if (SpawnTransform == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid target transform");
                return null;
            }

            Pawn p = objectSpawner?.Create(GetDefaultPawnPrefabForController(NewPlayer)) as Pawn;
            if (p == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to spawn Pawn, check spawn pipeline");
                return null;
            }

            TeleportPawn(p, SpawnTransform.position, SpawnTransform.rotation);
            p.NotifyInitialRotation(SpawnTransform.rotation);
            return p;
        }

        /// <summary>
        /// Teleports a pawn to the specified position and rotation.
        /// Handles CharacterController, Rigidbody, or pure Transform movement systems.
        /// </summary>
        protected virtual void TeleportPawn(Pawn pawn, Vector3 position, Quaternion rotation)
        {
            if (pawn == null) return;

            var characterController = pawn.GetComponent<CharacterController>();
            if (characterController != null)
            {
                // CharacterController requires disable/enable cycle to sync internal state
                characterController.enabled = false;
                pawn.transform.SetPositionAndRotation(position, rotation);
                pawn.transform.localScale = Vector3.one;
                characterController.enabled = true;
                return;
            }

            var rigidbody = pawn.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                if (rigidbody.isKinematic)
                {
                    pawn.transform.SetPositionAndRotation(position, rotation);
                    pawn.transform.localScale = Vector3.one;
                    rigidbody.MovePosition(position);
                    rigidbody.MoveRotation(rotation);
                }
                else
                {
#if UNITY_6000_0_OR_NEWER
                    rigidbody.linearVelocity = Vector3.zero;
#else
                    rigidbody.velocity = Vector3.zero;
#endif
                    rigidbody.angularVelocity = Vector3.zero;
                    pawn.transform.SetPositionAndRotation(position, rotation);
                    pawn.transform.localScale = Vector3.one;
                }
                Physics.SyncTransforms();
                return;
            }

            pawn.transform.SetPositionAndRotation(position, rotation);
            pawn.transform.localScale = Vector3.one;
        }

        protected virtual Pawn SpawnDefaultPawnAtLocation(Controller NewPlayer, Vector3 NewLocation)
        {
            Pawn p = objectSpawner?.Create(GetDefaultPawnPrefabForController(NewPlayer)) as Pawn;
            if (p == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to spawn Pawn");
                return null;
            }
            p.transform.SetParent(null);
            p.transform.SetPositionAndRotation(NewLocation, Quaternion.identity);
            p.transform.localScale = Vector3.one;
            p.NotifyInitialRotation(Quaternion.identity);
            return p;
        }
        #endregion

        #region Pawn Class
        /// <summary>
        /// Override to return a different pawn prefab per controller (e.g., team-based or class-based selection).
        /// </summary>
        protected virtual Pawn GetDefaultPawnPrefabForController(Controller InController)
        {
            return InController.GetDefaultPawnPrefab();
        }
        #endregion

        #region Player Controller
        private PlayerController cachedPlayerController;

        protected virtual PlayerController SpawnPlayerController()
        {
            cachedPlayerController = objectSpawner?.Create(worldSettings?.PlayerControllerClass) as PlayerController;
            if (cachedPlayerController == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Spawn PlayerController Failed, check spawn pipeline");
                return null;
            }
            cachedPlayerController.Initialize(objectSpawner, worldSettings);
            cachedPlayerController.InitializeRuntimeComponents();
            return cachedPlayerController;
        }

        public PlayerController GetPlayerController() => cachedPlayerController;
        #endregion

        #region Match Lifecycle
        /// <summary>
        /// Called when a new player is being set up. Override for custom player initialization.
        /// </summary>
        protected virtual void HandleStartingNewPlayer(PlayerController NewPlayer) { }

        protected virtual void HandleMatchHasStarted()
        {
            gameSession?.HandleMatchHasStarted();
        }

        protected virtual void HandleMatchHasEnded()
        {
            gameSession?.HandleMatchHasEnded();
        }
        #endregion

        #region Login / Logout
        /// <summary>
        /// Validates whether a player should be allowed to join before creating their PlayerController.
        /// Delegates to IGameSession.ApproveLogin if a session is set.
        /// Override for custom validation (whitelist, matchmaking tickets, etc.)
        /// </summary>
        protected virtual bool PreLogin(string options, string address, out string errorMessage)
        {
            if (gameSession != null && !gameSession.ApproveLogin(options, address, out errorMessage))
            {
                return false;
            }
            errorMessage = null;
            return true;
        }

        /// <summary>
        /// Called after a PlayerController is fully initialized and ready for gameplay.
        /// Registers the player with the session and invokes HandleStartingNewPlayer.
        /// </summary>
        public virtual void PostLogin(PlayerController NewPlayer)
        {
            HandleStartingNewPlayer(NewPlayer);
            gameSession?.RegisterPlayer(NewPlayer);
        }

        /// <summary>
        /// Called when a player leaves the game (disconnect, quit, kicked).
        /// Unregisters the player from the session.
        /// </summary>
        public virtual void Logout(Controller Exiting)
        {
            if (Exiting is PlayerController pc)
            {
                gameSession?.UnregisterPlayer(pc);
            }
        }
        #endregion

        #region Level Transition
        /// <summary>
        /// Travel to a new level by replacing the current navigation history entry.
        /// Performs game-side cleanup then delegates scene navigation to ISceneTransitionHandler.
        /// 
        /// NOTE: Do NOT call LaunchGameModeAsync after this returns —
        /// the new scene's ISceneEntryPoint is responsible for bootstrapping its own GameMode.
        /// </summary>
        public virtual async UniTask TravelToLevel(string levelName, CancellationToken cancellationToken = default)
        {
            CLogger.LogInfo($"{DEBUG_FLAG} Traveling to level: {levelName}");

            if (sceneTransitionHandler == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} SceneTransitionHandler not set. Cannot travel to level.");
                return;
            }

            // Game-side cleanup before navigation
            await EndGameAsync(cancellationToken);

            // Delegate to scene navigation system (e.g. Navigathena)
            // Change resets navigation history — typical for level-to-level travel
            await sceneTransitionHandler.ChangeScene(levelName, cancellationToken);
        }

        /// <summary>
        /// Override to customize game-side cleanup before a scene transition.
        /// Default implementation is a no-op yield.
        /// </summary>
        protected virtual async UniTask EndGameAsync(CancellationToken cancellationToken = default)
        {
            CLogger.LogInfo($"{DEBUG_FLAG} Ending game");
            await UniTask.Yield(cancellationToken);
        }
        #endregion

        #region Launch
        public virtual UniTask LaunchGameModeAsync(CancellationToken cancellationToken = default)
        {
            CLogger.LogInfo($"{DEBUG_FLAG} Launch GameMode");

            if (cancellationToken.IsCancellationRequested) return UniTask.CompletedTask;

            PlayerController PC = SpawnPlayerController();
            if (PC == null) return UniTask.CompletedTask;

            if (cancellationToken.IsCancellationRequested) return UniTask.CompletedTask;

            PostLogin(PC);

            HandleMatchHasStarted();

            RestartPlayer(PC);
            return UniTask.CompletedTask;
        }
        #endregion
    }
}