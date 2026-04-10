using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public struct CameraPose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float Fov;

        public CameraPose(Vector3 position, Quaternion rotation, float fov)
        {
            Position = position;
            Rotation = rotation;
            Fov = fov;
        }

        public static CameraPose Lerp(in CameraPose fromPose, in CameraPose toPose, float t)
        {
            return new CameraPose(
                Vector3.LerpUnclamped(fromPose.Position, toPose.Position, t),
                Quaternion.SlerpUnclamped(fromPose.Rotation, toPose.Rotation, t),
                Mathf.LerpUnclamped(fromPose.Fov, toPose.Fov, t));
        }
    }
}