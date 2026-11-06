using System;

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

    /// <summary>
    /// Clock driven by the caller. Advance <see cref="NowSeconds"/> from a fixed-step loop, a replay
    /// cursor, or a test, and every sensor ages against that value.
    /// </summary>
    public sealed class ManualPerceptionClock : IPerceptionClock
    {
        public ManualPerceptionClock(double nowSeconds = 0d)
        {
            NowSeconds = nowSeconds;
        }

        public double NowSeconds { get; set; }

        public void Advance(double seconds)
        {
            NowSeconds += seconds;
        }
    }

    /// <summary>
    /// Clock backed by a delegate, so a product can route perception onto any existing time source
    /// (a scheduler lane, a choreography driver, a match clock) without this package referencing it.
    /// </summary>
    /// <example>
    /// <code>
    /// var clock = new DelegatePerceptionClock(() =&gt; scheduler.ElapsedSeconds);
    /// var manager = new SensorManager(registry, 4096, clock);
    /// </code>
    /// </example>
    public sealed class DelegatePerceptionClock : IPerceptionClock
    {
        private readonly Func<double> _nowSeconds;

        public DelegatePerceptionClock(Func<double> nowSeconds)
        {
            _nowSeconds = nowSeconds ?? throw new ArgumentNullException(nameof(nowSeconds));
        }

        public double NowSeconds => _nowSeconds();
    }
}
