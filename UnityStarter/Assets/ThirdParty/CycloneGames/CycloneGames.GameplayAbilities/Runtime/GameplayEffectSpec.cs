using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Represents a stateful, runtime instance of a GameplayEffect.
    /// This class encapsulates all the necessary context for an effect's application,
    /// such as its source, target, level, and pre-calculated modifier magnitudes.
    /// It acts as a "live" version of the stateless GameplayEffect definition.
    /// This object is designed to be pooled for high performance, minimizing garbage collection.
    /// </summary>
    public class GameplayEffectSpec
    {
        // object pool, reducing GC overhead during gameplay.
        private static readonly Stack<GameplayEffectSpec> pool = new Stack<GameplayEffectSpec>(32);

        /// <summary>
        /// The stateless definition (template) of this effect. Contains all the core data like duration, modifiers, tags, etc.
        /// </summary>
        public GameplayEffect Def { get; private set; }

        /// <summary>
        /// The AbilitySystemComponent that created and applied this effect.
        /// </summary>
        public AbilitySystemComponent Source { get; private set; }

        /// <summary>
        /// The AbilitySystemComponent that this effect is applied to.
        /// </summary>
        public AbilitySystemComponent Target { get; private set; }

        /// <summary>
        /// A context object carrying metadata about the effect's application, such as the instigating ability and targeting data.
        /// </summary>
        public IGameplayEffectContext Context { get; private set; }

        /// <summary>
        /// The level at which this effect spec was created. Used for calculating level-scalable magnitudes.
        /// </summary>
        public int Level { get; private set; }

        /// <summary>
        /// The duration for this specific instance of the effect. Initialized from the definition but can potentially be modified.
        /// </summary>
        public float Duration { get; private set; }

        // A cache of calculated magnitudes for each modifier in the effect definition.
        // This is a critical performance optimization to avoid re-calculating magnitudes on every evaluation.
        private readonly Dictionary<ModifierInfo, float> calculatedMagnitudes = new Dictionary<ModifierInfo, float>();

        private readonly Dictionary<GameplayTag, float> setByCallerMagnitudes = new Dictionary<GameplayTag, float>();

        // Private constructor to enforce creation via the pooling system.
        private GameplayEffectSpec() { }

        /// <summary>
        /// Factory method to create or retrieve a GameplayEffectSpec from the pool.
        /// This is the primary way to instantiate a new effect spec.
        /// </summary>
        /// <param name="def">The stateless GameplayEffect definition.</param>
        /// <param name="source">The AbilitySystemComponent applying the effect.</param>
        /// <param name="level">The level to create the effect at.</param>
        /// <returns>An initialized GameplayEffectSpec instance.</returns>
        public static GameplayEffectSpec Create(GameplayEffect def, AbilitySystemComponent source, int level = 1)
        {
            var spec = pool.Count > 0 ? pool.Pop() : new GameplayEffectSpec();
            spec.Def = def;
            spec.Source = source;
            spec.Level = level;
            spec.Duration = def.Duration;

            // Acquire a context object (likely also pooled) from the source ASC's factory.
            spec.Context = source.MakeEffectContext();
            // Set the instigator of this effect. The ability instance can be null if the effect is not applied from an ability.
            spec.Context.AddInstigator(source, null);

            // Pre-calculate the magnitude of all modifiers based on the spec's level and context at creation time.
            // This captures the values "on creation," which is essential for snapshotting behavior and performance.
            foreach (var mod in def.Modifiers)
            {
                float magnitude;
                if (mod.CustomCalculation != null)
                {
                    magnitude = mod.CustomCalculation.CalculateMagnitude(spec);
                }
                else
                {
                    magnitude = mod.Magnitude.GetValueAtLevel(level);
                }
                spec.calculatedMagnitudes[mod] = magnitude;
            }
            return spec;
        }

        /// <summary>
        /// Resets the spec's state and returns it to the object pool.
        /// This is essential for preventing memory leaks and ensuring instances are clean for reuse.
        /// </summary>
        public void ReturnToPool()
        {
            // If the context itself is a pooled object, ensure it's returned to its own pool.
            if (Context is GameplayEffectContext pooledContext)
            {
                pooledContext.ReturnToPool();
            }

            Def = null;
            Source = null;
            Target = null;
            Context = null;
            Level = 0;
            Duration = 0;
            calculatedMagnitudes.Clear();
            setByCallerMagnitudes.Clear();

            pool.Push(this);
        }

        /// <summary>
        /// Sets a magnitude value associated with a GameplayTag. This is the "snapshotting" mechanism.
        /// </summary>
        /// <param name="dataTag">The GameplayTag to use as a key.</param>
        /// <param name="magnitude">The float value to store.</param>
        public void SetSetByCallerMagnitude(GameplayTag dataTag, float magnitude)
        {
            if (dataTag == GameplayTag.None)
            {
                // Optional: Add a warning here if you want to prevent using invalid tags.
                return;
            }
            setByCallerMagnitudes[dataTag] = magnitude;
        }

        /// <summary>
        /// Retrieves a magnitude value associated with a GameplayTag from the SetByCaller cache.
        /// </summary>
        /// <param name="dataTag">The GameplayTag key to look up.</param>
        /// <param name="warnIfNotFound">If true, logs a warning if the key is not found.</param>
        /// <param name="defaultValue">The value to return if the key is not found.</param>
        /// <returns>The stored magnitude or the default value.</returns>
        public float GetSetByCallerMagnitude(GameplayTag dataTag, bool warnIfNotFound = true, float defaultValue = 0f)
        {
            if (setByCallerMagnitudes.TryGetValue(dataTag, out float magnitude))
            {
                return magnitude;
            }

            if (warnIfNotFound)
            {
                // Consider using your CLogger here
                CLogger.LogWarning($"GetSetByCallerMagnitude: Tag '{dataTag.Name}' not found in spec for effect '{Def?.Name}'. Returning default value.");
            }

            return defaultValue;
        }

        /// <summary>
        /// Retrieves the pre-calculated magnitude for a given modifier.
        /// Using this avoids expensive re-computation during attribute recalculations.
        /// </summary>
        /// <param name="modifier">The modifier definition to look up.</param>
        /// <returns>The calculated magnitude, or 0 if the modifier is not found in the cache.</returns>
        public float GetCalculatedMagnitude(ModifierInfo modifier)
        {
            return calculatedMagnitudes.TryGetValue(modifier, out var magnitude) ? magnitude : 0;
        }

        /// <summary>
        /// Assigns the target AbilitySystemComponent to this spec.
        /// This is typically done just before the effect is applied.
        /// </summary>
        /// <param name="target">The component that will receive the effect.</param>
        public void SetTarget(AbilitySystemComponent target)
        {
            Target = target;
        }
    }
}
