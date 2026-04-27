using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Default runtime registry for deterministic ID mapping.
    /// </summary>
    public sealed class DefaultGASNetIdRegistry : IGASNetIdRegistry
    {
        private readonly Dictionary<int, GameplayAbility> _abilityById = new Dictionary<int, GameplayAbility>(128);
        private readonly Dictionary<GameplayAbility, int> _idByAbility = new Dictionary<GameplayAbility, int>(128);

        private readonly Dictionary<int, GameplayEffect> _effectById = new Dictionary<int, GameplayEffect>(128);
        private readonly Dictionary<GameplayEffect, int> _idByEffect = new Dictionary<GameplayEffect, int>(128);

        private readonly Dictionary<int, string> _attributeNameById = new Dictionary<int, string>(128);
        private readonly Dictionary<string, int> _idByAttributeName = new Dictionary<string, int>(128, StringComparer.Ordinal);

        private readonly Dictionary<int, GameplayTag> _tagByHash = new Dictionary<int, GameplayTag>(256);

        private readonly Dictionary<uint, AbilitySystemComponent> _ascByNetworkId = new Dictionary<uint, AbilitySystemComponent>(64);
        private readonly Dictionary<AbilitySystemComponent, uint> _networkIdByAsc = new Dictionary<AbilitySystemComponent, uint>(64);

        public int GetAbilityDefinitionId(GameplayAbility ability)
        {
            if (ability == null) return 0;
            if (_idByAbility.TryGetValue(ability, out int existing)) return existing;

            int id = HashStable("ability:" + (ability.Name ?? string.Empty));
            RegisterAbilityDefinition(id, ability);
            return id;
        }

        public bool TryResolveAbilityDefinition(int abilityDefinitionId, out GameplayAbility ability)
        {
            return _abilityById.TryGetValue(abilityDefinitionId, out ability);
        }

        public int GetEffectDefinitionId(GameplayEffect effect)
        {
            if (effect == null) return 0;
            if (_idByEffect.TryGetValue(effect, out int existing)) return existing;

            int id = HashStable("effect:" + (effect.Name ?? string.Empty));
            RegisterEffectDefinition(id, effect);
            return id;
        }

        public bool TryResolveEffectDefinition(int effectDefinitionId, out GameplayEffect effect)
        {
            return _effectById.TryGetValue(effectDefinitionId, out effect);
        }

        public int GetAttributeId(GameplayAttribute attribute)
        {
            if (attribute == null || string.IsNullOrEmpty(attribute.Name)) return 0;
            if (_idByAttributeName.TryGetValue(attribute.Name, out int existing)) return existing;

            int id = HashStable("attr:" + attribute.Name);
            RegisterAttribute(id, attribute.Name);
            return id;
        }

        public bool TryResolveAttributeName(int attributeId, out string attributeName)
        {
            return _attributeNameById.TryGetValue(attributeId, out attributeName);
        }

        public int GetTagHash(GameplayTag tag)
        {
            if (tag.IsNone || !tag.IsValid) return 0;

            int hash = HashStable("tag:" + tag.Name);
            RegisterTag(hash, tag);
            return hash;
        }

        public bool TryResolveTag(int tagHash, out GameplayTag tag)
        {
            return _tagByHash.TryGetValue(tagHash, out tag);
        }

        public void RegisterAbilityDefinition(int abilityDefinitionId, GameplayAbility ability)
        {
            if (abilityDefinitionId == 0 || ability == null) return;

            if (_abilityById.TryGetValue(abilityDefinitionId, out var existing) && !ReferenceEquals(existing, ability))
            {
                GASNetLogger.LogWarning($"[DefaultGASNetIdRegistry] Ability ID collision: {abilityDefinitionId}.");
                return;
            }

            _abilityById[abilityDefinitionId] = ability;
            _idByAbility[ability] = abilityDefinitionId;
        }

        public void RegisterEffectDefinition(int effectDefinitionId, GameplayEffect effect)
        {
            if (effectDefinitionId == 0 || effect == null) return;

            if (_effectById.TryGetValue(effectDefinitionId, out var existing) && !ReferenceEquals(existing, effect))
            {
                GASNetLogger.LogWarning($"[DefaultGASNetIdRegistry] Effect ID collision: {effectDefinitionId}.");
                return;
            }

            _effectById[effectDefinitionId] = effect;
            _idByEffect[effect] = effectDefinitionId;
        }

        public void RegisterAttribute(int attributeId, string attributeName)
        {
            if (attributeId == 0 || string.IsNullOrEmpty(attributeName)) return;

            if (_attributeNameById.TryGetValue(attributeId, out var existing) && !string.Equals(existing, attributeName, StringComparison.Ordinal))
            {
                GASNetLogger.LogWarning($"[DefaultGASNetIdRegistry] Attribute ID collision: {attributeId}. existing='{existing}', incoming='{attributeName}'.");
                return;
            }

            _attributeNameById[attributeId] = attributeName;
            _idByAttributeName[attributeName] = attributeId;
        }

        public void RegisterTag(int tagHash, GameplayTag tag)
        {
            if (tagHash == 0 || tag.IsNone || !tag.IsValid) return;

            if (_tagByHash.TryGetValue(tagHash, out var existing) && existing != tag)
            {
                GASNetLogger.LogWarning($"[DefaultGASNetIdRegistry] Tag hash collision: {tagHash}. existing='{existing}', incoming='{tag}'.");
                return;
            }

            _tagByHash[tagHash] = tag;
        }

        public void RegisterAsc(uint networkId, AbilitySystemComponent asc)
        {
            if (networkId == 0 || asc == null) return;
            _ascByNetworkId[networkId] = asc;
            _networkIdByAsc[asc] = networkId;
        }

        public void UnregisterAsc(uint networkId)
        {
            if (_ascByNetworkId.TryGetValue(networkId, out var asc))
            {
                _networkIdByAsc.Remove(asc);
                _ascByNetworkId.Remove(networkId);
            }
        }

        public bool TryResolveAsc(uint networkId, out AbilitySystemComponent asc)
        {
            return _ascByNetworkId.TryGetValue(networkId, out asc);
        }

        public bool TryResolveNetworkId(AbilitySystemComponent asc, out uint networkId)
        {
            if (asc != null && _networkIdByAsc.TryGetValue(asc, out networkId)) return true;
            networkId = 0;
            return false;
        }

        private static int HashStable(string value)
        {
            unchecked
            {
                const uint offsetBasis = 2166136261u;
                const uint prime = 16777619u;

                uint hash = offsetBasis;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= prime;
                }

                return (int)hash;
            }
        }
    }
}