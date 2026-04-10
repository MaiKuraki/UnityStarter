using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Orbital camera mode.
    /// Rotates camera around the target actor at a fixed distance and height.
    /// Useful for third-person action games, RTS, or inspection cameras.
    /// </summary>
    public class OrbitalCameraMode : CameraMode
    {
        [SerializeField] private float radius = 5f;
        [SerializeField] private float height = 2f;
        [SerializeField] private float rotationSpeed = 45f; // degrees/sec
        [SerializeField] private bool bAutoRotate = false;

        private float currentYaw;

        public float Radius { get => radius; set => radius = value; }
        public float Height { get => height; set => height = value; }
        public float RotationSpeed { get => rotationSpeed; set => rotationSpeed = value; }
        public bool AutoRotate { get => bAutoRotate; set => bAutoRotate = value; }

        public override void Tick(CameraContext context, float deltaTime)
        {
            if (bAutoRotate && rotationSpeed != 0)
            {
                currentYaw += rotationSpeed * deltaTime;
                if (currentYaw >= 360) currentYaw -= 360;
                else if (currentYaw < 0) currentYaw += 360;
            }
        }

        public override CameraPose Evaluate(CameraContext context, in CameraPose basePose, float deltaTime)
        {
            Actor target = context?.CurrentViewTarget;
            if (target == null) return basePose;

            Vector3 targetPos = target.GetActorLocation() + Vector3.up * height;

            // Calculate orbital position using current yaw
            float radians = Mathf.Deg2Rad * currentYaw;
            Vector3 cameraPos = targetPos + new Vector3(
                Mathf.Sin(radians) * radius,
                0,
                Mathf.Cos(radians) * radius
            );

            // Look at target
            Vector3 direction = (targetPos - cameraPos).normalized;
            Quaternion lookRot = Quaternion.LookRotation(direction);

            return new CameraPose(cameraPos, lookRot, basePose.Fov);
        }

        /// <summary>
        /// Manually set the orbital yaw angle in degrees.
        /// </summary>
        public void SetYaw(float yawDegrees)
        {
            currentYaw = yawDegrees;
            if (currentYaw >= 360) currentYaw -= 360;
            else if (currentYaw < 0) currentYaw += 360;
        }

        /// <summary>
        /// Get the current orbital yaw angle in degrees.
        /// </summary>
        public float GetYaw() => currentYaw;
    }
}
