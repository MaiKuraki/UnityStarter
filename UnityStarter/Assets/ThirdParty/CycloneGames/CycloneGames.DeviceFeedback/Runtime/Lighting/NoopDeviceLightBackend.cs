using UnityEngine;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Explicit no-op backend used when no platform light integration is installed.
    /// </summary>
    public sealed class NoopDeviceLightBackend : IDeviceLightBackend
    {
        public static readonly NoopDeviceLightBackend Instance = new NoopDeviceLightBackend();

        private NoopDeviceLightBackend()
        {
        }

        public bool IsAvailable => false;

        public void Initialize()
        {
        }

        public void SetColor(Color color)
        {
        }

        public void Flash(Color onColor, Color offColor, float onDurationSeconds, float offDurationSeconds)
        {
        }

        public void PlayGradient(Gradient gradient, float durationSeconds, int sampleIntervalMs)
        {
        }

        public void PlayIntensityCurve(Color baseColor, AnimationCurve intensityCurve, float durationSeconds, int sampleIntervalMs)
        {
        }

        public void CancelAnimation()
        {
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }
}
