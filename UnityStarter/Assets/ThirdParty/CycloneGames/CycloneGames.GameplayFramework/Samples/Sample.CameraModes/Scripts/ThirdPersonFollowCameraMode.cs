using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public sealed class ThirdPersonFollowCameraMode : CameraMode
    {
        public float FollowDistance { get; set; } = 4.0f;
        public float PivotHeight { get; set; } = 1.5f;
        public float LookAtHeight { get; set; } = 1.0f;
        public float OverrideFov { get; set; } = 60.0f;

        public override float BlendDuration => 0.25f;

        public override CameraPose Evaluate(CameraContext context, in CameraPose basePose, float deltaTime)
        {
            Actor target = context != null ? context.CurrentViewTarget : null;
            if (target == null)
            {
                return basePose;
            }

            target.CalcCamera(deltaTime, out CameraPose targetPose, basePose.Fov);
            Vector3 pivot = targetPose.Position + Vector3.up * PivotHeight;
            Vector3 desiredPosition = pivot + (targetPose.Rotation * Vector3.back) * FollowDistance;
            Vector3 lookAtPoint = targetPose.Position + Vector3.up * LookAtHeight;
            Vector3 lookDirection = lookAtPoint - desiredPosition;
            Quaternion desiredRotation = lookDirection.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
                : targetPose.Rotation;

            return new CameraPose(desiredPosition, desiredRotation, OverrideFov > 0f ? OverrideFov : basePose.Fov);
        }
    }
}