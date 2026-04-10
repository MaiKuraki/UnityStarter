using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Scriptable camera preset that designers can bind to action/VFX assets.
    /// The preset is data-only and can be evaluated by a CameraMode at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "CameraActionPreset", menuName = "CycloneGames/GameplayFramework/Camera/CameraActionPreset")]
    public class CameraActionPreset : ScriptableObject
    {
        public enum OffsetSpace
        {
            World,
            TargetLocal
        }

        [Header("Timing")]
        [SerializeField] private float duration = 0.40f;
        [SerializeField] private float blendDuration = 0.12f;
        [SerializeField] private AnimationCurve weightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Framing")]
        [SerializeField] private float followDistance = 3.5f;
        [SerializeField] private float pivotHeight = 1.4f;
        [SerializeField] private Vector3 pivotOffset = Vector3.zero;
        [SerializeField] private Vector3 lookAtOffset = new Vector3(0f, 1f, 0f);
        [SerializeField] private float yawOffsetDegrees;
        [SerializeField] private OffsetSpace offsetSpace = OffsetSpace.TargetLocal;
        [SerializeField] private bool useTargetUpAxis = true;
        [SerializeField, Min(0.000001f)] private float minLookDirectionSqrMagnitude = 0.0001f;

        [Header("Lens")]
        [SerializeField] private float overrideFov = 52f;

        public float Duration => Mathf.Max(0.01f, duration);
        public float BlendDuration => Mathf.Max(0f, blendDuration);

        public virtual float EvaluateWeight(float normalizedTime)
        {
            float t = Mathf.Clamp01(normalizedTime);
            if (weightCurve == null) return t;
            return Mathf.Clamp01(weightCurve.Evaluate(t));
        }

        public virtual CameraPose EvaluatePose(Actor target, in CameraPose basePose, float normalizedTime, float deltaTime)
        {
            if (target == null)
            {
                return basePose;
            }

            target.CalcCamera(deltaTime, out CameraPose targetPose, basePose.Fov);

            Vector3 upAxis = ResolveUpAxis(target, targetPose);
            Vector3 pivot = ComputePivotPoint(target, targetPose, upAxis);
            Vector3 desiredPosition = ComputeDesiredPosition(target, targetPose, pivot, upAxis);
            Vector3 lookAtPoint = ComputeLookAtPoint(target, targetPose, upAxis);
            Quaternion desiredRotation = ComputeDesiredRotation(targetPose, desiredPosition, lookAtPoint, upAxis);
            float desiredFov = ResolveDesiredFov(basePose);

            CameraPose desiredPose = new CameraPose(desiredPosition, desiredRotation, desiredFov);

            float weight = EvaluateWeight(normalizedTime);
            return CameraPose.Lerp(basePose, desiredPose, weight);
        }

        protected virtual Vector3 ResolveUpAxis(Actor target, in CameraPose targetPose)
        {
            if (useTargetUpAxis)
            {
                return targetPose.Rotation * Vector3.up;
            }

            return Vector3.up;
        }

        protected virtual Vector3 ResolveOffset(in CameraPose targetPose, Vector3 offset)
        {
            return offsetSpace == OffsetSpace.TargetLocal
                ? targetPose.Rotation * offset
                : offset;
        }

        protected virtual Vector3 ComputePivotPoint(Actor target, in CameraPose targetPose, Vector3 upAxis)
        {
            return targetPose.Position + ResolveOffset(targetPose, pivotOffset) + upAxis * pivotHeight;
        }

        protected virtual Vector3 ComputeDesiredPosition(Actor target, in CameraPose targetPose, Vector3 pivot, Vector3 upAxis)
        {
            Quaternion yawOffset = Quaternion.AngleAxis(yawOffsetDegrees, upAxis);
            Vector3 backward = yawOffset * (targetPose.Rotation * Vector3.back);
            return pivot + backward * followDistance;
        }

        protected virtual Vector3 ComputeLookAtPoint(Actor target, in CameraPose targetPose, Vector3 upAxis)
        {
            return targetPose.Position + ResolveOffset(targetPose, lookAtOffset);
        }

        protected virtual Quaternion ComputeDesiredRotation(in CameraPose targetPose, Vector3 desiredPosition, Vector3 lookAtPoint, Vector3 upAxis)
        {
            Vector3 lookDirection = lookAtPoint - desiredPosition;
            if (lookDirection.sqrMagnitude <= minLookDirectionSqrMagnitude)
            {
                return targetPose.Rotation;
            }

            return Quaternion.LookRotation(lookDirection.normalized, upAxis);
        }

        protected virtual float ResolveDesiredFov(in CameraPose basePose)
        {
            return overrideFov > 0f ? overrideFov : basePose.Fov;
        }
    }
}
