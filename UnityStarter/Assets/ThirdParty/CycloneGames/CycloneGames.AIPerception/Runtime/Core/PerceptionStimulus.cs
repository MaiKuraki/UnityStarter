using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace CycloneGames.AIPerception.Runtime
{
    public enum PerceptionStimulusKind : byte
    {
        /// <summary>Instantaneous damage. Strength is the incoming damage amount.</summary>
        Damage = 0,

        /// <summary>Instantaneous noise. Strength is the loudness of the event.</summary>
        Noise = 1
    }

    /// <summary>
    /// One-shot stimulus reported by gameplay code. Unlike the registered snapshot, a stimulus has no
    /// lifetime: it is consumed once by every sensor that is interested and then ages out of the ring.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PerceptionStimulus
    {
        /// <summary>Registered perceptible that caused the stimulus. Must resolve in the owning registry.</summary>
        public PerceptibleHandle Instigator;

        public float3 Position;
        public float Strength;
        public PerceptionStimulusKind Kind;

        /// <summary>Clock time at report, taken from the owning manager.</summary>
        public double Timestamp;

        /// <summary>Monotonic id assigned by the manager. Sensors use it to consume each event once.</summary>
        public long Sequence;
    }
}
