using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Tracks moving platform state for character movement.
    /// Simple approach: store the character's world position relative to the platform,
    /// then each frame calculate where that position should be now.
    /// </summary>
    public struct MovingPlatformData
    {
        public Rigidbody platform;
        public Transform platformTransform;
        public Vector3 platformVelocity;
        public bool isOnPlatform;
        
        // Stores where the character was standing on the platform (local space)
        public Vector3 localPosition;
        public Quaternion localRotation;
        
        // Stores the character's world position at the end of last frame
        private Vector3 _lastCharacterWorldPosition;
        private bool _hasLastPosition;

        public void Clear()
        {
            platform = null;
            platformTransform = null;
            platformVelocity = Vector3.zero;
            isOnPlatform = false;
            localPosition = Vector3.zero;
            localRotation = Quaternion.identity;
            _lastCharacterWorldPosition = Vector3.zero;
            _hasLastPosition = false;
        }

        /// <summary>
        /// Called when character lands on a new platform.
        /// </summary>
        public void SetPlatform(Rigidbody rb, Transform characterTransform)
        {
            if (rb == null)
            {
                Clear();
                return;
            }

            platform = rb;
            platformTransform = rb.transform;
            
            // Store character's position in platform's local space
            localPosition = platformTransform.InverseTransformPoint(characterTransform.position);
            localRotation = Quaternion.Inverse(platformTransform.rotation) * characterTransform.rotation;
            
            // No last position yet - first frame should not apply any delta
            _hasLastPosition = false;
            _lastCharacterWorldPosition = characterTransform.position;
            
            isOnPlatform = true;
            platformVelocity = Vector3.zero;
        }

        /// <summary>
        /// Calculates the delta position the character should move to stay on the platform.
        /// Call this at the START of the frame before any other movement.
        /// </summary>
        public Vector3 GetPlatformDelta(Vector3 currentCharacterPosition, float deltaTime)
        {
            if (!isOnPlatform || platformTransform == null)
                return Vector3.zero;

            // First frame after landing - no delta yet
            if (!_hasLastPosition)
            {
                _hasLastPosition = true;
                _lastCharacterWorldPosition = currentCharacterPosition;
                return Vector3.zero;
            }

            // Calculate where the character SHOULD be based on their local position on the platform
            Vector3 targetWorldPos = platformTransform.TransformPoint(localPosition);
            
            // The delta is the difference between where they should be and where they are
            Vector3 delta = targetWorldPos - currentCharacterPosition;
            
            // Calculate velocity for momentum inheritance
            if (deltaTime > 0.0001f)
            {
                platformVelocity = delta / deltaTime;
            }

            return delta;
        }

        /// <summary>
        /// Gets the rotation delta from platform rotation change.
        /// </summary>
        public Quaternion GetPlatformDeltaRotation(Transform characterTransform)
        {
            if (!isOnPlatform || platformTransform == null) return Quaternion.identity;

            Quaternion targetWorldRot = platformTransform.rotation * localRotation;
            return targetWorldRot * Quaternion.Inverse(characterTransform.rotation);
        }

        /// <summary>
        /// Updates the stored local position after character has moved.
        /// Call this at the END of the frame after all character movement is done.
        /// </summary>
        public void UpdateLocalPosition(Vector3 characterWorldPosition)
        {
            if (!isOnPlatform || platformTransform == null) return;
            
            // Update local position so next frame knows where the character is on the platform
            localPosition = platformTransform.InverseTransformPoint(characterWorldPosition);
            _lastCharacterWorldPosition = characterWorldPosition;
        }

        /// <summary>
        /// Updates the stored local rotation after character has rotated.
        /// </summary>
        public void UpdateLocalRotation(Quaternion characterWorldRotation)
        {
            if (!isOnPlatform || platformTransform == null) return;
            localRotation = Quaternion.Inverse(platformTransform.rotation) * characterWorldRotation;
        }
    }
}
