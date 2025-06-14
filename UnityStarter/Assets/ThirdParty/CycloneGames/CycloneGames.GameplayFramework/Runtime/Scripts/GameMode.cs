using CycloneGames.Logger;
using CycloneGames.Factory.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayFramework
{
    public interface IGameMode
    {
        void LaunchGameMode();
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
        Actor FindPlayerStart(Controller Player, string IncommingName = "")
        {
            PlayerStart[] playerStartArray = GameObject.FindObjectsByType<PlayerStart>(FindObjectsSortMode.InstanceID);

            if (playerStartArray == null || playerStartArray.Length == 0)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} No PlayerStart found in the scene");
                return null;
            }

            if (!string.IsNullOrEmpty(IncommingName))
            {
                foreach (var st in playerStartArray)
                {
                    if (st.GetName() == IncommingName)
                    {
                        Player.SetStartSpot(st);
                        return st;
                    }
                }
            }

            if (playerStartArray.Length > 0)
            {
                var randomStartSpot = playerStartArray[0]; //   Return first one in the list.
                Player.SetStartSpot(randomStartSpot);
                return randomStartSpot;
            }

            return null;
        }
        public void RestartPlayer(PlayerController NewPlayer)
        {
            if (NewPlayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid Player Controller");
                return;
            }

            Actor StartSpot = FindPlayerStart(NewPlayer);
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

            Quaternion SpawnRotation = SpawnTransform.rotation;
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
            Pawn p = objectSpawner?.Create(GetDefaultPawnPrefabForController(NewPlayer)) as Pawn;
            if (p == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to spawn Pawn, please check your spawn pipeline");
                return null;
            }
            if (SpawnTransform == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid target transform, please check your spawn pipeline");
                return null;
            }
            p.transform.position = SpawnTransform.position;
            p.transform.localScale = Vector3.one;
            p.transform.rotation = SpawnTransform.rotation;
            return p;
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

        public virtual void LaunchGameMode()
        {
            CLogger.LogInfo($"{DEBUG_FLAG} Launch GameMode");

            PlayerController PC = SpawnPlayerController();
            RestartPlayer(PC);
        }
    }
}