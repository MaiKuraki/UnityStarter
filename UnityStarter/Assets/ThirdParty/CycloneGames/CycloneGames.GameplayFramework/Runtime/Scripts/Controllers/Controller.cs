using System;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Non-physical intent source. Possession is an exclusive, main-thread transaction and
    /// does not transfer Pawn lifetime ownership.
    /// </summary>
    public class Controller : Actor
    {
        private Actor startSpot;
        private Pawn pawn;
        private PlayerState playerState;
        private Quaternion controlRotation = Quaternion.identity;
        private int ignoreMoveInputCount;
        private int ignoreLookInputCount;
        private bool isChangingPossession;

        public event Action<Pawn, Pawn> OnPossessedPawnChanged;

        public bool IsInitialized { get; private set; }
        public bool IsChangingPossession => isChangingPossession;
        public virtual bool IsLocalController => false;

        public virtual void Initialize(World targetWorld, PlayerState state = null)
        {
            if (targetWorld == null)
            {
                throw new ArgumentNullException(nameof(targetWorld));
            }

            targetWorld.AssertOwnerThread();

            if (!ReferenceEquals(World, targetWorld))
            {
                throw new InvalidOperationException("Controller must be registered with the target World before initialization.");
            }

            if (IsInitialized)
            {
                if (ReferenceEquals(playerState, state))
                {
                    return;
                }

                throw new InvalidOperationException("Controller is already initialized with a different PlayerState.");
            }

            if (state != null && !ReferenceEquals(state.World, targetWorld))
            {
                throw new InvalidOperationException("PlayerState must belong to the same World as its Controller.");
            }

            playerState = state;
            IsInitialized = true;
        }

        public Pawn GetDefaultPawnPrefab() => World?.Definition.PawnClass;

        public void SetInitialLocationAndRotation(Vector3 newLocation, Quaternion newRotation)
        {
            transform.position = newLocation;
            SetControlRotation(newRotation);
        }

        public void SetStartSpot(Actor newStartSpot) => startSpot = newStartSpot;
        public Actor GetStartSpot() => startSpot;

        #region Pawn and possession
        public Pawn GetPawn() => pawn;
        public T GetPawn<T>() where T : Pawn => pawn as T;

        public virtual void Possess(Pawn newPawn)
        {
            if (!TryPossess(newPawn, out string error))
            {
                throw new InvalidOperationException(error);
            }
        }

        public bool TryPossess(Pawn newPawn, out string error)
        {
            World?.AssertOwnerThread();
            if (!IsInitialized)
            {
                error = "Controller must be initialized before possession.";
                return false;
            }

            if (newPawn == null)
            {
                error = "Pawn is required.";
                return false;
            }

            if (isChangingPossession)
            {
                error = "A possession transaction is already in progress.";
                return false;
            }

            if (!ReferenceEquals(newPawn.World, World))
            {
                error = "Controller and Pawn must belong to the same World.";
                return false;
            }

            if (ReferenceEquals(pawn, newPawn))
            {
                error = null;
                return true;
            }

            Controller previousController = newPawn.Controller;
            if (previousController != null && previousController.isChangingPossession)
            {
                error = "The Pawn's current Controller is changing possession.";
                return false;
            }

            Pawn previousPawn = pawn;
            PlayerState controllerPlayerState = playerState;
            PlayerState previousPawnState = previousPawn?.GetPlayerState();
            PlayerState incomingPawnState = newPawn.GetPlayerState();
            bool possessionLinked = false;

            isChangingPossession = true;
            if (previousController != null)
            {
                previousController.isChangingPossession = true;
            }

            try
            {
                // Commit every relationship before publishing any callback.
                if (previousPawn != null)
                {
                    previousPawn.SetPossessionState(null, null);
                }

                if (previousController != null)
                {
                    previousController.pawn = null;
                }

                pawn = newPawn;
                newPawn.SetPossessionState(this, controllerPlayerState);
                possessionLinked = true;

                Pawn oldPlayerStatePawn = controllerPlayerState?.SetPawnSilently(newPawn);
                Pawn oldIncomingStatePawn = null;
                if (incomingPawnState != null && !ReferenceEquals(incomingPawnState, playerState))
                {
                    oldIncomingStatePawn = incomingPawnState.SetPawnSilently(null);
                }

                if (previousPawnState != null &&
                    !ReferenceEquals(previousPawnState, playerState) &&
                    !ReferenceEquals(previousPawnState, incomingPawnState))
                {
                    previousPawnState.SetPawnSilently(null);
                }

                SetControlRotation(newPawn.GetActorRotation());
                newPawn.DispatchRestart();
                if (!EnsureCommittedPossession(newPawn, controllerPlayerState, out error))
                {
                    return false;
                }

                // Publish only after the bidirectional state is consistent.
                if (previousController != null)
                {
                    previousController.OnUnPossess();
                    if (!EnsureCommittedPossession(newPawn, controllerPlayerState, out error))
                    {
                        return false;
                    }

                    previousController.OnPossessedPawnChanged?.Invoke(newPawn, null);
                    if (!EnsureCommittedPossession(newPawn, controllerPlayerState, out error))
                    {
                        return false;
                    }
                }

                if (previousPawn != null)
                {
                    previousPawn.PublishControllerChanged(this, null);
                    if (!EnsureCommittedPossession(newPawn, controllerPlayerState, out error))
                    {
                        return false;
                    }
                }

                newPawn.PublishControllerChanged(previousController, this);
                if (!EnsureCommittedPossession(newPawn, controllerPlayerState, out error))
                {
                    return false;
                }

                incomingPawnState?.PublishPawnChanged(null, oldIncomingStatePawn);
                if (!EnsureCommittedPossession(newPawn, controllerPlayerState, out error))
                {
                    return false;
                }

                if (controllerPlayerState != null)
                {
                    controllerPlayerState.PublishPawnChanged(newPawn, oldPlayerStatePawn);
                    if (!EnsureCommittedPossession(newPawn, controllerPlayerState, out error))
                    {
                        return false;
                    }
                }

                if (previousPawn != null)
                {
                    OnUnPossess();
                    if (!EnsureCommittedPossession(newPawn, controllerPlayerState, out error))
                    {
                        return false;
                    }
                }

                OnPossess(newPawn);
                if (!EnsureCommittedPossession(newPawn, controllerPlayerState, out error))
                {
                    return false;
                }

                OnPossessedPawnChanged?.Invoke(previousPawn, newPawn);
                if (!EnsureCommittedPossession(newPawn, controllerPlayerState, out error))
                {
                    return false;
                }

                error = null;
                return true;
            }
            finally
            {
                if (possessionLinked && !IsCommittedPossessionValid(newPawn, controllerPlayerState))
                {
                    DetachPossessionWithoutCallbacks(newPawn, controllerPlayerState);
                }

                if (previousController != null)
                {
                    previousController.isChangingPossession = false;
                }

                isChangingPossession = false;
            }
        }

        public virtual void UnPossess()
        {
            World?.AssertOwnerThread();
            Pawn previousPawn = pawn;
            if (previousPawn == null)
            {
                return;
            }

            if (isChangingPossession)
            {
                throw new InvalidOperationException("Cannot unpossess during a possession callback.");
            }

            isChangingPossession = true;
            try
            {
                PlayerState previousState = previousPawn.GetPlayerState();
                pawn = null;
                previousPawn.SetPossessionState(null, null);
                Pawn oldStatePawn = previousState?.SetPawnSilently(null);

                previousPawn.PublishControllerChanged(this, null);
                previousState?.PublishPawnChanged(null, oldStatePawn);
                OnUnPossess();
                OnPossessedPawnChanged?.Invoke(previousPawn, null);
            }
            finally
            {
                isChangingPossession = false;
            }
        }

        internal void DetachDestroyedPawn(Pawn destroyedPawn)
        {
            DetachPossessionWithoutCallbacks(destroyedPawn, playerState);
        }

        private bool EnsureCommittedPossession(
            Pawn expectedPawn,
            PlayerState expectedPlayerState,
            out string error)
        {
            if (IsCommittedPossessionValid(expectedPawn, expectedPlayerState))
            {
                error = null;
                return true;
            }

            DetachPossessionWithoutCallbacks(expectedPawn, expectedPlayerState);
            error = "Possession callback invalidated the committed Controller, Pawn, or PlayerState.";
            return false;
        }

        private bool IsCommittedPossessionValid(Pawn expectedPawn, PlayerState expectedPlayerState)
        {
            if (this == null || expectedPawn == null ||
                !ReferenceEquals(pawn, expectedPawn) ||
                !ReferenceEquals(expectedPawn.Controller, this))
            {
                return false;
            }

            return ReferenceEquals(expectedPlayerState, null) ||
                   expectedPlayerState != null &&
                   ReferenceEquals(expectedPlayerState.GetPawn(), expectedPawn);
        }

        private void DetachPossessionWithoutCallbacks(Pawn expectedPawn, PlayerState expectedPlayerState)
        {
            if (ReferenceEquals(pawn, expectedPawn))
            {
                pawn = null;
            }

            if (!ReferenceEquals(expectedPawn, null) &&
                ReferenceEquals(expectedPawn.Controller, this))
            {
                expectedPawn.SetPossessionState(null, null);
            }

            if (!ReferenceEquals(expectedPlayerState, null) &&
                ReferenceEquals(expectedPlayerState.GetPawn(), expectedPawn))
            {
                expectedPlayerState.SetPawnSilently(null);
            }
        }

        protected virtual void OnPossess(Pawn newPawn) { }
        protected virtual void OnUnPossess() { }
        #endregion

        #region PlayerState
        public PlayerState GetPlayerState() => playerState;
        public T GetPlayerState<T>() where T : PlayerState => playerState as T;
        #endregion

        #region Control rotation and input suppression
        public virtual void SetControlRotation(Quaternion newRotation)
        {
            if (pawn != null)
            {
                Vector3 euler = newRotation.eulerAngles;
                float signedPitch = Mathf.DeltaAngle(0f, euler.x);
                signedPitch = Mathf.Clamp(signedPitch, -pawn.MaxLookUpAngle, pawn.MaxLookDownAngle);
                newRotation = Quaternion.Euler(signedPitch, euler.y, euler.z);
            }

            controlRotation = newRotation;
        }

        public Quaternion ControlRotation() => controlRotation;

        public virtual void SetIgnoreMoveInput(bool ignore)
        {
            ignoreMoveInputCount = Mathf.Max(0, ignoreMoveInputCount + (ignore ? 1 : -1));
        }

        public virtual void ResetIgnoreMoveInput() => ignoreMoveInputCount = 0;
        public virtual bool IsMoveInputIgnored() => ignoreMoveInputCount > 0;

        public virtual void SetIgnoreLookInput(bool ignore)
        {
            ignoreLookInputCount = Mathf.Max(0, ignoreLookInputCount + (ignore ? 1 : -1));
        }

        public virtual void ResetIgnoreLookInput() => ignoreLookInputCount = 0;
        public virtual bool IsLookInputIgnored() => ignoreLookInputCount > 0;

        public virtual void ResetIgnoreInputFlags()
        {
            ResetIgnoreMoveInput();
            ResetIgnoreLookInput();
        }
        #endregion

        #region View and game flow
        public virtual Actor GetViewTarget() => pawn != null ? pawn : this;

        public override void GetActorEyesViewPoint(out Vector3 outLocation, out Quaternion outRotation)
        {
            if (pawn != null)
            {
                pawn.GetActorEyesViewPoint(out outLocation, out outRotation);
                return;
            }

            base.GetActorEyesViewPoint(out outLocation, out outRotation);
        }

        public virtual void StopMovement() { }
        public virtual void GameHasEnded(Actor endGameFocus = null, bool isWinner = false) { }
        public virtual void FailedToSpawnPawn() { }
        #endregion

        protected override void OnWorldUnbound(EndPlayReason reason)
        {
            Pawn possessedPawn = pawn;
            try
            {
                if (!ReferenceEquals(possessedPawn, null))
                {
                    if (!isChangingPossession && possessedPawn != null)
                    {
                        UnPossess();
                    }
                    else
                    {
                        DetachPossessionWithoutCallbacks(possessedPawn, playerState);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                DetachPossessionWithoutCallbacks(possessedPawn, playerState);
            }
            finally
            {
                pawn = null;
                playerState = null;
                startSpot = null;
                ResetIgnoreInputFlags();
                IsInitialized = false;
                base.OnWorldUnbound(reason);
            }
        }

        protected override void OnDestroy()
        {
            Pawn possessedPawn = pawn;
            if (!ReferenceEquals(possessedPawn, null))
            {
                if (!isChangingPossession && possessedPawn != null)
                {
                    try
                    {
                        UnPossess();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, this);
                    }
                }
                else
                {
                    DetachPossessionWithoutCallbacks(possessedPawn, playerState);
                }
            }

            OnPossessedPawnChanged = null;
            IsInitialized = false;
            base.OnDestroy();
            pawn = null;
            playerState = null;
            startSpot = null;
        }
    }
}
