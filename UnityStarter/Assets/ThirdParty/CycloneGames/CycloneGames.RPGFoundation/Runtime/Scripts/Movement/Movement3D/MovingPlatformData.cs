using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Tracks moving platform state for character movement.
    /// </summary>
    public struct MovingPlatformData
    {
        public Rigidbody platform;
        public Transform platformTransform;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 platformVelocity;
        public Vector3 lastPlatformPosition;
        public Quaternion lastPlatformRotation;
        public bool isOnPlatform;

        public void Clear()
        {
            platform = null;
            platformTransform = null;
            localPosition = Vector3.zero;
            localRotation = Quaternion.identity;
            platformVelocity = Vector3.zero;
            lastPlatformPosition = Vector3.zero;
            lastPlatformRotation = Quaternion.identity;
            isOnPlatform = false;
        }

        public void SetPlatform(Rigidbody rb, Transform characterTransform)
        {
            if (rb == null)
            {
                Clear();
                return;
            }

            platform = rb;
            platformTransform = rb.transform;
            localPosition = platformTransform.InverseTransformPoint(characterTransform.position);
            localRotation = Quaternion.Inverse(platformTransform.rotation) * characterTransform.rotation;
            lastPlatformPosition = platformTransform.position;
            lastPlatformRotation = platformTransform.rotation;
            platformVelocity = rb.velocity;
            isOnPlatform = true;
        }

        /// <summary>
        /// Updates the velocity by calculating character's actual world velocity on the platform.
        /// This includes both linear velocity from platform translation AND tangential velocity
        /// from platform rotation (v = ω × r for rotating platforms).
        /// </summary>
        public void UpdatePlatformVelocity(float deltaTime)
        {
            if (!isOnPlatform || platformTransform == null) return;

            if (deltaTime > 0.0001f)
            {
                // Calculate character's world position based on local position
                Vector3 lastCharacterWorldPos = lastPlatformRotation * localPosition + lastPlatformPosition;
                Vector3 currentCharacterWorldPos = platformTransform.TransformPoint(localPosition);
                
                // Character's actual velocity = delta position / time
                // This naturally includes both linear and tangential components
                platformVelocity = (currentCharacterWorldPos - lastCharacterWorldPos) / deltaTime;
            }
            lastPlatformPosition = platformTransform.position;
            lastPlatformRotation = platformTransform.rotation;
        }

        public Vector3 GetPlatformDeltaPosition(Transform characterTransform)
        {
            if (!isOnPlatform || platformTransform == null) return Vector3.zero;

            Vector3 targetWorldPos = platformTransform.TransformPoint(localPosition);
            return targetWorldPos - characterTransform.position;
        }

        public Quaternion GetPlatformDeltaRotation(Transform characterTransform)
        {
            if (!isOnPlatform || platformTransform == null) return Quaternion.identity;

            Quaternion targetWorldRot = platformTransform.rotation * localRotation;
            return targetWorldRot * Quaternion.Inverse(characterTransform.rotation);
        }
    }
}
