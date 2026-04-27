using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Core
{
    public interface IGASAttributeRegistry
    {
        GASAttributeId RegisterAttribute(string stableName, uint contentHash = 0);
        bool TryGetAttributeId(string stableName, out GASAttributeId id);
        bool TryGetAttributeDefinition(GASAttributeId id, out GASAttributeDefinition definition);
    }

    public sealed class GASDefaultAttributeRegistry : IGASAttributeRegistry
    {
        public static readonly GASDefaultAttributeRegistry Instance = new GASDefaultAttributeRegistry();

        private readonly object syncRoot = new object();
        private readonly Dictionary<string, GASAttributeDefinition> byName = new Dictionary<string, GASAttributeDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<int, GASAttributeDefinition> byId = new Dictionary<int, GASAttributeDefinition>();
        private int nextId = 1;

        private GASDefaultAttributeRegistry()
        {
        }

        public GASAttributeId RegisterAttribute(string stableName, uint contentHash = 0)
        {
            if (string.IsNullOrEmpty(stableName))
            {
                return default;
            }

            lock (syncRoot)
            {
                if (byName.TryGetValue(stableName, out var existing))
                {
                    return existing.Id;
                }

                uint hash = contentHash != 0 ? contentHash : ComputeStableHash(stableName);
                var id = new GASAttributeId(nextId++);
                var definition = new GASAttributeDefinition(id, stableName, hash);
                byName.Add(stableName, definition);
                byId.Add(id.Value, definition);
                return id;
            }
        }

        public bool TryGetAttributeId(string stableName, out GASAttributeId id)
        {
            lock (syncRoot)
            {
                if (!string.IsNullOrEmpty(stableName) && byName.TryGetValue(stableName, out var definition))
                {
                    id = definition.Id;
                    return true;
                }
            }

            id = default;
            return false;
        }

        public bool TryGetAttributeDefinition(GASAttributeId id, out GASAttributeDefinition definition)
        {
            lock (syncRoot)
            {
                return byId.TryGetValue(id.Value, out definition);
            }
        }

        private static uint ComputeStableHash(string stableName)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < stableName.Length; i++)
                {
                    hash = (hash ^ stableName[i]) * 16777619u;
                }

                return hash != 0 ? hash : 2166136261u;
            }
        }
    }
}
