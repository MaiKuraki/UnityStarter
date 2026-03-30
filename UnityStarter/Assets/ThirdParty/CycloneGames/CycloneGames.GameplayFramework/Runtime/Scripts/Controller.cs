using System;
using UnityEngine;
using CycloneGames.Logger;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.GameplayFramework.Runtime
{
    public class Controller : Actor
    {
        protected IUnityObjectSpawner objectSpawner;
        protected IWorldSettings worldSettings;
        protected bool IsInitialized { get; private set; }

        private Actor startSpot;
        private Pawn pawn;
        private PlayerState playerState;
        private Quaternion controlRotation = Quaternion.identity;

        // Stacked input suppression (increment to suppress, decrement to restore)
        private int ignoreMoveInputCount;
        private int ignoreLookInputCount;

        /// <summary>
        /// Fired when the possessed pawn changes. Args: (OldPawn, NewPawn)
        /// </summary>
        public event Action<Pawn, Pawn> OnPossessedPawnChanged;

        public Pawn GetDefaultPawnPrefab() => worldSettings?.PawnClass;

        public void Initialize(IUnityObjectSpawner objectSpawner, IWorldSettings worldSettings)
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

        public void SetStartSpot(Actor NewStartSpot) => startSpot = NewStartSpot;
        public Actor GetStartSpot() => startSpot;

        #region Pawn
        public Pawn GetPawn() => pawn;
        public T GetPawn<T>() where T : Pawn => pawn as T;

        public void SetPawn(Pawn InPawn)
        {
            Pawn oldPawn = pawn;
            pawn = InPawn;
            if (!ReferenceEquals(oldPawn, InPawn))
            {
                OnPossessedPawnChanged?.Invoke(oldPawn, InPawn);
            }
        }
        #endregion

        #region Possess / UnPossess
        public virtual void Possess(Pawn InPawn)
        {
            if (InPawn == null)
            {
                CLogger.LogError("[Controller] Possess called with null Pawn");
                return;
            }
            OnPossess(InPawn);
        }

        protected virtual void OnPossess(Pawn InPawn)
        {
            bool bNewPawn = !ReferenceEquals(GetPawn(), InPawn);

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
            if (GetPawn() == null) return;
            OnUnPossess();
        }

        protected virtual void OnUnPossess()
        {
            if (GetPawn() != null)
            {
                GetPawn().UnPossessed();
                SetPawn(null);
            }
        }
        #endregion

        #region PlayerState
        public PlayerState GetPlayerState() => playerState;
        public T GetPlayerState<T>() where T : PlayerState => playerState as T;

        protected void InitPlayerState()
        {
            playerState = objectSpawner?.Create(worldSettings?.PlayerStateClass) as PlayerState;
            if (playerState == null)
            {
                CLogger.LogError("[Controller] Spawn PlayerState Failed, check spawn pipeline");
            }
        }
        #endregion

        #region Control Rotation
        public virtual void SetControlRotation(Quaternion NewRotation)
        {
            controlRotation = NewRotation;
        }

        public Quaternion ControlRotation() => controlRotation;
        #endregion

        #region Input Suppression
        /// <summary>
        /// Stacked move input suppression. Each call with true increments; each call with false decrements.
        /// Use ResetIgnoreMoveInput() to clear all stacked suppressions.
        /// </summary>
        public virtual void SetIgnoreMoveInput(bool bNewMoveInput)
        {
            ignoreMoveInputCount = Mathf.Max(0, ignoreMoveInputCount + (bNewMoveInput ? 1 : -1));
        }
        public virtual void ResetIgnoreMoveInput() => ignoreMoveInputCount = 0;
        public virtual bool IsMoveInputIgnored() => ignoreMoveInputCount > 0;

        public virtual void SetIgnoreLookInput(bool bNewLookInput)
        {
            ignoreLookInputCount = Mathf.Max(0, ignoreLookInputCount + (bNewLookInput ? 1 : -1));
        }
        public virtual void ResetIgnoreLookInput() => ignoreLookInputCount = 0;
        public virtual bool IsLookInputIgnored() => ignoreLookInputCount > 0;

        public virtual void ResetIgnoreInputFlags()
        {
            ResetIgnoreMoveInput();
            ResetIgnoreLookInput();
        }
        #endregion

        #region View
        public virtual Actor GetViewTarget()
        {
            return GetPawn() != null ? (Actor)GetPawn() : this;
        }
        #endregion

        #region Movement
        public virtual void StopMovement() { }
        #endregion

        #region Game Flow
        public virtual void GameHasEnded(Actor EndGameFocus = null, bool bIsWinner = false) { }
        public virtual void FailedToSpawnPawn() { }
        #endregion

        protected override void OnDestroy()
        {
            OnPossessedPawnChanged = null;
            base.OnDestroy();
        }
    }
}