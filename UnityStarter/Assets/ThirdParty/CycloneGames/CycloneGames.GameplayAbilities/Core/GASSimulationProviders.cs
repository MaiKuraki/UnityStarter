using System;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Provides time for simulation. Override for deterministic replay.
    /// </summary>
    public interface ISimulationTimeProvider
    {
        long DeltaTimeRaw { get; }
        long TotalTimeRaw { get; }
        GASFixedValue FixedDeltaTime { get; }
        GASFixedValue FixedTotalTime { get; }
        int FrameCount { get; }
    }

    /// <summary>
    /// Provides random values for simulation. Override for deterministic replay.
    /// </summary>
    public interface ISimulationRandomProvider
    {
        long NextRaw();
        GASFixedValue NextFixed();
        GASFixedValue NextFixed(GASFixedValue min, GASFixedValue max);
        int NextInt(int min, int max);
    }

    /// <summary>
    /// Default time provider using system time.
    /// </summary>
    public sealed class DefaultTimeProvider : ISimulationTimeProvider
    {
        public static readonly DefaultTimeProvider Instance = new DefaultTimeProvider();
        private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        private double _lastTime;
        private double _deltaTime;
        private int _frameCount;

        private DefaultTimeProvider() { }

        /// <summary>
        /// Advances the time provider by sampling the stopwatch. Call exactly once per frame.
        /// Separating the advance step from the read step eliminates the data race where two callers
        /// reading DeltaTime would each modify _lastTime, producing incorrect deltas.
        /// </summary>
        public void Advance()
        {
            double current = _stopwatch.Elapsed.TotalSeconds;
            _deltaTime = current - _lastTime;
            _lastTime = current;
            _frameCount++;
        }

        public long DeltaTimeRaw => GASFixedValue.FromDouble(_deltaTime).RawValue;
        public long TotalTimeRaw => GASFixedValue.FromDouble(_stopwatch.Elapsed.TotalSeconds).RawValue;
        public GASFixedValue FixedDeltaTime => GASFixedValue.FromRaw(DeltaTimeRaw);
        public GASFixedValue FixedTotalTime => GASFixedValue.FromRaw(TotalTimeRaw);
        public int FrameCount => _frameCount;
    }

    /// <summary>
    /// Default random provider using System.Random.
    /// </summary>
    public sealed class DefaultRandomProvider : ISimulationRandomProvider
    {
        public static readonly DefaultRandomProvider Instance = new DefaultRandomProvider();

        // [ThreadStatic] per-thread Random instances eliminate thread-safety concerns with System.Random.
        // Each thread gets a uniquely seeded instance on first access, preventing determinism issues.
        [System.ThreadStatic]
        private static Random s_ThreadRandom;

        private static Random ThreadRandom
            => s_ThreadRandom ??= new Random(
                System.Environment.TickCount ^ System.Threading.Thread.CurrentThread.ManagedThreadId);

        private DefaultRandomProvider() { }

        public int NextInt(int min, int max) => ThreadRandom.Next(min, max);
        public long NextRaw() => GASFixedValue.FromDouble(ThreadRandom.NextDouble()).RawValue;
        public GASFixedValue NextFixed() => GASFixedValue.FromRaw(NextRaw());
        public GASFixedValue NextFixed(GASFixedValue min, GASFixedValue max)
        {
            return min + (max - min) * NextFixed();
        }
    }

    /// <summary>
    /// Deterministic time provider for unit tests and replays.
    /// </summary>
    public sealed class DeterministicTimeProvider : ISimulationTimeProvider
    {
        private long _totalTimeRaw;
        private long _deltaTimeRaw;
        private int _frameCount;

        public long DeltaTimeRaw => _deltaTimeRaw;
        public long TotalTimeRaw => _totalTimeRaw;
        public GASFixedValue FixedDeltaTime => GASFixedValue.FromRaw(_deltaTimeRaw);
        public GASFixedValue FixedTotalTime => GASFixedValue.FromRaw(_totalTimeRaw);
        public int FrameCount => _frameCount;

        public void Tick(GASFixedValue deltaTime)
        {
            TickRaw(deltaTime.RawValue);
        }

        public void TickRaw(long deltaTimeRaw)
        {
            _deltaTimeRaw = deltaTimeRaw;
            _totalTimeRaw += deltaTimeRaw;
            _frameCount++;
        }

        /// <summary>
        /// Resets time to zero.
        /// </summary>
        public void Reset()
        {
            _totalTimeRaw = 0L;
            _deltaTimeRaw = 0L;
            _frameCount = 0;
        }
    }

    /// <summary>
    /// Deterministic random provider for unit tests and replays.
    /// </summary>
    public sealed class DeterministicRandomProvider : ISimulationRandomProvider
    {
        private uint _state;

        public DeterministicRandomProvider(int seed)
        {
            _state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
        }

        public int NextInt(int min, int max)
        {
            if (min >= max)
            {
                throw new ArgumentOutOfRangeException(nameof(max), "max must be greater than min.");
            }

            uint range = unchecked((uint)(max - min));
            return min + (int)(NextUInt() % range);
        }

        public long NextRaw()
        {
            return NextUInt();
        }

        public GASFixedValue NextFixed()
        {
            return GASFixedValue.FromRaw(NextRaw());
        }

        public GASFixedValue NextFixed(GASFixedValue min, GASFixedValue max)
        {
            return min + (max - min) * NextFixed();
        }

        private uint NextUInt()
        {
            uint value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value == 0 ? 0x9E3779B9u : value;
            return _state;
        }
    }
}
