using UnityEngine;

namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    public static class MovementUnityLifetime
    {
        // UnityEngine.Object == is resolved on the static type. An interface-typed
        // reference bypasses the overload and still calls a destroyed object.
        public static bool IsAlive(object candidate)
        {
            if (candidate == null) return false;
            if (candidate is Object unityObject) return unityObject != null;
            return true;
        }

        public static T Alive<T>(T candidate) where T : class
        {
            return IsAlive(candidate) ? candidate : null;
        }
    }

    public interface IMovementRootMotionCapture
    {
    }
}
