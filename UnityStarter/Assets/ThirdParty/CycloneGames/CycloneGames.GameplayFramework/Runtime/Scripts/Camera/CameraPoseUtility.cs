using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public static class CameraPoseUtility
    {
        public static CameraPose GetCameraPose(Transform transform, float fallbackFov)
        {
            if (transform == null)
            {
                return new CameraPose(Vector3.zero, Quaternion.identity, fallbackFov);
            }

            return new CameraPose(transform.position, transform.rotation, fallbackFov);
        }
    }
}