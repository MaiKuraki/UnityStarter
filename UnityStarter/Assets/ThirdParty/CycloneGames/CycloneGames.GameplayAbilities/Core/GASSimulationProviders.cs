using System;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Provides time for simulation. Override for deterministic replay.
    /// </summary>
    public interface ISimulationTimeProvider
    {
        float DeltaTime { get; }
        float TotalTime { get; }
        int FrameCount { get; }
    }

    /// <summary>
    /// Provides random values for simulation. Override for deterministic replay.
    /// </summary>
    public interface ISimulationRandomProvider
    {
        float NextFloat();
        float NextFloat(float min, float max);
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

        /// <summary>Read-only. Call Advance() once per frame before reading this value.</summary>
        public float DeltaTime => (float)_deltaTime;
        public float TotalTime => (float)_stopwatch.Elapsed.TotalSeconds;
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

        public float NextFloat() => (float)ThreadRandom.NextDouble();
        public float NextFloat(float min, float max) => min + (float)ThreadRandom.NextDouble() * (max - min);
        public int NextInt(int min, int max) => ThreadRandom.Next(min, max);
    }

    /// <summary>
    /// Deterministic time provider for unit tests and replays.
    /// </summary>
    public sealed class DeterministicTimeProvider : ISimulationTimeProvider
    {
        private double _totalTime;
        private double _deltaTime;
        private int _frameCount;

        public float DeltaTime => (float)_deltaTime;
        public float TotalTime => (float)_totalTime;
        public int FrameCount => _frameCount;

        /// <summary>
        /// Advances time by the specified delta.
        /// </summary>
        public void Tick(float deltaTime)
        {
            _deltaTime = deltaTime;
            _totalTime += deltaTime;
            _frameCount++;
        }

        /// <summary>
        /// Resets time to zero.
        /// </summary>
        public void Reset()
        {
            _totalTime = 0d;
            _deltaTime = 0d;
            _frameCount = 0;
        }
    }

    /// <summary>
    /// Deterministic random provider for unit tests and replays.
    /// </summary>
    public sealed class DeterministicRandomProvider : ISimulationRandomProvider
    {
        private readonly Random _random;

        public DeterministicRandomProvider(int seed)
        {
            _random = new Random(seed);
        }

        public float NextFloat() => (float)_random.NextDouble();
        public float NextFloat(float min, float max) => min + (float)_random.NextDouble() * (max - min);
        public int NextInt(int min, int max) => _random.Next(min, max);
    }
}
