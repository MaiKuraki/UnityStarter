using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Runtime camera mode that evaluates a ScriptableObject camera preset.
    /// Useful for action-game camera shots authored by designers.
    /// </summary>
    public sealed class PresetCameraMode : CameraMode
    {
        private CameraActionPreset preset;
        private float elapsed;
        private float durationOverride = -1f;

        public override float BlendDuration => preset != null ? preset.BlendDuration : base.BlendDuration;

        public CameraActionPreset Preset => preset;

        public bool IsFinished
        {
            get
            {
                float duration = GetDuration();
                return duration > 0f && elapsed >= duration;
            }
        }

        public void Setup(CameraActionPreset cameraPreset, float overrideDuration = -1f)
        {
            preset = cameraPreset;
            durationOverride = overrideDuration;
            elapsed = 0f;
        }

        public void ResetTime()
        {
            elapsed = 0f;
        }

        public override void Tick(CameraContext context, float deltaTime)
        {
            if (preset == null) return;
            elapsed = Mathf.Max(0f, elapsed + Mathf.Max(0f, deltaTime));
        }

        public override CameraPose Evaluate(CameraContext context, in CameraPose basePose, float deltaTime)
        {
            if (preset == null)
            {
                return basePose;
            }

            Actor target = context != null ? context.CurrentViewTarget : null;
            float duration = GetDuration();
            float normalizedTime = duration > 0.0001f ? Mathf.Clamp01(elapsed / duration) : 1f;
            return preset.EvaluatePose(target, basePose, normalizedTime, deltaTime);
        }

        private float GetDuration()
        {
            if (durationOverride >= 0f)
            {
                return Mathf.Max(0.01f, durationOverride);
            }

            return preset != null ? preset.Duration : 0f;
        }
    }
}
