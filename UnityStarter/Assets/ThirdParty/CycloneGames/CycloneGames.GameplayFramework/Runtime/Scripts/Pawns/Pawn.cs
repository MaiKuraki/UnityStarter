using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// A physical world representation that can be exclusively possessed by a Controller.
    /// Pawn does not assume humanoid movement, a physics backend, or a network prediction model.
    /// </summary>
    public class Pawn : Actor
    {
        [SerializeField] private PawnConfig pawnConfig;

        private PlayerState playerState;
        private Controller controller;
        private Vector3 pendingMovementInput;
        private Vector3 lastMovementInput;
        private bool useControllerRotationPitch;
        private bool useControllerRotationYaw;
        private bool useControllerRotationRoll;
        private bool isTurnedOff;
        private float baseEyeHeight = 0.8f;
        private float maxLookUpAngle = 89f;
        private float maxLookDownAngle = 89f;
        private readonly List<MonoBehaviour> cachedComponents = new List<MonoBehaviour>(16);

        public Controller Controller
        {
            get
            {
                AssertActorOwnerThread();
                return controller;
            }
        }
        public bool UseControllerRotationPitch
        {
            get
            {
                AssertActorOwnerThread();
                return useControllerRotationPitch;
            }
            set
            {
                AssertActorOwnerThread();
                useControllerRotationPitch = value;
            }
        }

        public bool UseControllerRotationYaw
        {
            get
            {
                AssertActorOwnerThread();
                return useControllerRotationYaw;
            }
            set
            {
                AssertActorOwnerThread();
                useControllerRotationYaw = value;
            }
        }

        public bool UseControllerRotationRoll
        {
            get
            {
                AssertActorOwnerThread();
                return useControllerRotationRoll;
            }
            set
            {
                AssertActorOwnerThread();
                useControllerRotationRoll = value;
            }
        }

        public float BaseEyeHeight
        {
            get
            {
                AssertActorOwnerThread();
                return baseEyeHeight;
            }
            set
            {
                AssertActorOwnerThread();
                ValidateFinite(value, nameof(value));
                baseEyeHeight = value;
            }
        }

        public float MaxLookUpAngle
        {
            get
            {
                AssertActorOwnerThread();
                return maxLookUpAngle;
            }
            set
            {
                AssertActorOwnerThread();
                ValidateLookAngle(value, nameof(value));
                maxLookUpAngle = value;
            }
        }

        public float MaxLookDownAngle
        {
            get
            {
                AssertActorOwnerThread();
                return maxLookDownAngle;
            }
            set
            {
                AssertActorOwnerThread();
                ValidateLookAngle(value, nameof(value));
                maxLookDownAngle = value;
            }
        }

        public virtual void SetPawnConfig(PawnConfig config)
        {
            AssertActorOwnerThread();
            if (config == null)
            {
                throw new System.ArgumentNullException(nameof(config));
            }

            config.ValidateOrThrow();
            pawnConfig = config;
            ApplyPawnConfig(config);
        }

        public PawnConfig GetPawnConfig()
        {
            AssertActorOwnerThread();
            return pawnConfig;
        }

        #region Movement input
        public virtual void AddMovementInput(
            Vector3 worldDirection,
            float scaleValue = 1f,
            bool force = false)
        {
            AssertActorOwnerThread();
            if (isTurnedOff && !force)
            {
                return;
            }

            if (controller != null && controller.IsMoveInputIgnored() && !force)
            {
                return;
            }

            if (!IsFinite(worldDirection) || float.IsNaN(scaleValue) || float.IsInfinity(scaleValue))
            {
                return;
            }

            pendingMovementInput = Vector3.ClampMagnitude(
                pendingMovementInput + worldDirection * scaleValue,
                1f);
        }

        public Vector3 GetPendingMovementInputVector()
        {
            AssertActorOwnerThread();
            return pendingMovementInput;
        }
        public Vector3 GetLastMovementInputVector()
        {
            AssertActorOwnerThread();
            return lastMovementInput;
        }

        public Vector3 ConsumeMovementInputVector()
        {
            AssertActorOwnerThread();
            lastMovementInput = pendingMovementInput;
            pendingMovementInput = Vector3.zero;
            return lastMovementInput;
        }
        #endregion

        #region Initialization and restart
        public void NotifyInitialRotation(Quaternion rotation)
        {
            AssertActorOwnerThread();
            cachedComponents.Clear();
            GetComponents(cachedComponents);
            for (int i = 0; i < cachedComponents.Count; i++)
            {
                if (cachedComponents[i] is IInitialRotationSettable settable)
                {
                    settable.SetInitialRotation(rotation, immediate: true);
                }
            }
        }

        public void DispatchRestart()
        {
            AssertActorOwnerThread();
            Restart();
        }

        protected virtual void Restart()
        {
            ConsumeMovementInputVector();
        }
        #endregion

        #region Possession
        internal void SetPossessionState(Controller newController, PlayerState newPlayerState)
        {
            controller = newController;
            playerState = newPlayerState;
        }

        internal void PublishControllerChanged(Controller oldController, Controller newController)
        {
            NotifyControllerChanged(oldController, newController);
        }

        protected virtual void NotifyControllerChanged(Controller oldController, Controller newController) { }

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

        #region Control and view
        public Quaternion GetControlRotation()
        {
            AssertActorOwnerThread();
            return controller != null ? controller.ControlRotation() : Quaternion.identity;
        }

        public virtual Quaternion GetViewRotation()
        {
            AssertActorOwnerThread();
            return controller != null ? controller.ControlRotation() : GetActorRotation();
        }

        public virtual Quaternion GetBaseAimRotation()
        {
            AssertActorOwnerThread();
            return GetViewRotation();
        }

        public virtual Vector3 GetPawnViewLocation()
        {
            AssertActorOwnerThread();
            return GetActorLocation() + Vector3.up * baseEyeHeight;
        }

        public override void GetActorEyesViewPoint(out Vector3 outLocation, out Quaternion outRotation)
        {
            AssertActorOwnerThread();
            outLocation = GetPawnViewLocation();
            outRotation = GetViewRotation();
        }

        /// <summary>
        /// Applies the configured controller-rotation axes. Movement or character adapters call
        /// this from their own tick; base Pawn does not add an empty per-object Update callback.
        /// </summary>
        public virtual void ApplyControllerRotation(float deltaTime)
        {
            AssertActorOwnerThread();
            if (isTurnedOff || controller == null)
            {
                return;
            }

            FaceRotation(controller.ControlRotation(), deltaTime);
        }

        public virtual void FaceRotation(Quaternion newControlRotation, float deltaTime = 0f)
        {
            AssertActorOwnerThread();
            Vector3 euler = newControlRotation.eulerAngles;
            Vector3 current = transform.eulerAngles;

            if (useControllerRotationPitch) current.x = euler.x;
            if (useControllerRotationYaw) current.y = euler.y;
            if (useControllerRotationRoll) current.z = euler.z;

            if (useControllerRotationPitch || useControllerRotationYaw || useControllerRotationRoll)
            {
                SetActorRotation(Quaternion.Euler(current));
            }
        }
        #endregion

        #region State
        public bool IsPawnControlled()
        {
            AssertActorOwnerThread();
            return controller != null;
        }

        public bool IsPlayerControlled()
        {
            AssertActorOwnerThread();
            return controller is PlayerController;
        }

        public bool IsBotControlled()
        {
            AssertActorOwnerThread();
            return controller is AIController;
        }

        public virtual bool IsLocallyControlled()
        {
            AssertActorOwnerThread();
            return controller != null && controller.IsLocalController;
        }

        public bool IsTurnedOff()
        {
            AssertActorOwnerThread();
            return isTurnedOff;
        }

        public virtual void TurnOff()
        {
            AssertActorOwnerThread();
            isTurnedOff = true;
        }

        public virtual void TurnOn()
        {
            AssertActorOwnerThread();
            isTurnedOff = false;
        }

        public virtual void DetachFromControllerPendingDestroy()
        {
            AssertActorOwnerThread();
            controller?.UnPossess();
        }
        #endregion

        protected override void OnDestroy()
        {
            var terminalExceptions = new TerminalExceptionAccumulator();
            Controller previousController = controller;
            if (!ReferenceEquals(previousController, null))
            {
                try
                {
                    if (previousController != null && !previousController.IsChangingPossession)
                    {
                        previousController.UnPossess();
                    }
                    else
                    {
                        previousController.DetachDestroyedPawn(this);
                    }
                }
                catch (System.Exception exception)
                {
                    terminalExceptions.HandleAndLog(
                        exception,
                        "Pawn failed to release its Controller during destruction; fallback detachment will run.");
                    try
                    {
                        previousController.DetachDestroyedPawn(this);
                    }
                    catch (System.Exception fallbackException)
                    {
                        terminalExceptions.HandleAndLog(
                            fallbackException,
                            "Pawn fallback Controller detachment failed during destruction.");
                    }
                }
            }

            cachedComponents.Clear();
            controller = null;
            playerState = null;
            try
            {
                base.OnDestroy();
            }
            catch (System.Exception exception)
            {
                terminalExceptions.HandleAndLog(
                    exception,
                    "Pawn base Actor cleanup failed during destruction.");
            }

            terminalExceptions.ThrowIfCaptured();
        }

        protected override void Awake()
        {
            base.Awake();
            if (pawnConfig != null)
            {
                pawnConfig.ValidateOrThrow();
                ApplyPawnConfig(pawnConfig);
            }
        }

        private void ApplyPawnConfig(PawnConfig config)
        {
            useControllerRotationPitch = config.UseControllerRotationPitch;
            useControllerRotationYaw = config.UseControllerRotationYaw;
            useControllerRotationRoll = config.UseControllerRotationRoll;
            baseEyeHeight = config.BaseEyeHeight;
            maxLookUpAngle = config.MaxLookUpAngle;
            maxLookDownAngle = config.MaxLookDownAngle;
        }

        private static void ValidateFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new System.ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "Pawn configuration values must be finite.");
            }
        }

        private static void ValidateLookAngle(float value, string parameterName)
        {
            ValidateFinite(value, parameterName);
            if (value < 0f || value > 180f)
            {
                throw new System.ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "Pawn look angles must be between 0 and 180 degrees.");
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
