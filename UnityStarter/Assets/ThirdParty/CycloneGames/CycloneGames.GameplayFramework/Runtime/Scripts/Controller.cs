using CycloneGames.Logger;
using UnityEngine;
using CycloneGames.Factory;

namespace CycloneGames.GameplayFramework
{
    public class Controller : Actor
    {
        protected IUnityObjectSpawner objectSpawner;
        protected IWorldSettings worldSettings;
        protected bool IsInitialized { get; private set; } = false;
        private Actor StartSpot;
        private Pawn pawn;
        private PlayerState playerState;
        private Quaternion controlRotation = Quaternion.identity;

        public Pawn GetDefaultPawnPrefab() => worldSettings.PawnClass;
        public void Initialize(in IUnityObjectSpawner objectSpawner, in IWorldSettings worldSettings)
        {
            this.objectSpawner = objectSpawner;
            this.worldSettings = worldSettings;
            IsInitialized = true;
        }

        public void SetInitialLocationAndRotation(Vector3 NewLocation, Quaternion NewRotation)
        {
            transform.position = NewLocation;
            SetControlRotation(NewRotation);
        }

        public void SetStartSpot(Actor NewStartSpot)
        {
            StartSpot = NewStartSpot;
        }

        public Pawn GetPawn()
        {
            return pawn;
        }

        public void SetPawn(Pawn InPawn)
        {
            pawn = InPawn;
        }

        public virtual void Possess(Pawn InPawn)
        {
            OnPossess(InPawn);
        }

        public virtual void OnPossess(Pawn InPawn)
        {
            bool bNewPawn = GetPawn() != InPawn;

            if (bNewPawn && GetPawn() != null)
            {
                UnPossess();
            }

            if (InPawn.Controller != null)
            {
                InPawn.Controller.UnPossess();
            }

            InPawn.PossessedBy(this);
            SetPawn(InPawn);

            SetControlRotation(GetPawn().GetActorRotation());
            GetPawn().DispatchRestart();
        }

        public virtual void UnPossess()
        {
            if (!GetPawn())
            {
                return;
            }

            OnUnPossess();
        }

        public virtual void OnUnPossess()
        {
            if (GetPawn())
            {
                GetPawn().UnPossessed();
                SetPawn(null);
            }
        }

        public PlayerState GetPlayerState()
        {
            return playerState;
        }

        public T GetPlayerState<T>() where T : PlayerState
        {
            return playerState is T ps ? ps : null;
        }

        protected void InitPlayerState()
        {
            playerState = objectSpawner?.Create(worldSettings?.PlayerStateClass) as PlayerState;
            if (playerState == null)
            {
                CLogger.LogError("Spawn PlayerState Failed, please check your spawn pipeline");
            }
        }

        public void SetControlRotation(Quaternion NewRotation)
        {
            if (!controlRotation.Equals(NewRotation))
            {
                controlRotation = NewRotation;
            }
        }

        public Quaternion ControlRotation() => controlRotation;
    }
}