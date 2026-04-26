using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using UnityEngine;

namespace CycloneGames.Networking.GAS.Integrations.GameplayAbilities
{
    public enum GASStateDriftReason : byte
    {
        None,
        OutOfOrderSequence,
        BaseVersionMismatch,
        ChecksumMismatch,
        TargetNetworkIdMismatch,
        InvalidVersionRange
    }

    public readonly struct ReplicatedAbilitySystemStateDelta
    {
        public readonly GASAbilitySystemStateDeltaBuffer StateDelta;
        public readonly GrantedAbilityEntry[] GrantedAbilities;
        public readonly int GrantedAbilityCount;
        public readonly EffectReplicationData[] AddedActiveEffects;
        public readonly int AddedActiveEffectCount;
        public readonly EffectUpdateData[] UpdatedActiveEffects;
        public readonly int UpdatedActiveEffectCount;
        public readonly EffectStackChangeData[] StackChangedEffects;
        public readonly int StackChangedEffectCount;
        public readonly int[] RemovedEffectInstanceIds;
        public readonly int RemovedEffectInstanceIdCount;
        public readonly AttributeUpdateData AttributeUpdate;
        public readonly TagUpdateData TagUpdate;

        public ReplicatedAbilitySystemStateDelta(
            GASAbilitySystemStateDeltaBuffer stateDelta,
            GrantedAbilityEntry[] grantedAbilities,
            EffectReplicationData[] addedActiveEffects,
            EffectUpdateData[] updatedActiveEffects,
            EffectStackChangeData[] stackChangedEffects,
            int[] removedEffectInstanceIds,
            AttributeUpdateData attributeUpdate,
            TagUpdateData tagUpdate)
            : this(
                stateDelta,
                grantedAbilities,
                grantedAbilities != null ? grantedAbilities.Length : 0,
                addedActiveEffects,
                addedActiveEffects != null ? addedActiveEffects.Length : 0,
                updatedActiveEffects,
                updatedActiveEffects != null ? updatedActiveEffects.Length : 0,
                stackChangedEffects,
                stackChangedEffects != null ? stackChangedEffects.Length : 0,
                removedEffectInstanceIds,
                removedEffectInstanceIds != null ? removedEffectInstanceIds.Length : 0,
                attributeUpdate,
                tagUpdate)
        {
        }

        public ReplicatedAbilitySystemStateDelta(
            GASAbilitySystemStateDeltaBuffer stateDelta,
            GrantedAbilityEntry[] grantedAbilities,
            int grantedAbilityCount,
            EffectReplicationData[] addedActiveEffects,
            int addedActiveEffectCount,
            EffectUpdateData[] updatedActiveEffects,
            int updatedActiveEffectCount,
            EffectStackChangeData[] stackChangedEffects,
            int stackChangedEffectCount,
            int[] removedEffectInstanceIds,
            int removedEffectInstanceIdCount,
            AttributeUpdateData attributeUpdate,
            TagUpdateData tagUpdate)
        {
            StateDelta = stateDelta;
            GrantedAbilities = grantedAbilities;
            GrantedAbilityCount = grantedAbilityCount;
            AddedActiveEffects = addedActiveEffects;
            AddedActiveEffectCount = addedActiveEffectCount;
            UpdatedActiveEffects = updatedActiveEffects;
            UpdatedActiveEffectCount = updatedActiveEffectCount;
            StackChangedEffects = stackChangedEffects;
            StackChangedEffectCount = stackChangedEffectCount;
            RemovedEffectInstanceIds = removedEffectInstanceIds;
            RemovedEffectInstanceIdCount = removedEffectInstanceIdCount;
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
        private readonly IGASNetIdRegistry _idRegistry;

        private readonly Dictionary<int, ActiveGameplayEffect> _remoteEffectToLocal =
            new Dictionary<int, ActiveGameplayEffect>(64);

        private readonly Dictionary<ActiveGameplayEffect, int> _localEffectToRemote =
            new Dictionary<ActiveGameplayEffect, int>(64);

        private readonly Dictionary<int, EffectReplicationData> _replicatedEffectStates =
            new Dictionary<int, EffectReplicationData>(64);

        private readonly Dictionary<int, SetByCallerEntry[]> _replicatedSetByCallerBaselineArrays =
            new Dictionary<int, SetByCallerEntry[]>(64);

        private readonly Dictionary<string, int> _attributeIdByName =
            new Dictionary<string, int>(32, StringComparer.Ordinal);

        private readonly Dictionary<int, EffectReplicationData> _currentEffectsByIdScratch =
            new Dictionary<int, EffectReplicationData>(64);

        private readonly Dictionary<int, GameplayAbilitySpec> _abilitySpecByDefinitionIdScratch =
            new Dictionary<int, GameplayAbilitySpec>(32);

        private readonly HashSet<int> _fullStateAbilityDefinitionIdsScratch =
            new HashSet<int>();

        private readonly List<EffectReplicationData> _addedEffectsScratch =
            new List<EffectReplicationData>(16);

        private readonly List<EffectUpdateData> _updatedEffectsScratch =
            new List<EffectUpdateData>(16);

        private readonly List<EffectStackChangeData> _stackChangedEffectsScratch =
            new List<EffectStackChangeData>(16);

        private readonly List<int> _removedEffectIdsScratch =
            new List<int>(16);

        private EffectReplicationData[] _currentEffectsScratch = Array.Empty<EffectReplicationData>();
        private EffectReplicationData[] _addedEffectsBuffer = Array.Empty<EffectReplicationData>();
        private EffectUpdateData[] _updatedEffectsBuffer = Array.Empty<EffectUpdateData>();
        private EffectStackChangeData[] _stackChangedEffectsBuffer = Array.Empty<EffectStackChangeData>();
        private int[] _removedEffectIdsBuffer = Array.Empty<int>();
        private GrantedAbilityEntry[] _grantedAbilitiesBuffer = Array.Empty<GrantedAbilityEntry>();
        private AttributeEntry[] _attributeEntriesBuffer = Array.Empty<AttributeEntry>();
        private AttributeEntry[] _dirtyAttributeEntriesBuffer = Array.Empty<AttributeEntry>();
        private int[] _addedTagHashesBuffer = Array.Empty<int>();
        private int[] _removedTagHashesBuffer = Array.Empty<int>();
        private int[] _fullStateTagHashesBuffer = Array.Empty<int>();
        private int[] _currentFullStateTagHashesBuffer = Array.Empty<int>();
        private SetByCallerEntry[][] _currentEffectSetByCallerEntries = Array.Empty<SetByCallerEntry[]>();

        private readonly HashSet<int> _targetTagHashSetScratch =
            new HashSet<int>();

        private readonly List<int> _tagDiffScratch =
            new List<int>(16);

        private GameplayTag[] _setByCallerTagsScratch = Array.Empty<GameplayTag>();
        private float[] _setByCallerValuesScratch = Array.Empty<float>();
        private readonly GASAbilitySystemStateDeltaBuffer _stateDeltaBuffer = new GASAbilitySystemStateDeltaBuffer();

        private int _nextLocalEffectInstanceId = 1;
        private uint _outgoingStateSyncSequence;
        private uint _lastStateSyncSequence;
        private uint _lastRejectedStateSyncSequence;
        private ulong _lastServerStateVersion;
        private uint _lastServerStateChecksum;
        private int _runtimeThreadId;
        private long _runtimeThreadViolationCount;

        public uint NetworkId { get; }
        public int OwnerConnectionId { get; }

        public Action<int, int> OnConfirmActivation { get; set; }
        public Action<int, int> OnRejectActivation { get; set; }
        public Action<int, GASPredictionKey> OnConfirmActivationKey { get; set; }
        public Action<int, GASPredictionKey> OnRejectActivationKey { get; set; }
        public Action<int> OnReplicatedAbilityEnd { get; set; }
        public Action<int> OnReplicatedAbilityCancel { get; set; }
        public Action<AbilityMulticastData> OnAbilityMulticastReceived { get; set; }
        public Action<GASFullStateData> OnFullStateReceived { get; set; }
        public Action<GASStateSyncMetadata, GASStateDriftReason> OnStateDriftDetected { get; set; }
        public bool EnableStrictChecksumValidation { get; set; }
        public GASRuntimeThreadPolicy RuntimeThreadPolicy { get; set; } = GASRuntimeThreadPolicy.Disabled;
        public uint LastAcceptedStateSyncSequence => _lastStateSyncSequence;
        public uint LastRejectedStateSyncSequence => _lastRejectedStateSyncSequence;
        public ulong LastServerStateVersion => _lastServerStateVersion;
        public uint LastServerStateChecksum => _lastServerStateChecksum;
        public int RuntimeThreadId => _runtimeThreadId;
        public long RuntimeThreadViolationCount => _runtimeThreadViolationCount;

        /// <summary>
        /// Optional project-provided callbacks for effect removal/stack changes.
        /// These are needed because ASC currently has no direct public API by remote effect instance ID.
        /// </summary>
        public IGASReplicatedEffectMutationHandler EffectMutationHandler { get; set; }
        public Func<int, bool> TryRemoveReplicatedEffect { get; set; }
        public Func<int, int, bool> TryApplyReplicatedStackChange { get; set; }
        public Func<EffectUpdateData, bool> TryApplyReplicatedEffectUpdate { get; set; }

        public GameplayAbilitiesNetworkedASCAdapter(
            AbilitySystemComponent asc,
            uint networkId,
            int ownerConnectionId,
            IGASNetIdRegistry idRegistry = null)
        {
            _asc = asc ?? throw new ArgumentNullException(nameof(asc));
            _idRegistry = idRegistry ?? new DefaultGASNetIdRegistry();

            NetworkId = networkId;
            OwnerConnectionId = ownerConnectionId;
            BindRuntimeThreadToCurrent();

            _idRegistry.RegisterAsc(networkId, asc);
        }

        public void BindRuntimeThreadToCurrent()
        {
            _runtimeThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void AssertRuntimeThread()
        {
            if (RuntimeThreadPolicy == GASRuntimeThreadPolicy.Disabled)
                return;

            int currentThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            if (_runtimeThreadId == 0)
            {
                _runtimeThreadId = currentThreadId;
                return;
            }

            if (currentThreadId == _runtimeThreadId)
                return;

            _runtimeThreadViolationCount++;
            if (RuntimeThreadPolicy == GASRuntimeThreadPolicy.Throw)
            {
                throw new InvalidOperationException($"GameplayAbilitiesNetworkedASCAdapter accessed from thread {currentThreadId}; runtime thread is {_runtimeThreadId}.");
            }

            Debug.LogWarning($"[GAS Integration] Adapter accessed from thread {currentThreadId}; runtime thread is {_runtimeThreadId}.");
        }

        public void ReserveRuntimeCapacity(
            int abilityCapacity = 16,
            int attributeCapacity = 32,
            int activeEffectCapacity = 32,
            int tagDeltaCapacity = 16,
            int maxSetByCallerPerEffect = 8)
        {
            EnsureArrayCapacity(ref _currentEffectsScratch, activeEffectCapacity);
            EnsureArrayCapacity(ref _addedEffectsBuffer, activeEffectCapacity);
            EnsureArrayCapacity(ref _updatedEffectsBuffer, activeEffectCapacity);
            EnsureArrayCapacity(ref _stackChangedEffectsBuffer, activeEffectCapacity);
            EnsureArrayCapacity(ref _removedEffectIdsBuffer, activeEffectCapacity);
            EnsureArrayCapacity(ref _grantedAbilitiesBuffer, abilityCapacity);
            EnsureArrayCapacity(ref _attributeEntriesBuffer, attributeCapacity);
            EnsureArrayCapacity(ref _dirtyAttributeEntriesBuffer, attributeCapacity);
            EnsureArrayCapacity(ref _addedTagHashesBuffer, tagDeltaCapacity);
            EnsureArrayCapacity(ref _removedTagHashesBuffer, tagDeltaCapacity);
            EnsureArrayCapacity(ref _fullStateTagHashesBuffer, tagDeltaCapacity);
            EnsureArrayCapacity(ref _currentFullStateTagHashesBuffer, tagDeltaCapacity);
            EnsureSetByCallerScratchCapacity(maxSetByCallerPerEffect);

            if (activeEffectCapacity > 0 && maxSetByCallerPerEffect > 0)
            {
                for (int i = 0; i < activeEffectCapacity; i++)
                {
                    EnsureCurrentEffectSetByCallerCapacity(i, maxSetByCallerPerEffect);
                }
            }

            _stateDeltaBuffer.Reserve(
                abilityCapacity,
                abilityCapacity,
                activeEffectCapacity,
                activeEffectCapacity,
                attributeCapacity,
                tagDeltaCapacity,
                tagDeltaCapacity,
                maxSetByCallerPerEffect);
        }

        public void OnServerConfirmActivation(int abilityIndex, int predictionKey)
        {
            AssertRuntimeThread();
            OnConfirmActivation?.Invoke(abilityIndex, predictionKey);
        }

        public void OnServerConfirmActivation(int abilityIndex, int predictionKey, int predictionKeyOwner, int predictionInputSequence)
        {
            AssertRuntimeThread();
            var key = BuildPredictionKey(predictionKey, predictionKeyOwner, predictionInputSequence);
            OnConfirmActivationKey?.Invoke(abilityIndex, key);
            OnConfirmActivation?.Invoke(abilityIndex, predictionKey);
        }

        public void OnServerRejectActivation(int abilityIndex, int predictionKey)
        {
            AssertRuntimeThread();
            OnRejectActivation?.Invoke(abilityIndex, predictionKey);
        }

        public void OnServerRejectActivation(int abilityIndex, int predictionKey, int predictionKeyOwner, int predictionInputSequence)
        {
            AssertRuntimeThread();
            var key = BuildPredictionKey(predictionKey, predictionKeyOwner, predictionInputSequence);
            OnRejectActivationKey?.Invoke(abilityIndex, key);
            OnRejectActivation?.Invoke(abilityIndex, predictionKey);
        }

        public void OnAbilityEnded(int abilityIndex)
        {
            AssertRuntimeThread();
            var spec = TryResolveAbilitySpec(abilityIndex);
            if (spec?.GetPrimaryInstance() != null)
            {
                spec.GetPrimaryInstance().EndAbility();
            }

            OnReplicatedAbilityEnd?.Invoke(abilityIndex);
        }

        public void OnAbilityCancelled(int abilityIndex)
        {
            AssertRuntimeThread();
            var spec = TryResolveAbilitySpec(abilityIndex);
            if (spec?.GetPrimaryInstance() != null)
            {
                spec.GetPrimaryInstance().CancelAbility();
            }

            OnReplicatedAbilityCancel?.Invoke(abilityIndex);
        }

        public void OnAbilityMulticast(AbilityMulticastData data)
        {
            AssertRuntimeThread();
            OnAbilityMulticastReceived?.Invoke(data);
        }

        public void OnReplicatedEffectApplied(EffectReplicationData data)
        {
            AssertRuntimeThread();
            if (!AcceptTargetNetworkId(data.TargetNetworkId, nameof(OnReplicatedEffectApplied)))
                return;

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
            var predictionKey = BuildPredictionKey(data.PredictionKey, data.PredictionKeyOwner, data.PredictionInputSequence);
            if (predictionKey.IsValid && spec.Context != null)
            {
                spec.Context.PredictionKey = predictionKey;
            }

            ApplySetByCaller(spec, data);
            _asc.ApplyGameplayEffectSpecToSelf(spec);

            if (_asc.ActiveEffects.Count > beforeCount)
            {
                var localEffect = _asc.ActiveEffects[_asc.ActiveEffects.Count - 1];
                _remoteEffectToLocal[data.EffectInstanceId] = localEffect;
                _localEffectToRemote[localEffect] = data.EffectInstanceId;

                int setByCallerCount = FillSetByCallerScratch(data.SetByCallerEntries, data.SetByCallerCount);
                _asc.TryApplyReplicatedEffectUpdate(
                    localEffect,
                    Math.Max(1, data.Level),
                    data.StackCount,
                    data.Duration,
                    data.TimeRemaining,
                    data.PeriodTimeRemaining,
                    _setByCallerTagsScratch,
                    _setByCallerValuesScratch,
                    setByCallerCount);
            }

            _replicatedEffectStates[data.EffectInstanceId] = data;
        }

        public void OnReplicatedEffectRemoved(int effectInstanceId)
        {
            AssertRuntimeThread();
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
            AssertRuntimeThread();
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
            AssertRuntimeThread();
            if (!AcceptTargetNetworkId(data.TargetNetworkId, nameof(OnReplicatedEffectUpdated)))
                return;

            bool handled = (EffectMutationHandler != null && EffectMutationHandler.TryApplyReplicatedEffectUpdate(data))
                || (TryApplyReplicatedEffectUpdate != null && TryApplyReplicatedEffectUpdate(data));

            if (!handled && _remoteEffectToLocal.TryGetValue(data.EffectInstanceId, out var localEffect))
            {
                int setByCallerCount = FillSetByCallerScratch(data.SetByCallerEntries, data.SetByCallerCount);
                handled = _asc.TryApplyReplicatedEffectUpdate(
                    localEffect,
                    Math.Max(1, data.Level),
                    data.StackCount,
                    data.Duration,
                    data.TimeRemaining,
                    data.PeriodTimeRemaining,
                    _setByCallerTagsScratch,
                    _setByCallerValuesScratch,
                    setByCallerCount);
            }

            _replicatedEffectStates[data.EffectInstanceId] = ToEffectReplicationData(data);

            if (handled)
                return;

            Debug.LogWarning($"[GAS Integration] Effect update not applied for remote instance {data.EffectInstanceId}. " +
                "Provide TryApplyReplicatedEffectUpdate callback or expose ASC API for explicit in-place effect updates.");
        }

        public void OnReplicatedAttributeUpdate(AttributeUpdateData data)
        {
            AssertRuntimeThread();
            if (!AcceptTargetNetworkId(data.TargetNetworkId, nameof(OnReplicatedAttributeUpdate)))
                return;

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
            AssertRuntimeThread();
            if (!AcceptTargetNetworkId(data.TargetNetworkId, nameof(OnReplicatedTagUpdate)))
                return;

            ApplyTagArray(data.AddedTagHashes, data.AddedCount, add: true);
            ApplyTagArray(data.RemovedTagHashes, data.RemovedCount, add: false);
        }

        public GASFullStateData CaptureFullState()
        {
            AssertRuntimeThread();
            int abilityCount = FillGrantedAbilitiesFromAsc(ref _grantedAbilitiesBuffer);
            int effectCount = FillActiveEffects(_asc.ActiveEffects, ref _currentEffectsScratch);
            int attributeCount = FillAttributeEntriesFromAsc(ref _attributeEntriesBuffer);
            int tagCount = FillTagHashesFromAsc(ref _fullStateTagHashesBuffer);

            SyncReplicatedEffectBaseline(_currentEffectsScratch, effectCount);

            return new GASFullStateData
            {
                TargetNetworkId = NetworkId,
                StateVersion = _asc.StateVersion,
                StateChecksum = _asc.ComputeReplicatedStateChecksum(),
                AbilityCount = abilityCount,
                Abilities = _grantedAbilitiesBuffer,
                EffectCount = effectCount,
                Effects = _currentEffectsScratch,
                AttributeCount = attributeCount,
                Attributes = _attributeEntriesBuffer,
                TagCount = tagCount,
                TagHashes = _fullStateTagHashesBuffer
            };
        }

        public void OnFullState(GASFullStateData data)
        {
            AssertRuntimeThread();
            if (data.TargetNetworkId != NetworkId)
            {
                Debug.LogWarning($"[GAS Integration] Ignored full state for target {data.TargetNetworkId}; adapter target is {NetworkId}.");
                return;
            }

            ApplyFullStateAbilities(data.Abilities, data.AbilityCount);
            ApplyFullStateEffects(data.Effects, data.EffectCount);

            var attrData = new AttributeUpdateData
            {
                TargetNetworkId = data.TargetNetworkId,
                IsFullSync = true,
                AttributeCount = data.AttributeCount,
                Attributes = data.Attributes
            };

            OnReplicatedAttributeUpdate(attrData);

            int currentTagCount = FillTagHashesFromAsc(ref _currentFullStateTagHashesBuffer);
            int removedTagCount = FillTagDiff(_currentFullStateTagHashesBuffer, currentTagCount, data.TagHashes, data.TagCount, ref _removedTagHashesBuffer);

            OnReplicatedTagUpdate(new TagUpdateData
            {
                TargetNetworkId = data.TargetNetworkId,
                AddedCount = data.TagCount,
                AddedTagHashes = data.TagHashes,
                RemovedCount = removedTagCount,
                RemovedTagHashes = _removedTagHashesBuffer
            });

            _asc.ConsumePendingStateChanges();
            _lastServerStateVersion = data.StateVersion;
            _lastServerStateChecksum = data.StateChecksum;
            _lastStateSyncSequence = 0;
            _lastRejectedStateSyncSequence = 0;
            OnFullStateReceived?.Invoke(data);
        }

        public bool OnStateSyncMetadata(GASStateSyncMetadata metadata)
        {
            AssertRuntimeThread();
            GASStateDriftReason reason = GASStateDriftReason.None;

            if (metadata.TargetNetworkId != NetworkId)
            {
                reason = GASStateDriftReason.TargetNetworkIdMismatch;
            }
            else if (metadata.CurrentVersion < metadata.BaseVersion)
            {
                reason = GASStateDriftReason.InvalidVersionRange;
            }
            else if (_lastStateSyncSequence != 0 && !IsSequenceNewer(metadata.Sequence, _lastStateSyncSequence))
            {
                reason = GASStateDriftReason.OutOfOrderSequence;
            }
            else if (_lastServerStateVersion != 0 && metadata.BaseVersion != _lastServerStateVersion)
            {
                reason = GASStateDriftReason.BaseVersionMismatch;
            }
            else if (EnableStrictChecksumValidation && metadata.StateChecksum != 0)
            {
                uint localChecksum = _asc.ComputeReplicatedStateChecksum();
                if (localChecksum != metadata.StateChecksum)
                {
                    reason = GASStateDriftReason.ChecksumMismatch;
                }
            }

            if (reason == GASStateDriftReason.None)
            {
                _lastStateSyncSequence = metadata.Sequence;
                _lastServerStateVersion = metadata.CurrentVersion;
                _lastServerStateChecksum = metadata.StateChecksum;
                return true;
            }

            _lastRejectedStateSyncSequence = metadata.Sequence;
            OnStateDriftDetected?.Invoke(metadata, reason);
            return false;
        }

        private static bool IsSequenceNewer(uint incoming, uint lastAccepted)
        {
            return incoming != lastAccepted && unchecked((int)(incoming - lastAccepted)) > 0;
        }

        private void ApplyFullStateAbilities(GrantedAbilityEntry[] abilities, int abilityCount)
        {
            int safeCount = abilities != null ? Math.Min(abilityCount, abilities.Length) : 0;
            var specs = _asc.GetActivatableAbilities();
            _abilitySpecByDefinitionIdScratch.Clear();
            _fullStateAbilityDefinitionIdsScratch.Clear();

            for (int i = 0; i < safeCount; i++)
            {
                int abilityDefinitionId = abilities[i].AbilityDefinitionId;
                if (abilityDefinitionId != 0)
                {
                    _fullStateAbilityDefinitionIdsScratch.Add(abilityDefinitionId);
                }
            }

            for (int i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                var ability = spec.AbilityCDO ?? spec.Ability;
                int abilityId = ability != null ? _idRegistry.GetAbilityDefinitionId(ability) : 0;
                if (abilityId != 0)
                {
                    _abilitySpecByDefinitionIdScratch[abilityId] = spec;
                }
            }

            for (int i = specs.Count - 1; i >= 0; i--)
            {
                var spec = specs[i];
                var ability = spec.AbilityCDO ?? spec.Ability;
                int abilityId = ability != null ? _idRegistry.GetAbilityDefinitionId(ability) : 0;
                if (abilityId == 0 || !_fullStateAbilityDefinitionIdsScratch.Contains(abilityId))
                {
                    _asc.ClearAbility(spec);
                    _abilitySpecByDefinitionIdScratch.Remove(abilityId);
                }
            }

            for (int i = 0; i < safeCount; i++)
            {
                var entry = abilities[i];
                if (entry.AbilityDefinitionId == 0)
                    continue;

                _abilitySpecByDefinitionIdScratch.TryGetValue(entry.AbilityDefinitionId, out var existingSpec);
                if (existingSpec != null)
                {
                    existingSpec.Level = Math.Max(1, entry.Level);
                    continue;
                }

                if (_idRegistry.TryResolveAbilityDefinition(entry.AbilityDefinitionId, out var ability) && ability != null)
                {
                    var granted = _asc.GrantAbility(ability, Math.Max(1, entry.Level));
                    if (granted != null)
                    {
                        _abilitySpecByDefinitionIdScratch[entry.AbilityDefinitionId] = granted;
                    }
                }
            }
        }

        private void ApplyFullStateEffects(EffectReplicationData[] effects, int effectCount)
        {
            int safeCount = effects != null ? Math.Min(effectCount, effects.Length) : 0;

            _currentEffectsByIdScratch.Clear();
            for (int i = 0; i < safeCount; i++)
            {
                int effectInstanceId = effects[i].EffectInstanceId;
                if (effectInstanceId != 0)
                {
                    _currentEffectsByIdScratch[effectInstanceId] = effects[i];
                }
            }

            _removedEffectIdsScratch.Clear();
            foreach (var pair in _remoteEffectToLocal)
            {
                if (!_currentEffectsByIdScratch.ContainsKey(pair.Key))
                {
                    _removedEffectIdsScratch.Add(pair.Key);
                }
            }

            for (int i = 0; i < _removedEffectIdsScratch.Count; i++)
            {
                OnReplicatedEffectRemoved(_removedEffectIdsScratch[i]);
            }

            for (int i = 0; i < safeCount; i++)
            {
                OnReplicatedEffectApplied(effects[i]);
            }
        }

        private bool AcceptTargetNetworkId(uint targetNetworkId, string operation)
        {
            if (targetNetworkId == NetworkId)
                return true;

            Debug.LogWarning($"[GAS Integration] Ignored {operation} for target {targetNetworkId}; adapter target is {NetworkId}.");
            return false;
        }

        public ReplicatedAbilitySystemStateDelta CapturePendingReplicatedStateDelta()
        {
            AssertRuntimeThread();
            var changeMask = _asc.PendingStateChangeMask;

            int grantedAbilityCount = changeMask.HasFlag(AbilitySystemStateChangeMask.GrantedAbilities)
                ? FillGrantedAbilitiesFromAsc(ref _grantedAbilitiesBuffer)
                : 0;
            var grantedAbilities = _grantedAbilitiesBuffer;

            var addedActiveEffects = Array.Empty<EffectReplicationData>();
            var updatedActiveEffects = Array.Empty<EffectUpdateData>();
            var stackChangedEffects = Array.Empty<EffectStackChangeData>();
            var removedEffectInstanceIds = Array.Empty<int>();
            int addedActiveEffectCount = 0;
            int updatedActiveEffectCount = 0;
            int stackChangedEffectCount = 0;
            int removedEffectInstanceIdCount = 0;

            if (changeMask.HasFlag(AbilitySystemStateChangeMask.ActiveEffects))
            {
                ClassifyActiveEffectChanges(
                    _asc.ActiveEffects,
                    out addedActiveEffects,
                    out addedActiveEffectCount,
                    out updatedActiveEffects,
                    out updatedActiveEffectCount,
                    out stackChangedEffects,
                    out stackChangedEffectCount,
                    out removedEffectInstanceIds,
                    out removedEffectInstanceIdCount);
            }

            var attributeUpdate = changeMask.HasFlag(AbilitySystemStateChangeMask.Attributes)
                ? BuildAttributeDeltaFromAscPending()
                : default;

            var tagUpdate = changeMask.HasFlag(AbilitySystemStateChangeMask.Tags)
                ? BuildTagDeltaFromPendingAscTags()
                : default;

            _asc.CapturePendingStateDeltaNonAlloc(_stateDeltaBuffer, ResolveEffectInstanceId);
            if (_stateDeltaBuffer.HasChanges)
            {
                _stateDeltaBuffer.Sequence = ++_outgoingStateSyncSequence;
            }

            return new ReplicatedAbilitySystemStateDelta(
                _stateDeltaBuffer,
                grantedAbilities,
                grantedAbilityCount,
                addedActiveEffects,
                addedActiveEffectCount,
                updatedActiveEffects,
                updatedActiveEffectCount,
                stackChangedEffects,
                stackChangedEffectCount,
                removedEffectInstanceIds,
                removedEffectInstanceIdCount,
                attributeUpdate,
                tagUpdate);
        }

        public ReplicatedAbilitySystemStateDelta CaptureAndReplicatePendingStateDelta(
            NetworkedAbilityBridge bridge,
            IReadOnlyList<INetConnection> observers)
        {
            AssertRuntimeThread();
            if (bridge == null)
                throw new ArgumentNullException(nameof(bridge));

            var delta = CapturePendingReplicatedStateDelta();
            if (observers == null || observers.Count == 0)
                return delta;

            for (int i = 0; i < delta.AddedActiveEffectCount; i++)
            {
                bridge.ServerReplicateEffectApplied(observers, NetworkId, delta.AddedActiveEffects[i]);
            }

            for (int i = 0; i < delta.UpdatedActiveEffectCount; i++)
            {
                bridge.ServerReplicateEffectUpdated(observers, NetworkId, delta.UpdatedActiveEffects[i]);
            }

            for (int i = 0; i < delta.StackChangedEffectCount; i++)
            {
                var stackChange = delta.StackChangedEffects[i];
                bridge.ServerReplicateStackChange(observers, NetworkId, stackChange.EffectInstanceId, stackChange.NewStackCount);
            }

            for (int i = 0; i < delta.RemovedEffectInstanceIdCount; i++)
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

            if (delta.StateDelta != null && delta.StateDelta.HasChanges)
            {
                bridge.ServerBroadcastStateSyncMetadata(observers, new GASStateSyncMetadata
                {
                    TargetNetworkId = NetworkId,
                    Sequence = delta.StateDelta.Sequence,
                    BaseVersion = delta.StateDelta.BaseVersion,
                    CurrentVersion = delta.StateDelta.CurrentVersion,
                    StateChecksum = delta.StateDelta.StateChecksum,
                    ChangeMask = (uint)delta.StateDelta.ChangeMask
                });
            }

            return delta;
        }

        public void Dispose()
        {
            AssertRuntimeThread();
            _idRegistry.UnregisterAsc(NetworkId);
            _remoteEffectToLocal.Clear();
            _localEffectToRemote.Clear();
            _replicatedEffectStates.Clear();
            _replicatedSetByCallerBaselineArrays.Clear();
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

        private int FillSetByCallerScratch(SetByCallerEntry[] entries, int entryCount)
        {
            if (entries == null || entryCount <= 0)
                return 0;

            int count = Math.Min(entryCount, entries.Length);
            EnsureSetByCallerScratchCapacity(count);
            int resolvedCount = 0;

            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (_idRegistry.TryResolveTag(entry.TagHash, out var tag) && tag.IsValid && !tag.IsNone)
                {
                    _setByCallerTagsScratch[resolvedCount] = tag;
                    _setByCallerValuesScratch[resolvedCount] = entry.Value;
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

        private int FillGrantedAbilitiesFromAsc(ref GrantedAbilityEntry[] entries)
        {
            var abilities = _asc.GetActivatableAbilities();
            if (abilities == null || abilities.Count == 0)
                return 0;

            EnsureArrayCapacity(ref entries, abilities.Count);
            return FillGrantedAbilities(abilities, entries);
        }

        private int FillGrantedAbilities(IReadOnlyList<GameplayAbilitySpec> abilities, GrantedAbilityEntry[] entries)
        {
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

            return abilities.Count;
        }

        private int FillAttributeEntriesFromAsc(ref AttributeEntry[] entries)
        {
            int attributeCount = CountAttributesFromAsc();
            if (attributeCount <= 0)
                return 0;

            EnsureArrayCapacity(ref entries, attributeCount);
            return FillAttributeEntriesFromAsc(entries);
        }

        private int CountAttributesFromAsc()
        {
            var attributeSets = _asc.AttributeSets;
            if (attributeSets == null || attributeSets.Count == 0)
                return 0;

            int attributeCount = 0;
            for (int i = 0; i < attributeSets.Count; i++)
            {
                attributeCount += attributeSets[i].GetAttributes().Count;
            }

            return attributeCount;
        }

        private int FillAttributeEntriesFromAsc(AttributeEntry[] entries)
        {
            var attributeSets = _asc.AttributeSets;
            if (attributeSets == null || entries == null)
                return 0;

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

            return index;
        }

        private int FillTagHashesFromAsc(ref int[] hashes)
        {
            if (_asc.CombinedTags == null || _asc.CombinedTags.TagCount <= 0)
                return 0;

            EnsureArrayCapacity(ref hashes, _asc.CombinedTags.TagCount);
            int count = 0;
            foreach (var tag in _asc.CombinedTags)
            {
                if (!tag.IsValid || tag.IsNone)
                    continue;

                hashes[count++] = _idRegistry.GetTagHash(tag);
            }

            return count;
        }

        private AttributeUpdateData BuildAttributeDeltaFromAscPending()
        {
            int attributeCount = _asc.IsAttributeStructureDirty
                ? FillAttributeEntriesFromAsc(ref _attributeEntriesBuffer)
                : FillDirtyAttributeEntriesFromAsc(ref _dirtyAttributeEntriesBuffer);
            var attributes = _asc.IsAttributeStructureDirty ? _attributeEntriesBuffer : _dirtyAttributeEntriesBuffer;

            return new AttributeUpdateData
            {
                TargetNetworkId = NetworkId,
                IsFullSync = false,
                AttributeCount = attributeCount,
                Attributes = attributes
            };
        }

        private int FillDirtyAttributeEntriesFromAsc(ref AttributeEntry[] entries)
        {
            var dirtyNames = _asc.DirtyAttributeNames;
            if (dirtyNames == null || dirtyNames.Count == 0)
                return 0;

            EnsureArrayCapacity(ref entries, dirtyNames.Count);
            return FillDirtyAttributeEntriesFromAsc(entries);
        }

        private int FillDirtyAttributeEntriesFromAsc(AttributeEntry[] entries)
        {
            var dirtyNames = _asc.DirtyAttributeNames;
            if (dirtyNames == null || entries == null)
                return 0;

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

            return index;
        }

        private TagUpdateData BuildTagDeltaFromPendingAscTags()
        {
            int addedCount = FillTagHashes(_asc.PendingAddedTags, ref _addedTagHashesBuffer);
            int removedCount = FillTagHashes(_asc.PendingRemovedTags, ref _removedTagHashesBuffer);

            return new TagUpdateData
            {
                TargetNetworkId = NetworkId,
                AddedCount = addedCount,
                AddedTagHashes = _addedTagHashesBuffer,
                RemovedCount = removedCount,
                RemovedTagHashes = _removedTagHashesBuffer
            };
        }

        private int FillActiveEffects(IReadOnlyList<ActiveGameplayEffect> activeEffects, ref EffectReplicationData[] entries)
        {
            if (activeEffects == null || activeEffects.Count == 0)
                return 0;

            EnsureArrayCapacity(ref entries, activeEffects.Count);
            return FillActiveEffects(activeEffects, entries);
        }

        private int FillActiveEffects(IReadOnlyList<ActiveGameplayEffect> activeEffects, EffectReplicationData[] entries)
        {
            if (activeEffects == null || entries == null)
                return 0;

            for (int i = 0; i < activeEffects.Count; i++)
            {
                var effect = activeEffects[i];
                uint sourceNetworkId = 0;
                _idRegistry.TryResolveNetworkId(effect.Spec.Source, out sourceNetworkId);
                var predictionKey = effect.Spec.Context?.PredictionKey ?? default;

                var setByCallerEntries = BuildSetByCallerEntries(effect.Spec, i, out int setByCallerCount);

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
                    PredictionKey = predictionKey.Key,
                    PredictionKeyOwner = predictionKey.Owner.Value,
                    PredictionInputSequence = predictionKey.InputSequence,
                    SetByCallerCount = setByCallerCount,
                    SetByCallerEntries = setByCallerEntries
                };
            }

            return activeEffects.Count;
        }

        private int FillTagHashes(IReadOnlyCollection<GameplayTag> tags, ref int[] hashes)
        {
            if (tags == null || tags.Count == 0)
                return 0;

            EnsureArrayCapacity(ref hashes, tags.Count);
            return FillTagHashes(tags, hashes);
        }

        private int FillTagHashes(IReadOnlyCollection<GameplayTag> tags, int[] hashes)
        {
            if (tags == null || hashes == null)
                return 0;

            int index = 0;
            foreach (var tag in tags)
            {
                hashes[index++] = _idRegistry.GetTagHash(tag);
            }

            return index;
        }

        private SetByCallerEntry[] BuildSetByCallerEntries(GameplayEffectSpec spec, int effectIndex, out int copiedCount)
        {
            copiedCount = 0;
            if (spec == null)
                return Array.Empty<SetByCallerEntry>();

            int count = spec.SetByCallerTagMagnitudeCount;
            if (count <= 0)
                return Array.Empty<SetByCallerEntry>();

            EnsureSetByCallerScratchCapacity(count);

            copiedCount = spec.CopySetByCallerTagMagnitudes(_setByCallerTagsScratch, _setByCallerValuesScratch);
            if (copiedCount <= 0)
                return Array.Empty<SetByCallerEntry>();

            var networkEntries = EnsureCurrentEffectSetByCallerCapacity(effectIndex, copiedCount);
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

        private SetByCallerEntry[] EnsureCurrentEffectSetByCallerCapacity(int effectIndex, int capacity)
        {
            if (effectIndex < 0 || capacity <= 0)
                return Array.Empty<SetByCallerEntry>();

            if (_currentEffectSetByCallerEntries.Length <= effectIndex)
            {
                var existing = _currentEffectSetByCallerEntries;
                int next = Math.Max(effectIndex + 1, existing.Length == 0 ? 4 : existing.Length * 2);
                _currentEffectSetByCallerEntries = new SetByCallerEntry[next][];
                for (int i = 0; i < existing.Length; i++)
                {
                    _currentEffectSetByCallerEntries[i] = existing[i];
                }
            }

            var entries = _currentEffectSetByCallerEntries[effectIndex];
            if (entries == null || entries.Length < capacity)
            {
                int next = Math.Max(capacity, entries == null || entries.Length == 0 ? 4 : entries.Length * 2);
                entries = new SetByCallerEntry[next];
                _currentEffectSetByCallerEntries[effectIndex] = entries;
            }

            return entries;
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

        private static int CopyListToBuffer<T>(List<T> source, ref T[] buffer)
        {
            int count = source != null ? source.Count : 0;
            if (count <= 0)
                return 0;

            EnsureArrayCapacity(ref buffer, count);
            for (int i = 0; i < count; i++)
            {
                buffer[i] = source[i];
            }

            return count;
        }

        private static void EnsureArrayCapacity<T>(ref T[] buffer, int count)
        {
            if (buffer == null || buffer.Length < count)
            {
                int current = buffer != null ? buffer.Length : 0;
                int next = Math.Max(count, current == 0 ? 4 : current * 2);
                Array.Resize(ref buffer, next);
            }
        }

        private int FillTagDiff(int[] current, int currentCount, int[] target, int targetCount, ref int[] destination)
        {
            _targetTagHashSetScratch.Clear();
            _tagDiffScratch.Clear();

            if (target != null)
            {
                int safeTargetCount = Math.Min(targetCount, target.Length);
                for (int i = 0; i < safeTargetCount; i++)
                {
                    _targetTagHashSetScratch.Add(target[i]);
                }
            }

            if (current != null)
            {
                int safeCurrentCount = Math.Min(currentCount, current.Length);
                for (int i = 0; i < safeCurrentCount; i++)
                {
                    if (!_targetTagHashSetScratch.Contains(current[i]))
                        _tagDiffScratch.Add(current[i]);
                }
            }

            return CopyListToBuffer(_tagDiffScratch, ref destination);
        }

        private void SyncReplicatedEffectBaseline(EffectReplicationData[] effects)
        {
            SyncReplicatedEffectBaseline(effects, effects != null ? effects.Length : 0);
        }

        private void SyncReplicatedEffectBaseline(EffectReplicationData[] effects, int count)
        {
            _replicatedEffectStates.Clear();

            if (effects == null)
                return;

            int safeCount = Math.Min(count, effects.Length);
            for (int i = 0; i < safeCount; i++)
            {
                var effect = effects[i];
                if (effect.SetByCallerEntries != null && effect.SetByCallerCount > 0)
                {
                    int setByCallerCount = Math.Min(effect.SetByCallerCount, effect.SetByCallerEntries.Length);
                    var baselineEntries = EnsureBaselineSetByCallerCapacity(effect.EffectInstanceId, setByCallerCount);
                    for (int entryIndex = 0; entryIndex < setByCallerCount; entryIndex++)
                    {
                        baselineEntries[entryIndex] = effect.SetByCallerEntries[entryIndex];
                    }

                    effect.SetByCallerEntries = baselineEntries;
                    effect.SetByCallerCount = setByCallerCount;
                }

                _replicatedEffectStates[effect.EffectInstanceId] = effect;
            }
        }

        private SetByCallerEntry[] EnsureBaselineSetByCallerCapacity(int effectInstanceId, int capacity)
        {
            if (capacity <= 0)
                return Array.Empty<SetByCallerEntry>();

            if (!_replicatedSetByCallerBaselineArrays.TryGetValue(effectInstanceId, out var entries) ||
                entries == null ||
                entries.Length < capacity)
            {
                int next = Math.Max(capacity, entries == null || entries.Length == 0 ? 4 : entries.Length * 2);
                entries = new SetByCallerEntry[next];
                _replicatedSetByCallerBaselineArrays[effectInstanceId] = entries;
            }

            return entries;
        }

        private void ClassifyActiveEffectChanges(
            IReadOnlyList<ActiveGameplayEffect> activeEffects,
            out EffectReplicationData[] addedActiveEffects,
            out int addedActiveEffectCount,
            out EffectUpdateData[] updatedActiveEffects,
            out int updatedActiveEffectCount,
            out EffectStackChangeData[] stackChangedEffects,
            out int stackChangedEffectCount,
            out int[] removedEffectInstanceIds,
            out int removedEffectInstanceIdCount)
        {
            int currentEffectCount = FillActiveEffects(activeEffects, ref _currentEffectsScratch);
            _currentEffectsByIdScratch.Clear();
            _addedEffectsScratch.Clear();
            _updatedEffectsScratch.Clear();
            _stackChangedEffectsScratch.Clear();
            _removedEffectIdsScratch.Clear();

            for (int i = 0; i < currentEffectCount; i++)
            {
                var current = _currentEffectsScratch[i];
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
                _replicatedSetByCallerBaselineArrays.Remove(removedId);
                _remoteEffectToLocal.Remove(removedId);
            }

            SyncReplicatedEffectBaseline(_currentEffectsScratch, currentEffectCount);

            addedActiveEffectCount = CopyListToBuffer(_addedEffectsScratch, ref _addedEffectsBuffer);
            updatedActiveEffectCount = CopyListToBuffer(_updatedEffectsScratch, ref _updatedEffectsBuffer);
            stackChangedEffectCount = CopyListToBuffer(_stackChangedEffectsScratch, ref _stackChangedEffectsBuffer);
            removedEffectInstanceIdCount = CopyListToBuffer(_removedEffectIdsScratch, ref _removedEffectIdsBuffer);

            addedActiveEffects = _addedEffectsBuffer;
            updatedActiveEffects = _updatedEffectsBuffer;
            stackChangedEffects = _stackChangedEffectsBuffer;
            removedEffectInstanceIds = _removedEffectIdsBuffer;
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
                && previous.PredictionKeyOwner == current.PredictionKeyOwner
                && previous.PredictionInputSequence == current.PredictionInputSequence
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
                && previous.PredictionKeyOwner == current.PredictionKeyOwner
                && previous.PredictionInputSequence == current.PredictionInputSequence
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

        private int ResolveEffectInstanceId(ActiveGameplayEffect effect)
        {
            return effect != null ? GetOrCreateEffectInstanceId(effect) : 0;
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
                PredictionKeyOwner = data.PredictionKeyOwner,
                PredictionInputSequence = data.PredictionInputSequence,
                SetByCallerCount = data.SetByCallerCount,
                SetByCallerEntries = data.SetByCallerEntries
            };
        }

        private static GASPredictionKey BuildPredictionKey(int value, int owner, int inputSequence)
        {
            return value != 0
                ? new GASPredictionKey(value, new GASEntityId(owner), inputSequence)
                : default;
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
                PredictionKeyOwner = data.PredictionKeyOwner,
                PredictionInputSequence = data.PredictionInputSequence,
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
