using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Null replication resolver. All lookups fail; all IDs return 0.
    /// Suitable only when replication is entirely disabled.
    /// For single-player or listen-server use, prefer <see cref="GASLocalReplicationResolver"/>.
    /// </summary>
    public sealed class GASNullReplicationResolver : IGASReplicationResolver
    {
        public static readonly GASNullReplicationResolver Instance = new GASNullReplicationResolver();
        private GASNullReplicationResolver() { }

        public int GetAbilitySystemNetworkId(IGASNetworkTarget asc) => 0;
        public bool TryResolveAbilitySystem(int networkId, out IGASNetworkTarget asc) { asc = null; return false; }
        public int GetGameplayEffectDefinitionId(object effectDefinition) => 0;
        public object ResolveGameplayEffectDefinition(int effectDefinitionId) => null;
    }

    /// <summary>
    /// Bidirectional in-process replication resolver.
    /// Assigns stable monotonically-increasing integer IDs to ASC instances and effect definitions
    /// and resolves them back. Thread-safe via lock.
    /// 
    /// Use this as the default for single-player, listen-server, or test contexts where all objects
    /// live in the same process. For dedicated-server / client-server topologies, implement
    /// <see cref="IGASReplicationResolver"/> using your transport's network object IDs.
    /// </summary>
    public sealed class GASLocalReplicationResolver : IGASReplicationResolver
    {
        public static readonly GASLocalReplicationResolver Instance = new GASLocalReplicationResolver();

        private readonly object _lock = new object();
        private int _nextId = 1;

        // ASC registry
        private readonly Dictionary<IGASNetworkTarget, int> _ascToId = new Dictionary<IGASNetworkTarget, int>(16);
        private readonly Dictionary<int, IGASNetworkTarget> _idToAsc = new Dictionary<int, IGASNetworkTarget>(16);

        // Effect definition registry
        private readonly Dictionary<object, int> _defToId = new Dictionary<object, int>(64);
        private readonly Dictionary<int, object> _idToDef = new Dictionary<int, object>(64);

        // ----- ASC -----

        /// <summary>Registers an ASC and returns its stable ID. Idempotent: re-registering the same instance returns the same ID.</summary>
        public int Register(IGASNetworkTarget asc)
        {
            if (asc == null) return 0;
            lock (_lock)
            {
                if (_ascToId.TryGetValue(asc, out int existing)) return existing;
                int id = _nextId++;
                _ascToId[asc] = id;
                _idToAsc[id] = asc;
                return id;
            }
        }

        /// <summary>Removes an ASC from the registry (call when the ASC is destroyed).</summary>
        public void Unregister(IGASNetworkTarget asc)
        {
            if (asc == null) return;
            lock (_lock)
            {
                if (_ascToId.TryGetValue(asc, out int id))
                {
                    _ascToId.Remove(asc);
                    _idToAsc.Remove(id);
                }
            }
        }

        public int GetAbilitySystemNetworkId(IGASNetworkTarget asc)
        {
            if (asc == null) return 0;
            lock (_lock)
            {
                if (_ascToId.TryGetValue(asc, out int id)) return id;
                // Auto-register on first encounter so callers don't have to pre-register.
                int newId = _nextId++;
                _ascToId[asc] = newId;
                _idToAsc[newId] = asc;
                return newId;
            }
        }

        public bool TryResolveAbilitySystem(int networkId, out IGASNetworkTarget asc)
        {
            lock (_lock) { return _idToAsc.TryGetValue(networkId, out asc); }
        }

        // ----- Effect Definitions -----

        /// <summary>Registers an effect definition and returns its stable ID. Idempotent.</summary>
        public int RegisterDefinition(object effectDefinition)
        {
            if (effectDefinition == null) return 0;
            lock (_lock)
            {
                if (_defToId.TryGetValue(effectDefinition, out int existing)) return existing;
                int id = _nextId++;
                _defToId[effectDefinition] = id;
                _idToDef[id] = effectDefinition;
                return id;
            }
        }

        public int GetGameplayEffectDefinitionId(object effectDefinition)
        {
            if (effectDefinition == null) return 0;
            lock (_lock)
            {
                if (_defToId.TryGetValue(effectDefinition, out int id)) return id;
                int newId = _nextId++;
                _defToId[effectDefinition] = newId;
                _idToDef[newId] = effectDefinition;
                return newId;
            }
        }

        public object ResolveGameplayEffectDefinition(int effectDefinitionId)
        {
            lock (_lock)
            {
                _idToDef.TryGetValue(effectDefinitionId, out var def);
                return def;
            }
        }

        /// <summary>Clears all registrations. Use when transitioning between scenes / sessions.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _ascToId.Clear();
                _idToAsc.Clear();
                _defToId.Clear();
                _idToDef.Clear();
                _nextId = 1;
            }
        }
    }
}
