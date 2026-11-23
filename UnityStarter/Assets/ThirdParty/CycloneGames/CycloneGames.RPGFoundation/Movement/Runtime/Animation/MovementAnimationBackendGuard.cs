using UnityEngine;
using CycloneGames.Logging;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    public interface IMovementAnimationBackend
    {
    }

    // Inspection is side-effect free; TryClaim is the only path that logs
    // and disables a conflicting backend.
    public static class MovementAnimationBackendGuard
    {
        private static readonly LogChannel Log = RpgMovementLog.Channel;

        public static MovementBackendClaim Evaluate(MonoBehaviour self)
        {
            if (self == null) return new MovementBackendClaim(0, -1);

            MonoBehaviour[] components = self.GetComponents<MonoBehaviour>();
            int backendCount = 0;
            int selfOrder = -1;
            for (int i = 0; i < components.Length; i++)
            {
                MonoBehaviour component = components[i];
                if (component == null) continue;
                if (!(component is IMovementAnimationBackend)) continue;
                if (ReferenceEquals(component, self)) selfOrder = backendCount;
                backendCount++;
            }
            return new MovementBackendClaim(backendCount, selfOrder);
        }

        public static bool TryClaim(MonoBehaviour self, string backendName)
        {
            MovementBackendClaim claim = Evaluate(self);
            if (claim.IsOwner) return true;

            Log.Error(
                "[MovementAnimationBackendGuard] " + claim.BackendCount +
                " movement animation backends are attached to '" + self.gameObject.name +
                "'. One Animator can be driven by only one backend. Disabling " +
                backendName + "; remove one backend from the GameObject.");
            self.enabled = false;
            return false;
        }

        public static int Count(MonoBehaviour target)
        {
            return Evaluate(target).BackendCount;
        }
    }
    public static class MovementRootMotionCaptureGuard
    {
        private static readonly LogChannel Log = RpgMovementLog.Channel;

        public static MovementBackendClaim Evaluate(MonoBehaviour self)
        {
            if (self == null) return new MovementBackendClaim(0, -1);

            MonoBehaviour[] components = self.GetComponents<MonoBehaviour>();
            int captureCount = 0;
            int selfOrder = -1;
            for (int i = 0; i < components.Length; i++)
            {
                MonoBehaviour component = components[i];
                if (component == null) continue;
                if (!(component is IMovementRootMotionCapture)) continue;
                if (ReferenceEquals(component, self)) selfOrder = captureCount;
                captureCount++;
            }
            return new MovementBackendClaim(captureCount, selfOrder);
        }

        public static bool TryClaim(MonoBehaviour self, string captureName)
        {
            MovementBackendClaim claim = Evaluate(self);
            if (claim.IsOwner) return true;

            Log.Error(
                "[MovementRootMotionCaptureGuard] " + claim.BackendCount +
                " root motion captures are attached to '" + self.gameObject.name +
                "'. Unity sends OnAnimatorMove to each of them, so the same delta would " +
                "be consumed twice. Disabling " + captureName +
                "; keep exactly one capture on the Animator's GameObject.");
            self.enabled = false;
            return false;
        }

        public static int Count(MonoBehaviour target)
        {
            return Evaluate(target).BackendCount;
        }
    }

}
