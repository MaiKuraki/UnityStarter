using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using UnityEngine;

namespace CycloneGames.Networking.GAS.Integrations.GameplayAbilities
{
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
            }
        }

        public void OnReplicatedEffectRemoved(int effectInstanceId)
        {
            bool handled = (EffectMutationHandler != null && EffectMutationHandler.TryRemoveReplicatedEffect(effectInstanceId))
                || (TryRemoveReplicatedEffect != null && TryRemoveReplicatedEffect(effectInstanceId));

            if (handled)
            {
                _remoteEffectToLocal.Remove(effectInstanceId);
                return;
            }

            Debug.LogWarning($"[GAS Integration] Effect remove not applied for remote instance {effectInstanceId}. " +
                "Provide TryRemoveReplicatedEffect callback or expose ASC API for explicit effect removal.");
        }

        public void OnReplicatedStackChanged(int effectInstanceId, int newStackCount)
        {
            bool handled = (EffectMutationHandler != null && EffectMutationHandler.TryApplyReplicatedStackChange(effectInstanceId, newStackCount))
                || (TryApplyReplicatedStackChange != null && TryApplyReplicatedStackChange(effectInstanceId, newStackCount));

            if (handled)
                return;

            Debug.LogWarning($"[GAS Integration] Stack change not applied for remote instance {effectInstanceId}. " +
                "Provide TryApplyReplicatedStackChange callback or expose ASC API for explicit stack updates.");
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
            var abilities = BuildGrantedAbilities();
            var effects = BuildActiveEffects();
            var attributes = BuildAttributeEntries();
            var tags = BuildTagHashes();

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

            var currentTagHashes = BuildTagHashes();
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

        public void Dispose()
        {
            _idRegistry.UnregisterAsc(NetworkId);
            _remoteEffectToLocal.Clear();
            _localEffectToRemote.Clear();
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

        private GrantedAbilityEntry[] BuildGrantedAbilities()
        {
            var specs = _asc.GetActivatableAbilities();
            var entries = new GrantedAbilityEntry[specs.Count];

            for (int i = 0; i < specs.Count; i++)
            {
                var ability = specs[i].AbilityCDO ?? specs[i].Ability;
                entries[i] = new GrantedAbilityEntry
                {
                    AbilityDefinitionId = _idRegistry.GetAbilityDefinitionId(ability),
                    Level = specs[i].Level,
                    IsActive = specs[i].IsActive
                };
            }

            return entries;
        }

        private EffectReplicationData[] BuildActiveEffects()
        {
            var active = _asc.ActiveEffects;
            var entries = new EffectReplicationData[active.Count];

            for (int i = 0; i < active.Count; i++)
            {
                var effect = active[i];
                uint sourceNetworkId = 0;
                _idRegistry.TryResolveNetworkId(effect.Spec.Source, out sourceNetworkId);

                entries[i] = new EffectReplicationData
                {
                    TargetNetworkId = NetworkId,
                    SourceNetworkId = sourceNetworkId,
                    EffectInstanceId = GetOrCreateEffectInstanceId(effect),
                    EffectDefinitionId = _idRegistry.GetEffectDefinitionId(effect.Spec.Def),
                    Level = effect.Spec.Level,
                    StackCount = effect.StackCount,
                    Duration = effect.Spec.Duration,
                    TimeRemaining = effect.TimeRemaining,
                    PredictionKey = effect.Spec.Context?.PredictionKey.Key ?? 0,
                    SetByCallerCount = 0,
                    SetByCallerEntries = null
                };
            }

            return entries;
        }

        private AttributeEntry[] BuildAttributeEntries()
        {
            var entries = new List<AttributeEntry>(64);

            for (int s = 0; s < _asc.AttributeSets.Count; s++)
            {
                var set = _asc.AttributeSets[s];
                foreach (var attr in set.GetAttributes())
                {
                    entries.Add(new AttributeEntry
                    {
                        AttributeId = _idRegistry.GetAttributeId(attr),
                        BaseValue = attr.BaseValue,
                        CurrentValue = attr.CurrentValue
                    });
                }
            }

            return entries.ToArray();
        }

        private int[] BuildTagHashes()
        {
            var hashes = new List<int>(64);
            foreach (var tag in _asc.CombinedTags)
            {
                if (tag.IsNone || !tag.IsValid) continue;
                hashes.Add(_idRegistry.GetTagHash(tag));
            }
            return hashes.ToArray();
        }

        private static int[] BuildTagDiff(int[] current, int[] target)
        {
            var targetSet = new HashSet<int>(target ?? Array.Empty<int>());
            var diff = new List<int>();

            if (current != null)
            {
                for (int i = 0; i < current.Length; i++)
                {
                    if (!targetSet.Contains(current[i]))
                        diff.Add(current[i]);
                }
            }

            return diff.ToArray();
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