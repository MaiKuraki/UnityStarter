using System;
using System.Collections.Generic;
using CycloneGames.Hash.Core;

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
        private readonly object syncRoot = new object();
        private readonly Dictionary<string, GASAttributeDefinition> byName = new Dictionary<string, GASAttributeDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<int, GASAttributeDefinition> byId = new Dictionary<int, GASAttributeDefinition>();
        private int nextId = 1;

        public GASDefaultAttributeRegistry()
        {
        }

        public GASAttributeId RegisterAttribute(string stableName, uint contentHash = 0)
        {
            if (string.IsNullOrWhiteSpace(stableName))
            {
                throw new ArgumentException("Attribute names must be non-empty.", nameof(stableName));
            }

            lock (syncRoot)
            {
                if (byName.TryGetValue(stableName, out var existing))
                {
                    return existing.Id;
                }

                if (nextId == int.MaxValue)
                {
                    throw new InvalidOperationException("The GAS attribute registry exhausted its ID space.");
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
            return StableHash32.ComputeUtf16Ordinal(stableName.AsSpan());
        }
    }
}
