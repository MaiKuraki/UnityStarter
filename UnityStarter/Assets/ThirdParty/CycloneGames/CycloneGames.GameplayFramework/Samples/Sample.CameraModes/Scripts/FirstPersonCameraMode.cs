using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// First-person camera mode.
    /// Positions the camera at the actor's eye position with optional offset.
    /// Ideal for FPS games or direct POV gameplay.
    /// </summary>
    public class FirstPersonCameraMode : CameraMode
    {
        [SerializeField] private Vector3 eyeOffset = Vector3.zero;
        [SerializeField] private bool bFollowControlRotation = true;

        public Vector3 EyeOffset { get => eyeOffset; set => eyeOffset = value; }
        public bool FollowControlRotation { get => bFollowControlRotation; set => bFollowControlRotation = value; }

        public override CameraPose Evaluate(CameraContext context, in CameraPose basePose, float deltaTime)
        {
            Actor target = context?.CurrentViewTarget;
            if (target == null) return basePose;

            // Get target's eye view
            target.CalcCamera(deltaTime, out CameraPose eyePose, basePose.Fov);

            // Apply eye offset (typically for specific named bone or attachment point)
            Vector3 offsetPosition = eyePose.Position + eyePose.Rotation * eyeOffset;

            // Use target rotation or apply additional look input
            Quaternion viewRotation = bFollowControlRotation ? eyePose.Rotation : Quaternion.identity;

            return new CameraPose(offsetPosition, viewRotation, basePose.Fov);
        }
    }
}
