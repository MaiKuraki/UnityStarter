using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Scriptable Object configuration for universal camera parameters.
    /// Contains only fields that apply to any camera type.
    ///
    /// Camera-type-specific parameters (follow distance, orbital radius, etc.)
    /// belong on the individual CameraMode subclass, not here.
    /// </summary>
    [CreateAssetMenu(fileName = "CameraProfile", menuName = "CycloneGames/GameplayFramework/Camera/CameraProfile")]
    public class CameraProfile : ScriptableObject
    {
        [Header("Camera View")]
        [SerializeField] private float fov = 60f;

        [Header("Blend")]
        [Tooltip("Fallback blend duration used when the active CameraMode does not specify one.")]
        [SerializeField] private float blendDuration = 0.25f;

        public float FOV => fov;
        public float BlendDuration => blendDuration;

        /// <summary>
        /// Apply this profile to a CameraManager instance.
        /// Sets the default FOV and blend duration on the manager.
        /// </summary>
        public virtual void ApplyTo(CameraManager cameraManager)
        {
            if (cameraManager == null) return;

            cameraManager.SetDefaultFOV(fov);
            cameraManager.SetDefaultBlendDuration(blendDuration);
        }
    }
}
