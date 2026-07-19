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
        [SerializeField] private bool bUseControllerRotationPitch;
        [SerializeField] private bool bUseControllerRotationYaw;
        [SerializeField] private bool bUseControllerRotationRoll;
        [SerializeField] private float baseEyeHeight = 0.8f;
        [SerializeField] private PawnConfig pawnConfig;

        private PlayerState playerState;
        private Controller controller;
        private Vector3 pendingMovementInput;
        private Vector3 lastMovementInput;
        private bool bIsTurnedOff;
        private float maxLookUpAngle = 89f;
        private float maxLookDownAngle = 89f;
        private readonly List<MonoBehaviour> cachedComponents = new List<MonoBehaviour>(16);

        public Controller Controller => controller;
        public bool UseControllerRotationPitch { get => bUseControllerRotationPitch; set => bUseControllerRotationPitch = value; }
        public bool UseControllerRotationYaw { get => bUseControllerRotationYaw; set => bUseControllerRotationYaw = value; }
        public bool UseControllerRotationRoll { get => bUseControllerRotationRoll; set => bUseControllerRotationRoll = value; }
        public float BaseEyeHeight { get => baseEyeHeight; set => baseEyeHeight = value; }
        public float MaxLookUpAngle { get => maxLookUpAngle; set => maxLookUpAngle = Mathf.Clamp(value, 0f, 180f); }
        public float MaxLookDownAngle { get => maxLookDownAngle; set => maxLookDownAngle = Mathf.Clamp(value, 0f, 180f); }

        public virtual void SetPawnConfig(PawnConfig config)
        {
            if (config == null)
            {
                return;
            }

            pawnConfig = config;
            config.ApplyTo(this);
        }

        public PawnConfig GetPawnConfig() => pawnConfig;

        #region Movement input
        public virtual void AddMovementInput(
            Vector3 worldDirection,
            float scaleValue = 1f,
            bool force = false)
        {
            if (bIsTurnedOff && !force)
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

        public Vector3 GetPendingMovementInputVector() => pendingMovementInput;
        public Vector3 GetLastMovementInputVector() => lastMovementInput;

        public Vector3 ConsumeMovementInputVector()
        {
            lastMovementInput = pendingMovementInput;
            pendingMovementInput = Vector3.zero;
            return lastMovementInput;
        }
        #endregion

        #region Initialization and restart
        public void NotifyInitialRotation(Quaternion rotation)
        {
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

        public PlayerState GetPlayerState() => playerState;
        public T GetPlayerState<T>() where T : PlayerState => playerState as T;
        #endregion

        #region Control and view
        public Quaternion GetControlRotation()
        {
            return controller != null ? controller.ControlRotation() : Quaternion.identity;
        }

        public virtual Quaternion GetViewRotation()
        {
            return controller != null ? controller.ControlRotation() : GetActorRotation();
        }

        public virtual Quaternion GetBaseAimRotation() => GetViewRotation();

        public virtual Vector3 GetPawnViewLocation()
        {
            return GetActorLocation() + Vector3.up * baseEyeHeight;
        }

        public override void GetActorEyesViewPoint(out Vector3 outLocation, out Quaternion outRotation)
        {
            outLocation = GetPawnViewLocation();
            outRotation = GetViewRotation();
        }

        /// <summary>
        /// Applies the configured controller-rotation axes. Movement or character adapters call
        /// this from their own tick; base Pawn does not add an empty per-object Update callback.
        /// </summary>
        public virtual void ApplyControllerRotation(float deltaTime)
        {
            if (bIsTurnedOff || controller == null)
            {
                return;
            }

            FaceRotation(controller.ControlRotation(), deltaTime);
        }

        public virtual void FaceRotation(Quaternion newControlRotation, float deltaTime = 0f)
        {
            Vector3 euler = newControlRotation.eulerAngles;
            Vector3 current = transform.eulerAngles;

            if (bUseControllerRotationPitch) current.x = euler.x;
            if (bUseControllerRotationYaw) current.y = euler.y;
            if (bUseControllerRotationRoll) current.z = euler.z;

            if (bUseControllerRotationPitch || bUseControllerRotationYaw || bUseControllerRotationRoll)
            {
                SetActorRotation(Quaternion.Euler(current));
            }
        }
        #endregion

        #region State
        public bool IsPawnControlled() => controller != null;
        public bool IsPlayerControlled() => controller is PlayerController;
        public bool IsBotControlled() => controller is AIController;
        public virtual bool IsLocallyControlled() => controller != null && controller.IsLocalController;
        public bool IsTurnedOff() => bIsTurnedOff;
        public virtual void TurnOff() => bIsTurnedOff = true;
        public virtual void TurnOn() => bIsTurnedOff = false;

        public virtual void DetachFromControllerPendingDestroy()
        {
            controller?.UnPossess();
        }
        #endregion

        protected override void OnDestroy()
        {
            Controller previousController = controller;
            if (!ReferenceEquals(previousController, null))
            {
                if (previousController != null && !previousController.IsChangingPossession)
                {
                    try
                    {
                        previousController.UnPossess();
                    }
                    catch (System.Exception exception)
                    {
                        Debug.LogException(exception, this);
                    }
                }
                else
                {
                    previousController.DetachDestroyedPawn(this);
                }
            }

            cachedComponents.Clear();
            controller = null;
            playerState = null;
            base.OnDestroy();
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
