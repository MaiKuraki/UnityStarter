using System;
using UnityEngine;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Low-level device light adapter. Platform integrations own device discovery,
    /// timing, player loop hooks, and native API calls.
    /// </summary>
    public interface IDeviceLightBackend : IDisposable
    {
        bool IsAvailable { get; }

        void Initialize();
        void SetColor(Color color);
        void Flash(Color onColor, Color offColor, float onDurationSeconds, float offDurationSeconds);
        void PlayGradient(Gradient gradient, float durationSeconds, int sampleIntervalMs);
        void PlayIntensityCurve(Color baseColor, AnimationCurve intensityCurve, float durationSeconds, int sampleIntervalMs);
        void CancelAnimation();
        void Reset();
    }
}
