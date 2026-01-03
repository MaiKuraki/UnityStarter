using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Represents a stateful, runtime instance of a GameplayEffect.
    /// This class encapsulates all the necessary context for an effect's application,
    /// such as its source, target, level, and pre-calculated modifier magnitudes.
    /// Designed to be pooled for high performance using GASPool.
    /// </summary>
    public class GameplayEffectSpec : IGASPoolable
    {
        /// <summary>
        /// The stateless definition (template) of this effect.
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
        /// A context object carrying metadata about the effect's application.
        /// </summary>
        public IGameplayEffectContext Context { get; private set; }

        /// <summary>
        /// The level at which this effect spec was created.
        /// </summary>
        public int Level { get; private set; }

        /// <summary>
        /// The duration for this specific instance of the effect.
        /// </summary>
        public float Duration { get; private set; }

        // Raw arrays for maximum performance (direct memory access)
        public float[] ModifierMagnitudes = System.Array.Empty<float>();
        public GameplayAttribute[] TargetAttributes = System.Array.Empty<GameplayAttribute>();

        // SetByCaller magnitude storage
        private readonly Dictionary<GameplayTag, float> setByCallerMagnitudes = new Dictionary<GameplayTag, float>();
        private readonly Dictionary<string, float> setByCallerMagnitudesByName = new Dictionary<string, float>(System.StringComparer.Ordinal);

        public GameplayEffectSpec() { }

        #region IGASPoolable Implementation

        void IGASPoolable.OnGetFromPool()
        {
            // Initialization happens in Initialize() after pool retrieval
        }

        void IGASPoolable.OnReturnToPool()
        {
            // Return nested context to its pool
            if (Context is GameplayEffectContext pooledContext)
            {
                GASPool<GameplayEffectContext>.Shared.Return(pooledContext);
            }

            Def = null;
            Source = null;
            Target = null;
            Context = null;
            Level = 0;
            Duration = 0;

            // Fast clear of references
            System.Array.Clear(TargetAttributes, 0, TargetAttributes.Length);
            setByCallerMagnitudes.Clear();
            setByCallerMagnitudesByName.Clear();
        }

        #endregion

        #region Factory Methods

        /// <summary>
        /// Factory method to create or retrieve a GameplayEffectSpec from the pool.
        /// </summary>
        public static GameplayEffectSpec Create(GameplayEffect def, AbilitySystemComponent source, int level = 1)
        {
            var spec = GASPool<GameplayEffectSpec>.Shared.Get();
            spec.Initialize(def, source, level);
            return spec;
        }

        private void Initialize(GameplayEffect def, AbilitySystemComponent source, int level)
        {
            Def = def;
            Source = source;
            Level = level;
            Duration = def.Duration;

            Context = GASPool<GameplayEffectContext>.Shared.Get();
            Context.AddInstigator(source, null);

            int modCount = def.Modifiers.Count;
            EnsureCapacity(modCount);

            for (int i = 0; i < modCount; i++)
            {
                var mod = def.Modifiers[i];
                float magnitude = mod.CustomCalculation != null
                    ? mod.CustomCalculation.CalculateMagnitude(this)
                    : mod.Magnitude.GetValueAtLevel(level);

                ModifierMagnitudes[i] = magnitude;
                TargetAttributes[i] = null;
            }
        }

        private void EnsureCapacity(int count)
        {
            if (ModifierMagnitudes.Length < count)
            {
                int newSize = System.Math.Max(count, ModifierMagnitudes.Length == 0 ? 8 : ModifierMagnitudes.Length * 2);
                System.Array.Resize(ref ModifierMagnitudes, newSize);
                System.Array.Resize(ref TargetAttributes, newSize);
            }
        }

        /// <summary>
        /// Returns this spec to the object pool.
        /// </summary>
        public void ReturnToPool()
        {
            GASPool<GameplayEffectSpec>.Shared.Return(this);
        }

        #endregion

        #region SetByCaller API

        public void SetSetByCallerMagnitude(GameplayTag dataTag, float magnitude)
        {
            if (dataTag.IsNone) return;
            setByCallerMagnitudes[dataTag] = magnitude;
        }

        public float GetSetByCallerMagnitude(GameplayTag dataTag, bool warnIfNotFound = true, float defaultValue = 0f)
        {
            if (setByCallerMagnitudes.TryGetValue(dataTag, out float magnitude))
            {
                return magnitude;
            }

            if (warnIfNotFound)
            {
                GASLog.Warning($"GetSetByCallerMagnitude: Tag '{dataTag.Name}' not found in spec for effect '{Def?.Name}'.");
            }
            return defaultValue;
        }

        public void SetSetByCallerMagnitude(string dataName, float magnitude)
        {
            if (string.IsNullOrEmpty(dataName))
            {
                GASLog.Warning("SetSetByCallerMagnitude: dataName cannot be null or empty.");
                return;
            }
            setByCallerMagnitudesByName[dataName] = magnitude;
        }

        public float GetSetByCallerMagnitude(string dataName, bool warnIfNotFound = true, float defaultValue = 0f)
        {
            if (setByCallerMagnitudesByName.TryGetValue(dataName, out float magnitude))
            {
                return magnitude;
            }

            if (warnIfNotFound)
            {
                GASLog.Warning($"GetSetByCallerMagnitude: Name '{dataName}' not found in spec for effect '{Def?.Name}'.");
            }
            return defaultValue;
        }

        public bool HasSetByCallerMagnitude(string dataName)
        {
            return !string.IsNullOrEmpty(dataName) && setByCallerMagnitudesByName.ContainsKey(dataName);
        }

        public bool HasSetByCallerMagnitude(GameplayTag dataTag)
        {
            return !dataTag.IsNone && setByCallerMagnitudes.ContainsKey(dataTag);
        }

        #endregion

        #region Magnitude Lookup

        public float GetCalculatedMagnitude(ModifierInfo modifier)
        {
            if (Def == null || Def.Modifiers == null) return 0f;

            int index = -1;
            for (int i = 0; i < Def.Modifiers.Count; i++)
            {
                if (Def.Modifiers[i].Equals(modifier))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0 && index < ModifierMagnitudes.Length)
            {
                return ModifierMagnitudes[index];
            }
            return 0f;
        }

        public float GetCalculatedMagnitude(int index)
        {
            if (index >= 0 && index < ModifierMagnitudes.Length)
            {
                return ModifierMagnitudes[index];
            }
            return 0f;
        }

        #endregion

        /// <summary>
        /// Assigns the target AbilitySystemComponent and resolves attribute cache.
        /// </summary>
        public void SetTarget(AbilitySystemComponent target)
        {
            Target = target;
            if (Def != null && Def.Modifiers != null)
            {
                for (int i = 0; i < Def.Modifiers.Count; i++)
                {
                    if (i < TargetAttributes.Length)
                    {
                        TargetAttributes[i] = target.GetAttribute(Def.Modifiers[i].AttributeName);
                    }
                }
            }
        }
    }
}