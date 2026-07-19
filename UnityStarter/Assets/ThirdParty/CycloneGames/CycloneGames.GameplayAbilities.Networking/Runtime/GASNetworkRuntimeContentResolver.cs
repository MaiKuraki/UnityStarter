using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Immutable runtime resolver built from one frozen <see cref="GASNetworkContentCatalog"/>.
    /// Unity assets are accessed only while the resolver is constructed. Lookups use only explicit
    /// reference-identity or ordinal-string registrations.
    /// </summary>
    public sealed class GASNetworkRuntimeContentResolver : IGASNetworkRuntimeContentResolver
    {
        private const int MaximumRuntimeNameLength = 256;

        private readonly Dictionary<GameplayAbility, GASNetworkContentId> abilityIds;
        private readonly Dictionary<ulong, GameplayAbility> abilities;
        private readonly Dictionary<GameplayEffect, GASNetworkContentId> effectIds;
        private readonly Dictionary<ulong, GameplayEffect> effects;
        private readonly Dictionary<string, GASNetworkContentId> attributeIds;
        private readonly Dictionary<ulong, string> attributeNames;
        private readonly Dictionary<string, GASNetworkContentId> setByCallerNameIds;
        private readonly Dictionary<ulong, string> setByCallerNames;
        private readonly Dictionary<object, GASNetworkContentId> targetSurfaceIds;
        private readonly Dictionary<ulong, object> targetSurfaces;

        public GASNetworkRuntimeContentResolver(GASNetworkContentCatalog catalog)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            ContentCounts counts = CountSupportedEntries(catalog);
            abilityIds = new Dictionary<GameplayAbility, GASNetworkContentId>(
                counts.Abilities,
                ReferenceEqualityComparer<GameplayAbility>.Instance);
            abilities = new Dictionary<ulong, GameplayAbility>(counts.Abilities);
            effectIds = new Dictionary<GameplayEffect, GASNetworkContentId>(
                counts.Effects,
                ReferenceEqualityComparer<GameplayEffect>.Instance);
            effects = new Dictionary<ulong, GameplayEffect>(counts.Effects);
            attributeIds = new Dictionary<string, GASNetworkContentId>(counts.Attributes, StringComparer.Ordinal);
            attributeNames = new Dictionary<ulong, string>(counts.Attributes);
            setByCallerNameIds = new Dictionary<string, GASNetworkContentId>(counts.SetByCallerNames, StringComparer.Ordinal);
            setByCallerNames = new Dictionary<ulong, string>(counts.SetByCallerNames);
            targetSurfaceIds = new Dictionary<object, GASNetworkContentId>(
                counts.TargetSurfaces,
                ReferenceEqualityComparer<object>.Instance);
            targetSurfaces = new Dictionary<ulong, object>(counts.TargetSurfaces);

            var abilityNames = new Dictionary<string, GASNetworkContentId>(
                counts.Abilities,
                StringComparer.Ordinal);
            var effectNames = new Dictionary<string, GASNetworkContentId>(
                counts.Effects,
                StringComparer.Ordinal);

            for (int i = 0; i < catalog.Count; i++)
            {
                GASNetworkContentEntry entry = catalog.GetEntry(i);
                switch (entry.Kind)
                {
                    case GASNetworkContentKind.AbilityDefinition:
                        AddAbility(entry, ResolveAbilityDefinition(entry), abilityNames);
                        break;
                    case GASNetworkContentKind.EffectDefinition:
                        AddEffect(entry, ResolveEffectDefinition(entry), effectNames);
                        break;
                    case GASNetworkContentKind.Attribute:
                        AddName(entry, attributeIds, attributeNames, "attribute");
                        break;
                    case GASNetworkContentKind.SetByCallerName:
                        AddName(entry, setByCallerNameIds, setByCallerNames, "SetByCaller name");
                        break;
                    case GASNetworkContentKind.TargetSurface:
                        AddTargetSurface(entry);
                        break;
                }
            }
        }

        public bool TryGetAbilityId(GameplayAbility ability, out GASNetworkContentId id)
        {
            if (ability != null && abilityIds.TryGetValue(ability, out id))
                return true;

            id = default;
            return false;
        }

        public bool TryResolveAbility(GASNetworkContentId id, out GameplayAbility ability)
        {
            if (id.IsValid && abilities.TryGetValue(id.Value, out ability))
                return true;

            ability = null;
            return false;
        }

        public bool TryGetEffectId(GameplayEffect effect, out GASNetworkContentId id)
        {
            if (effect != null && effectIds.TryGetValue(effect, out id))
                return true;

            id = default;
            return false;
        }

        public bool TryResolveEffect(GASNetworkContentId id, out GameplayEffect effect)
        {
            if (id.IsValid && effects.TryGetValue(id.Value, out effect))
                return true;

            effect = null;
            return false;
        }

        public bool TryGetAttributeId(string attributeName, out GASNetworkContentId id)
        {
            if (attributeName != null && attributeIds.TryGetValue(attributeName, out id))
                return true;

            id = default;
            return false;
        }

        public bool TryResolveAttributeName(GASNetworkContentId id, out string attributeName)
        {
            if (id.IsValid && attributeNames.TryGetValue(id.Value, out attributeName))
                return true;

            attributeName = null;
            return false;
        }

        public bool TryGetSetByCallerNameId(string setByCallerName, out GASNetworkContentId id)
        {
            if (setByCallerName != null && setByCallerNameIds.TryGetValue(setByCallerName, out id))
                return true;

            id = default;
            return false;
        }

        public bool TryResolveSetByCallerName(GASNetworkContentId id, out string setByCallerName)
        {
            if (id.IsValid && setByCallerNames.TryGetValue(id.Value, out setByCallerName))
                return true;

            setByCallerName = null;
            return false;
        }

        public bool TryGetTargetSurfaceId(object targetSurface, out GASNetworkContentId id)
        {
            if (targetSurface != null && targetSurfaceIds.TryGetValue(targetSurface, out id))
                return true;

            id = default;
            return false;
        }

        public bool TryResolveTargetSurface(GASNetworkContentId id, out object targetSurface)
        {
            if (id.IsValid && targetSurfaces.TryGetValue(id.Value, out targetSurface))
                return true;

            targetSurface = null;
            return false;
        }

        private static ContentCounts CountSupportedEntries(GASNetworkContentCatalog catalog)
        {
            var counts = new ContentCounts();
            for (int i = 0; i < catalog.Count; i++)
            {
                switch (catalog.GetEntry(i).Kind)
                {
                    case GASNetworkContentKind.AbilityDefinition:
                        counts.Abilities++;
                        break;
                    case GASNetworkContentKind.EffectDefinition:
                        counts.Effects++;
                        break;
                    case GASNetworkContentKind.Attribute:
                        counts.Attributes++;
                        break;
                    case GASNetworkContentKind.SetByCallerName:
                        counts.SetByCallerNames++;
                        break;
                    case GASNetworkContentKind.TargetSurface:
                        counts.TargetSurfaces++;
                        break;
                }
            }

            return counts;
        }

        private void AddAbility(
            GASNetworkContentEntry entry,
            GameplayAbility ability,
            Dictionary<string, GASNetworkContentId> runtimeNames)
        {
            ValidateRuntimeName(ability.Name, entry, "ability");
            if (runtimeNames.TryGetValue(ability.Name, out GASNetworkContentId conflictingId))
            {
                throw new InvalidOperationException(
                    $"Ability runtime name '{ability.Name}' is registered by both content IDs {conflictingId.Value} and {entry.Id.Value}.");
            }
            if (abilityIds.ContainsKey(ability))
            {
                throw new InvalidOperationException(
                    $"Ability runtime definition '{ability.Name}' is registered more than once.");
            }

            runtimeNames.Add(ability.Name, entry.Id);
            abilityIds.Add(ability, entry.Id);
            abilities.Add(entry.Id.Value, ability);
        }

        private void AddEffect(
            GASNetworkContentEntry entry,
            GameplayEffect effect,
            Dictionary<string, GASNetworkContentId> runtimeNames)
        {
            ValidateRuntimeName(effect.Name, entry, "effect");
            if (runtimeNames.TryGetValue(effect.Name, out GASNetworkContentId conflictingId))
            {
                throw new InvalidOperationException(
                    $"Effect runtime name '{effect.Name}' is registered by both content IDs {conflictingId.Value} and {entry.Id.Value}.");
            }
            if (effectIds.ContainsKey(effect))
            {
                throw new InvalidOperationException(
                    $"Effect runtime definition '{effect.Name}' is registered more than once.");
            }

            runtimeNames.Add(effect.Name, entry.Id);
            effectIds.Add(effect, entry.Id);
            effects.Add(entry.Id.Value, effect);
        }

        private static void AddName(
            GASNetworkContentEntry entry,
            Dictionary<string, GASNetworkContentId> ids,
            Dictionary<ulong, string> names,
            string displayKind)
        {
            if (!(entry.Value is string value))
            {
                throw new InvalidOperationException(
                    $"Content entry '{entry.Kind}:{entry.StableKey}' must provide an explicit {displayKind} string value.");
            }

            ValidateRuntimeName(value, entry, displayKind);
            if (ids.TryGetValue(value, out GASNetworkContentId conflictingId))
            {
                throw new InvalidOperationException(
                    $"The {displayKind} '{value}' is registered by both content IDs {conflictingId.Value} and {entry.Id.Value}.");
            }

            ids.Add(value, entry.Id);
            names.Add(entry.Id.Value, value);
        }

        private void AddTargetSurface(GASNetworkContentEntry entry)
        {
            object value = entry.Value;
            if (value == null)
            {
                throw new InvalidOperationException(
                    $"Content entry '{entry.Kind}:{entry.StableKey}' must provide an explicit target-surface reference.");
            }
            if (targetSurfaceIds.ContainsKey(value))
            {
                throw new InvalidOperationException(
                    $"Target-surface reference for '{entry.StableKey}' is registered more than once.");
            }

            targetSurfaceIds.Add(value, entry.Id);
            targetSurfaces.Add(entry.Id.Value, value);
        }

        private static GameplayAbility ResolveAbilityDefinition(GASNetworkContentEntry entry)
        {
            if (entry.Value is GameplayAbility ability)
                return ability;

            if (entry.Value is GameplayAbilitySO abilityAsset)
            {
                if (abilityAsset == null)
                    throw MissingRuntimeValue(entry, "GameplayAbility or live GameplayAbilitySO");

                try
                {
                    ability = abilityAsset.GetGameplayAbility();
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Ability asset for '{entry.StableKey}' failed to create its runtime definition.",
                        exception);
                }

                if (ability == null)
                    throw MissingRuntimeValue(entry, "GameplayAbility created by GameplayAbilitySO");
                return ability;
            }

            throw MissingRuntimeValue(entry, "GameplayAbility or GameplayAbilitySO");
        }

        private static GameplayEffect ResolveEffectDefinition(GASNetworkContentEntry entry)
        {
            if (entry.Value is GameplayEffect effect)
                return effect;

            if (entry.Value is GameplayEffectSO effectAsset)
            {
                if (effectAsset == null)
                    throw MissingRuntimeValue(entry, "GameplayEffect or live GameplayEffectSO");

                try
                {
                    effect = effectAsset.GetGameplayEffect();
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Effect asset for '{entry.StableKey}' failed to create its cached runtime definition.",
                        exception);
                }

                if (effect == null)
                    throw MissingRuntimeValue(entry, "GameplayEffect cached by GameplayEffectSO");
                return effect;
            }

            throw MissingRuntimeValue(entry, "GameplayEffect or GameplayEffectSO");
        }

        private static InvalidOperationException MissingRuntimeValue(
            GASNetworkContentEntry entry,
            string expected)
        {
            return new InvalidOperationException(
                $"Content entry '{entry.Kind}:{entry.StableKey}' must provide a {expected} value.");
        }

        private static void ValidateRuntimeName(
            string value,
            GASNetworkContentEntry entry,
            string displayKind)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Content entry '{entry.Kind}:{entry.StableKey}' has an empty {displayKind} runtime name.");
            }
            if (value.Length > MaximumRuntimeNameLength)
            {
                throw new InvalidOperationException(
                    $"Content entry '{entry.Kind}:{entry.StableKey}' has a {displayKind} runtime name longer than {MaximumRuntimeNameLength} characters.");
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]) || char.IsSurrogate(value[i]))
                {
                    throw new InvalidOperationException(
                        $"Content entry '{entry.Kind}:{entry.StableKey}' has an invalid {displayKind} runtime name.");
                }
            }
        }

        private struct ContentCounts
        {
            public int Abilities;
            public int Effects;
            public int Attributes;
            public int SetByCallerNames;
            public int TargetSurfaces;
        }

        private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
            where T : class
        {
            public static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();

            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => obj == null ? 0 : RuntimeHelpers.GetHashCode(obj);
        }
    }
}
