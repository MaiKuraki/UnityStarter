using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Built-in blend curve options for zero-allocation fast path.
    /// </summary>
    public enum CameraBlendCurveType : byte
    {
        Linear = 0,
        SmoothStep = 1,
        EaseOut = 2,
        EaseIn = 3,
        Custom = 255
    }

    /// <summary>
    /// Central evaluator for built-in blend curves.
    /// </summary>
    public static class CameraBlendCurveEvaluator
    {
        public static float Evaluate(CameraBlendCurveType curveType, float t)
        {
            float clampedT = Mathf.Clamp01(t);
            switch (curveType)
            {
                case CameraBlendCurveType.Linear:
                    return clampedT;
                case CameraBlendCurveType.SmoothStep:
                    return Mathf.SmoothStep(0f, 1f, clampedT);
                case CameraBlendCurveType.EaseOut:
                    return 1f - Mathf.Pow(1f - clampedT, 3f);
                case CameraBlendCurveType.EaseIn:
                    return Mathf.Pow(clampedT, 3f);
                case CameraBlendCurveType.Custom:
                default:
                    return clampedT;
            }
        }
    }

    /// <summary>
    /// Contract for camera blend interpolation curves.
    /// Allows customization of how the camera transitions between poses.
    /// </summary>
    public interface ICameraBlendCurve
    {
        /// <summary>
        /// Evaluate blend progress.
        /// t parameter is normalized [0, 1] representing blend progress.
        /// Returns interpolation factor [0, 1] for LERPing between start and target poses.
        /// </summary>
        float Evaluate(float t);
    }

    /// <summary>
    /// Custom curve using AnimationCurve for fine control.
    /// </summary>
    public sealed class CustomCameraBlendCurve : ICameraBlendCurve
    {
        private readonly AnimationCurve curve;

        public CustomCameraBlendCurve(AnimationCurve animationCurve)
        {
            curve = animationCurve ?? AnimationCurve.Linear(0, 0, 1, 1);
        }

        public float Evaluate(float t) => curve.Evaluate(t);
    }
}
