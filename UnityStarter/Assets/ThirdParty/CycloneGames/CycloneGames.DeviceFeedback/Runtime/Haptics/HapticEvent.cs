using System;
using UnityEngine;

namespace CycloneGames.DeviceFeedback.Runtime
{
    public enum HapticEventType : byte
    {
        Transient,
        Continuous
    }

    [Serializable]
    public struct HapticEvent : IComparable<HapticEvent>
    {
        [Tooltip("Transient = sharp click; Continuous = sustained vibration")]
        public HapticEventType type;

        [Tooltip("Start time in seconds relative to clip start")]
        [Min(0f)] public float time;

        [Tooltip("Duration in seconds (Continuous only; ignored for Transient)")]
        [Min(0f)] public float duration;

        [Range(0f, 1f)]
        public float intensity;

        [Range(0f, 1f)]
        [Tooltip("0 = deep/broad, 1 = sharp/crisp (iOS Core Haptics native; approximated on other platforms)")]
        public float sharpness;

        public int CompareTo(HapticEvent other) => time.CompareTo(other.time);
    }
}
