using System;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.GameplayAbilities.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public abstract class AttributeSet
    {
        #region Optimized Attribute Discovery

        /// <summary>
        /// Cached compiled delegates for attribute getters per AttributeSet subclass.
        /// </summary>
        private static readonly Dictionary<Type, List<Func<AttributeSet, GameplayAttribute>>> s_AttributeGetterCache
            = new Dictionary<Type, List<Func<AttributeSet, GameplayAttribute>>>();
        private static readonly object s_CacheLock = new object();

        #endregion

        private readonly Dictionary<string, GameplayAttribute> discoveredAttributes = new Dictionary<string, GameplayAttribute>();
        private bool attributesDiscovered;

        public AbilitySystemComponent OwningAbilitySystemComponent { get; internal set; }

        protected AttributeSet()
        {
        }

        /// <summary>
        /// Override this in generated or handwritten AttributeSet types to avoid runtime reflection discovery.
        /// </summary>
        protected virtual void RegisterAttributes()
        {
        }

        protected void RegisterAttribute(GameplayAttribute attribute)
        {
            if (attribute == null)
            {
                return;
            }

            attribute.OwningSet = this;
            discoveredAttributes[attribute.Name] = attribute;
        }

        private void EnsureAttributesDiscovered()
        {
            if (attributesDiscovered)
            {
                return;
            }

            DiscoverAndInitAttributes();
            attributesDiscovered = true;
        }

        private void DiscoverAndInitAttributes()
        {
            int explicitAttributeCount = discoveredAttributes.Count;
            RegisterAttributes();
            if (discoveredAttributes.Count != explicitAttributeCount)
            {
                return;
            }

            Type setType = GetType();
            List<Func<AttributeSet, GameplayAttribute>> getters;

            lock (s_CacheLock)
            {
                if (!s_AttributeGetterCache.TryGetValue(setType, out getters))
                {
                    getters = new List<Func<AttributeSet, GameplayAttribute>>();

                    // Discover properties and compile getter delegates (only once per type)
                    foreach (var prop in setType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (prop.PropertyType == typeof(GameplayAttribute))
                        {
                            var getMethod = prop.GetGetMethod();
                            if (getMethod != null)
                            {
                                var getter = CreateGetter(setType, getMethod);
                                if (getter != null)
                                {
                                    getters.Add(getter);
                                }
                            }
                        }
                    }
                    s_AttributeGetterCache[setType] = getters;
                }
            }

            // Use cached delegates to get attribute values (no reflection, no boxing)
            for (int i = 0; i < getters.Count; i++)
            {
                var attr = getters[i](this);
                if (attr != null)
                {
                    RegisterAttribute(attr);
                }
            }
        }

        /// <summary>
        /// Creates a compiled getter delegate for a property.
        /// Uses Delegate.CreateDelegate for maximum performance (no boxing, no reflection invoke).
        /// </summary>
        private static Func<AttributeSet, GameplayAttribute> CreateGetter(Type declaringType, MethodInfo getMethod)
        {
            try
            {
                return (Func<AttributeSet, GameplayAttribute>)Delegate.CreateDelegate(
                    typeof(Func<AttributeSet, GameplayAttribute>), getMethod);
            }
            catch
            {
                // Fallback for virtual/override methods
                return (AttributeSet set) => getMethod.Invoke(set, null) as GameplayAttribute;
            }
        }

        public IReadOnlyCollection<GameplayAttribute> GetAttributes()
        {
            EnsureAttributesDiscovered();
            return discoveredAttributes.Values;
        }

        public float GetBaseValue(GameplayAttribute attribute) => attribute.BaseValue;
        public float GetCurrentValue(GameplayAttribute attribute) => attribute.CurrentValue;
        public long GetBaseValueRaw(GameplayAttribute attribute) => attribute.BaseValueRaw;
        public long GetCurrentValueRaw(GameplayAttribute attribute) => attribute.CurrentValueRaw;
        public GASFixedValue GetBaseFixedValue(GameplayAttribute attribute) => attribute.BaseFixedValue;
        public GASFixedValue GetCurrentFixedValue(GameplayAttribute attribute) => attribute.CurrentFixedValue;

        public void SetBaseValue(GameplayAttribute attribute, float value)
        {
            SetBaseValueRaw(attribute, GASFixedValue.FromFloat(value).RawValue);
        }

        public void SetBaseValue(GameplayAttribute attribute, GASFixedValue value)
        {
            SetBaseValueRaw(attribute, value.RawValue);
        }

        public void SetBaseValueRaw(GameplayAttribute attribute, long valueRaw)
        {
            if (attribute.BaseValueRaw != valueRaw)
            {
                attribute.SetBaseValueRaw(valueRaw);
                OwningAbilitySystemComponent?.MarkAttributeDirty(attribute);
            }
        }

        public void SetCurrentValue(GameplayAttribute attribute, float value)
        {
            attribute.SetCurrentValue(value);
        }

        public void SetCurrentValue(GameplayAttribute attribute, GASFixedValue value)
        {
            attribute.SetCurrentValue(value);
        }

        public void SetCurrentValueRaw(GameplayAttribute attribute, long valueRaw)
        {
            attribute.SetCurrentValueRaw(valueRaw);
        }

        /// <summary>
        /// Retrieves an attribute by its name.
        /// </summary>
        public GameplayAttribute GetAttribute(string name)
        {
            EnsureAttributesDiscovered();
            discoveredAttributes.TryGetValue(name, out var attribute);
            return attribute;
        }

        /// <summary>
        /// HOOK for derived classes. Called before the default modification.
        /// Can be overridden to implement special logic (like damage mitigation).
        /// </summary>
        /// <returns>Return true to indicate the effect has been fully handled and to skip the default logic.</returns>
        protected virtual bool PreProcessInstantEffect(GameplayEffectModCallbackData data)
        {
            return false;
        }

        /// <summary>
        /// HOOK for derived classes. Called after the default modification.
        /// Can be overridden to implement reactive logic (like checking for level up).
        /// </summary>
        protected virtual void PostProcessInstantEffect(GameplayEffectModCallbackData data)
        {
        }

        /// <summary>
        /// Hook invoked before an attribute's CurrentValue is recalculated. Default is a no-op.
        /// Override to clamp or adjust the value. Stays in fixed-point: use GASFixedValue.Clamp / Min / Max
        /// so results are bit-identical across platforms and backends (required for lockstep /
        /// server-authoritative play).
        /// </summary>
        public virtual void PreAttributeChange(GameplayAttribute attribute, ref GASFixedValue newValue) { }

        /// <summary>
        /// Hook invoked before an attribute's BaseValue changes (instant effects). Default is a no-op.
        /// Override to clamp or adjust the base value. Stays in fixed-point for deterministic results.
        /// </summary>
        public virtual void PreAttributeBaseChange(GameplayAttribute attribute, ref GASFixedValue newBaseValue) { }

        /// <summary>
        /// Called after a GameplayEffect is executed on this AttributeSet. This is the main entry point for attribute modifications.
        /// It follows a Pre-Process, Default-Process, Post-Process flow.
        /// </summary>
        public virtual void PostGameplayEffectExecute(GameplayEffectModCallbackData data)
        {
            // --- Pre-Process ---
            if (PreProcessInstantEffect(data))
            {
                return;
            }

            // --- Default-Process ---
            ApplyDefaultInstantEffectModification(data);

            // --- Post-Process ---
            PostProcessInstantEffect(data);
        }

        /// <summary>
        /// This contains the standard calculation logic.
        /// </summary>
        protected virtual void ApplyDefaultInstantEffectModification(GameplayEffectModCallbackData data)
        {
            var attribute = GetAttribute(data.Modifier.AttributeName);
            if (attribute == null)
            {
                return;
            }

            var newBase = GetBaseFixedValue(attribute);
            var magnitude = data.EvaluatedMagnitudeFixed;
            switch (data.Modifier.Operation)
            {
                case EAttributeModifierOperation.Add:
                    newBase += magnitude;
                    break;
                case EAttributeModifierOperation.Multiply:
                    newBase *= magnitude;
                    break;
                case EAttributeModifierOperation.Division:
                    if (magnitude.RawValue != 0)
                    {
                        newBase /= magnitude;
                    }

                    break;
                case EAttributeModifierOperation.Override:
                    newBase = magnitude;
                    break;
            }

            PreAttributeBaseChange(attribute, ref newBase);

            SetBaseValue(attribute, newBase);
            SetCurrentValue(attribute, newBase);
        }
    }

    // Data struct passed to PostGameplayEffectExecute
    public struct GameplayEffectModCallbackData
    {
        public GameplayEffectSpec EffectSpec { get; }
        public ModifierInfo Modifier { get; }
        public float EvaluatedMagnitude { get; }
        public long EvaluatedMagnitudeRaw { get; }
        public GASFixedValue EvaluatedMagnitudeFixed => GASFixedValue.FromRaw(EvaluatedMagnitudeRaw);
        public AbilitySystemComponent Target { get; }
        public AbilitySystemComponent Source => EffectSpec.Source;

        public GameplayEffectModCallbackData(GameplayEffectSpec spec, ModifierInfo modifier, float magnitude, AbilitySystemComponent target)
            : this(spec, modifier, GASFixedValue.FromFloat(magnitude).RawValue, target)
        {
        }

        public GameplayEffectModCallbackData(GameplayEffectSpec spec, ModifierInfo modifier, long magnitudeRaw, AbilitySystemComponent target)
        {
            EffectSpec = spec;
            Modifier = modifier;
            EvaluatedMagnitudeRaw = magnitudeRaw;
            EvaluatedMagnitude = GASFixedValue.FromRaw(magnitudeRaw).ToFloat();
            Target = target;
        }
    }
}
