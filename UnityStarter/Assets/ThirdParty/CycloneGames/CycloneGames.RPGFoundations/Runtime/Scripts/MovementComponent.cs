using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime
{
    [RequireComponent(typeof(CharacterController))]
    public class MovementComponent : MonoBehaviour
    {
        [Header("Dependencies")]
        [Tooltip("Optional Animator component. Used for Root Motion and setting animation parameters like 'MovementSpeed'.")]
        [SerializeField] private Animator characterAC = null;

        [Header("Movement Settings")]
        [SerializeField] private float _moveSpeed = 5f;
        public float moveSpeed => _moveSpeed;
        [SerializeField] private float rotationSpeed = 20f;
        [SerializeField] private float gravity = -25f;
        [SerializeField] private bool useRootMotion = false;
        [Tooltip("If true, all movement and rotation calculations will use unscaled time, ignoring Time.timeScale. Useful for characters that should not be affected by slow-motion effects.")]
        [SerializeField] private bool ignoreTimeScale = false;

        /// <summary>
        /// A per-character time scale multiplier. Can be smoothly interpolated by external scripts to create custom slow-motion effects for this character.
        /// </summary>
        public float LocalTimeScale { get; set; } = 1f;

        [Header("Gravity & Alignment")]
        [Tooltip("An optional Transform used to initialize the WorldUp direction at the start. After initialization, WorldUp must be managed by an external script if dynamic changes are needed.")]
        public Transform WorldUpSource;

        /// <summary>
        /// The direction of "up" in world space. This is the single source of truth for all alignment and gravity logic.
        /// It can be set at any time by external scripts (e.g., from a ground check raycast).
        /// Defaults to Vector3.up.
        /// </summary>
        public Vector3 WorldUp { get; set; } = Vector3.up;

        private CharacterController characterController;

        private readonly int _animIDMovementSpeed = Animator.StringToHash("MovementSpeed"); //  TODO: mabye passed from other class not directly HardCode.

        private Vector3 _lookDirection = Vector3.zero;
        private bool _isGrounded = false;
        private float _verticalVelocity = 0f;

        // A helper property to get the correct delta time, taking into account both global and local time scales.
        private float DeltaTime => (ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime) * LocalTimeScale;
        private const float _minSqrMagnitudeForMovement = 0.0001f;
        private const float _groundedVerticalVelocity = -2f; // A small negative velocity to keep the character grounded.

        void Awake()
        {
            characterController = GetComponent<CharacterController>();
            if (characterAC == null)
            {
                characterAC = GetComponent<Animator>();
            }
        }

        void Start()
        {
            characterController.minMoveDistance = 0f;

            // Initialize WorldUp from the source transform if it's assigned at the start.
            if (WorldUpSource != null)
            {
                WorldUp = WorldUpSource.up;
            }
        }

        /// <summary>
        /// The core movement method, driven by a world-space velocity vector.
        /// This is the primary method for both AI and Player control.
        /// </summary>
        /// <param name="worldVelocity">The desired world-space velocity of movement on the character's current ground plane. The magnitude determines the speed.</param>
        public void MoveWithVelocity(Vector3 worldVelocity)
        {
            // Note: The WorldUp vector is NOT updated here. It is the responsibility of an external
            // script to manage and update the WorldUp property before this method is called.

            bool canUseRootMotion = useRootMotion && characterAC != null;

            if (canUseRootMotion)
            {
                // For root motion, the animator speed is driven by the desired velocity magnitude.
                // The actual movement is handled by the animation clip.
                if (worldVelocity.sqrMagnitude > _minSqrMagnitudeForMovement)
                {
                    _lookDirection = worldVelocity.normalized;
                }
                TickRotation();
                characterAC.SetFloat(_animIDMovementSpeed, worldVelocity.magnitude);
            }
            else
            {
                // For scripted movement, we execute the movement directly.
                ExecuteScriptedMovement(worldVelocity);
            }

            if (characterAC != null)
            {
                characterAC.applyRootMotion = canUseRootMotion;
            }
        }

        private void ExecuteScriptedMovement(Vector3 horizontalVelocity)
        {
            _isGrounded = characterController.isGrounded;

            Vector3 horizontal = horizontalVelocity;

            // Update vertical velocity
            if (_isGrounded && _verticalVelocity < 0)
            {
                _verticalVelocity = _groundedVerticalVelocity;
            }
            else if (!_isGrounded)
            {
                _verticalVelocity += gravity * DeltaTime;
            }

            Vector3 horizontalDisplacement = horizontal * DeltaTime;
            Vector3 verticalDisplacement = WorldUp * _verticalVelocity * DeltaTime;

            Vector3 displacement = horizontalDisplacement + verticalDisplacement;
            characterController.Move(displacement);

            if (characterAC != null)
            {
                characterAC.SetFloat(_animIDMovementSpeed, horizontal.magnitude);
            }

            if (horizontal.sqrMagnitude > 0.0001f)
            {
                _lookDirection = horizontal.normalized;
            }

            TickRotation();
        }

        void TickRotation()
        {
            // The target rotation should align the character's up vector with the current WorldUp,
            // and its forward vector with the look direction.
            Quaternion targetRotation;
            if (_lookDirection.sqrMagnitude > _minSqrMagnitudeForMovement)
            {
                targetRotation = Quaternion.LookRotation(_lookDirection, WorldUp);
            }
            else
            {
                // If not moving, just align to the world up direction.
                // This prevents spinning when standing still on a curved surface.
                Quaternion toUp = Quaternion.FromToRotation(transform.up, WorldUp);
                targetRotation = toUp * transform.rotation;
            }

            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * DeltaTime);
        }
    }
}