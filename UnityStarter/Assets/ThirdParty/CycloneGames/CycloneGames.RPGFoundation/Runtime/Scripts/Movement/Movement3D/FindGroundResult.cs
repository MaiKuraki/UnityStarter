using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Contains the result of a ground detection query.
    /// </summary>
    public struct FindGroundResult
    {
        /// <summary>
        /// Did we hit ground? (impacted capsule's bottom sphere)
        /// </summary>
        public bool hitGround;

        /// <summary>
        /// Is the found ground walkable? (slope angle within limit)
        /// </summary>
        public bool isWalkable;

        /// <summary>
        /// Is walkable ground? (hitGround && isWalkable)
        /// </summary>
        public bool isWalkableGround => hitGround && isWalkable;

        /// <summary>
        /// The character's position at time of ground check.
        /// </summary>
        public Vector3 position;

        /// <summary>
        /// The impact point in world space.
        /// </summary>
        public Vector3 point;

        /// <summary>
        /// The normal of the hit surface (may be adjusted for geometry).
        /// </summary>
        public Vector3 surfaceNormal;

        /// <summary>
        /// The collider of the hit ground.
        /// </summary>
        public Collider collider;

        /// <summary>
        /// The distance from capsule bottom to ground.
        /// </summary>
        public float groundDistance;

        /// <summary>
        /// True if result came from a raycast (more accurate for walkability).
        /// </summary>
        public bool isRaycastResult;

        /// <summary>
        /// Distance from raycast. Valid only if isRaycastResult is true.
        /// </summary>
        public float raycastDistance;

        /// <summary>
        /// Raw hit result from the query.
        /// </summary>
        public RaycastHit hitResult;

        /// <summary>
        /// Gets the effective distance to ground.
        /// </summary>
        public float GetDistanceToGround()
        {
            return isRaycastResult ? raycastDistance : groundDistance;
        }

        /// <summary>
        /// Initialize from a sweep test result.
        /// </summary>
        public void SetFromSweepResult(bool hitGround, bool isWalkable, Vector3 position, 
            float sweepDistance, ref RaycastHit inHit, Vector3 surfaceNormal)
        {
            this.hitGround = hitGround;
            this.isWalkable = isWalkable;
            this.position = position;
            this.point = inHit.point;
            this.surfaceNormal = surfaceNormal;
            this.collider = inHit.collider;
            this.groundDistance = sweepDistance;
            this.isRaycastResult = false;
            this.raycastDistance = 0f;
            this.hitResult = inHit;
        }

        /// <summary>
        /// Initialize from a raycast result.
        /// </summary>
        public void SetFromRaycastResult(bool hitGround, bool isWalkable, Vector3 position,
            float sweepDistance, float raycastDist, ref RaycastHit inHit, Vector3 surfaceNormal)
        {
            this.hitGround = hitGround;
            this.isWalkable = isWalkable;
            this.position = position;
            this.point = inHit.point;
            this.surfaceNormal = surfaceNormal;
            this.collider = inHit.collider;
            this.groundDistance = sweepDistance;
            this.isRaycastResult = true;
            this.raycastDistance = raycastDist;
            this.hitResult = inHit;
        }

        /// <summary>
        /// Clear the result.
        /// </summary>
        public void Clear()
        {
            hitGround = false;
            isWalkable = false;
            position = Vector3.zero;
            point = Vector3.zero;
            surfaceNormal = Vector3.up;
            collider = null;
            groundDistance = 0f;
            isRaycastResult = false;
            raycastDistance = 0f;
            hitResult = default;
        }
    }
}
