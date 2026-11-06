namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Time source for perception aging. Inject a custom implementation to drive perception from a
    /// fixed-step simulation, a replay clock, or a dedicated server scheduler instead of
    /// <see cref="UnityEngine.Time"/>.
    /// </summary>
    /// <remarks>
    /// The value must be monotonic and expressed in seconds. Perception never compares clocks from
    /// two different implementations, so switching implementations at runtime invalidates the
    /// aging baseline of every sensor.
    /// </remarks>
    public interface IPerceptionClock
    {
        double NowSeconds { get; }
    }

    /// <summary>
    /// Default clock bound to <see cref="UnityEngine.Time.timeAsDouble"/>.
    /// </summary>
    internal sealed class UnityPerceptionClock : IPerceptionClock
    {
        public static readonly UnityPerceptionClock Instance = new UnityPerceptionClock();

        public double NowSeconds => UnityEngine.Time.timeAsDouble;
    }
}
