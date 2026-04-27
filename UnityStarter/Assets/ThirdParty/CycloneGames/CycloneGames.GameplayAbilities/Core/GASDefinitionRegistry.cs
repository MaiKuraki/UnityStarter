using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Core
{
    public interface IGASDefinitionRegistry
    {
        GASDefinitionId RegisterAbilityDefinition(object abilityDefinition, string stableName, uint contentHash = 0);
        GASDefinitionId RegisterEffectDefinition(object effectDefinition, string stableName, uint contentHash = 0);
        bool TryGetAbilityDefinitionId(object abilityDefinition, out GASDefinitionId id);
        bool TryGetEffectDefinitionId(object effectDefinition, out GASDefinitionId id);
        object ResolveAbilityDefinition(GASDefinitionId id);
        object ResolveEffectDefinition(GASDefinitionId id);
        bool TryGetDefinitionVersion(GASDefinitionId id, out GASDefinitionVersion version);
    }

    public sealed class GASDefaultDefinitionRegistry : IGASDefinitionRegistry
    {
        public static readonly GASDefaultDefinitionRegistry Instance = new GASDefaultDefinitionRegistry();

        private readonly object syncRoot = new object();
        private readonly Dictionary<object, GASDefinitionVersion> byObject = new Dictionary<object, GASDefinitionVersion>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<int, object> abilityById = new Dictionary<int, object>();
        private readonly Dictionary<int, object> effectById = new Dictionary<int, object>();
        private readonly Dictionary<int, GASDefinitionVersion> versionById = new Dictionary<int, GASDefinitionVersion>();
        private int nextId = 1;

        private GASDefaultDefinitionRegistry()
        {
        }

        public GASDefinitionId RegisterAbilityDefinition(object abilityDefinition, string stableName, uint contentHash = 0)
        {
            return RegisterDefinition(abilityDefinition, stableName, contentHash, GASDefinitionKind.Ability, abilityById);
        }

        public GASDefinitionId RegisterEffectDefinition(object effectDefinition, string stableName, uint contentHash = 0)
        {
            return RegisterDefinition(effectDefinition, stableName, contentHash, GASDefinitionKind.Effect, effectById);
        }

        public bool TryGetAbilityDefinitionId(object abilityDefinition, out GASDefinitionId id)
        {
            return TryGetDefinitionId(abilityDefinition, GASDefinitionKind.Ability, out id);
        }

        public bool TryGetEffectDefinitionId(object effectDefinition, out GASDefinitionId id)
        {
            return TryGetDefinitionId(effectDefinition, GASDefinitionKind.Effect, out id);
        }

        public object ResolveAbilityDefinition(GASDefinitionId id)
        {
            lock (syncRoot)
            {
                abilityById.TryGetValue(id.Value, out var definition);
                return definition;
            }
        }

        public object ResolveEffectDefinition(GASDefinitionId id)
        {
            lock (syncRoot)
            {
                effectById.TryGetValue(id.Value, out var definition);
                return definition;
            }
        }

        public bool TryGetDefinitionVersion(GASDefinitionId id, out GASDefinitionVersion version)
        {
            lock (syncRoot)
            {
                return versionById.TryGetValue(id.Value, out version);
            }
        }

        private GASDefinitionId RegisterDefinition(object definition, string stableName, uint contentHash, GASDefinitionKind kind, Dictionary<int, object> typedLookup)
        {
            if (definition == null)
            {
                return default;
            }

            lock (syncRoot)
            {
                if (byObject.TryGetValue(definition, out var existing))
                {
                    return existing.Id;
                }

                uint hash = contentHash != 0 ? contentHash : ComputeStableHash(stableName);
                var id = new GASDefinitionId(nextId++);
                var version = new GASDefinitionVersion(kind, id, hash);
                byObject.Add(definition, version);
                typedLookup.Add(id.Value, definition);
                versionById.Add(id.Value, version);
                return id;
            }
        }

        private bool TryGetDefinitionId(object definition, GASDefinitionKind expectedKind, out GASDefinitionId id)
        {
            lock (syncRoot)
            {
                if (definition != null && byObject.TryGetValue(definition, out var version) && version.Kind == expectedKind)
                {
                    id = version.Id;
                    return true;
                }
            }

            id = default;
            return false;
        }

        private static uint ComputeStableHash(string stableName)
        {
            unchecked
            {
                uint hash = 2166136261u;
                if (!string.IsNullOrEmpty(stableName))
                {
                    for (int i = 0; i < stableName.Length; i++)
                    {
                        hash = (hash ^ stableName[i]) * 16777619u;
                    }
                }

                return hash != 0 ? hash : 2166136261u;
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return obj == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
