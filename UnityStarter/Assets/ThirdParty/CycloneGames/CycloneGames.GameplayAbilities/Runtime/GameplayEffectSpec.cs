using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayTags.Core;

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
        public float Duration => GASFixedValue.FromRaw(DurationRaw).ToFloat();
        public long DurationRaw { get; private set; }

        // Raw arrays for maximum performance (direct memory access)
        public float[] ModifierMagnitudes = System.Array.Empty<float>();
        public long[] ModifierMagnitudeRawValues = System.Array.Empty<long>();
        public GameplayAttribute[] TargetAttributes = System.Array.Empty<GameplayAttribute>();

        /// <summary>
        /// Tags added at runtime to this specific spec instance, supplementing the definition's GrantedTags.
        /// UE5: FGameplayEffectSpec::DynamicGrantedTags.
        /// These tags are granted to the target in addition to Def.GrantedTags.
        /// </summary>
        public GameplayTagContainer DynamicGrantedTags { get; } = new GameplayTagContainer();

        /// <summary>
        /// Tags added at runtime to this specific spec instance, supplementing the definition's AssetTags.
        /// UE5: FGameplayEffectSpec::DynamicAssetTags.
        /// These tags describe this specific instance and can be used for immunity/removal checks.
        /// </summary>
        public GameplayTagContainer DynamicAssetTags { get; } = new GameplayTagContainer();

        // SetByCaller magnitude storage --null-lazy to avoid Dictionary allocation on specs that never use SetByCaller.
        // The vast majority of effect specs (damage, buffs, cooldowns) do not use this API.
        private Dictionary<GameplayTag, long> setByCallerMagnitudes;
        private Dictionary<string, long> setByCallerMagnitudesByName;

        private Dictionary<GameplayTag, long> GetOrCreateTagMagnitudes()
            => setByCallerMagnitudes ??= new Dictionary<GameplayTag, long>();

        private Dictionary<string, long> GetOrCreateNameMagnitudes()
            => setByCallerMagnitudesByName ??= new Dictionary<string, long>(System.StringComparer.Ordinal);

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
            DurationRaw = 0L;

            // Fast clear of references --clear BOTH arrays so stale magnitudes never survive pool reuse.
            System.Array.Clear(TargetAttributes, 0, TargetAttributes.Length);
            System.Array.Clear(ModifierMagnitudes, 0, ModifierMagnitudes.Length);
            System.Array.Clear(ModifierMagnitudeRawValues, 0, ModifierMagnitudeRawValues.Length);
            setByCallerMagnitudes?.Clear();
            setByCallerMagnitudesByName?.Clear();
            DynamicGrantedTags.Clear();
            DynamicAssetTags.Clear();
        }

        #endregion

        #region Factory Methods

        /// <summary>
        /// Factory method to create or retrieve a GameplayEffectSpec from the pool.
        /// </summary>
        public static GameplayEffectSpec Create(GameplayEffect def, AbilitySystemComponent source, int level = 1)
        {
            return Create(def, source, source?.MakeEffectContext(), level);
        }

        /// <summary>
        /// Factory method that allows callers to provide a custom effect context.
        /// </summary>
        public static GameplayEffectSpec Create(GameplayEffect def, AbilitySystemComponent source, IGameplayEffectContext context, int level = 1)
        {
            var spec = GASPool<GameplayEffectSpec>.Shared.Get();
            spec.Initialize(def, source, context, level);
            return spec;
        }

        public static void WarmPool(int count, int modifierCapacity = 8)
        {
            if (count <= 0)
            {
                return;
            }

            var specs = new GameplayEffectSpec[count];
            for (int i = 0; i < count; i++)
            {
                var spec = GASPool<GameplayEffectSpec>.Shared.Get();
                spec.ReserveModifierCapacity(modifierCapacity);
                specs[i] = spec;
            }

            for (int i = 0; i < count; i++)
            {
                GASPool<GameplayEffectSpec>.Shared.Return(specs[i]);
            }
        }

        private void Initialize(GameplayEffect def, AbilitySystemComponent source, IGameplayEffectContext context, int level)
        {
            Def = def;
            Source = source;
            Level = level;
            DurationRaw = GASFixedValue.FromFloat(def.Duration).RawValue;

            Context = context ?? GASPool<GameplayEffectContext>.Shared.Get();
            if (source != null)
            {
                Context.AddInstigator(source, null);
            }

            int modCount = def.Modifiers.Count;
            EnsureCapacity(modCount);

            for (int i = 0; i < modCount; i++)
            {
                var mod = def.Modifiers[i];
                StoreCalculatedMagnitudeRaw(i, mod.CalculateMagnitudeRaw(this, level));
                TargetAttributes[i] = null;
            }
        }

        private void EnsureCapacity(int count)
        {
            if (ModifierMagnitudes.Length < count)
            {
                int newSize = System.Math.Max(count, ModifierMagnitudes.Length == 0 ? 8 : ModifierMagnitudes.Length * 2);
                System.Array.Resize(ref ModifierMagnitudes, newSize);
                System.Array.Resize(ref ModifierMagnitudeRawValues, newSize);
                System.Array.Resize(ref TargetAttributes, newSize);
            }
        }

        public void ReserveModifierCapacity(int count)
        {
            EnsureCapacity(count);
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
            GetOrCreateTagMagnitudes()[dataTag] = GASFixedValue.FromFloat(magnitude).RawValue;
        }

        public void SetSetByCallerMagnitude(GameplayTag dataTag, GASFixedValue magnitude)
        {
            SetSetByCallerMagnitudeRaw(dataTag, magnitude.RawValue);
        }

        public void SetSetByCallerMagnitudeRaw(GameplayTag dataTag, long magnitudeRaw)
        {
            if (dataTag.IsNone) return;
            GetOrCreateTagMagnitudes()[dataTag] = magnitudeRaw;
            RecalculateSetByCallerMagnitudes();
        }

        public float GetSetByCallerMagnitude(GameplayTag dataTag, bool warnIfNotFound = true, float defaultValue = 0f)
        {
            return GASFixedValue.FromRaw(GetSetByCallerMagnitudeRaw(
                dataTag,
                warnIfNotFound,
                GASFixedValue.FromFloat(defaultValue).RawValue)).ToFloat();
        }

        public long GetSetByCallerMagnitudeRaw(GameplayTag dataTag, bool warnIfNotFound = true, long defaultValueRaw = 0L)
        {
            if (setByCallerMagnitudes != null && setByCallerMagnitudes.TryGetValue(dataTag, out long magnitudeRaw))
            {
                return magnitudeRaw;
            }

            if (warnIfNotFound)
            {
                string tagName = dataTag.IsNone ? "<None>" : dataTag.Name;
                GASLog.Warning(sb => sb.Append("GetSetByCallerMagnitude: Tag '").Append(tagName)
                    .Append("' not found in spec for effect '").Append(Def?.Name).Append("'."));
            }
            return defaultValueRaw;
        }

        public void SetSetByCallerMagnitude(string dataName, float magnitude)
        {
            if (string.IsNullOrEmpty(dataName))
            {
                GASLog.Warning(sb => sb.Append("SetSetByCallerMagnitude: dataName cannot be null or empty."));
                return;
            }
            GetOrCreateNameMagnitudes()[dataName] = GASFixedValue.FromFloat(magnitude).RawValue;
        }

        public void SetSetByCallerMagnitude(string dataName, GASFixedValue magnitude)
        {
            SetSetByCallerMagnitudeRaw(dataName, magnitude.RawValue);
        }

        public void SetSetByCallerMagnitudeRaw(string dataName, long magnitudeRaw)
        {
            if (string.IsNullOrEmpty(dataName))
            {
                GASLog.Warning(sb => sb.Append("SetSetByCallerMagnitude: dataName cannot be null or empty."));
                return;
            }
            GetOrCreateNameMagnitudes()[dataName] = magnitudeRaw;
            RecalculateSetByCallerMagnitudes();
        }

        public float GetSetByCallerMagnitude(string dataName, bool warnIfNotFound = true, float defaultValue = 0f)
        {
            return GASFixedValue.FromRaw(GetSetByCallerMagnitudeRaw(
                dataName,
                warnIfNotFound,
                GASFixedValue.FromFloat(defaultValue).RawValue)).ToFloat();
        }

        public long GetSetByCallerMagnitudeRaw(string dataName, bool warnIfNotFound = true, long defaultValueRaw = 0L)
        {
            if (setByCallerMagnitudesByName != null && setByCallerMagnitudesByName.TryGetValue(dataName, out long magnitudeRaw))
            {
                return magnitudeRaw;
            }

            if (warnIfNotFound)
            {
                GASLog.Warning(sb => sb.Append("GetSetByCallerMagnitude: Name '").Append(dataName)
                    .Append("' not found in spec for effect '").Append(Def?.Name).Append("'."));
            }
            return defaultValueRaw;
        }

        public bool HasSetByCallerMagnitude(string dataName)
        {
            return !string.IsNullOrEmpty(dataName) && setByCallerMagnitudesByName != null && setByCallerMagnitudesByName.ContainsKey(dataName);
        }

        public bool HasSetByCallerMagnitude(GameplayTag dataTag)
        {
            return !dataTag.IsNone && setByCallerMagnitudes != null && setByCallerMagnitudes.ContainsKey(dataTag);
        }

        public int SetByCallerTagMagnitudeCount => setByCallerMagnitudes?.Count ?? 0;

        public int CopySetByCallerTagMagnitudes(GameplayTag[] destinationTags, float[] destinationValues)
        {
            if (destinationTags == null || destinationValues == null || setByCallerMagnitudes == null || setByCallerMagnitudes.Count == 0)
            {
                return 0;
            }

            int capacity = System.Math.Min(destinationTags.Length, destinationValues.Length);
            if (capacity <= 0)
            {
                return 0;
            }

            int index = 0;
            foreach (var pair in setByCallerMagnitudes)
            {
                if (index >= capacity)
                {
                    break;
                }

                destinationTags[index] = pair.Key;
                destinationValues[index] = GASFixedValue.FromRaw(pair.Value).ToFloat();
                index++;
            }

            return index;
        }

        public int CopySetByCallerTagMagnitudesRaw(GameplayTag[] destinationTags, long[] destinationValuesRaw)
        {
            if (destinationTags == null || destinationValuesRaw == null || setByCallerMagnitudes == null || setByCallerMagnitudes.Count == 0)
            {
                return 0;
            }

            int capacity = System.Math.Min(destinationTags.Length, destinationValuesRaw.Length);
            if (capacity <= 0)
            {
                return 0;
            }

            int index = 0;
            foreach (var pair in setByCallerMagnitudes)
            {
                if (index >= capacity)
                {
                    break;
                }

                destinationTags[index] = pair.Key;
                destinationValuesRaw[index] = pair.Value;
                index++;
            }

            return index;
        }

        public int CopySetByCallerTagStateData(GASSetByCallerTagStateData[] destination)
        {
            if (destination == null || setByCallerMagnitudes == null || setByCallerMagnitudes.Count == 0)
            {
                return 0;
            }

            int index = 0;
            foreach (var pair in setByCallerMagnitudes)
            {
                if (index >= destination.Length)
                {
                    break;
                }

                destination[index++] = GASSetByCallerTagStateData.FromRaw(pair.Key, pair.Value);
            }

            return index;
        }

        public void ApplyReplicatedState(
            int level,
            float duration,
            GameplayTag[] setByCallerTags,
            float[] setByCallerValues,
            int setByCallerCount)
        {
            Level = level;
            DurationRaw = GASFixedValue.FromFloat(duration).RawValue;

            setByCallerMagnitudes?.Clear();
            if (setByCallerTags != null && setByCallerValues != null)
            {
                int count = System.Math.Min(setByCallerCount, System.Math.Min(setByCallerTags.Length, setByCallerValues.Length));
                for (int i = 0; i < count; i++)
                {
                    var tag = setByCallerTags[i];
                    if (!tag.IsNone)
                    {
                        GetOrCreateTagMagnitudes()[tag] = GASFixedValue.FromFloat(setByCallerValues[i]).RawValue;
                    }
                }
            }

            RecalculateModifierMagnitudes();
        }

        public void ApplyReplicatedStateRaw(
            int level,
            long durationRaw,
            GameplayTag[] setByCallerTags,
            long[] setByCallerValuesRaw,
            int setByCallerCount)
        {
            Level = level;
            DurationRaw = durationRaw;

            setByCallerMagnitudes?.Clear();
            if (setByCallerTags != null && setByCallerValuesRaw != null)
            {
                int count = System.Math.Min(setByCallerCount, System.Math.Min(setByCallerTags.Length, setByCallerValuesRaw.Length));
                for (int i = 0; i < count; i++)
                {
                    var tag = setByCallerTags[i];
                    if (!tag.IsNone)
                    {
                        GetOrCreateTagMagnitudes()[tag] = setByCallerValuesRaw[i];
                    }
                }
            }

            RecalculateModifierMagnitudes();
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

        public long GetCalculatedMagnitudeRaw(int index)
        {
            if (index >= 0 && index < ModifierMagnitudeRawValues.Length)
            {
                return ModifierMagnitudeRawValues[index];
            }
            return 0L;
        }

        public float GetCalculatedMagnitude(int index)
        {
            if (index >= 0 && index < ModifierMagnitudes.Length)
            {
                return ModifierMagnitudes[index];
            }
            return 0f;
        }

        public void SetCalculatedMagnitude(int index, float magnitude)
        {
            if (index < 0 || index >= ModifierMagnitudes.Length || index >= ModifierMagnitudeRawValues.Length)
            {
                return;
            }

            StoreCalculatedMagnitudeRaw(index, GASFixedValue.FromFloat(magnitude).RawValue);
        }

        public void SetCalculatedMagnitudeRaw(int index, long magnitudeRaw)
        {
            if (index < 0 || index >= ModifierMagnitudes.Length || index >= ModifierMagnitudeRawValues.Length)
            {
                return;
            }

            StoreCalculatedMagnitudeRaw(index, magnitudeRaw);
        }

        private void StoreCalculatedMagnitudeRaw(int index, long magnitudeRaw)
        {
            ModifierMagnitudeRawValues[index] = magnitudeRaw;
            ModifierMagnitudes[index] = GASFixedValue.FromRaw(magnitudeRaw).ToFloat();
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
                        TargetAttributes[i] = target != null ? target.GetAttribute(Def.Modifiers[i].AttributeName) : null;
                    }
                }

                RecalculateTargetDependentMagnitudes();
            }
        }

        private void RecalculateModifierMagnitudes()
        {
            if (Def == null)
            {
                return;
            }

            int modCount = Def.Modifiers.Count;
            EnsureCapacity(modCount);

            for (int i = 0; i < modCount; i++)
            {
                var mod = Def.Modifiers[i];
                StoreCalculatedMagnitudeRaw(i, mod.CalculateMagnitudeRaw(this, Level));

                if (Target != null)
                {
                    TargetAttributes[i] = Target.GetAttribute(mod.AttributeName);
                }
            }
        }

        private void RecalculateTargetDependentMagnitudes()
        {
            if (Def == null)
            {
                return;
            }

            int modCount = Def.Modifiers.Count;
            EnsureCapacity(modCount);

            for (int i = 0; i < modCount; i++)
            {
                var mod = Def.Modifiers[i];
                if (mod.ShouldRecalculateWhenTargetAssigned)
                {
                    StoreCalculatedMagnitudeRaw(i, mod.CalculateMagnitudeRaw(this, Level));
                }
            }
        }

        private void RecalculateSetByCallerMagnitudes()
        {
            if (Def == null)
            {
                return;
            }

            int modCount = Def.Modifiers.Count;
            EnsureCapacity(modCount);
            for (int i = 0; i < modCount; i++)
            {
                var mod = Def.Modifiers[i];
                if (mod.MagnitudeCalculationType == EGameplayEffectMagnitudeCalculation.SetByCaller)
                {
                    StoreCalculatedMagnitudeRaw(i, mod.CalculateMagnitudeRaw(this, Level));
                }
            }
        }
    }
}
