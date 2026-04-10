using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public class Pawn : Actor
    {
        [SerializeField] private bool bUseControllerRotationPitch;
        [SerializeField] private bool bUseControllerRotationYaw;
        [SerializeField] private bool bUseControllerRotationRoll;
        [SerializeField] private float baseEyeHeight = 0.8f;
        [SerializeField] private PawnConfig pawnConfig;

        private PlayerState playerState;
        private Controller controller;
        public Controller Controller => controller;

        private Vector3 pendingMovementInput;
        private Vector3 lastMovementInput;
        private bool bIsTurnedOff;

        private readonly List<MonoBehaviour> _cachedComponents = new List<MonoBehaviour>(16);

        #region Controller Rotation Properties
        public bool UseControllerRotationPitch { get => bUseControllerRotationPitch; set => bUseControllerRotationPitch = value; }
        public bool UseControllerRotationYaw { get => bUseControllerRotationYaw; set => bUseControllerRotationYaw = value; }
        public bool UseControllerRotationRoll { get => bUseControllerRotationRoll; set => bUseControllerRotationRoll = value; }
        public float BaseEyeHeight { get => baseEyeHeight; set => baseEyeHeight = value; }

        /// <summary>
        /// Set pawn configuration from a ScriptableObject asset.
        /// Can be called during initialization or at runtime to change Pawn settings.
        /// </summary>
        public virtual void SetPawnConfig(PawnConfig config)
        {
            if (config != null)
            {
                pawnConfig = config;
                config.ApplyTo(this);
            }
        }

        public PawnConfig GetPawnConfig() => pawnConfig;
        #endregion

        #region Movement Input
        /// <summary>
        /// Adds movement input along the given world direction, scaled by ScaleValue.
        /// Accumulated each frame and consumed by movement component via ConsumeMovementInputVector().
        /// </summary>
        public virtual void AddMovementInput(Vector3 WorldDirection, float ScaleValue = 1.0f, bool bForce = false)
        {
            if (bIsTurnedOff && !bForce) return;
            if (controller != null && controller.IsMoveInputIgnored() && !bForce) return;
            pendingMovementInput += WorldDirection * ScaleValue;
        }

        public Vector3 GetPendingMovementInputVector() => pendingMovementInput;
        public Vector3 GetLastMovementInputVector() => lastMovementInput;

        /// <summary>
        /// Returns and clears the pending movement input. Typically called by PawnMovementComponent each tick.
        /// </summary>
        public Vector3 ConsumeMovementInputVector()
        {
            lastMovementInput = pendingMovementInput;
            pendingMovementInput = Vector3.zero;
            return lastMovementInput;
        }
        #endregion

        #region Initial Rotation
        /// <summary>
        /// Notifies IInitialRotationSettable components when a Pawn is spawned with rotation.
        /// Uses cached component list to avoid GC allocation.
        /// </summary>
        public void NotifyInitialRotation(Quaternion rotation)
        {
            _cachedComponents.Clear();
            GetComponents(_cachedComponents);
            for (int i = 0; i < _cachedComponents.Count; i++)
            {
                if (_cachedComponents[i] is IInitialRotationSettable settable)
                {
                    settable.SetInitialRotation(rotation, immediate: true);
                }
            }
        }
        #endregion

        #region Restart
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
        public virtual void PossessedBy(Controller NewController)
        {
            Controller oldController = controller;
            SetOwner(NewController);
            controller = NewController;

            if (controller?.GetPlayerState() != null)
            {
                SetPlayerState(controller.GetPlayerState());
            }

            NotifyControllerChanged(oldController, NewController);
        }

        public virtual void UnPossessed()
        {
            Controller oldController = controller;
            SetPlayerState(null);
            SetOwner(null);
            controller = null;
            NotifyControllerChanged(oldController, null);
        }

        protected virtual void NotifyControllerChanged(Controller OldController, Controller NewController) { }
        #endregion

        #region PlayerState
        private void SetPlayerState(PlayerState NewPlayerState)
        {
            if (playerState != null && ReferenceEquals(playerState.GetPawn(), this))
            {
                playerState.SetPawnPrivate(null);
            }
            playerState = NewPlayerState;
            if (playerState != null)
            {
                playerState.SetPawnPrivate(this);
            }
        }

        public PlayerState GetPlayerState() => playerState;
        public T GetPlayerState<T>() where T : PlayerState => playerState as T;
        #endregion

        #region Control Rotation
        public Quaternion GetControlRotation()
        {
            return controller != null ? controller.ControlRotation() : Quaternion.identity;
        }
        #endregion

        #region View
        public virtual Quaternion GetViewRotation()
        {
            return controller != null ? controller.ControlRotation() : GetActorRotation();
        }

        public virtual Quaternion GetBaseAimRotation()
        {
            return GetViewRotation();
        }

        public virtual Vector3 GetPawnViewLocation()
        {
            return GetActorLocation() + Vector3.up * baseEyeHeight;
        }

        public override void GetActorEyesViewPoint(out Vector3 outLocation, out Quaternion outRotation)
        {
            outLocation = GetPawnViewLocation();
            outRotation = GetViewRotation();
        }
        #endregion

        #region FaceRotation
        /// <summary>
        /// Updates Pawn rotation to match the controller's ControlRotation,
        /// respecting bUseControllerRotation* flags. Called automatically each Update.
        /// </summary>
        public virtual void FaceRotation(Quaternion NewControlRotation, float DeltaTime = 0f)
        {
            Vector3 euler = NewControlRotation.eulerAngles;
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

        #region State Queries
        public bool IsPawnControlled() => controller != null;

        public bool IsPlayerControlled()
        {
            return controller is PlayerController;
        }

        public bool IsBotControlled()
        {
            return controller is AIController;
        }

        public virtual bool IsLocallyControlled()
        {
            return controller != null;
        }
        #endregion

        #region Turn Off/On
        public bool IsTurnedOff() => bIsTurnedOff;

        public virtual void TurnOff()
        {
            bIsTurnedOff = true;
        }

        public virtual void TurnOn()
        {
            bIsTurnedOff = false;
        }
        #endregion

        #region Detach
        public virtual void DetachFromControllerPendingDestroy()
        {
            if (controller != null)
            {
                controller.UnPossess();
            }
        }
        #endregion

        protected override void Update()
        {
            base.Update();
            if (!bIsTurnedOff && controller != null)
            {
                if (bUseControllerRotationPitch || bUseControllerRotationYaw || bUseControllerRotationRoll)
                {
                    FaceRotation(controller.ControlRotation(), Time.deltaTime);
                }
            }
        }
    }
}