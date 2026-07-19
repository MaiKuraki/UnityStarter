using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Explicit Unity authoring source for one immutable GAS network content catalog.
    /// Revisions are reviewed and changed by the author; this asset never derives or persists them automatically.
    /// </summary>
    [CreateAssetMenu(
        fileName = "GASNetworkContentCatalog",
        menuName = "CycloneGames/Gameplay Abilities/Networking Content Catalog")]
    public sealed class GASNetworkContentCatalogAsset : ScriptableObject
    {
        private const int MaximumRevisionLength = 512;
        private const int MaximumRuntimeNameLength = 256;

        [SerializeField] private List<AbilityRegistration> abilities = new List<AbilityRegistration>();
        [SerializeField] private List<EffectRegistration> effects = new List<EffectRegistration>();
        [SerializeField] private List<NameRegistration> attributes = new List<NameRegistration>();
        [SerializeField] private List<NameRegistration> setByCallerNames = new List<NameRegistration>();
        [SerializeField] private List<TargetSurfaceRegistration> targetSurfaces = new List<TargetSurfaceRegistration>();

        /// <summary>
        /// Builds a new immutable catalog. This is a cold-path operation and must run on the Unity
        /// main thread because ability/effect assets may create or expose cached runtime definitions.
        /// </summary>
        public GASNetworkContentCatalog BuildCatalog()
        {
            int totalCount = GetTotalCount();
            if (totalCount > GASNetworkContentCatalogBuilder.MaximumEntryCount)
            {
                throw new InvalidOperationException(
                    $"The authoring asset contains {totalCount} entries, exceeding the catalog limit of {GASNetworkContentCatalogBuilder.MaximumEntryCount}.");
            }

            ValidateSerializedEntries();
            ValidateReferencedNetworkContent();

            var builder = new GASNetworkContentCatalogBuilder(totalCount);
            var abilityRuntimeNames = new Dictionary<string, string>(abilities.Count, StringComparer.Ordinal);
            var effectRuntimeNames = new Dictionary<string, string>(effects.Count, StringComparer.Ordinal);

            for (int i = 0; i < abilities.Count; i++)
            {
                AbilityRegistration registration = abilities[i];
                GameplayAbility definition;
                try
                {
                    definition = registration.reference.GetGameplayAbility();
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Ability registration {i} ('{registration.stableKey}') failed to create its runtime definition.",
                        exception);
                }

                if (definition == null)
                {
                    throw new InvalidOperationException(
                        $"Ability registration {i} ('{registration.stableKey}') created a null runtime definition.");
                }

                RegisterRuntimeName(
                    definition.Name,
                    registration.stableKey,
                    abilityRuntimeNames,
                    "ability");
                builder.Add(
                    GASNetworkContentKind.AbilityDefinition,
                    registration.stableKey,
                    ComputeRevision(registration.revision),
                    definition);
            }

            for (int i = 0; i < effects.Count; i++)
            {
                EffectRegistration registration = effects[i];
                GameplayEffect definition;
                try
                {
                    definition = registration.reference.GetGameplayEffect();
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Effect registration {i} ('{registration.stableKey}') failed to obtain its cached runtime definition.",
                        exception);
                }

                if (definition == null)
                {
                    throw new InvalidOperationException(
                        $"Effect registration {i} ('{registration.stableKey}') produced a null runtime definition.");
                }

                RegisterRuntimeName(
                    definition.Name,
                    registration.stableKey,
                    effectRuntimeNames,
                    "effect");
                builder.Add(
                    GASNetworkContentKind.EffectDefinition,
                    registration.stableKey,
                    ComputeRevision(registration.revision),
                    definition);
            }

            AddNames(builder, attributes, GASNetworkContentKind.Attribute);
            AddNames(builder, setByCallerNames, GASNetworkContentKind.SetByCallerName);
            AddTargetSurfaces(builder, targetSurfaces);
            return builder.Build();
        }

        private int GetTotalCount()
        {
            long total = (long)(abilities?.Count ?? 0) +
                         (effects?.Count ?? 0) +
                         (attributes?.Count ?? 0) +
                         (setByCallerNames?.Count ?? 0) +
                         (targetSurfaces?.Count ?? 0);
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        private void ValidateSerializedEntries()
        {
            if (abilities == null || effects == null || attributes == null ||
                setByCallerNames == null || targetSurfaces == null)
            {
                throw new InvalidOperationException("Catalog registration groups must not be null.");
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            var assetReferences = new HashSet<UnityEngine.Object>(
                UnityObjectReferenceEqualityComparer.Instance);
            for (int i = 0; i < abilities.Count; i++)
            {
                AbilityRegistration registration = abilities[i];
                if (registration == null)
                    throw NullRegistration("Ability", i);
                ValidateCommon(registration.stableKey, registration.revision, keys, "Ability", i);
                if (registration.reference == null)
                    throw MissingReference("Ability", i, registration.stableKey);
                if (!assetReferences.Add(registration.reference))
                {
                    throw new InvalidOperationException(
                        $"Ability asset reference for '{registration.stableKey}' is registered more than once.");
                }
            }

            keys.Clear();
            assetReferences.Clear();
            for (int i = 0; i < effects.Count; i++)
            {
                EffectRegistration registration = effects[i];
                if (registration == null)
                    throw NullRegistration("Effect", i);
                ValidateCommon(registration.stableKey, registration.revision, keys, "Effect", i);
                if (registration.reference == null)
                    throw MissingReference("Effect", i, registration.stableKey);
                if (!assetReferences.Add(registration.reference))
                {
                    throw new InvalidOperationException(
                        $"Effect asset reference for '{registration.stableKey}' is registered more than once.");
                }
            }

            ValidateNameGroup(attributes, GASNetworkContentKind.Attribute, keys);
            ValidateNameGroup(setByCallerNames, GASNetworkContentKind.SetByCallerName, keys);

            keys.Clear();
            assetReferences.Clear();
            for (int i = 0; i < targetSurfaces.Count; i++)
            {
                TargetSurfaceRegistration registration = targetSurfaces[i];
                if (registration == null)
                    throw NullRegistration("TargetSurface", i);
                ValidateCommon(
                    registration.stableKey,
                    registration.revision,
                    keys,
                    "TargetSurface",
                    i);
                if (registration.reference == null)
                    throw MissingReference("TargetSurface", i, registration.stableKey);
                if (!assetReferences.Add(registration.reference))
                {
                    throw new InvalidOperationException(
                        $"TargetSurface reference for '{registration.stableKey}' is registered more than once.");
                }
            }
        }

        private void ValidateReferencedNetworkContent()
        {
            var registeredAbilities = new HashSet<UnityEngine.Object>(
                UnityObjectReferenceEqualityComparer.Instance);
            var registeredEffects = new HashSet<UnityEngine.Object>(
                UnityObjectReferenceEqualityComparer.Instance);
            for (int i = 0; i < abilities.Count; i++)
                registeredAbilities.Add(abilities[i].reference);
            for (int i = 0; i < effects.Count; i++)
                registeredEffects.Add(effects[i].reference);

            for (int i = 0; i < abilities.Count; i++)
            {
                GameplayAbilitySO ability = abilities[i].reference;
                ValidatePersistentEffectReference(
                    ability.CostEffect,
                    registeredEffects,
                    $"Ability '{abilities[i].stableKey}' cost effect");
                ValidatePersistentEffectReference(
                    ability.CooldownEffect,
                    registeredEffects,
                    $"Ability '{abilities[i].stableKey}' cooldown effect");
            }

            for (int i = 0; i < effects.Count; i++)
            {
                GameplayEffectSO effect = effects[i].reference;
                if (effect.GrantedAbilities != null)
                {
                    for (int j = 0; j < effect.GrantedAbilities.Count; j++)
                    {
                        GameplayAbilitySO granted = effect.GrantedAbilities[j];
                        if (granted == null)
                        {
                            throw new InvalidOperationException(
                                $"Effect '{effects[i].stableKey}' has a null granted-ability reference at index {j}.");
                        }
                        if (!registeredAbilities.Contains(granted))
                        {
                            throw new InvalidOperationException(
                                $"Effect '{effects[i].stableKey}' grants ability asset '{granted.name}', but that ability is not registered in this catalog.");
                        }
                    }
                }

                if (effect.OverflowEffects == null)
                    continue;
                for (int j = 0; j < effect.OverflowEffects.Count; j++)
                {
                    GameplayEffectSO overflow = effect.OverflowEffects[j];
                    if (overflow == null)
                    {
                        throw new InvalidOperationException(
                            $"Effect '{effects[i].stableKey}' has a null overflow-effect reference at index {j}.");
                    }
                    ValidatePersistentEffectReference(
                        overflow,
                        registeredEffects,
                        $"Effect '{effects[i].stableKey}' overflow effect");
                }
            }
        }

        private static void ValidatePersistentEffectReference(
            GameplayEffectSO effect,
            HashSet<UnityEngine.Object> registeredEffects,
            string owner)
        {
            if (effect == null || effect.DurationPolicy == EDurationPolicy.Instant)
                return;
            if (!registeredEffects.Contains(effect))
            {
                throw new InvalidOperationException(
                    $"{owner} asset '{effect.name}' can produce persistent replicated state but is not registered in this catalog.");
            }
        }

        private static void ValidateNameGroup(
            List<NameRegistration> registrations,
            GASNetworkContentKind kind,
            HashSet<string> keys)
        {
            keys.Clear();
            var values = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < registrations.Count; i++)
            {
                NameRegistration registration = registrations[i];
                if (registration == null)
                    throw NullRegistration(kind.ToString(), i);
                ValidateCommon(registration.stableKey, registration.revision, keys, kind.ToString(), i);
                ValidateRuntimeText(registration.value, kind.ToString(), i, "value");
                if (!values.Add(registration.value))
                {
                    throw new InvalidOperationException(
                        $"{kind} value '{registration.value}' is registered more than once.");
                }
            }
        }

        private static void ValidateCommon(
            string stableKey,
            string revision,
            HashSet<string> keys,
            string group,
            int index)
        {
            ValidateRuntimeText(stableKey, group, index, "stable key", GASNetworkContentCatalogBuilder.MaximumStableKeyLength);
            ValidateRuntimeText(revision, group, index, "revision", MaximumRevisionLength);
            if (!keys.Add(stableKey))
            {
                throw new InvalidOperationException(
                    $"{group} stable key '{stableKey}' is registered more than once.");
            }
        }

        private static void ValidateRuntimeText(
            string value,
            string group,
            int index,
            string field,
            int maximumLength = MaximumRuntimeNameLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"{group} registration {index} has an empty {field}.");
            if (value.Length > maximumLength)
            {
                throw new InvalidOperationException(
                    $"{group} registration {index} has a {field} longer than {maximumLength} characters.");
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]) || char.IsSurrogate(value[i]))
                {
                    throw new InvalidOperationException(
                        $"{group} registration {index} has an invalid {field}.");
                }
            }
        }

        private static void RegisterRuntimeName(
            string runtimeName,
            string stableKey,
            Dictionary<string, string> stableKeyByRuntimeName,
            string displayKind)
        {
            ValidateRuntimeText(runtimeName, displayKind, 0, "runtime name");
            if (stableKeyByRuntimeName.TryGetValue(runtimeName, out string previousKey))
            {
                throw new InvalidOperationException(
                    $"The {displayKind} runtime name '{runtimeName}' is shared by stable keys '{previousKey}' and '{stableKey}'.");
            }

            stableKeyByRuntimeName.Add(runtimeName, stableKey);
        }

        private static void AddNames(
            GASNetworkContentCatalogBuilder builder,
            List<NameRegistration> registrations,
            GASNetworkContentKind kind)
        {
            for (int i = 0; i < registrations.Count; i++)
            {
                NameRegistration registration = registrations[i];
                builder.Add(
                    kind,
                    registration.stableKey,
                    ComputeRevision(registration.revision),
                    CopyForCatalog(registration.value));
            }
        }

        private static void AddTargetSurfaces(
            GASNetworkContentCatalogBuilder builder,
            List<TargetSurfaceRegistration> registrations)
        {
            for (int i = 0; i < registrations.Count; i++)
            {
                TargetSurfaceRegistration registration = registrations[i];
                builder.Add(
                    GASNetworkContentKind.TargetSurface,
                    registration.stableKey,
                    ComputeRevision(registration.revision),
                    registration.reference);
            }
        }

        private static ulong ComputeRevision(string revision)
        {
            return GASNetworkContentCatalogBuilder.ComputeRevisionHash(revision);
        }

        private static string CopyForCatalog(string value)
        {
            return new string(value.ToCharArray());
        }

        private static InvalidOperationException NullRegistration(string group, int index)
        {
            return new InvalidOperationException($"{group} registration {index} is null.");
        }

        private static InvalidOperationException MissingReference(string group, int index, string stableKey)
        {
            return new InvalidOperationException(
                $"{group} registration {index} ('{stableKey}') has no asset reference.");
        }

        [Serializable]
        private sealed class AbilityRegistration
        {
            [SerializeField, Tooltip("Stable, version-independent network key. Renaming it changes wire identity.")]
            internal string stableKey;

            [SerializeField, Tooltip("Explicit semantic revision. Change it whenever authoritative behavior changes.")]
            internal string revision = "1";

            [SerializeField, Tooltip("GameplayAbility authoring asset used to create the registered runtime definition.")]
            internal GameplayAbilitySO reference;
        }

        [Serializable]
        private sealed class EffectRegistration
        {
            [SerializeField, Tooltip("Stable, version-independent network key. Renaming it changes wire identity.")]
            internal string stableKey;

            [SerializeField, Tooltip("Explicit semantic revision. Change it whenever authoritative behavior changes.")]
            internal string revision = "1";

            [SerializeField, Tooltip("GameplayEffect authoring asset whose cached runtime definition is registered.")]
            internal GameplayEffectSO reference;
        }

        [Serializable]
        private sealed class NameRegistration
        {
            [SerializeField, Tooltip("Stable, version-independent network key. Renaming it changes wire identity.")]
            internal string stableKey;

            [SerializeField, Tooltip("Explicit semantic revision. Change it whenever authoritative behavior changes.")]
            internal string revision = "1";

            [SerializeField, Tooltip("Exact ordinal runtime value. It is not inferred from a type or Unity object.")]
            internal string value;
        }

        [Serializable]
        private sealed class TargetSurfaceRegistration
        {
            [SerializeField, Tooltip("Stable, version-independent network key. Renaming it changes wire identity.")]
            internal string stableKey;

            [SerializeField, Tooltip("Explicit semantic revision. Change it whenever target validation semantics change.")]
            internal string revision = "1";

            [SerializeField, Tooltip("Exact target-surface object registered by reference identity.")]
            internal UnityEngine.Object reference;
        }

        private sealed class UnityObjectReferenceEqualityComparer : IEqualityComparer<UnityEngine.Object>
        {
            public static readonly UnityObjectReferenceEqualityComparer Instance =
                new UnityObjectReferenceEqualityComparer();

            public bool Equals(UnityEngine.Object x, UnityEngine.Object y) => ReferenceEquals(x, y);

            public int GetHashCode(UnityEngine.Object obj)
            {
                return obj == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
