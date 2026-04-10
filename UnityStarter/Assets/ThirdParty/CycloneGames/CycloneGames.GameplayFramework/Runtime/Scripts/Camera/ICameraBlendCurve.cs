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
    /// Linear camera blend curve: constant velocity blend.
    /// </summary>
    public sealed class LinearCameraBlendCurve : ICameraBlendCurve
    {
        public static readonly LinearCameraBlendCurve Instance = new LinearCameraBlendCurve();

        private LinearCameraBlendCurve() { }

        public float Evaluate(float t) => t;
    }

    /// <summary>
    /// Smooth step camera blend curve: ease in & out.
    /// Produces smooth acceleration and deceleration.
    /// </summary>
    public sealed class SmoothStepCameraBlendCurve : ICameraBlendCurve
    {
        public static readonly SmoothStepCameraBlendCurve Instance = new SmoothStepCameraBlendCurve();

        private SmoothStepCameraBlendCurve() { }

        public float Evaluate(float t) => Mathf.SmoothStep(0, 1, t);
    }

    /// <summary>
    /// Ease out camera blend curve: starts fast, slows down.
    /// Good for responsive camera transitions.
    /// </summary>
    public sealed class EaseOutCameraBlendCurve : ICameraBlendCurve
    {
        public static readonly EaseOutCameraBlendCurve Instance = new EaseOutCameraBlendCurve();

        private EaseOutCameraBlendCurve() { }

        public float Evaluate(float t) => 1f - Mathf.Pow(1f - t, 3f);
    }

    /// <summary>
    /// Ease in camera blend curve: starts slow, speeds up.
    /// Good for cinematic camera movements.
    /// </summary>
    public sealed class EaseInCameraBlendCurve : ICameraBlendCurve
    {
        public static readonly EaseInCameraBlendCurve Instance = new EaseInCameraBlendCurve();

        private EaseInCameraBlendCurve() { }

        public float Evaluate(float t) => Mathf.Pow(t, 3f);
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
