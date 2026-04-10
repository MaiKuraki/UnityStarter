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
        [SerializeField] private float maxLookUpAngle = 89f;
        [SerializeField] private float maxLookDownAngle = 89f;

        public bool UseControllerRotationPitch => useControllerRotationPitch;
        public bool UseControllerRotationYaw => useControllerRotationYaw;
        public bool UseControllerRotationRoll => useControllerRotationRoll;
        public float BaseEyeHeight => baseEyeHeight;
        public float MaxLookUpAngle => maxLookUpAngle;
        public float MaxLookDownAngle => maxLookDownAngle;

        /// <summary>
        /// Apply this configuration to a Pawn instance.
        /// Call this during Pawn initialization.
        /// </summary>
        public virtual void ApplyTo(Pawn pawn)
        {
            if (pawn == null) return;

            pawn.UseControllerRotationPitch = useControllerRotationPitch;
            pawn.UseControllerRotationYaw = useControllerRotationYaw;
            pawn.UseControllerRotationRoll = useControllerRotationRoll;
            pawn.BaseEyeHeight = baseEyeHeight;
        }
    }
}
