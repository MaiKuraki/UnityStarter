using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public struct CameraBlendState
    {
        private CameraPose startPose;
        private float duration;
        private float elapsed;
        private CameraBlendCurveType builtInCurveType;
        private ICameraBlendCurve customCurve;

        public bool IsActive => duration > 0f;
        public float Duration => duration;
        public float Elapsed => elapsed;
        public float Remaining => duration > 0f ? Mathf.Max(0f, duration - elapsed) : 0f;
        public float NormalizedTime => duration > 0.0001f ? Mathf.Clamp01(elapsed / duration) : (duration > 0f ? 1f : 0f);
        public CameraBlendCurveType CurveType => builtInCurveType;
        public bool HasCustomCurve => customCurve != null && builtInCurveType == CameraBlendCurveType.Custom;

        public void Start(in CameraPose fromPose, float blendDuration, ICameraBlendCurve curve = null)
        {
            startPose = fromPose;
            duration = Mathf.Max(0f, blendDuration);
            elapsed = 0f;

            if (curve == null)
            {
                builtInCurveType = CameraBlendCurveType.Linear;
                customCurve = null;
                return;
            }

            builtInCurveType = CameraBlendCurveType.Custom;
            customCurve = curve;
        }

        /// <summary>
        /// Starts a blend using built-in fast path curve evaluation.
        /// </summary>
        public void Start(in CameraPose fromPose, float blendDuration, CameraBlendCurveType curveType)
        {
            startPose = fromPose;
            duration = Mathf.Max(0f, blendDuration);
            elapsed = 0f;
            builtInCurveType = curveType == CameraBlendCurveType.Custom ? CameraBlendCurveType.Linear : curveType;
            customCurve = null;
        }

        public CameraPose Evaluate(in CameraPose targetPose, float deltaTime)
        {
            if (duration <= 0f)
            {
                return targetPose;
            }

            elapsed += Mathf.Max(0f, deltaTime);
            float normalizedT = duration <= 0.0001f ? 1f : Mathf.Clamp01(elapsed / duration);

            float easedT;
            if (customCurve != null && builtInCurveType == CameraBlendCurveType.Custom)
            {
                easedT = customCurve.Evaluate(normalizedT);
            }
            else
            {
                easedT = CameraBlendCurveEvaluator.Evaluate(builtInCurveType, normalizedT);
            }

            CameraPose blendedPose = CameraPose.Lerp(startPose, targetPose, easedT);
            if (normalizedT >= 1f)
            {
                duration = 0f;
            }

            return blendedPose;
        }

        /// <summary>
        /// Set a custom blend curve for this blend state.
        /// </summary>
        public void SetBlendCurve(ICameraBlendCurve curve)
        {
            if (curve == null)
            {
                builtInCurveType = CameraBlendCurveType.Linear;
                customCurve = null;
                return;
            }

            builtInCurveType = CameraBlendCurveType.Custom;
            customCurve = curve;
        }

        /// <summary>
        /// Sets a built-in blend curve for fast path evaluation.
        /// </summary>
        public void SetBlendCurve(CameraBlendCurveType curveType)
        {
            builtInCurveType = curveType == CameraBlendCurveType.Custom ? CameraBlendCurveType.Linear : curveType;
            customCurve = null;
        }
    }
}