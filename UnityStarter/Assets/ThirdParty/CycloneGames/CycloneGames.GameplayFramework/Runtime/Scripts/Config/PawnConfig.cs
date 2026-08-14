using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Scriptable Object configuration for Pawn view and control properties.
    /// Only contains fields that the base Pawn class itself reads.
    ///
    /// For input sensitivities and device-specific settings,
    /// configure those in your InputSystem integration instead.
    /// </summary>
    [CreateAssetMenu(fileName = "PawnConfig", menuName = "CycloneGames/GameplayFramework/PawnConfig")]
    public class PawnConfig : ScriptableObject
    {
        [Header("Rotation")]
        [SerializeField] private bool useControllerRotationPitch;
        [SerializeField] private bool useControllerRotationYaw = true;
        [SerializeField] private bool useControllerRotationRoll;

        [Header("View")]
        [SerializeField] private float baseEyeHeight = 0.8f;
        [SerializeField, Range(0f, 180f)] private float maxLookUpAngle = 89f;
        [SerializeField, Range(0f, 180f)] private float maxLookDownAngle = 89f;

        public bool UseControllerRotationPitch => useControllerRotationPitch;
        public bool UseControllerRotationYaw => useControllerRotationYaw;
        public bool UseControllerRotationRoll => useControllerRotationRoll;
        public float BaseEyeHeight => baseEyeHeight;
        public float MaxLookUpAngle => maxLookUpAngle;
        public float MaxLookDownAngle => maxLookDownAngle;

        internal void ValidateOrThrow()
        {
            if (float.IsNaN(baseEyeHeight) || float.IsInfinity(baseEyeHeight))
            {
                throw new System.InvalidOperationException(
                    "PawnConfig Base Eye Height must be finite.");
            }

            ValidateLookAngle(maxLookUpAngle, "Max Look Up Angle");
            ValidateLookAngle(maxLookDownAngle, "Max Look Down Angle");
        }

        private static void ValidateLookAngle(float value, string displayName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 180f)
            {
                throw new System.InvalidOperationException(
                    $"PawnConfig {displayName} must be finite and between 0 and 180 degrees.");
            }
        }
    }
}
