using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Represents where on the capsule a hit occurred.
    /// </summary>
    public enum HitLocation : byte
    {
        /// <summary>Hit on the bottom hemisphere (ground)</summary>
        Below,
        /// <summary>Hit on the side (wall)</summary>
        Sides,
        /// <summary>Hit on the top hemisphere (ceiling)</summary>
        Above
    }

    /// <summary>
    /// Contains the result of a movement sweep test.
    /// </summary>
    public struct CollisionResult
    {
        /// <summary>
        /// True if the sweep started in penetration.
        /// </summary>
        public bool startPenetrating;

        /// <summary>
        /// Location of the hit on the capsule.
        /// </summary>
        public HitLocation hitLocation;

        /// <summary>
        /// Is the hit surface walkable?
        /// </summary>
        public bool isWalkable;

        /// <summary>
        /// Character position at hit point.
        /// </summary>
        public Vector3 position;

        /// <summary>
        /// Character velocity at time of hit.
        /// </summary>
        public Vector3 velocity;

        /// <summary>
        /// Hit point in world space.
        /// </summary>
        public Vector3 point;

        /// <summary>
        /// Hit normal (may be adjusted for blocking).
        /// </summary>
        public Vector3 normal;

        /// <summary>
        /// Actual surface normal (geometry-based).
        /// </summary>
        public Vector3 surfaceNormal;

        /// <summary>
        /// Displacement to reach the hit point.
        /// </summary>
        public Vector3 displacementToHit;

        /// <summary>
        /// Remaining displacement after hit.
        /// </summary>
        public Vector3 remainingDisplacement;

        /// <summary>
        /// The collider that was hit.
        /// </summary>
        public Collider collider;

        /// <summary>
        /// Raw hit result.
        /// </summary>
        public RaycastHit hitResult;

        /// <summary>
        /// Get the rigidbody of the hit collider (if any).
        /// </summary>
        public Rigidbody rigidbody => collider != null ? collider.attachedRigidbody : null;
    }
}
