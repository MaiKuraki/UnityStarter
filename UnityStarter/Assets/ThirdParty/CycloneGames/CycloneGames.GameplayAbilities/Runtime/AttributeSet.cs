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
        private readonly List<GameplayAttribute> discoveredAttributes = new List<GameplayAttribute>();

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
                    discoveredAttributes.Add(attr);
                }
            }
        }

        public IReadOnlyList<GameplayAttribute> GetAttributes() => discoveredAttributes;

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

        public virtual void PreAttributeChange(GameplayAttribute attribute, ref float newValue) { }
        public virtual void PreAttributeBaseChange(GameplayAttribute attribute, ref float newBaseValue) { }
        public virtual void PostGameplayEffectExecute(GameplayEffectModCallbackData data) { }
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