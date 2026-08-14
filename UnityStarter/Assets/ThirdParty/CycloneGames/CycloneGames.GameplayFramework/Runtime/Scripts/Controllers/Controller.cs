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
        private bool isInitialized;
        private Action<Pawn, Pawn> possessedPawnChangedObservers;

        public event Action<Pawn, Pawn> OnPossessedPawnChanged
        {
            add
            {
                AssertActorOwnerThread();
                possessedPawnChangedObservers += value;
            }
            remove
            {
                AssertActorOwnerThread();
                possessedPawnChangedObservers -= value;
            }
        }

        public bool IsInitialized
        {
            get
            {
                AssertActorOwnerThread();
                return isInitialized;
            }
            private set => isInitialized = value;
        }

        public bool IsChangingPossession
        {
            get
            {
                AssertActorOwnerThread();
                return isChangingPossession;
            }
        }

        public virtual bool IsLocalController
        {
            get
            {
                AssertActorOwnerThread();
                return false;
            }
        }

        public virtual void Initialize(World targetWorld, PlayerState state = null)
        {
            AssertActorOwnerThread();
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

        public Pawn GetDefaultPawnPrefab()
        {
            AssertActorOwnerThread();
            return World?.Definition.PawnClass;
        }

        public void SetInitialLocationAndRotation(Vector3 newLocation, Quaternion newRotation)
        {
            AssertActorOwnerThread();
            transform.position = newLocation;
            SetControlRotation(newRotation);
        }

        public void SetStartSpot(Actor newStartSpot)
        {
            AssertActorOwnerThread();
            startSpot = newStartSpot;
        }

        public Actor GetStartSpot()
        {
            AssertActorOwnerThread();
            return startSpot;
        }

        #region Pawn and possession
        public Pawn GetPawn()
        {
            AssertActorOwnerThread();
            return pawn;
        }

        public T GetPawn<T>() where T : Pawn
        {
            AssertActorOwnerThread();
            return pawn as T;
        }

        public void Possess(Pawn newPawn)
        {
            AssertActorOwnerThread();
            if (!TryPossess(newPawn, out string error))
            {
                throw new InvalidOperationException(error);
            }
        }

        public bool TryPossess(Pawn newPawn, out string error)
        {
            AssertActorOwnerThread();
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

                    previousController.possessedPawnChangedObservers?.Invoke(newPawn, null);
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

                possessedPawnChangedObservers?.Invoke(previousPawn, newPawn);
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

        public void UnPossess()
        {
            AssertActorOwnerThread();
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
                possessedPawnChangedObservers?.Invoke(previousPawn, null);
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
        public PlayerState GetPlayerState()
        {
            AssertActorOwnerThread();
            return playerState;
        }

        public T GetPlayerState<T>() where T : PlayerState
        {
            AssertActorOwnerThread();
            return playerState as T;
        }
        #endregion

        #region Control rotation and input suppression
        public virtual void SetControlRotation(Quaternion newRotation)
        {
            AssertActorOwnerThread();
            if (pawn != null)
            {
                Vector3 euler = newRotation.eulerAngles;
                float signedPitch = Mathf.DeltaAngle(0f, euler.x);
                signedPitch = Mathf.Clamp(signedPitch, -pawn.MaxLookUpAngle, pawn.MaxLookDownAngle);
                newRotation = Quaternion.Euler(signedPitch, euler.y, euler.z);
            }

            controlRotation = newRotation;
        }

        public Quaternion ControlRotation()
        {
            AssertActorOwnerThread();
            return controlRotation;
        }

        public virtual void SetIgnoreMoveInput(bool ignore)
        {
            AssertActorOwnerThread();
            ignoreMoveInputCount = Mathf.Max(0, ignoreMoveInputCount + (ignore ? 1 : -1));
        }

        public virtual void ResetIgnoreMoveInput()
        {
            AssertActorOwnerThread();
            ignoreMoveInputCount = 0;
        }

        public virtual bool IsMoveInputIgnored()
        {
            AssertActorOwnerThread();
            return ignoreMoveInputCount > 0;
        }

        public virtual void SetIgnoreLookInput(bool ignore)
        {
            AssertActorOwnerThread();
            ignoreLookInputCount = Mathf.Max(0, ignoreLookInputCount + (ignore ? 1 : -1));
        }

        public virtual void ResetIgnoreLookInput()
        {
            AssertActorOwnerThread();
            ignoreLookInputCount = 0;
        }

        public virtual bool IsLookInputIgnored()
        {
            AssertActorOwnerThread();
            return ignoreLookInputCount > 0;
        }

        public virtual void ResetIgnoreInputFlags()
        {
            AssertActorOwnerThread();
            ResetIgnoreMoveInput();
            ResetIgnoreLookInput();
        }
        #endregion

        #region View and game flow
        public virtual Actor GetViewTarget()
        {
            AssertActorOwnerThread();
            return pawn != null ? pawn : this;
        }

        public override void GetActorEyesViewPoint(out Vector3 outLocation, out Quaternion outRotation)
        {
            AssertActorOwnerThread();
            if (pawn != null)
            {
                pawn.GetActorEyesViewPoint(out outLocation, out outRotation);
                return;
            }

            base.GetActorEyesViewPoint(out outLocation, out outRotation);
        }

        public virtual void StopMovement()
        {
            AssertActorOwnerThread();
        }

        public virtual void GameHasEnded(Actor endGameFocus = null, bool isWinner = false)
        {
            AssertActorOwnerThread();
        }

        public virtual void FailedToSpawnPawn()
        {
            AssertActorOwnerThread();
        }
        #endregion

        protected override void OnWorldUnbound(EndPlayReason reason)
        {
            var terminalExceptions = new TerminalExceptionAccumulator();
            ReleasePossessionForTerminal(
                "Controller failed to release possession while unbinding from its World; fallback detachment will run.",
                ref terminalExceptions);
            ResetControllerRuntimeState();
            try
            {
                base.OnWorldUnbound(reason);
            }
            catch (Exception exception)
            {
                terminalExceptions.HandleAndLog(
                    exception,
                    "Controller base Actor cleanup failed while unbinding from its World.");
            }

            terminalExceptions.ThrowIfCaptured();
        }

        protected override void OnDestroy()
        {
            var terminalExceptions = new TerminalExceptionAccumulator();
            ReleasePossessionForTerminal(
                "Controller failed to release possession during destruction; fallback detachment will run.",
                ref terminalExceptions);
            possessedPawnChangedObservers = null;
            ResetControllerRuntimeState();
            try
            {
                base.OnDestroy();
            }
            catch (Exception exception)
            {
                terminalExceptions.HandleAndLog(
                    exception,
                    "Controller base Actor cleanup failed during destruction.");
            }

            terminalExceptions.ThrowIfCaptured();
        }

        private void ReleasePossessionForTerminal(
            string failureDescription,
            ref TerminalExceptionAccumulator terminalExceptions)
        {
            Pawn possessedPawn = pawn;
            if (ReferenceEquals(possessedPawn, null))
            {
                return;
            }

            bool attemptedUnPossess = false;
            try
            {
                if (!isChangingPossession && possessedPawn != null)
                {
                    attemptedUnPossess = true;
                    UnPossess();
                }
                else
                {
                    DetachPossessionWithoutCallbacks(possessedPawn, playerState);
                }
            }
            catch (Exception exception)
            {
                terminalExceptions.HandleAndLog(exception, failureDescription);
                if (!attemptedUnPossess)
                {
                    return;
                }

                try
                {
                    DetachPossessionWithoutCallbacks(possessedPawn, playerState);
                }
                catch (Exception fallbackException)
                {
                    terminalExceptions.HandleAndLog(
                        fallbackException,
                        "Controller fallback possession detachment failed during terminal cleanup.");
                }
            }
        }

        private void ResetControllerRuntimeState()
        {
            pawn = null;
            playerState = null;
            startSpot = null;
            ignoreMoveInputCount = 0;
            ignoreLookInputCount = 0;
            isChangingPossession = false;
            IsInitialized = false;
        }
    }
}
