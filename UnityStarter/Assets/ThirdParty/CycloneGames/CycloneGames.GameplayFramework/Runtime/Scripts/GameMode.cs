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

        public virtual void Initialize(IUnityObjectSpawner objectSpawner, IWorldSettings worldSettings)
        {
            this.objectSpawner = objectSpawner;
            this.worldSettings = worldSettings;
        }

        void InitNewPlayer(PlayerController NewPlayerController, string Portal = "")
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
        bool UpdatePlayerStartSpot(PlayerController Player, string Portal = "")
        {
            Actor StartSpot = FindPlayerStart(Player, Portal);
            if (StartSpot)
            {
                Quaternion StartRotation =
                    Quaternion.Euler(0, StartSpot.GetYaw(), 0);
                Player.SetInitialLocationAndRotation(StartSpot.transform.position, StartRotation);

                Player.SetStartSpot(StartSpot);

                return true;
            }

            return false;
        }

        /// <summary>
        /// Finds a PlayerStart for the given controller. 
        /// </summary>
        Actor FindPlayerStart(Controller Player, string IncommingName = "")
        {
            var playerStarts = PlayerStart.GetAllPlayerStarts();

            if (playerStarts == null || playerStarts.Count == 0)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} No PlayerStart found in the scene");
                return null;
            }

            if (!string.IsNullOrEmpty(IncommingName))
            {
                for (int i = 0; i < playerStarts.Count; i++)
                {
                    var st = playerStarts[i];
                    if (string.Equals(st.GetName(), IncommingName, System.StringComparison.Ordinal))
                    {
                        Player.SetStartSpot(st);
                        return st;
                    }
                }
            }

            if (playerStarts.Count > 0)
            {
                var randomStartSpot = playerStarts[0]; // Return first one in the list
                Player.SetStartSpot(randomStartSpot);
                return randomStartSpot;
            }

            return null;
        }
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
                CLogger.LogWarning($"{DEBUG_FLAG} Invalid Player Start, player will spawn at Vector3(0, 0, 0)");
                RestartPlayerAtLocation(NewPlayer, Vector3.zero);
                return;
            }

            RestartPlayerAtPlayerStart(NewPlayer, StartSpot);
        }

        void RestartPlayerAtPlayerStart(PlayerController NewPlayer, Actor StartSpot)
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
            if (NewPlayer.GetPawn() != null)
            {
                SpawnRotation = NewPlayer.GetPawn().transform.rotation;
            }
            else if (GetDefaultPawnPrefabForController(NewPlayer))
            {
                Pawn NewPawn = SpawnDefaultPawnAtPlayerStart(NewPlayer, StartSpot);
                if (NewPawn)
                {
                    NewPlayer.SetPawn(NewPawn);
                }
            }

            if (!NewPlayer.GetPawn())
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to restart player at PlayerStart, Invalid Pawn");
            }
            else
            {
                FinishRestartPlayer(NewPlayer, SpawnRotation);
            }
        }

        void RestartPlayerAtTransform(PlayerController NewPlayer, Transform SpawnTransform)
        {
            if (NewPlayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid Player Controller");
                return;
            }

            Quaternion SpawnRotation = SpawnTransform != null ? SpawnTransform.rotation : Quaternion.identity;
            if (NewPlayer.GetPawn() != null)
            {
                SpawnRotation = NewPlayer.GetPawn().transform.rotation;
            }
            else if (GetDefaultPawnPrefabForController(NewPlayer))
            {
                Pawn NewPawn = SpawnDefaultPawnAtTransform(NewPlayer, SpawnTransform);
                if (NewPawn)
                {
                    NewPlayer.SetPawn(NewPawn);
                }
            }

            if (!NewPlayer.GetPawn())
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to restart player at Transform");
            }
            else
            {
                FinishRestartPlayer(NewPlayer, SpawnRotation);
            }
        }

        void RestartPlayerAtLocation(PlayerController NewPlayer, Vector3 NewLocation)
        {
            if (NewPlayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid Player Controller");
                return;
            }

            Quaternion SpawnRotation = Quaternion.identity;
            if (NewPlayer.GetPawn() != null)
            {
                SpawnRotation = NewPlayer.GetPawn().transform.rotation;
            }
            else if (GetDefaultPawnPrefabForController(NewPlayer))
            {
                Pawn NewPawn = SpawnDefaultPawnAtLocation(NewPlayer, NewLocation);
                if (NewPawn)
                {
                    NewPlayer.SetPawn(NewPawn);
                }
            }

            if (!NewPlayer.GetPawn())
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to restart player at Transform");
            }
            else
            {
                FinishRestartPlayer(NewPlayer, SpawnRotation);
            }
        }

        void FinishRestartPlayer(Controller NewPlayer, Quaternion StartRotation)
        {
            NewPlayer.Possess(NewPlayer.GetPawn());

            if (!NewPlayer.GetPawn())
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid Player Pawn");
            }
            else
            {
                Quaternion NewControllerRot = StartRotation;
                NewPlayer.SetControlRotation(NewControllerRot);
            }
        }

        Pawn SpawnDefaultPawnAtPlayerStart(Controller NewPlayer, Actor StartSpot)
        {
            return SpawnDefaultPawnAtTransform(NewPlayer, StartSpot.transform);
        }

        Pawn SpawnDefaultPawnAtTransform(Controller NewPlayer, Transform SpawnTransform)
        {
            if (SpawnTransform == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid target transform, please check your spawn pipeline");
                return null;
            }
            Pawn p = objectSpawner?.Create(GetDefaultPawnPrefabForController(NewPlayer)) as Pawn;
            if (p == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to spawn Pawn, please check your spawn pipeline");
                return null;
            }

            TeleportPawn(p, SpawnTransform.position, SpawnTransform.rotation);
            p.NotifyInitialRotation(SpawnTransform.rotation);
            return p;
        }

        /// <summary>
        /// Teleports a pawn to the specified position and rotation.
        /// Handles different movement systems: CharacterController, Rigidbody, or pure Transform.
        /// </summary>
        protected virtual void TeleportPawn(Pawn pawn, Vector3 position, Quaternion rotation)
        {
            if (pawn == null) return;

            #region Unity Simple Character Controller
            // CharacterController teleport (always available, Unity built-in component)
            var characterController = pawn.GetComponent<CharacterController>();
            if (characterController != null)
            {
                // CharacterController requires disable/enable cycle to sync internal state
                characterController.enabled = false;
                pawn.transform.position = position;
                pawn.transform.localScale = Vector3.one;
                pawn.transform.rotation = rotation;
                characterController.enabled = true;
                return;
            }
            #endregion

            #region Physics (Rigidbody)
            var rigidbody = pawn.GetComponent<Rigidbody>();
            CLogger.LogInfo($"{DEBUG_FLAG} Teleporting {pawn.name} to {position}");
            if (rigidbody != null)
            {
                if (rigidbody.isKinematic)
                {
                    // Kinematic: Set transform directly, then sync with MovePosition
                    pawn.transform.position = position;
                    pawn.transform.localScale = Vector3.one;
                    pawn.transform.rotation = rotation;
                    rigidbody.MovePosition(position);
                    rigidbody.MoveRotation(rotation);
                }
                else
                {
                    // Dynamic: Reset velocities before teleporting
#if UNITY_6000_0_OR_NEWER
                    rigidbody.linearVelocity = Vector3.zero;
                    rigidbody.angularVelocity = Vector3.zero;
#else
                    rigidbody.velocity = Vector3.zero;
                    rigidbody.angularVelocity = Vector3.zero;
#endif
                    pawn.transform.position = position;
                    pawn.transform.localScale = Vector3.one;
                    pawn.transform.rotation = rotation;
                }
                
                Physics.SyncTransforms();
                return;
            }
            #endregion

            // Fallback: Pure Transform teleport (no physics components)
            pawn.transform.position = position;
            pawn.transform.localScale = Vector3.one;
            pawn.transform.rotation = rotation;
        }

        Pawn SpawnDefaultPawnAtLocation(Controller NewPlayer, Vector3 NewLocation)
        {
            Pawn p = objectSpawner?.Create(GetDefaultPawnPrefabForController(NewPlayer)) as Pawn;
            if (p == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to spawn Pawn");
                return null;
            }
            p.transform.SetParent(null);
            p.transform.position = NewLocation;
            p.transform.localScale = Vector3.one;
            p.transform.rotation = Quaternion.identity;
            p.NotifyInitialRotation(Quaternion.identity);

            return p;
        }

        private PlayerController cachedPlayerController;
        PlayerController SpawnPlayerController()
        {
            //  TODO: maybe should not bind in the DI framework, if you are using the DI to implement the IObjectSpawner?
            cachedPlayerController = objectSpawner?.Create(worldSettings?.PlayerControllerClass) as PlayerController;
            if (cachedPlayerController == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Spawn PlayerController Failed, please check your spawn pipeline");
                return null;
            }
            cachedPlayerController.Initialize(objectSpawner, worldSettings);
            return cachedPlayerController;
        }

        public PlayerController GetPlayerController() => cachedPlayerController;

        Pawn GetDefaultPawnPrefabForController(Controller InController)
        {
            return InController.GetDefaultPawnPrefab();
        }

        public virtual async UniTask LaunchGameModeAsync(CancellationToken cancellationToken = default)
        {
            CLogger.LogInfo($"{DEBUG_FLAG} Launch GameMode");

            PlayerController PC = SpawnPlayerController();
            if (!PC)
            {
                return;
            }

            await PC.InitializationTask.AttachExternalCancellation(cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            //  Now PlayerController is fully initialized, we can restart player(spawn pawn and possess it)
            RestartPlayer(PC);
        }
    }
}