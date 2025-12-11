using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    [CreateAssetMenu(fileName = "MovementConfig", menuName = "CycloneGames/RPG Foundation/Movement Config")]
    public class MovementConfig : MovementConfigBase
    {
        [Header("3D Specific - Special Movement")]
        public float rollDistance = 5f;
        public float rollDuration = 0.5f;
        public float climbSpeed = 2f;
        public float swimSpeed = 3f;
        public float flySpeed = 6f;

        [Header("3D Specific - Physics")]
        public float gravity = -25f;
        public float airControlMultiplier = 0.5f;
        public float groundedCheckDistance = 0.2f;
        public float slopeLimit = 45f;
        public float stepHeight = 0.3f;

        [Header("3D Specific - Rotation")]
        public float rotationSpeed = 20f;

        [Header("3D Animation (Additional)")]
        [Tooltip("Parameter name for roll trigger (Trigger)")]
        public string rollTrigger = "Roll";
    }
}