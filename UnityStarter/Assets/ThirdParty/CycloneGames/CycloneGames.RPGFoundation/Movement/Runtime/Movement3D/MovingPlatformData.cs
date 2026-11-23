using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    public struct MovingPlatformData
    {
        public Rigidbody platform;
        public Transform platformTransform;
        public Vector3 platformVelocity;
        public bool isOnPlatform;
        
        public Vector3 localPosition;
        public Quaternion localRotation;
        
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
            
            _hasLastPosition = false;
            _lastCharacterWorldPosition = characterTransform.position;
            
            isOnPlatform = true;
            platformVelocity = Vector3.zero;
        }

        public Vector3 GetPlatformDelta(Vector3 currentCharacterPosition, float deltaTime)
        {
            if (!isOnPlatform || platformTransform == null)
                return Vector3.zero;

            if (!_hasLastPosition)
            {
                _hasLastPosition = true;
                _lastCharacterWorldPosition = currentCharacterPosition;
                return Vector3.zero;
            }

            Vector3 targetWorldPos = platformTransform.TransformPoint(localPosition);
            
            Vector3 delta = targetWorldPos - currentCharacterPosition;
            
            if (deltaTime > 0.0001f)
            {
                platformVelocity = delta / deltaTime;
            }

            return delta;
        }

        public Quaternion GetPlatformDeltaRotation(Transform characterTransform)
        {
            if (!isOnPlatform || platformTransform == null) return Quaternion.identity;

            Quaternion targetWorldRot = platformTransform.rotation * localRotation;
            return targetWorldRot * Quaternion.Inverse(characterTransform.rotation);
        }

        public void UpdateLocalPosition(Vector3 characterWorldPosition)
        {
            if (!isOnPlatform || platformTransform == null) return;
            
            localPosition = platformTransform.InverseTransformPoint(characterWorldPosition);
            _lastCharacterWorldPosition = characterWorldPosition;
        }

        public void UpdateLocalRotation(Quaternion characterWorldRotation)
        {
            if (!isOnPlatform || platformTransform == null) return;
            localRotation = Quaternion.Inverse(platformTransform.rotation) * characterWorldRotation;
        }
    }
}
