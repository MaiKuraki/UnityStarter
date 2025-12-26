using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Describes the type of event that triggered a GameplayCue.
    /// Shared between client (VFX) and server (no-op) implementations.
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
        /// <summary>
        /// Registers a static cue by tag and address.
        /// </summary>
        void RegisterStaticCue(GameplayTag cueTag, string assetAddress);

        /// <summary>
        /// Handles a cue event.
        /// </summary>
        void HandleCue(object asc, GameplayTag cueTag, EGameplayCueEvent eventType, GameplayCueEventParams parameters);
        
        /// <summary>
        /// Removes all active cue instances for a specific ASC.
        /// </summary>
        void RemoveAllCuesFor(object asc);
        
        /// <summary>
        /// Initializes the cue manager with required dependencies.
        /// </summary>
        void Initialize(object assetPackage);
    }

    /// <summary>
    /// Service locator for GAS services. Provides default implementations while allowing DI override.
    /// Thread-safe for read operations after initialization.
    /// </summary>
    public static class GASServices
    {
        private static IGameplayCueManager s_CueManager;

        /// <summary>
        /// Gets or sets the GameplayCue manager. Returns NullGameplayCueManager if not set.
        /// </summary>
        public static IGameplayCueManager CueManager
        {
            get => s_CueManager ?? NullGameplayCueManager.Instance;
            set => s_CueManager = value;
        }

        /// <summary>
        /// Resets all services to null. Call during game shutdown or test teardown.
        /// </summary>
        public static void Reset()
        {
            s_CueManager = null;
        }
    }

    /// <summary>
    /// Null object pattern implementation for server-side or headless environments.
    /// All cue operations are no-ops, ensuring zero overhead on servers.
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
}
