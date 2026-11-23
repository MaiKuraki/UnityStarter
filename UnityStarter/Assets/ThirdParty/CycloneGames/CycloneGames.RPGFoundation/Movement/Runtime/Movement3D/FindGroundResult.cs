using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    public struct FindGroundResult
    {
        public bool hitGround;

        public bool isWalkable;

        public bool isWalkableGround => hitGround && isWalkable;

        public Vector3 position;

        public Vector3 point;

        public Vector3 surfaceNormal;

        public Collider collider;

        public float groundDistance;

        public bool isRaycastResult;

        public float raycastDistance;

        public RaycastHit hitResult;

        public float GetDistanceToGround()
        {
            return isRaycastResult ? raycastDistance : groundDistance;
        }

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
