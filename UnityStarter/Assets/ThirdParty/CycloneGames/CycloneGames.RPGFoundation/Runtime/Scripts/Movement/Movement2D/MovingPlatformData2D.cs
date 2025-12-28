using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D
{
    /// <summary>
    /// Tracks moving platform state for 2D character movement.
    /// </summary>
    public struct MovingPlatformData2D
    {
        public Rigidbody2D platform;
        public Transform platformTransform;
        public Vector2 localPosition;
        public float localRotationZ;
        public Vector2 platformVelocity;
        public Vector2 lastPlatformPosition;
        public float lastPlatformRotationZ;
        public bool isOnPlatform;

        public void Clear()
        {
            platform = null;
            platformTransform = null;
            localPosition = Vector2.zero;
            localRotationZ = 0f;
            platformVelocity = Vector2.zero;
            lastPlatformPosition = Vector2.zero;
            lastPlatformRotationZ = 0f;
            isOnPlatform = false;
        }

        public void SetPlatform(Rigidbody2D rb, Transform characterTransform)
        {
            if (rb == null)
            {
                Clear();
                return;
            }

            platform = rb;
            platformTransform = rb.transform;
            localPosition = platformTransform.InverseTransformPoint(characterTransform.position);
            localRotationZ = characterTransform.eulerAngles.z - platformTransform.eulerAngles.z;
            lastPlatformPosition = platformTransform.position;
            lastPlatformRotationZ = platformTransform.eulerAngles.z;
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
                // For 2D, we need to manually apply rotation to local position
                float lastRadians = lastPlatformRotationZ * Mathf.Deg2Rad;
                float cosLast = Mathf.Cos(lastRadians);
                float sinLast = Mathf.Sin(lastRadians);
                Vector2 lastCharacterWorldPos = new Vector2(
                    localPosition.x * cosLast - localPosition.y * sinLast,
                    localPosition.x * sinLast + localPosition.y * cosLast
                ) + lastPlatformPosition;

                Vector2 currentCharacterWorldPos = platformTransform.TransformPoint(localPosition);

                // Character's actual velocity = delta position / time
                platformVelocity = (currentCharacterWorldPos - lastCharacterWorldPos) / deltaTime;
            }
            lastPlatformPosition = platformTransform.position;
            lastPlatformRotationZ = platformTransform.eulerAngles.z;
        }

        public Vector2 GetPlatformDeltaPosition(Transform characterTransform)
        {
            if (!isOnPlatform || platformTransform == null) return Vector2.zero;

            Vector2 targetWorldPos = platformTransform.TransformPoint(localPosition);
            return targetWorldPos - (Vector2)characterTransform.position;
        }

        public float GetPlatformDeltaRotationZ(Transform characterTransform)
        {
            if (!isOnPlatform || platformTransform == null) return 0f;

            float targetWorldRotZ = platformTransform.eulerAngles.z + localRotationZ;
            return Mathf.DeltaAngle(characterTransform.eulerAngles.z, targetWorldRotZ);
        }
    }
}
