using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using UnityEngine;

namespace CycloneGames.Networking.GAS.Integrations.GameplayAbilities
{
    public readonly struct ReplicatedAbilitySystemStateDelta
    {
        public readonly AbilitySystemStateDeltaSnapshot Snapshot;
        public readonly GrantedAbilityEntry[] GrantedAbilities;
        public readonly EffectReplicationData[] AddedActiveEffects;
        public readonly EffectUpdateData[] UpdatedActiveEffects;
        public readonly EffectStackChangeData[] StackChangedEffects;
        public readonly int[] RemovedEffectInstanceIds;
        public readonly AttributeUpdateData AttributeUpdate;
        public readonly TagUpdateData TagUpdate;

        public ReplicatedAbilitySystemStateDelta(
            AbilitySystemStateDeltaSnapshot snapshot,
            GrantedAbilityEntry[] grantedAbilities,
            EffectReplicationData[] addedActiveEffects,
            EffectUpdateData[] updatedActiveEffects,
            EffectStackChangeData[] stackChangedEffects,
            int[] removedEffectInstanceIds,
            AttributeUpdateData attributeUpdate,
            TagUpdateData tagUpdate)
        {
            Snapshot = snapshot;
            GrantedAbilities = grantedAbilities;
            AddedActiveEffects = addedActiveEffects;
            UpdatedActiveEffects = updatedActiveEffects;
            StackChangedEffects = stackChangedEffects;
            RemovedEffectInstanceIds = removedEffectInstanceIds;
            AttributeUpdate = attributeUpdate;
            TagUpdate = tagUpdate;
        }
    }

    /// <summary>
    /// Bridge adapter that connects AbilitySystemComponent to NetworkedAbilityBridge callbacks.
    /// </summary>
    public sealed class GameplayAbilitiesNetworkedASCAdapter : INetworkedASC, IDisposable
    {
        private readonly AbilitySystemComponent _asc;
        private readonly IGasNetIdRegistry _idRegistry;

        private readonly Dictionary<int, ActiveGameplayEffect> _remoteEffectToLocal =
            new Dictionary<int, ActiveGameplayEffect>(64);

        private readonly Dictionary<ActiveGameplayEffect, int> _localEffectToRemote =
            new Dictionary<ActiveGameplayEffect, int>(64);

        private readonly Dictionary<int, EffectReplicationData> _replicatedEffectStates =
            new Dictionary<int, EffectReplicationData>(64);

        private readonly Dictionary<string, int> _attributeIdByName =
            new Dictionary<string, int>(32, StringComparer.Ordinal);

        private readonly Dictionary<int, EffectReplicationData> _currentEffectsByIdScratch =
            new Dictionary<int, EffectReplicationData>(64);

        private readonly List<EffectReplicationData> _addedEffectsScratch =
            new List<EffectReplicationData>(16);

        private readonly List<EffectUpdateData> _updatedEffectsScratch =
            new List<EffectUpdateData>(16);

        private readonly List<EffectStackChangeData> _stackChangedEffectsScratch =
            new List<EffectStackChangeData>(16);

        private readonly List<int> _removedEffectIdsScratch =
            new List<int>(16);

        private readonly HashSet<int> _targetTagHashSetScratch =
            new HashSet<int>();

        private readonly List<int> _tagDiffScratch =
            new List<int>(16);

        private GameplayTag[] _setByCallerTagsScratch = Array.Empty<GameplayTag>();
        private float[] _setByCallerValuesScratch = Array.Empty<float>();

        private int _nextLocalEffectInstanceId = 1;

        public uint NetworkId { get; }
        public int OwnerConnectionId { get; }

        public Action<int, int> OnConfirmActivation { get; set; }
        public Action<int, int> OnRejectActivation { get; set; }
        public Action<int> OnReplicatedAbilityEnd { get; set; }
        public Action<int> OnReplicatedAbilityCancel { get; set; }
        public Action<AbilityMulticastData> OnAbilityMulticastReceived { get; set; }
        public Action<FullStateSnapshotData> OnFullStateSnapshotReceived { get; set; }

        /// <summary>
        /// Optional project-provided callbacks for effect removal/stack changes.
        /// These are needed because ASC currently has no direct public API by remote effect instance ID.
        /// </summary>
        public IGasReplicatedEffectMutationHandler EffectMutationHandler { get; set; }
        public Func<int, bool> TryRemoveReplicatedEffect { get; set; }
        public Func<int, int, bool> TryApplyReplicatedStackChange { get; set; }
        public Func<EffectUpdateData, bool> TryApplyReplicatedEffectUpdate { get; set; }

        public GameplayAbilitiesNetworkedASCAdapter(
            AbilitySystemComponent asc,
            uint networkId,
            int ownerConnectionId,
            IGasNetIdRegistry idRegistry = null)
        {
            _asc = asc ?? throw new ArgumentNullException(nameof(asc));
            _idRegistry = idRegistry ?? new DefaultGasNetIdRegistry();

            NetworkId = networkId;
            OwnerConnectionId = ownerConnectionId;

            _idRegistry.RegisterAsc(networkId, asc);
        }

        public void OnServerConfirmActivation(int abilityIndex, int predictionKey)
        {
            OnConfirmActivation?.Invoke(abilityIndex, predictionKey);
        }

        public void OnServerRejectActivation(int abilityIndex, int predictionKey)
        {
            OnRejectActivation?.Invoke(abilityIndex, predictionKey);
        }

        public void OnAbilityEnded(int abilityIndex)
        {
            var spec = TryResolveAbilitySpec(abilityIndex);
            if (spec?.GetPrimaryInstance() != null)
            {
                spec.GetPrimaryInstance().EndAbility();
            }

            OnReplicatedAbilityEnd?.Invoke(abilityIndex);
        }

        public void OnAbilityCancelled(int abilityIndex)
        {
            var spec = TryResolveAbilitySpec(abilityIndex);
            if (spec?.GetPrimaryInstance() != null)
            {
                spec.GetPrimaryInstance().CancelAbility();
            }

            OnReplicatedAbilityCancel?.Invoke(abilityIndex);
        }

        public void OnAbilityMulticast(AbilityMulticastData data)
        {
            OnAbilityMulticastReceived?.Invoke(data);
        }

        public void OnReplicatedEffectApplied(EffectReplicationData data)
        {
            if (_remoteEffectToLocal.ContainsKey(data.EffectInstanceId))
            {
                OnReplicatedEffectUpdated(ToEffectUpdateData(data));
                return;
            }

            if (!_idRegistry.TryResolveEffectDefinition(data.EffectDefinitionId, out var effectDef) || effectDef == null)
            {
                Debug.LogWarning($"[GAS Integration] Unknown effectDefinitionId={data.EffectDefinitionId}.");
                return;
            }

            int beforeCount = _asc.ActiveEffects.Count;
            var sourceAsc = ResolveSourceAsc(data.SourceNetworkId);

            var spec = GameplayEffectSpec.Create(effectDef, sourceAsc ?? _asc, Math.Max(1, data.Level));
            ApplySetByCaller(spec, data);
            _asc.ApplyGameplayEffectSpecToSelf(spec);

            if (_asc.ActiveEffects.Count > beforeCount)
            {
                var localEffect = _asc.ActiveEffects[_asc.ActiveEffects.Count - 1];
                _remoteEffectToLocal[data.EffectInstanceId] = localEffect;
                _localEffectToRemote[localEffect] = data.EffectInstanceId;

                var setByCallerTags = ToSetByCallerTags(data.SetByCallerEntries, data.SetByCallerCount);
                var setByCallerValues = ToSetByCallerValues(data.SetByCallerEntries, data.SetByCallerCount);
                _asc.TryApplyReplicatedEffectUpdate(
                    localEffect,
                    Math.Max(1, data.Level),
                    data.StackCount,
                    data.Duration,
                    data.TimeRemaining,
                    data.PeriodTimeRemaining,
                    setByCallerTags,
                    setByCallerValues,
                    Math.Min(setByCallerTags.Length, setByCallerValues.Length));
            }

            _replicatedEffectStates[data.EffectInstanceId] = data;
        }

        public void OnReplicatedEffectRemoved(int effectInstanceId)
        {
            bool handled = (EffectMutationHandler != null && EffectMutationHandler.TryRemoveReplicatedEffect(effectInstanceId))
                || (TryRemoveReplicatedEffect != null && TryRemoveReplicatedEffect(effectInstanceId));

            if (!handled && _remoteEffectToLocal.TryGetValue(effectInstanceId, out var localEffect))
            {
                handled = _asc.TryRemoveActiveEffect(localEffect);
            }

            if (handled)
            {
                if (_remoteEffectToLocal.TryGetValue(effectInstanceId, out var removedEffect))
                {
                    _localEffectToRemote.Remove(removedEffect);
                }

                _remoteEffectToLocal.Remove(effectInstanceId);
                _replicatedEffectStates.Remove(effectInstanceId);
                return;
            }

            Debug.LogWarning($"[GAS Integration] Effect remove not applied for remote instance {effectInstanceId}. " +
                "Provide TryRemoveReplicatedEffect callback or expose ASC API for explicit effect removal.");
        }

        public void OnReplicatedStackChanged(int effectInstanceId, int newStackCount)
        {
            bool handled = (EffectMutationHandler != null && EffectMutationHandler.TryApplyReplicatedStackChange(effectInstanceId, newStackCount))
                || (TryApplyReplicatedStackChange != null && TryApplyReplicatedStackChange(effectInstanceId, newStackCount));

            if (!handled && _remoteEffectToLocal.TryGetValue(effectInstanceId, out var localEffect))
            {
                handled = _asc.TryApplyActiveEffectStackChange(localEffect, newStackCount);
            }

            if (_replicatedEffectStates.TryGetValue(effectInstanceId, out var existing))
            {
                existing.StackCount = newStackCount;
                _replicatedEffectStates[effectInstanceId] = existing;
            }

            if (handled)
                return;

            Debug.LogWarning($"[GAS Integration] Stack change not applied for remote instance {effectInstanceId}. " +
                "Provide TryApplyReplicatedStackChange callback or expose ASC API for explicit stack updates.");
        }

        public void OnReplicatedEffectUpdated(EffectUpdateData data)
        {
            bool handled = (EffectMutationHandler != null && EffectMutationHandler.TryApplyReplicatedEffectUpdate(data))
                || (TryApplyReplicatedEffectUpdate != null && TryApplyReplicatedEffectUpdate(data));

            if (!handled && _remoteEffectToLocal.TryGetValue(data.EffectInstanceId, out var localEffect))
            {
                var setByCallerTags = ToSetByCallerTags(data.SetByCallerEntries, data.SetByCallerCount);
                var setByCallerValues = ToSetByCallerValues(data.SetByCallerEntries, data.SetByCallerCount);
                handled = _asc.TryApplyReplicatedEffectUpdate(
                    localEffect,
                    Math.Max(1, data.Level),
                    data.StackCount,
                    data.Duration,
                    data.TimeRemaining,
                    data.PeriodTimeRemaining,
                    setByCallerTags,
                    setByCallerValues,
                    Math.Min(setByCallerTags.Length, setByCallerValues.Length));
            }

            _replicatedEffectStates[data.EffectInstanceId] = ToEffectReplicationData(data);

            if (handled)
                return;

            Debug.LogWarning($"[GAS Integration] Effect update not applied for remote instance {data.EffectInstanceId}. " +
                "Provide TryApplyReplicatedEffectUpdate callback or expose ASC API for explicit in-place effect updates.");
        }

        public void OnReplicatedAttributeUpdate(AttributeUpdateData data)
        {
            if (data.Attributes == null || data.AttributeCount <= 0)
                return;

            int count = Math.Min(data.AttributeCount, data.Attributes.Length);
            for (int i = 0; i < count; i++)
            {
                var entry = data.Attributes[i];
                if (!_idRegistry.TryResolveAttributeName(entry.AttributeId, out var attributeName))
                    continue;

                var attribute = _asc.GetAttribute(attributeName);
                var owningSet = attribute?.OwningSet;
                if (attribute == null || owningSet == null)
                    continue;

                owningSet.SetBaseValue(attribute, entry.BaseValue);
                owningSet.SetCurrentValue(attribute, entry.CurrentValue);
            }
        }

        public void OnReplicatedTagUpdate(TagUpdateData data)
        {
            ApplyTagArray(data.AddedTagHashes, data.AddedCount, add: true);
            ApplyTagArray(data.RemovedTagHashes, data.RemovedCount, add: false);
        }

        public FullStateSnapshotData CaptureFullState()
        {
            var abilities = BuildGrantedAbilitiesFromAsc();
            var effects = BuildActiveEffectsFromAsc();
            var attributes = BuildAttributeEntriesFromAsc();
            var tags = BuildTagHashesFromAsc();

            SyncReplicatedEffectBaseline(effects);

            return new FullStateSnapshotData
            {
                TargetNetworkId = NetworkId,
                AbilityCount = abilities.Length,
                Abilities = abilities,
                EffectCount = effects.Length,
                Effects = effects,
                AttributeCount = attributes.Length,
                Attributes = attributes,
                TagCount = tags.Length,
                TagHashes = tags
            };
        }

        public void OnFullStateSnapshot(FullStateSnapshotData snapshot)
        {
            var attrData = new AttributeUpdateData
            {
                TargetNetworkId = snapshot.TargetNetworkId,
                IsFullSync = true,
                AttributeCount = snapshot.AttributeCount,
                Attributes = snapshot.Attributes
            };

            OnReplicatedAttributeUpdate(attrData);

            var currentTagHashes = BuildTagHashesFromAsc();
            var toRemove = BuildTagDiff(currentTagHashes, snapshot.TagHashes);

            OnReplicatedTagUpdate(new TagUpdateData
            {
                TargetNetworkId = snapshot.TargetNetworkId,
                AddedCount = snapshot.TagCount,
                AddedTagHashes = snapshot.TagHashes,
                RemovedCount = toRemove.Length,
                RemovedTagHashes = toRemove
            });

            OnFullStateSnapshotReceived?.Invoke(snapshot);
        }

        public ReplicatedAbilitySystemStateDelta CapturePendingReplicatedStateDelta()
        {
            ulong stateVersion = _asc.StateVersion;
            var changeMask = _asc.PendingStateChangeMask;

            var grantedAbilities = changeMask.HasFlag(AbilitySystemStateChangeMask.GrantedAbilities)
                ? BuildGrantedAbilitiesFromAsc()
                : Array.Empty<GrantedAbilityEntry>();

            var addedActiveEffects = Array.Empty<EffectReplicationData>();
            var updatedActiveEffects = Array.Empty<EffectUpdateData>();
            var stackChangedEffects = Array.Empty<EffectStackChangeData>();
            var removedEffectInstanceIds = Array.Empty<int>();

            if (changeMask.HasFlag(AbilitySystemStateChangeMask.ActiveEffects))
            {
                ClassifyActiveEffectChanges(
                    _asc.ActiveEffects,
                    out addedActiveEffects,
                    out updatedActiveEffects,
                    out stackChangedEffects,
                    out removedEffectInstanceIds);
            }

            var attributeUpdate = changeMask.HasFlag(AbilitySystemStateChangeMask.Attributes)
                ? BuildAttributeDeltaFromAscPending()
                : default;

            var tagUpdate = changeMask.HasFlag(AbilitySystemStateChangeMask.Tags)
                ? BuildTagDeltaFromPendingAscTags()
                : default;

            _asc.ConsumePendingStateChanges();

            var snapshot = new AbilitySystemStateDeltaSnapshot(
                stateVersion,
                stateVersion,
                changeMask,
                Array.Empty<GrantedAbilityStateSnapshot>(),
                Array.Empty<IGASAbilityDefinition>(),
                Array.Empty<ActiveGameplayEffectStateSnapshot>(),
                Array.Empty<int>(),
                Array.Empty<GameplayAttributeStateSnapshot>(),
                Array.Empty<GameplayTag>(),
                Array.Empty<GameplayTag>());

            return new ReplicatedAbilitySystemStateDelta(
                snapshot,
                grantedAbilities,
                addedActiveEffects,
                updatedActiveEffects,
                stackChangedEffects,
                removedEffectInstanceIds,
                attributeUpdate,
                tagUpdate);
        }

        public ReplicatedAbilitySystemStateDelta CaptureAndReplicatePendingStateDelta(
            NetworkedAbilityBridge bridge,
            IReadOnlyList<INetConnection> observers)
        {
            if (bridge == null)
                throw new ArgumentNullException(nameof(bridge));

            var delta = CapturePendingReplicatedStateDelta();
            if (observers == null || observers.Count == 0)
                return delta;

            for (int i = 0; i < delta.AddedActiveEffects.Length; i++)
            {
                bridge.ServerReplicateEffectApplied(observers, NetworkId, delta.AddedActiveEffects[i]);
            }

            for (int i = 0; i < delta.UpdatedActiveEffects.Length; i++)
            {
                bridge.ServerReplicateEffectUpdated(observers, NetworkId, delta.UpdatedActiveEffects[i]);
            }

            for (int i = 0; i < delta.StackChangedEffects.Length; i++)
            {
                var stackChange = delta.StackChangedEffects[i];
                bridge.ServerReplicateStackChange(observers, NetworkId, stackChange.EffectInstanceId, stackChange.NewStackCount);
            }

            for (int i = 0; i < delta.RemovedEffectInstanceIds.Length; i++)
            {
                bridge.ServerReplicateEffectRemoved(observers, NetworkId, delta.RemovedEffectInstanceIds[i]);
            }

            if (delta.AttributeUpdate.AttributeCount > 0)
            {
                bridge.ServerBroadcastAttributes(observers, NetworkId, delta.AttributeUpdate);
            }

            if (delta.TagUpdate.AddedCount > 0 || delta.TagUpdate.RemovedCount > 0)
            {
                bridge.ServerSyncTags(observers, NetworkId, delta.TagUpdate);
            }

            return delta;
        }

        public void Dispose()
        {
            _idRegistry.UnregisterAsc(NetworkId);
            _remoteEffectToLocal.Clear();
            _localEffectToRemote.Clear();
            _replicatedEffectStates.Clear();
        }

        private GameplayAbilitySpec TryResolveAbilitySpec(int abilityDefinitionId)
        {
            var specs = _asc.GetActivatableAbilities();
            for (int i = 0; i < specs.Count; i++)
            {
                var ability = specs[i].AbilityCDO ?? specs[i].Ability;
                if (_idRegistry.GetAbilityDefinitionId(ability) == abilityDefinitionId)
                    return specs[i];
            }
            return null;
        }

        private AbilitySystemComponent ResolveSourceAsc(uint sourceNetworkId)
        {
            if (sourceNetworkId == 0)
                return null;

            _idRegistry.TryResolveAsc(sourceNetworkId, out var sourceAsc);
            return sourceAsc;
        }

        private void ApplySetByCaller(GameplayEffectSpec spec, EffectReplicationData data)
        {
            if (data.SetByCallerEntries == null || data.SetByCallerCount <= 0)
                return;

            int count = Math.Min(data.SetByCallerCount, data.SetByCallerEntries.Length);
            for (int i = 0; i < count; i++)
            {
                var entry = data.SetByCallerEntries[i];
                if (_idRegistry.TryResolveTag(entry.TagHash, out var tag) && tag.IsValid && !tag.IsNone)
                {
                    spec.SetSetByCallerMagnitude(tag, entry.Value);
                }
            }
        }

        private GameplayTag[] ToSetByCallerTags(SetByCallerEntry[] entries, int entryCount)
        {
            if (entries == null || entryCount <= 0)
                return Array.Empty<GameplayTag>();

            int count = Math.Min(entryCount, entries.Length);
            var tags = new GameplayTag[count];
            int resolvedCount = 0;

            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (_idRegistry.TryResolveTag(entry.TagHash, out var tag) && tag.IsValid && !tag.IsNone)
                {
                    tags[resolvedCount++] = tag;
                }
            }

            if (resolvedCount == count)
                return tags;

            if (resolvedCount == 0)
                return Array.Empty<GameplayTag>();

            Array.Resize(ref tags, resolvedCount);
            return tags;
        }

        private float[] ToSetByCallerValues(SetByCallerEntry[] entries, int entryCount)
        {
            if (entries == null || entryCount <= 0)
                return Array.Empty<float>();

            int count = Math.Min(entryCount, entries.Length);
            var values = new float[count];
            int resolvedCount = 0;

            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (_idRegistry.TryResolveTag(entry.TagHash, out var tag) && tag.IsValid && !tag.IsNone)
                {
                    values[resolvedCount++] = entry.Value;
                }
            }

            if (resolvedCount == count)
                return values;

            if (resolvedCount == 0)
                return Array.Empty<float>();

            Array.Resize(ref values, resolvedCount);
            return values;
        }

        private int GetResolvedSetByCallerCount(SetByCallerEntry[] entries, int entryCount)
        {
            if (entries == null || entryCount <= 0)
                return 0;

            int count = Math.Min(entryCount, entries.Length);
            int resolvedCount = 0;

            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (_idRegistry.TryResolveTag(entry.TagHash, out var tag) && tag.IsValid && !tag.IsNone)
                {
                    resolvedCount++;
                }
            }

            return resolvedCount;
        }

        private void ApplyTagArray(int[] tagHashes, int count, bool add)
        {
            if (tagHashes == null || count <= 0)
                return;

            int safeCount = Math.Min(count, tagHashes.Length);
            for (int i = 0; i < safeCount; i++)
            {
                if (!_idRegistry.TryResolveTag(tagHashes[i], out var tag) || !tag.IsValid || tag.IsNone)
                    continue;

                if (add)
                {
                    if (!_asc.HasMatchingGameplayTag(tag))
                        _asc.AddLooseGameplayTag(tag);
                }
                else if (_asc.HasMatchingGameplayTag(tag))
                {
                    _asc.RemoveLooseGameplayTag(tag);
                }
            }
        }

        private GrantedAbilityEntry[] BuildGrantedAbilitiesFromAsc()
        {
            var abilities = _asc.GetActivatableAbilities();
            if (abilities == null || abilities.Count == 0)
                return Array.Empty<GrantedAbilityEntry>();

            var entries = new GrantedAbilityEntry[abilities.Count];
            for (int i = 0; i < abilities.Count; i++)
            {
                var spec = abilities[i];
                var ability = spec.AbilityCDO ?? spec.Ability;
                entries[i] = new GrantedAbilityEntry
                {
                    AbilityDefinitionId = ability != null ? _idRegistry.GetAbilityDefinitionId(ability) : 0,
                    Level = spec.Level,
                    IsActive = spec.IsActive
                };
            }

            return entries;
        }

        private EffectReplicationData[] BuildActiveEffectsFromAsc()
        {
            return BuildActiveEffects(_asc.ActiveEffects);
        }

        private AttributeEntry[] BuildAttributeEntriesFromAsc()
        {
            var attributeSets = _asc.AttributeSets;
            if (attributeSets == null || attributeSets.Count == 0)
                return Array.Empty<AttributeEntry>();

            int attributeCount = 0;
            for (int i = 0; i < attributeSets.Count; i++)
            {
                attributeCount += attributeSets[i].GetAttributes().Count;
            }

            if (attributeCount == 0)
                return Array.Empty<AttributeEntry>();

            var entries = new AttributeEntry[attributeCount];
            int index = 0;
            for (int i = 0; i < attributeSets.Count; i++)
            {
                foreach (var attribute in attributeSets[i].GetAttributes())
                {
                    entries[index++] = new AttributeEntry
                    {
                        AttributeId = _idRegistry.GetAttributeId(attribute),
                        BaseValue = attribute.BaseValue,
                        CurrentValue = attribute.CurrentValue
                    };
                }
            }

            return entries;
        }

        private int[] BuildTagHashesFromAsc()
        {
            if (_asc.CombinedTags == null || _asc.CombinedTags.TagCount <= 0)
                return Array.Empty<int>();

            var hashes = new int[_asc.CombinedTags.TagCount];
            int count = 0;

            foreach (var tag in _asc.CombinedTags)
            {
                if (!tag.IsValid || tag.IsNone)
                    continue;

                hashes[count++] = _idRegistry.GetTagHash(tag);
            }

            if (count == 0)
                return Array.Empty<int>();

            if (count == hashes.Length)
                return hashes;

            Array.Resize(ref hashes, count);
            return hashes;
        }

        private AttributeUpdateData BuildAttributeDeltaFromAscPending()
        {
            var attributes = _asc.IsAttributeStructureDirty
                ? BuildAttributeEntriesFromAsc()
                : BuildDirtyAttributeEntriesFromAsc();

            return new AttributeUpdateData
            {
                TargetNetworkId = NetworkId,
                IsFullSync = false,
                AttributeCount = attributes.Length,
                Attributes = attributes
            };
        }

        private AttributeEntry[] BuildDirtyAttributeEntriesFromAsc()
        {
            var dirtyNames = _asc.DirtyAttributeNames;
            if (dirtyNames == null || dirtyNames.Count == 0)
                return Array.Empty<AttributeEntry>();

            var entries = new AttributeEntry[dirtyNames.Count];
            int index = 0;
            foreach (var attributeName in dirtyNames)
            {
                var attribute = _asc.GetAttribute(attributeName);
                if (attribute == null)
                    continue;

                entries[index++] = new AttributeEntry
                {
                    AttributeId = _idRegistry.GetAttributeId(attribute),
                    BaseValue = attribute.BaseValue,
                    CurrentValue = attribute.CurrentValue
                };
            }

            if (index == 0)
                return Array.Empty<AttributeEntry>();

            if (index == entries.Length)
                return entries;

            Array.Resize(ref entries, index);
            return entries;
        }

        private TagUpdateData BuildTagDeltaFromPendingAscTags()
        {
            var addedTagHashes = BuildTagHashes(_asc.PendingAddedTags);
            var removedTagHashes = BuildTagHashes(_asc.PendingRemovedTags);

            return new TagUpdateData
            {
                TargetNetworkId = NetworkId,
                AddedCount = addedTagHashes.Length,
                AddedTagHashes = addedTagHashes,
                RemovedCount = removedTagHashes.Length,
                RemovedTagHashes = removedTagHashes
            };
        }

        private EffectReplicationData[] BuildActiveEffects(IReadOnlyList<ActiveGameplayEffect> activeEffects)
        {
            if (activeEffects == null || activeEffects.Count == 0)
                return Array.Empty<EffectReplicationData>();

            var entries = new EffectReplicationData[activeEffects.Count];

            for (int i = 0; i < activeEffects.Count; i++)
            {
                var effect = activeEffects[i];
                uint sourceNetworkId = 0;
                _idRegistry.TryResolveNetworkId(effect.Spec.Source, out sourceNetworkId);

                var setByCallerEntries = BuildSetByCallerEntries(effect.Spec);

                entries[i] = new EffectReplicationData
                {
                    TargetNetworkId = NetworkId,
                    SourceNetworkId = sourceNetworkId,
                    EffectInstanceId = GetOrCreateEffectInstanceId(effect),
                    EffectDefinitionId = effect.Spec.Def != null ? _idRegistry.GetEffectDefinitionId(effect.Spec.Def) : 0,
                    Level = effect.Spec.Level,
                    StackCount = effect.StackCount,
                    Duration = effect.Spec.Duration,
                    TimeRemaining = effect.TimeRemaining,
                    PeriodTimeRemaining = effect.PeriodTimeRemaining,
                    PredictionKey = effect.Spec.Context?.PredictionKey.Key ?? 0,
                    SetByCallerCount = setByCallerEntries.Length,
                    SetByCallerEntries = setByCallerEntries
                };
            }

            return entries;
        }

        private int[] BuildTagHashes(IReadOnlyCollection<GameplayTag> tags)
        {
            if (tags == null || tags.Count == 0)
                return Array.Empty<int>();

            var hashes = new int[tags.Count];
            int index = 0;
            foreach (var tag in tags)
            {
                hashes[index++] = _idRegistry.GetTagHash(tag);
            }

            return hashes;
        }

        private SetByCallerEntry[] BuildSetByCallerEntries(GameplayEffectSpec spec)
        {
            if (spec == null)
                return Array.Empty<SetByCallerEntry>();

            int count = spec.SetByCallerTagMagnitudeCount;
            if (count <= 0)
                return Array.Empty<SetByCallerEntry>();

            EnsureSetByCallerScratchCapacity(count);

            int copiedCount = spec.CopySetByCallerTagMagnitudes(_setByCallerTagsScratch, _setByCallerValuesScratch);
            if (copiedCount <= 0)
                return Array.Empty<SetByCallerEntry>();

            var networkEntries = new SetByCallerEntry[copiedCount];
            for (int i = 0; i < copiedCount; i++)
            {
                networkEntries[i] = new SetByCallerEntry
                {
                    TagHash = _idRegistry.GetTagHash(_setByCallerTagsScratch[i]),
                    Value = _setByCallerValuesScratch[i]
                };
            }

            return networkEntries;
        }

        private void EnsureSetByCallerScratchCapacity(int count)
        {
            if (_setByCallerTagsScratch.Length < count)
            {
                int newSize = Math.Max(count, _setByCallerTagsScratch.Length == 0 ? 4 : _setByCallerTagsScratch.Length * 2);
                Array.Resize(ref _setByCallerTagsScratch, newSize);
                Array.Resize(ref _setByCallerValuesScratch, newSize);
            }
        }

        private int[] BuildTagDiff(int[] current, int[] target)
        {
            _targetTagHashSetScratch.Clear();
            _tagDiffScratch.Clear();

            if (target != null)
            {
                for (int i = 0; i < target.Length; i++)
                {
                    _targetTagHashSetScratch.Add(target[i]);
                }
            }

            if (current != null)
            {
                for (int i = 0; i < current.Length; i++)
                {
                    if (!_targetTagHashSetScratch.Contains(current[i]))
                        _tagDiffScratch.Add(current[i]);
                }
            }

            return _tagDiffScratch.Count == 0 ? Array.Empty<int>() : _tagDiffScratch.ToArray();
        }

        private void SyncReplicatedEffectBaseline(EffectReplicationData[] effects)
        {
            _replicatedEffectStates.Clear();

            for (int i = 0; i < effects.Length; i++)
            {
                _replicatedEffectStates[effects[i].EffectInstanceId] = effects[i];
            }
        }

        private void ClassifyActiveEffectChanges(
            IReadOnlyList<ActiveGameplayEffect> activeEffects,
            out EffectReplicationData[] addedActiveEffects,
            out EffectUpdateData[] updatedActiveEffects,
            out EffectStackChangeData[] stackChangedEffects,
            out int[] removedEffectInstanceIds)
        {
            var currentEffects = BuildActiveEffects(activeEffects);
            _currentEffectsByIdScratch.Clear();
            _addedEffectsScratch.Clear();
            _updatedEffectsScratch.Clear();
            _stackChangedEffectsScratch.Clear();
            _removedEffectIdsScratch.Clear();

            for (int i = 0; i < currentEffects.Length; i++)
            {
                var current = currentEffects[i];
                _currentEffectsByIdScratch[current.EffectInstanceId] = current;

                if (!_replicatedEffectStates.TryGetValue(current.EffectInstanceId, out var previous))
                {
                    _addedEffectsScratch.Add(current);
                    continue;
                }

                if (AreEffectStatesEquivalent(previous, current))
                    continue;

                if (IsStackOnlyChange(previous, current))
                {
                    _stackChangedEffectsScratch.Add(new EffectStackChangeData
                    {
                        TargetNetworkId = NetworkId,
                        EffectInstanceId = current.EffectInstanceId,
                        NewStackCount = current.StackCount
                    });
                    continue;
                }

                _updatedEffectsScratch.Add(ToEffectUpdateData(current));
            }

            foreach (var pair in _replicatedEffectStates)
            {
                if (!_currentEffectsByIdScratch.ContainsKey(pair.Key))
                    _removedEffectIdsScratch.Add(pair.Key);
            }

            for (int i = 0; i < _removedEffectIdsScratch.Count; i++)
            {
                int removedId = _removedEffectIdsScratch[i];
                if (_remoteEffectToLocal.TryGetValue(removedId, out var removedEffect))
                {
                    _localEffectToRemote.Remove(removedEffect);
                }

                _replicatedEffectStates.Remove(removedId);
                _remoteEffectToLocal.Remove(removedId);
            }

            SyncReplicatedEffectBaseline(currentEffects);

            addedActiveEffects = _addedEffectsScratch.Count == 0 ? Array.Empty<EffectReplicationData>() : _addedEffectsScratch.ToArray();
            updatedActiveEffects = _updatedEffectsScratch.Count == 0 ? Array.Empty<EffectUpdateData>() : _updatedEffectsScratch.ToArray();
            stackChangedEffects = _stackChangedEffectsScratch.Count == 0 ? Array.Empty<EffectStackChangeData>() : _stackChangedEffectsScratch.ToArray();
            removedEffectInstanceIds = _removedEffectIdsScratch.Count == 0 ? Array.Empty<int>() : _removedEffectIdsScratch.ToArray();
        }

        private static bool IsStackOnlyChange(EffectReplicationData previous, EffectReplicationData current)
        {
            return previous.StackCount != current.StackCount
                && previous.TargetNetworkId == current.TargetNetworkId
                && previous.SourceNetworkId == current.SourceNetworkId
                && previous.EffectInstanceId == current.EffectInstanceId
                && previous.EffectDefinitionId == current.EffectDefinitionId
                && previous.Level == current.Level
                && AreApproximatelyEqual(previous.Duration, current.Duration)
                && AreApproximatelyEqual(previous.TimeRemaining, current.TimeRemaining)
                && AreApproximatelyEqual(previous.PeriodTimeRemaining, current.PeriodTimeRemaining)
                && previous.PredictionKey == current.PredictionKey
                && AreSetByCallerEntriesEqual(previous.SetByCallerEntries, previous.SetByCallerCount, current.SetByCallerEntries, current.SetByCallerCount);
        }

        private static bool AreEffectStatesEquivalent(EffectReplicationData previous, EffectReplicationData current)
        {
            return previous.TargetNetworkId == current.TargetNetworkId
                && previous.SourceNetworkId == current.SourceNetworkId
                && previous.EffectInstanceId == current.EffectInstanceId
                && previous.EffectDefinitionId == current.EffectDefinitionId
                && previous.Level == current.Level
                && previous.StackCount == current.StackCount
                && AreApproximatelyEqual(previous.Duration, current.Duration)
                && AreApproximatelyEqual(previous.TimeRemaining, current.TimeRemaining)
                && AreApproximatelyEqual(previous.PeriodTimeRemaining, current.PeriodTimeRemaining)
                && previous.PredictionKey == current.PredictionKey
                && AreSetByCallerEntriesEqual(previous.SetByCallerEntries, previous.SetByCallerCount, current.SetByCallerEntries, current.SetByCallerCount);
        }

        private static bool AreApproximatelyEqual(float left, float right)
        {
            return Mathf.Abs(left - right) <= 0.0001f;
        }

        private static bool AreSetByCallerEntriesEqual(
            SetByCallerEntry[] leftEntries,
            int leftCount,
            SetByCallerEntry[] rightEntries,
            int rightCount)
        {
            if (leftCount != rightCount)
                return false;

            if (leftCount <= 0)
                return true;

            if (leftEntries == null || rightEntries == null)
                return false;

            for (int i = 0; i < leftCount; i++)
            {
                if (leftEntries[i].TagHash != rightEntries[i].TagHash)
                    return false;

                if (!AreApproximatelyEqual(leftEntries[i].Value, rightEntries[i].Value))
                    return false;
            }

            return true;
        }

        private static EffectUpdateData ToEffectUpdateData(EffectReplicationData data)
        {
            return new EffectUpdateData
            {
                TargetNetworkId = data.TargetNetworkId,
                SourceNetworkId = data.SourceNetworkId,
                EffectInstanceId = data.EffectInstanceId,
                EffectDefinitionId = data.EffectDefinitionId,
                Level = data.Level,
                StackCount = data.StackCount,
                Duration = data.Duration,
                TimeRemaining = data.TimeRemaining,
                PeriodTimeRemaining = data.PeriodTimeRemaining,
                PredictionKey = data.PredictionKey,
                SetByCallerCount = data.SetByCallerCount,
                SetByCallerEntries = data.SetByCallerEntries
            };
        }

        private static EffectReplicationData ToEffectReplicationData(EffectUpdateData data)
        {
            return new EffectReplicationData
            {
                TargetNetworkId = data.TargetNetworkId,
                SourceNetworkId = data.SourceNetworkId,
                EffectInstanceId = data.EffectInstanceId,
                EffectDefinitionId = data.EffectDefinitionId,
                Level = data.Level,
                StackCount = data.StackCount,
                Duration = data.Duration,
                TimeRemaining = data.TimeRemaining,
                PeriodTimeRemaining = data.PeriodTimeRemaining,
                PredictionKey = data.PredictionKey,
                SetByCallerCount = data.SetByCallerCount,
                SetByCallerEntries = data.SetByCallerEntries
            };
        }

        private int GetOrCreateEffectInstanceId(ActiveGameplayEffect effect)
        {
            if (_localEffectToRemote.TryGetValue(effect, out int existing))
                return existing;

            int next = _nextLocalEffectInstanceId++;
            _localEffectToRemote[effect] = next;
            _remoteEffectToLocal[next] = effect;
            return next;
        }
    }
}