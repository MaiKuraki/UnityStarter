using System;
using UnityEngine;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Contract for device RGB light control (e.g. DualSense light bar, keyboard RGB).
    /// </summary>
    public interface IDeviceLightService : IDisposable
    {
        bool IsAvailable { get; }
        bool IsActive { get; set; }

        void Initialize();

        /// <summary>Set the device light to a solid color.</summary>
        void SetColor(Color color);

        /// <summary>Flash the device light between two colors.</summary>
        void Flash(Color onColor, Color offColor, float onDurationSeconds, float offDurationSeconds);

        /// <summary>
        /// Smoothly transition the light color along a Gradient over time.
        /// Gradient alpha channel controls brightness.
        /// Requires a per-frame update loop (MonoBehaviour or playerloop hook) in the implementation.
        /// </summary>
        /// <param name="gradient">Color over normalized time (0~1). Alpha modulates brightness.</param>
        /// <param name="durationSeconds">Total animation duration.</param>
        /// <param name="sampleIntervalMs">Update interval in ms (default 50, i.e. 20Hz). Lower = smoother.</param>
        void PlayGradient(Gradient gradient, float durationSeconds, int sampleIntervalMs = 50);

        /// <summary>
        /// Pulse/breathe the light brightness on a base color following an AnimationCurve.
        /// Curve X-axis: normalized time (0~1). Y-axis: brightness multiplier (0~1).
        /// </summary>
        /// <param name="baseColor">The base RGB color. Brightness is scaled by the curve value.</param>
        /// <param name="intensityCurve">Brightness over normalized time.</param>
        /// <param name="durationSeconds">Total animation duration.</param>
        /// <param name="sampleIntervalMs">Update interval in ms (default 50).</param>
        void PlayIntensityCurve(Color baseColor, AnimationCurve intensityCurve, float durationSeconds, int sampleIntervalMs = 50);

        /// <summary>Stop any running light animation.</summary>
        void CancelAnimation();

        /// <summary>Turn off / reset to default.</summary>
        void Reset();
    }
}
