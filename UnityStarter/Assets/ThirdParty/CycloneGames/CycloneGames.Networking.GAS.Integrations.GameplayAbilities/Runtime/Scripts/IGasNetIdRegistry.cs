using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.Networking.GAS.Integrations.GameplayAbilities
{
    /// <summary>
    /// Registry for stable cross-network IDs used by the GAS bridge.
    /// </summary>
    public interface IGasNetIdRegistry
    {
        int GetAbilityDefinitionId(GameplayAbility ability);
        bool TryResolveAbilityDefinition(int abilityDefinitionId, out GameplayAbility ability);

        int GetEffectDefinitionId(GameplayEffect effect);
        bool TryResolveEffectDefinition(int effectDefinitionId, out GameplayEffect effect);

        int GetAttributeId(GameplayAttribute attribute);
        bool TryResolveAttributeName(int attributeId, out string attributeName);

        int GetTagHash(GameplayTag tag);
        bool TryResolveTag(int tagHash, out GameplayTag tag);

        void RegisterAbilityDefinition(int abilityDefinitionId, GameplayAbility ability);
        void RegisterEffectDefinition(int effectDefinitionId, GameplayEffect effect);
        void RegisterAttribute(int attributeId, string attributeName);
        void RegisterTag(int tagHash, GameplayTag tag);

        void RegisterAsc(uint networkId, AbilitySystemComponent asc);
        void UnregisterAsc(uint networkId);
        bool TryResolveAsc(uint networkId, out AbilitySystemComponent asc);
        bool TryResolveNetworkId(AbilitySystemComponent asc, out uint networkId);
    }
}