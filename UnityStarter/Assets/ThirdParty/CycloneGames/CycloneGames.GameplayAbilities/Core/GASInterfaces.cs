using System;
using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayAbilities.Core
{
    #region Simulation Interfaces

    /// <summary>
    /// Core interface for AbilitySystemComponent simulation.
    /// Enables pure C# simulation and unit testing without Unity dependencies.
    /// </summary>
    public interface IAbilitySystemComponent
    {
        /// <summary>
        /// An opaque reference to the owning actor (e.g., player controller).
        /// </summary>
        object OwnerActor { get; }

        /// <summary>
        /// An opaque reference to the physical avatar actor.
        /// </summary>
        object AvatarActor { get; }

        /// <summary>
        /// Gets the current tag count container.
        /// </summary>
        ITagCountContainer CombinedTags { get; }

        /// <summary>
        /// Gets an attribute by name.
        /// </summary>
        IGameplayAttribute GetAttribute(string name);

        /// <summary>
        /// Ticks the ASC simulation.
        /// </summary>
        void Tick(float deltaTime, bool isServer);
    }

    /// <summary>
    /// Interface abstraction for tag count containers.
    /// </summary>
    public interface ITagCountContainer
    {
        bool HasTag(GameplayTag tag);
        bool HasAny(IEnumerable<GameplayTag> tags);
        bool HasAll(IEnumerable<GameplayTag> tags);
        void AddTag(GameplayTag tag);
        void RemoveTag(GameplayTag tag);
        void Clear();
    }

    /// <summary>
    /// Interface for gameplay attributes.
    /// </summary>
    public interface IGameplayAttribute
    {
        string Name { get; }
        float BaseValue { get; }
        float CurrentValue { get; }
    }

    /// <summary>
    /// Interface for effect spec simulation.
    /// </summary>
    public interface IGameplayEffectSpec
    {
        int Level { get; }
        float Duration { get; }
        IGameplayEffectContext Context { get; }
    }

    /// <summary>
    /// Interface for effect context.
    /// </summary>
    public interface IGameplayEffectContext
    {
        PredictionKey PredictionKey { get; set; }
    }

    #endregion

    #region GameplayCue Interfaces (DI-friendly)

    /// <summary>
    /// Describes the type of event that triggered a GameplayCue.
    /// </summary>
    public enum EGameplayCueEvent
    {
        OnActive,
        WhileActive,
        Removed,
        Executed
    }

    /// <summary>
    /// Parameters passed to GameplayCue handlers. Uses object types to avoid Unity dependencies.
    /// </summary>
    public readonly struct GameplayCueEventParams
    {
        public readonly object Source;
        public readonly object Target;
        public readonly object EffectSpec;
        public readonly float Magnitude;

        public GameplayCueEventParams(object source, object target, object effectSpec, float magnitude = 0f)
        {
            Source = source;
            Target = target;
            EffectSpec = effectSpec;
            Magnitude = magnitude;
        }
    }

    /// <summary>
    /// Interface for GameplayCue management. Allows DI injection and server-side mocking.
    /// </summary>
    public interface IGameplayCueManager
    {
        void RegisterStaticCue(GameplayTag cueTag, string assetAddress);
        void HandleCue(object asc, GameplayTag cueTag, EGameplayCueEvent eventType, GameplayCueEventParams parameters);
        void RemoveAllCuesFor(object asc);
        void Initialize(object assetPackage);
    }

    #endregion

    #region Service Locator

    /// <summary>
    /// Service locator for GAS services. Provides default implementations while allowing DI override.
    /// Thread-safe with volatile read/write for lock-free access after initialization.
    /// </summary>
    public static class GASServices
    {
        private static volatile IGameplayCueManager s_CueManager;
        private static volatile ISimulationTimeProvider s_TimeProvider;
        private static volatile ISimulationRandomProvider s_RandomProvider;

        /// <summary>
        /// Gets or sets the GameplayCue manager. Returns NullGameplayCueManager if not set.
        /// </summary>
        public static IGameplayCueManager CueManager
        {
            get => s_CueManager ?? NullGameplayCueManager.Instance;
            set => s_CueManager = value;
        }

        /// <summary>
        /// Gets or sets the time provider for simulation. Allows deterministic replay.
        /// </summary>
        public static ISimulationTimeProvider TimeProvider
        {
            get => s_TimeProvider ?? DefaultTimeProvider.Instance;
            set => s_TimeProvider = value;
        }

        /// <summary>
        /// Gets or sets the random provider for simulation. Allows deterministic replay.
        /// </summary>
        public static ISimulationRandomProvider RandomProvider
        {
            get => s_RandomProvider ?? DefaultRandomProvider.Instance;
            set => s_RandomProvider = value;
        }

        /// <summary>
        /// Resets all services to null. Call during game shutdown or test teardown.
        /// </summary>
        public static void Reset()
        {
            s_CueManager = null;
            s_TimeProvider = null;
            s_RandomProvider = null;
        }
    }

    #endregion

    #region Null Object Implementations

    /// <summary>
    /// Null object pattern for server-side or headless environments.
    /// </summary>
    public sealed class NullGameplayCueManager : IGameplayCueManager
    {
        public static readonly NullGameplayCueManager Instance = new NullGameplayCueManager();
        private NullGameplayCueManager() { }

        public void RegisterStaticCue(GameplayTag cueTag, string assetAddress) { }
        public void HandleCue(object asc, GameplayTag cueTag, EGameplayCueEvent eventType, GameplayCueEventParams parameters) { }
        public void RemoveAllCuesFor(object asc) { }
        public void Initialize(object assetPackage) { }
    }

    #endregion

    #region Simulation Providers

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
        private float _lastTime;
        private int _frameCount;

        private DefaultTimeProvider() { }

        public float DeltaTime
        {
            get
            {
                float current = (float)_stopwatch.Elapsed.TotalSeconds;
                float delta = current - _lastTime;
                _lastTime = current;
                _frameCount++;
                return delta;
            }
        }

        public float TotalTime => (float)_stopwatch.Elapsed.TotalSeconds;
        public int FrameCount => _frameCount;
    }

    /// <summary>
    /// Default random provider using System.Random.
    /// </summary>
    public sealed class DefaultRandomProvider : ISimulationRandomProvider
    {
        public static readonly DefaultRandomProvider Instance = new DefaultRandomProvider();
        private readonly Random _random = new Random();

        private DefaultRandomProvider() { }

        public float NextFloat() => (float)_random.NextDouble();
        public float NextFloat(float min, float max) => min + (float)_random.NextDouble() * (max - min);
        public int NextInt(int min, int max) => _random.Next(min, max);
    }

    /// <summary>
    /// Deterministic time provider for unit tests and replays.
    /// </summary>
    public sealed class DeterministicTimeProvider : ISimulationTimeProvider
    {
        private float _totalTime;
        private float _deltaTime;
        private int _frameCount;

        public float DeltaTime => _deltaTime;
        public float TotalTime => _totalTime;
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
            _totalTime = 0;
            _deltaTime = 0;
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

    #endregion

    #region Prediction Key

    /// <summary>
    /// Represents a unique key for client-side prediction events.
    /// Thread-safe using Interlocked operations.
    /// </summary>
    public struct PredictionKey : IEquatable<PredictionKey>
    {
        public int Key { get; private set; }
        private static int s_NextKey = 1;

        public bool IsValid() => Key != 0;

        public static PredictionKey NewKey()
        {
            int key = System.Threading.Interlocked.Increment(ref s_NextKey);
            if (key >= int.MaxValue - 1)
            {
                System.Threading.Interlocked.Exchange(ref s_NextKey, 1);
            }
            return new PredictionKey { Key = key };
        }

        public bool Equals(PredictionKey other) => Key == other.Key;
        public override bool Equals(object obj) => obj is PredictionKey other && Equals(other);
        public override int GetHashCode() => Key;
        public static bool operator ==(PredictionKey left, PredictionKey right) => left.Equals(right);
        public static bool operator !=(PredictionKey left, PredictionKey right) => !left.Equals(right);
    }

    #endregion
}
