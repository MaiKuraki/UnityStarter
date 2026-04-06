using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.GAS
{
    /// <summary>
    /// Server-side effect replication tracker.
    /// 
    /// Assigns unique instance IDs to active effects and tracks which clients know about them.
    /// When effects are applied/removed/stacked, broadcasts changes to relevant observers.
    /// 
    /// ReplicationMode behavior:
    /// - Full:    All effects replicated (HP regen, buffs, debuffs, internal calculations)
    /// - Mixed:   Gameplay-visible effects replicated (buffs/debuffs with cues), internal ones skipped
    /// - Minimal: Only cue triggers replicated, no effect state (client trusts attribute sync)
    /// </summary>
    public sealed class EffectReplicationManager
    {
        private readonly NetworkedAbilityBridge _bridge;

        // Server-side: effectInstanceId → tracked effect
        private readonly Dictionary<int, TrackedEffect> _trackedEffects =
            new Dictionary<int, TrackedEffect>(128);

        // Per-ASC: networkId → list of active effect instance IDs
        private readonly Dictionary<uint, List<int>> _effectsByTarget =
            new Dictionary<uint, List<int>>(64);

        private int _nextEffectInstanceId = 1;
        private const int MaxEffectInstanceId = int.MaxValue - 1;

        public EffectReplicationManager(NetworkedAbilityBridge bridge)
        {
            _bridge = bridge;
        }

        /// <summary>
        /// Call on server when an effect is applied. Assigns a unique instance ID and
        /// replicates to observers.
        /// </summary>
        /// <returns>The assigned effect instance ID</returns>
        public int OnEffectApplied(uint targetNetworkId, uint sourceNetworkId,
            int effectDefinitionId, int level, int stackCount, float duration,
            int predictionKey, SetByCallerEntry[] setByCallerEntries,
            Func<uint, IReadOnlyList<INetConnection>> getObservers)
        {
            if (_nextEffectInstanceId >= MaxEffectInstanceId)
                _nextEffectInstanceId = 1; // Wrap around, reuse old IDs (they should be long gone)

            int instanceId = _nextEffectInstanceId++;

            _trackedEffects[instanceId] = new TrackedEffect
            {
                InstanceId = instanceId,
                TargetNetworkId = targetNetworkId,
                SourceNetworkId = sourceNetworkId,
                EffectDefinitionId = effectDefinitionId,
                Level = level,
                StackCount = stackCount
            };

            if (!_effectsByTarget.TryGetValue(targetNetworkId, out var list))
            {
                list = new List<int>(16);
                _effectsByTarget[targetNetworkId] = list;
            }
            list.Add(instanceId);

            // Replicate
            var data = new EffectReplicationData
            {
                TargetNetworkId = targetNetworkId,
                SourceNetworkId = sourceNetworkId,
                EffectInstanceId = instanceId,
                EffectDefinitionId = effectDefinitionId,
                Level = level,
                StackCount = stackCount,
                Duration = duration,
                TimeRemaining = duration,
                PredictionKey = predictionKey,
                SetByCallerCount = setByCallerEntries?.Length ?? 0,
                SetByCallerEntries = setByCallerEntries
            };

            var observers = getObservers(targetNetworkId);
            if (observers != null)
                _bridge.ServerReplicateEffectApplied(observers, targetNetworkId, data);

            return instanceId;
        }

        /// <summary>
        /// Call on server when an effect expires or is removed.
        /// </summary>
        public void OnEffectRemoved(int effectInstanceId,
            Func<uint, IReadOnlyList<INetConnection>> getObservers)
        {
            if (!_trackedEffects.TryGetValue(effectInstanceId, out var tracked))
                return;

            _trackedEffects.Remove(effectInstanceId);

            if (_effectsByTarget.TryGetValue(tracked.TargetNetworkId, out var list))
                list.Remove(effectInstanceId);

            var observers = getObservers(tracked.TargetNetworkId);
            if (observers != null)
                _bridge.ServerReplicateEffectRemoved(observers, tracked.TargetNetworkId, effectInstanceId);
        }

        /// <summary>
        /// Call on server when an effect's stack count changes.
        /// </summary>
        public void OnStackChanged(int effectInstanceId, int newStackCount,
            Func<uint, IReadOnlyList<INetConnection>> getObservers)
        {
            if (!_trackedEffects.TryGetValue(effectInstanceId, out var tracked))
                return;

            tracked.StackCount = newStackCount;
            _trackedEffects[effectInstanceId] = tracked;

            var observers = getObservers(tracked.TargetNetworkId);
            if (observers != null)
                _bridge.ServerReplicateStackChange(observers, tracked.TargetNetworkId,
                    effectInstanceId, newStackCount);
        }

        /// <summary>
        /// Get all tracked effects for a target (for full state snapshot on join/reconnect).
        /// </summary>
        public IReadOnlyList<int> GetEffectsForTarget(uint targetNetworkId)
        {
            return _effectsByTarget.TryGetValue(targetNetworkId, out var list) ? list : null;
        }

        /// <summary>
        /// Get tracked effect data by instance ID.
        /// </summary>
        public bool TryGetEffect(int instanceId, out TrackedEffect effect)
        {
            return _trackedEffects.TryGetValue(instanceId, out effect);
        }

        /// <summary>
        /// Send all active effects to a newly joining/reconnecting client.
        /// </summary>
        public void SendFullEffectState(INetConnection client, uint targetNetworkId)
        {
            if (!_effectsByTarget.TryGetValue(targetNetworkId, out var list)) return;

            for (int i = 0; i < list.Count; i++)
            {
                if (_trackedEffects.TryGetValue(list[i], out var tracked))
                {
                    var data = new EffectReplicationData
                    {
                        TargetNetworkId = tracked.TargetNetworkId,
                        SourceNetworkId = tracked.SourceNetworkId,
                        EffectInstanceId = tracked.InstanceId,
                        EffectDefinitionId = tracked.EffectDefinitionId,
                        Level = tracked.Level,
                        StackCount = tracked.StackCount,
                        Duration = 0,       // Full sync: client uses current remaining
                        TimeRemaining = 0,
                        PredictionKey = 0
                    };

                    _bridge.ServerReplicateEffectApplied(
                        new[] { client }, targetNetworkId, data);
                }
            }
        }

        /// <summary>
        /// Clear all tracked effects for a target (on despawn).
        /// </summary>
        public void ClearTarget(uint targetNetworkId)
        {
            if (_effectsByTarget.TryGetValue(targetNetworkId, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                    _trackedEffects.Remove(list[i]);
                list.Clear();
                _effectsByTarget.Remove(targetNetworkId);
            }
        }

        public struct TrackedEffect
        {
            public int InstanceId;
            public uint TargetNetworkId;
            public uint SourceNetworkId;
            public int EffectDefinitionId;
            public int Level;
            public int StackCount;
        }
    }
}
