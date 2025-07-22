using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class CharacterAttributeSet : AttributeSet
    {
        // --- Primary Attributes ---
        public GameplayAttribute Level { get; } = new GameplayAttribute(GASSampleTags.Attribute_Primary_Level);
        public GameplayAttribute AttackPower { get; } = new GameplayAttribute(GASSampleTags.Attribute_Primary_Attack);
        public GameplayAttribute Defense { get; } = new GameplayAttribute(GASSampleTags.Attribute_Primary_Defense);
        public GameplayAttribute Speed { get; } = new GameplayAttribute(GASSampleTags.Attribute_Secondary_Speed);

        // --- Secondary Attributes ---
        public GameplayAttribute Health { get; } = new GameplayAttribute(GASSampleTags.Attribute_Secondary_Health);
        public GameplayAttribute MaxHealth { get; } = new GameplayAttribute(GASSampleTags.Attribute_Secondary_MaxHealth);
        public GameplayAttribute Mana { get; } = new GameplayAttribute(GASSampleTags.Attribute_Secondary_Mana);
        public GameplayAttribute MaxMana { get; } = new GameplayAttribute(GASSampleTags.Attribute_Secondary_MaxMana);

        // --- Meta Attributes (temporary values for calculations) ---
        public GameplayAttribute Damage { get; } = new GameplayAttribute(GASSampleTags.Attribute_Meta_Damage);
        public GameplayAttribute Experience { get; } = new GameplayAttribute(GASSampleTags.Attribute_Meta_Experience);

        public CharacterAttributeSet()
        {
            // This is where you would initialize default values if needed,
            // but we'll do it via a GameplayEffect for better data-driven design.
        }

        /// <summary>
        /// Called before a change is made to an attribute's CurrentValue. Perfect for clamping.
        /// </summary>
        public override void PreAttributeChange(GameplayAttribute attribute, ref float newValue)
        {
            base.PreAttributeChange(attribute, ref newValue);

            if (attribute == Health)
            {
                newValue = System.Math.Clamp(newValue, 0, GetCurrentValue(MaxHealth));
            }
            else if (attribute == Mana)
            {
                newValue = System.Math.Clamp(newValue, 0, GetCurrentValue(MaxMana));
            }
        }

        /// <summary>
        /// Called after a GameplayEffect has been executed on this AttributeSet.
        /// This is the ideal place for complex calculations like damage mitigation.
        /// </summary>
        public override void PostGameplayEffectExecute(GameplayEffectModCallbackData data)
        {
            base.PostGameplayEffectExecute(data);

            var attribute = GetAttribute(data.Modifier.AttributeName);
            if (attribute == null) return;

            if (attribute == Damage)
            {
                // The magnitude from the GE is the raw, pre-mitigation damage value.
                // By convention, this should always be a positive number.
                float incomingDamage = data.EvaluatedMagnitude;

                if (incomingDamage <= 0) return;

                float currentHealth = GetCurrentValue(Health);
                float currentDefense = GetCurrentValue(Defense);

                //  TODO: in this simple sample, set a simple damage mitigation formula.
                float mitigatedDamage = incomingDamage * (1 - currentDefense / (currentDefense + 100));
                mitigatedDamage = System.Math.Max(0, mitigatedDamage);

                float newHealth = currentHealth - mitigatedDamage;
                SetBaseValue(Health, newHealth);

                // --- Death and Bounty ---
                if (newHealth <= 0 && currentHealth > 0)
                {
                    var targetASC = data.Target;
                    targetASC.AddLooseGameplayTag(GameplayTagManager.RequestTag(GASSampleTags.State_Dead));
                    CLogger.LogWarning($"{targetASC.OwnerActor} has died!");

                    // Find the killer from the effect's source.
                    var killerASC = data.EffectSpec.Source;
                    if (killerASC != null && killerASC != targetASC)
                    {
                        // The 'target' character that died needs to hold a reference to its bounty GE.
                        if (targetASC.OwnerActor is Character deadCharacter)
                        {
                            deadCharacter.GrantBountyTo(killerASC);
                        }
                    }
                }
                return; // Damage processing is complete.
            }

            if (attribute == Experience)
            {
                int xpGained = (int)data.EvaluatedMagnitude;
                if (data.Target.OwnerActor is Character character)
                {
                    character.AddExperience(xpGained);
                }
                return;
            }

            // --- Direct Attribute Modification Handling ---
            // This section handles permanent changes to regular attributes (like Health from a DoT).
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
            SetBaseValue(attribute, newBase);
        }
    }
}