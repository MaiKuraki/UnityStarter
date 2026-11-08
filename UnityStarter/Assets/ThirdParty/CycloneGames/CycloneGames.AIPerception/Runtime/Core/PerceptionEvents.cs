using Unity.Mathematics;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>Why a perception event was raised.</summary>
    public enum PerceptionEventKind : byte
    {
        /// <summary>The observer started tracking this handle.</summary>
        Acquired = 0,

        /// <summary>
        /// The observer stopped tracking this handle: the stimulus aged out, faded below the
        /// visibility floor, or was evicted by memory capacity.
        /// </summary>
        Lost = 1
    }

    /// <summary>
    /// One perception transition, delivered to <see cref="IPerceptionEventListener"/>.
    /// </summary>
    /// <remarks>
    /// Readonly with no references, so raising it allocates nothing. <see cref="Position"/> and
    /// <see cref="Strength"/> carry the last known values, including on
    /// <see cref="PerceptionEventKind.Lost"/>.
    /// </remarks>
    public readonly struct PerceptionEvent
    {
        public PerceptionEvent(
            int sensorId,
            SensorType sensorType,
            PerceptionEventKind kind,
            PerceptibleHandle handle,
            float3 position,
            float strength,
            float distance,
            double timestamp)
        {
            SensorId = sensorId;
            SensorType = sensorType;
            Kind = kind;
            Handle = handle;
            Position = position;
            Strength = strength;
            Distance = distance;
            Timestamp = timestamp;
        }

        /// <summary>Identity of the sensor that produced the transition.</summary>
        public readonly int SensorId;

        public readonly SensorType SensorType;

        public readonly PerceptionEventKind Kind;

        public readonly PerceptibleHandle Handle;

        /// <summary>Last known position of the target.</summary>
        public readonly float3 Position;

        /// <summary>Visibility at the last detection, 0..1.</summary>
        public readonly float Strength;

        /// <summary>Distance at the last detection.</summary>
        public readonly float Distance;

        /// <summary>Perception clock reading when the transition was committed.</summary>
        public readonly double Timestamp;
    }

    /// <summary>
    /// Receives perception transitions from a <see cref="SensorManager"/>.
    /// </summary>
    /// <remarks>
    /// Called on the perception owner thread, synchronously, once per transition. Implementations
    /// must not add or remove listeners from inside the callback; the manager rejects that with an
    /// <see cref="System.InvalidOperationException"/>.
    /// </remarks>
    public interface IPerceptionEventListener
    {
        void OnPerceptionEvent(in PerceptionEvent perceptionEvent);
    }

    /// <summary>
    /// Optional sensor interface: exposes the buffer whose stimulus-memory transitions the manager
    /// drains after each update. Built-in senses implement it; custom senses can either implement it
    /// or call <see cref="SensorManager.PublishEvent"/> directly.
    /// </summary>
    internal interface IPerceptionEventSource
    {
        SensorResultBuffer EventBuffer { get; }
    }
}
