using System;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Extended contract for native mobile device vibration (phone/tablet built-in motor).
    /// Inherits universal haptic operations from <see cref="IHapticFeedbackService"/>.
    /// </summary>
    public interface IMobileVibrationService : IHapticFeedbackService, IDisposable
    {
        bool HasVibrator { get; }

        void Vibrate();
        void Vibrate(long milliseconds);
        void Vibrate(long[] pattern, int repeat = -1);
        void VibratePop();
        void VibratePeek();
        void VibrateNope();

        void VibrateIOS(IOSImpactStyle style);
        void VibrateIOS(IOSNotificationStyle style);
        void VibrateIOSSelection();

        /// <summary>
        /// Real-time parameter modulation on the active continuous haptic (iOS 13+ Core Haptics).
        /// No-op on platforms without hardware support.
        /// </summary>
        void UpdateContinuousParameters(float intensity, float sharpness);
    }
}
