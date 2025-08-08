using System;
using System.Collections.Generic;
using System.Reflection;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public abstract class AttributeSet
    {
        private class AttributeData
        {
            public float BaseValue;
            public float CurrentValue;
        }

        // Static cache for attribute properties per AttributeSet subclass
        private static readonly Dictionary<Type, List<PropertyInfo>> s_AttributePropertyCache = new Dictionary<Type, List<PropertyInfo>>();

        private readonly Dictionary<string, AttributeData> attributeData = new Dictionary<string, AttributeData>();
        private readonly Dictionary<string, GameplayAttribute> discoveredAttributes = new Dictionary<string, GameplayAttribute>();

        public AbilitySystemComponent OwningAbilitySystemComponent { get; internal set; }

        protected AttributeSet()
        {
            DiscoverAndInitAttributes();
        }

        private void DiscoverAndInitAttributes()
        {
            Type setType = GetType();
            if (!s_AttributePropertyCache.TryGetValue(setType, out var properties))
            {
                properties = new List<PropertyInfo>();
                foreach (var prop in setType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (prop.PropertyType == typeof(GameplayAttribute))
                    {
                        properties.Add(prop);
                    }
                }
                s_AttributePropertyCache[setType] = properties;
            }

            foreach (var prop in properties)
            {
                var attr = prop.GetValue(this) as GameplayAttribute;
                if (attr != null)
                {
                    attr.OwningSet = this;
                    attributeData[attr.Name] = new AttributeData();
                    discoveredAttributes[attr.Name] = attr;
                }
            }
        }

        public IReadOnlyCollection<GameplayAttribute> GetAttributes() => discoveredAttributes.Values;

        public float GetBaseValue(GameplayAttribute attribute) => attributeData.TryGetValue(attribute.Name, out var data) ? data.BaseValue : 0f;
        public float GetCurrentValue(GameplayAttribute attribute) => attributeData.TryGetValue(attribute.Name, out var data) ? data.CurrentValue : 0f;

        public void SetBaseValue(GameplayAttribute attribute, float value)
        {
            if (attributeData.TryGetValue(attribute.Name, out var data))
            {
                if (Math.Abs(data.BaseValue - value) > float.Epsilon)
                {
                    data.BaseValue = value;
                    OwningAbilitySystemComponent?.MarkAttributeDirty(attribute);
                }
            }
        }

        public void SetCurrentValue(GameplayAttribute attribute, float value)
        {
            if (attributeData.TryGetValue(attribute.Name, out var data))
            {
                float oldValue = data.CurrentValue;
                if (Math.Abs(oldValue - value) > float.Epsilon)
                {
                    data.CurrentValue = value;
                    attribute.InvokeCurrentValueChanged(oldValue, value);
                }
            }
        }

        /// <summary>
        /// Retrieves an attribute by its name.
        /// </summary>
        /// <param name="name">The name of the attribute to retrieve.</param>
        /// <returns>The GameplayAttribute instance if found; otherwise, null.</returns>
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
        /// <param name="data">The data associated with the gameplay effect modification.</param>
        public virtual void PostGameplayEffectExecute(GameplayEffectModCallbackData data)
        {
            // --- Pre-Process ---
            // Give derived classes a chance to completely handle the effect and skip default logic.
            if (PreProcessInstantEffect(data))
            {
                return; // The derived class handled it.
            }

            // --- Default-Process ---
            // If not handled by Pre-Process, run the default attribute modification.
            ApplyDefaultInstantEffectModification(data);

            // --- Post-Process ---
            // Give derived classes a chance to react AFTER the default logic has run.
            PostProcessInstantEffect(data);
        }

        /// <summary>
        /// This contains the standard calculation logic. It is now virtual.
        /// Derived classes can override this method to provide a completely custom
        /// calculation for a specific attribute, instead of using the simple switch statement.
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
