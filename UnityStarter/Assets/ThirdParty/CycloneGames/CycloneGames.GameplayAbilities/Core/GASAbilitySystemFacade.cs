using System;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// UE GAS-style facade over the core state container.
    /// Runtime adapters should expose familiar ASC methods while delegating state mutation here.
    /// </summary>
    public sealed class GASAbilitySystemFacade
    {
        private readonly GASAbilitySystemState state;

        public GASAbilitySystemState State => state;
        public GASEntityId Entity => state.Entity;

        public GASAbilitySystemFacade(GASAbilitySystemState state)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public GASSpecHandle GiveAbility(
            GASDefinitionId abilityDefinitionId,
            ushort level,
            GASInstancingPolicy instancingPolicy = GASInstancingPolicy.InstancedPerActor)
        {
            return state.GrantAbility(
                abilityDefinitionId,
                level,
                instancingPolicy);
        }

        public bool GiveAbility(in GASAbilityGrantRequest request, out GASSpecHandle handle)
        {
            return state.TryGrantAbility(in request, out handle);
        }

        public bool ClearAbility(GASSpecHandle handle)
        {
            return state.RemoveAbility(handle);
        }

        public GASActiveEffectHandle ApplyGameplayEffectSpecToSelf(in GASGameplayEffectSpecData spec)
        {
            return state.ApplyGameplayEffectSpecToSelf(in spec);
        }

        public bool RemoveActiveGameplayEffect(GASActiveEffectHandle handle)
        {
            return state.RemoveActiveEffect(handle);
        }

        public void CommitPrediction(GASPredictionKey predictionKey)
        {
            state.CommitPrediction(predictionKey);
        }

        public void RollbackPrediction(GASPredictionKey predictionKey)
        {
            state.RollbackPrediction(predictionKey);
        }

        public bool SetNumericAttributeBaseRaw(GASAttributeId attributeId, long valueRaw)
        {
            return state.SetAttributeBaseRaw(attributeId, valueRaw);
        }

        public bool RemoveNumericAttribute(GASAttributeId attributeId)
        {
            return state.RemoveAttribute(attributeId);
        }

        public bool CanRemoveNumericAttribute(GASAttributeId attributeId)
        {
            return state.CanRemoveAttribute(attributeId);
        }

        public bool SetNumericAttributeBase(GASAttributeId attributeId, GASFixedValue value)
        {
            return state.SetAttributeBase(attributeId, value);
        }

        public bool ApplyInstantModifier(GASAttributeId attributeId, GASModifierOp op, GASFixedValue magnitude)
        {
            return state.ApplyInstantModifier(attributeId, op, magnitude);
        }

        public bool ApplyInstantModifierRaw(GASAttributeId attributeId, GASModifierOp op, long magnitudeRaw)
        {
            return state.ApplyInstantModifierRaw(attributeId, op, magnitudeRaw);
        }

        public bool ApplyInstantModifierRaw(GASAttributeId attributeId, GASModifierOp op, long magnitudeRaw, GASPredictionKey predictionKey)
        {
            return state.ApplyInstantModifierRaw(attributeId, op, magnitudeRaw, predictionKey);
        }

        public bool ApplyInstantModifier(GASAttributeId attributeId, GASModifierOp op, GASFixedValue magnitude, GASPredictionKey predictionKey)
        {
            return state.ApplyInstantModifier(attributeId, op, magnitude, predictionKey);
        }

        public bool GetGameplayAttributeRawValue(GASAttributeId attributeId, out long currentValueRaw)
        {
            if (state.TryGetAttribute(attributeId, out var attribute))
            {
                currentValueRaw = attribute.CurrentValueRaw;
                return true;
            }

            currentValueRaw = default;
            return false;
        }

        public bool GetGameplayAttributeFixedValue(GASAttributeId attributeId, out GASFixedValue currentValue)
        {
            if (state.TryGetAttribute(attributeId, out var attribute))
            {
                currentValue = GASFixedValue.FromRaw(attribute.CurrentValueRaw);
                return true;
            }

            currentValue = default;
            return false;
        }

        public bool GetGameplayAttributeFixedValues(GASAttributeId attributeId, out GASFixedValue baseValue, out GASFixedValue currentValue)
        {
            if (state.TryGetAttribute(attributeId, out var attribute))
            {
                baseValue = GASFixedValue.FromRaw(attribute.BaseValueRaw);
                currentValue = GASFixedValue.FromRaw(attribute.CurrentValueRaw);
                return true;
            }

            baseValue = default;
            currentValue = default;
            return false;
        }

        public bool GetGameplayAttributeRawValues(GASAttributeId attributeId, out long baseValueRaw, out long currentValueRaw)
        {
            if (state.TryGetAttribute(attributeId, out var attribute))
            {
                baseValueRaw = attribute.BaseValueRaw;
                currentValueRaw = attribute.CurrentValueRaw;
                return true;
            }

            baseValueRaw = default;
            currentValueRaw = default;
            return false;
        }

    }
}
