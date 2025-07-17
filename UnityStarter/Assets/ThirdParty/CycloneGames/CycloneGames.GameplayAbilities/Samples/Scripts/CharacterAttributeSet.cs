using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class CharacterAttributeSet : AttributeSet
    {
        // --- Primary Attributes ---
        public GameplayAttribute Level { get; } = new GameplayAttribute("Level");
        public GameplayAttribute AttackPower { get; } = new GameplayAttribute("AttackPower");
        public GameplayAttribute Defense { get; } = new GameplayAttribute("Defense");
        public GameplayAttribute Speed { get; } = new GameplayAttribute("Speed");

        // --- Secondary Attributes ---
        public GameplayAttribute Health { get; } = new GameplayAttribute("Health");
        public GameplayAttribute MaxHealth { get; } = new GameplayAttribute("MaxHealth");
        public GameplayAttribute Mana { get; } = new GameplayAttribute("Mana");
        public GameplayAttribute MaxMana { get; } = new GameplayAttribute("MaxMana");

        // --- Meta Attributes (temporary values for calculations) ---
        public GameplayAttribute Damage { get; } = new GameplayAttribute("Damage");

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

            // We use a "meta attribute" for damage. This effect applies a temporary value to the 'Damage' attribute.
            // We then intercept that change here to perform the final health modification.
            if (data.Modifier.AttributeName == Damage.Name)
            {
                float incomingDamage = data.EvaluatedMagnitude;

                // Convert the temporary 'Damage' into a permanent health reduction.
                // First, get the victim's current health and defense.
                float currentHealth = GetCurrentValue(Health);
                float currentDefense = GetCurrentValue(Defense);

                // Simple mitigation formula: Damage * (1 - Defense / (Defense + 100))
                // This provides diminishing returns for defense.
                float mitigatedDamage = incomingDamage * (1 - currentDefense / (currentDefense + 100));
                mitigatedDamage = System.Math.Max(0, mitigatedDamage); // Damage shouldn't heal.

                float newHealth = currentHealth - mitigatedDamage;

                // Apply the final health value.
                // Note: This directly sets the base value, but for health changes, it's common
                // to adjust the current value. We use SetBaseValue here for simplicity,
                // assuming direct health damage. A more robust system might use another effect.
                SetBaseValue(Health, newHealth);

                // If health has dropped to 0, broadcast a death event.
                if (newHealth <= 0 && currentHealth > 0)
                {
                    // The 'data.Target' is the AbilitySystemComponent of the character being damaged.
                    // It's a good practice to use tags for state changes.
                    data.Target.AddLooseGameplayTag(GameplayTagManager.RequestTag(ProjectGameplayTags.State_Dead));
                    CLogger.LogWarning($"{data.Target.OwnerActor} has died!");
                }
            }
        }
    }
}