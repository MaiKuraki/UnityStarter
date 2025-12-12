using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    [CreateAssetMenu(fileName = "MovementConfig", menuName = "CycloneGames/RPG Foundation/Movement Config")]
    public class MovementConfig : MovementConfigBase
    {
        // Special Movement - displayed in Custom Editor
        public float rollDistance = 5f;
        public float rollDuration = 0.5f;
        public float climbSpeed = 2f;
        public float swimSpeed = 3f;
        public float flySpeed = 6f;

        // Physics - displayed in Custom Editor
        public float gravity = -25f;
        public float airControlMultiplier = 0.5f;
        public float groundedCheckDistance = 0.03f;
        public LayerMask groundLayer = 1;
        public float slopeLimit = 45f;
        public float stepHeight = 0.3f;

        // Rotation - displayed in Custom Editor
        public float rotationSpeed = 20f;

        // Animation Parameters (Additional) - displayed in Custom Editor
        [Tooltip("Parameter name for roll trigger (Trigger)")]
        public string rollTrigger = "Roll";
    }
}