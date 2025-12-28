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
        public float groundedCheckDistance = 0.025f;
        public LayerMask groundLayer = 1;
        public float slopeLimit = 45f;
        public float stepHeight = 0.3f;

        // Moving Platform - displayed in Custom Editor
        [Tooltip("Enable moving platform support. Character will move with platforms.")]
        public bool enableMovingPlatform = true;
        [Tooltip("Inherit platform rotation. Character will rotate with rotating platforms.")]
        public bool inheritPlatformRotation = true;
        [Tooltip("Inherit platform momentum when jumping off. Character will keep platform velocity.")]
        public bool inheritPlatformMomentum = true;
        [Tooltip("Layer mask for detecting moving platforms. If empty, uses groundLayer.")]
        public LayerMask platformLayer = 0;

        // Ceiling Detection - displayed in Custom Editor
        [Tooltip("Enable ceiling detection to prevent head clipping during jumps.")]
        public bool enableCeilingDetection = true;
        [Tooltip("Extra distance above character to check for ceiling.")]
        public float ceilingCheckDistance = 0.1f;

        // Gap Bridging - displayed in Custom Editor
        [Tooltip("Enable gap bridging to auto-jump across small gaps while running.")]
        public bool enableGapBridging = true;
        [Tooltip("Minimum speed required to trigger gap bridging (m/s).")]
        public float minSpeedForGapBridge = 4f;
        [Tooltip("Maximum gap distance that can be bridged (m).")]
        public float maxGapDistance = 1.5f;
        [Tooltip("Maximum height difference allowed for gap bridging (m).")]
        public float maxGapHeightDiff = 0.3f;

        // AI Pathfinding - displayed in Custom Editor
        [Tooltip("Pathfinding system to use for AI navigation. Requires corresponding package installed.")]
        public PathfindingSystem pathfindingSystem = PathfindingSystem.None;

        // Rotation - displayed in Custom Editor
        public float rotationSpeed = 10f;

        // Animation Parameters (Additional) - displayed in Custom Editor
        [Tooltip("Parameter name for roll trigger (Trigger)")]
        public string rollTrigger = "Roll";

        [Tooltip("Parameter name for climbing state (Bool)")]
        public string climbingParameter = "IsClimbing";
    }
}