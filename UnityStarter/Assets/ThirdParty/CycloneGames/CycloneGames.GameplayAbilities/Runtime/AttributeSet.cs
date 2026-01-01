using System;
using System.Collections.Generic;
using System.Reflection;

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

        public AbilitySystemComponent OwningAbilitySystemComponent { get; internal set; }

        protected AttributeSet()
        {
            DiscoverAndInitAttributes();
        }

        private void DiscoverAndInitAttributes()
        {
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
                    attr.OwningSet = this;
                    discoveredAttributes[attr.Name] = attr;
                }
            }
        }
        
        /// <summary>
        /// Creates a getter function for a property.
        /// Caches MethodInfo to avoid repeated GetGetMethod calls.
        /// </summary>
        private static Func<AttributeSet, GameplayAttribute> CreateGetter(Type declaringType, MethodInfo getMethod)
        {
            return (AttributeSet set) => getMethod.Invoke(set, null) as GameplayAttribute;
        }

        public IReadOnlyCollection<GameplayAttribute> GetAttributes() => discoveredAttributes.Values;

        public float GetBaseValue(GameplayAttribute attribute) => attribute.BaseValue;
        public float GetCurrentValue(GameplayAttribute attribute) => attribute.CurrentValue;

        public void SetBaseValue(GameplayAttribute attribute, float value)
        {
            if (Math.Abs(attribute.BaseValue - value) > float.Epsilon)
            {
                attribute.SetBaseValue(value);
                OwningAbilitySystemComponent?.MarkAttributeDirty(attribute);
            }
        }

        public void SetCurrentValue(GameplayAttribute attribute, float value)
        {
            attribute.SetCurrentValue(value);
        }

        /// <summary>
        /// Retrieves an attribute by its name.
        /// </summary>
        public GameplayAttribute GetAttribute(string name)
        {
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

        public virtual void PreAttributeChange(GameplayAttribute attribute, ref float newValue) { }
        public virtual void PreAttributeBaseChange(GameplayAttribute attribute, ref float newBaseValue) { }

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
            if (attribute == null) return;

            float currentBase = GetBaseValue(attribute);
            float newBase = currentBase;
            switch (data.Modifier.Operation)
            {
                case EAttributeModifierOperation.Add:
                    newBase += data.EvaluatedMagnitude;
                    break;
                case EAttributeModifierOperation.Multiply:
                    newBase *= data.EvaluatedMagnitude;
                    break;
                case EAttributeModifierOperation.Division:
                    if (data.EvaluatedMagnitude != 0) newBase /= data.EvaluatedMagnitude;
                    break;
                case EAttributeModifierOperation.Override:
                    newBase = data.EvaluatedMagnitude;
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
        public AbilitySystemComponent Target { get; }
        public AbilitySystemComponent Source => EffectSpec.Source;

        public GameplayEffectModCallbackData(GameplayEffectSpec spec, ModifierInfo modifier, float magnitude, AbilitySystemComponent target)
        {
            EffectSpec = spec;
            Modifier = modifier;
            EvaluatedMagnitude = magnitude;
            Target = target;
        }
    }
}