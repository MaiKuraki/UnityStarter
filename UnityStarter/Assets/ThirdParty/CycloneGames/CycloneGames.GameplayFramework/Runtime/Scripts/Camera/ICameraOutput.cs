using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Applies the final pose produced by CameraManager to one concrete camera backend.
    /// Implementations are activated and released by the owning World on its owner thread.
    /// </summary>
    public interface ICameraOutput
    {
        string DisplayName { get; }
        bool IsActive { get; }
        CameraManager Owner { get; }
        Object OutputObject { get; }

        bool TryPrepare(out Object ownershipResource, out string error);
        bool TryActivate(CameraManager owner, out string error);
        void ApplyPose(in CameraPose pose);
        void Deactivate(CameraManager owner);
    }
}
