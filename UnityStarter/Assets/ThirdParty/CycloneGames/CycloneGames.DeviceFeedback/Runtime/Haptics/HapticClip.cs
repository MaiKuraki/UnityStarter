using UnityEngine;

namespace CycloneGames.DeviceFeedback.Runtime
{
    [CreateAssetMenu(fileName = "NewHapticClip", menuName = "CycloneGames/Device Feedback/Haptic Clip")]
    public sealed class HapticClip : ScriptableObject
    {
        [Min(0.01f)]
        public float duration = 0.5f;

        [Tooltip("Intensity over normalized time (0~1). Used when events array is empty.")]
        public AnimationCurve intensityCurve = AnimationCurve.Constant(0f, 1f, 1f);

        [Tooltip("Sharpness over normalized time (0~1). Used when events array is empty.")]
        public AnimationCurve sharpnessCurve = AnimationCurve.Constant(0f, 1f, 0.5f);

        [Tooltip("Discrete haptic events. When populated, curves above are ignored.")]
        public HapticEvent[] events;

        public bool HasEvents => events != null && events.Length > 0;
    }
}
