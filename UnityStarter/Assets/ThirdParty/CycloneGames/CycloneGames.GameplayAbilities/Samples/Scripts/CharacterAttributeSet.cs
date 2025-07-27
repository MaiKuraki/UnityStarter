using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class CharacterAttributeSet : AttributeSet
    {
        // --- Primary Attributes ---
        public GameplayAttribute Level { get; } = new GameplayAttribute(GASSampleTags.Attribute_Primary_Level);

        public GameplayAttribute Experience { get; } = new GameplayAttribute(GASSampleTags.Attribute_Meta_Experience);
        public GameplayAttribute AttackPower { get; } = new GameplayAttribute(GASSampleTags.Attribute_Primary_Attack);
        public GameplayAttribute Defense { get; } = new GameplayAttribute(GASSampleTags.Attribute_Primary_Defense);
        public GameplayAttribute Speed { get; } = new GameplayAttribute(GASSampleTags.Attribute_Secondary_Speed);

        // --- Secondary Attributes ---
        public GameplayAttribute Health { get; } = new GameplayAttribute(GASSampleTags.Attribute_Secondary_Health);
        public GameplayAttribute MaxHealth { get; } = new GameplayAttribute(GASSampleTags.Attribute_Secondary_MaxHealth);
        public GameplayAttribute Mana { get; } = new GameplayAttribute(GASSampleTags.Attribute_Secondary_Mana);
        public GameplayAttribute MaxMana { get; } = new GameplayAttribute(GASSampleTags.Attribute_Secondary_MaxMana);

        // --- Meta Attributes (temporary values for calculations) ---
        public GameplayAttribute BonusDamageMultiplier { get; } = new GameplayAttribute(GASSampleTags.Data_DamageMultiplier);
        public GameplayAttribute Damage { get; } = new GameplayAttribute(GASSampleTags.Attribute_Meta_Damage);

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
        /// This hook handles the 'Damage' meta attribute, completely overriding the default logic.
        /// </summary>
        protected override bool PreProcessInstantEffect(GameplayEffectModCallbackData data)
        {
            //  Do not call base.PreProcessInstantEffect(data) here,

            var attribute = GetAttribute(data.Modifier.AttributeName);
            if (attribute == Damage)
            {
                // --- Damage Mitigation Logic ---
                float incomingDamage = data.EvaluatedMagnitude;
                if (incomingDamage <= 0) return true;

                // Get the multiplier from the incoming Spec's SetByCaller data.
                // If the tag doesn't exist in the Spec, default to 1.0f.
                float damageMultiplier = data.EffectSpec.GetSetByCallerMagnitude(
                    GameplayTagManager.RequestTag(GASSampleTags.Data_DamageMultiplier),
                    warnIfNotFound: false,
                    defaultValue: 1.0f);

                if (damageMultiplier > 0)
                {
                    incomingDamage *= damageMultiplier;
                }

                float currentHealth = GetCurrentValue(Health);
                float currentDefense = GetCurrentValue(Defense);

                float mitigatedDamage = incomingDamage * (1 - currentDefense / (currentDefense + 100));
                mitigatedDamage = System.Math.Max(0, mitigatedDamage);

                float newHealth = currentHealth - mitigatedDamage;
                newHealth = System.Math.Max(0, newHealth);
                
                SetBaseValue(Health, newHealth);
                SetCurrentValue(Health, newHealth);

                // --- Death and Bounty Logic ---
                if (newHealth <= 0 && currentHealth > 0)
                {
                    var targetASC = data.Target;
                    targetASC.AddLooseGameplayTag(GameplayTagManager.RequestTag(GASSampleTags.State_Dead));
                    CLogger.LogWarning($"{targetASC.OwnerActor} has died!");

                    var killerASC = data.EffectSpec.Source;
                    if (killerASC != null && killerASC != targetASC)
                    {
                        if (targetASC.OwnerActor is Character deadCharacter)
                        {
                            deadCharacter.GrantBountyTo(killerASC);
                        }
                    }
                }

                return true; // Handled, no further processing needed.
            }

            return false; // For any other attribute, return false to allow the default logic to run.
        }

        /// <summary>
        /// Called after a GameplayEffect has been executed on this AttributeSet.
        /// This is the ideal place for complex calculations like damage mitigation.
        /// </summary>
        protected override void PostProcessInstantEffect(GameplayEffectModCallbackData data)
        {
            base.PostProcessInstantEffect(data);

            var attribute = GetAttribute(data.Modifier.AttributeName);
            if (attribute == Experience && data.EffectSpec.Def.AssetTags.HasTag(GameplayTagManager.RequestTag(GASSampleTags.Event_Experience_Gain)))
            {
                if (data.Target.OwnerActor is Character character)
                {
                    character.CheckForLevelUp();
                }
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

            if (attribute == Experience)
            {
                bool hasExpGainTag = data.EffectSpec.Def.AssetTags.HasTag(GameplayTagManager.RequestTag(GASSampleTags.Event_Experience_Gain));

                if (hasExpGainTag)
                {
                    if (data.Target.OwnerActor is Character character)
                    {
                        character.CheckForLevelUp();
                    }
                }
            }
        }
    }
}