using System.Collections.Generic;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Owns active gameplay effects and the indexes needed for stacking, network lookup, granted tags, and ability cleanup.
    /// </summary>
    public sealed class ActiveEffectContainer
    {
        private readonly List<ActiveGameplayEffect> _activeEffects;
        private readonly Dictionary<ActiveGameplayEffect, int> _indexByEffect;
        private readonly Dictionary<int, ActiveGameplayEffect> _effectByNetworkId;
        private readonly Dictionary<GameplayEffect, ActiveGameplayEffect> _stackingByTarget;
        private readonly Dictionary<(GameplayEffect, AbilitySystemComponent), ActiveGameplayEffect> _stackingBySource;
        private readonly Dictionary<int, List<ActiveGameplayEffect>> _effectsByGrantedTagIndex;
        private readonly Dictionary<GameplayAbility, List<ActiveGameplayEffect>> _effectsByAbility;
        private readonly List<ActiveGameplayEffect> _abilityEffectRemovalScratch;
        private readonly List<GameplayAbility> _emptyAbilityEffectListOwners;
        private readonly Stack<List<ActiveGameplayEffect>> _abilityEffectListPool;

        public ActiveEffectContainer(
            int activeEffectCapacity = 32,
            int stackingCapacity = 16,
            int grantedTagCapacity = 16,
            int abilityEffectCapacity = 8,
            int pooledListCapacity = 4)
        {
            _activeEffects = new List<ActiveGameplayEffect>(activeEffectCapacity);
            _indexByEffect = new Dictionary<ActiveGameplayEffect, int>(activeEffectCapacity);
            _effectByNetworkId = new Dictionary<int, ActiveGameplayEffect>(activeEffectCapacity);
            _stackingByTarget = new Dictionary<GameplayEffect, ActiveGameplayEffect>(stackingCapacity);
            _stackingBySource = new Dictionary<(GameplayEffect, AbilitySystemComponent), ActiveGameplayEffect>(stackingCapacity);
            _effectsByGrantedTagIndex = new Dictionary<int, List<ActiveGameplayEffect>>(grantedTagCapacity);
            _effectsByAbility = new Dictionary<GameplayAbility, List<ActiveGameplayEffect>>(abilityEffectCapacity);
            _abilityEffectRemovalScratch = new List<ActiveGameplayEffect>(abilityEffectCapacity);
            _emptyAbilityEffectListOwners = new List<GameplayAbility>(abilityEffectCapacity);
            _abilityEffectListPool = new Stack<List<ActiveGameplayEffect>>(pooledListCapacity);
        }

        public IReadOnlyList<ActiveGameplayEffect> ActiveEffects => _activeEffects;
        public int Count => _activeEffects.Count;
        public int IndexCount => _indexByEffect.Count;
        public int NetworkIndexCount => _effectByNetworkId.Count;
        public int GrantedTagIndexCount => _effectsByGrantedTagIndex.Count;
        public int AbilityEffectIndexCount => _effectsByAbility.Count;
        public int AbilityEffectListPoolSize => _abilityEffectListPool.Count;

        internal List<ActiveGameplayEffect> MutableActiveEffects => _activeEffects;
        internal Dictionary<ActiveGameplayEffect, int> MutableIndexByEffect => _indexByEffect;
        internal Dictionary<int, ActiveGameplayEffect> MutableEffectByNetworkId => _effectByNetworkId;
        internal Dictionary<GameplayEffect, ActiveGameplayEffect> MutableStackingByTarget => _stackingByTarget;
        internal Dictionary<(GameplayEffect, AbilitySystemComponent), ActiveGameplayEffect> MutableStackingBySource => _stackingBySource;
        internal Dictionary<int, List<ActiveGameplayEffect>> MutableEffectsByGrantedTagIndex => _effectsByGrantedTagIndex;
        internal Dictionary<GameplayAbility, List<ActiveGameplayEffect>> MutableEffectsByAbility => _effectsByAbility;
        internal List<ActiveGameplayEffect> MutableAbilityEffectRemovalScratch => _abilityEffectRemovalScratch;
        internal Stack<List<ActiveGameplayEffect>> MutableAbilityEffectListPool => _abilityEffectListPool;

        public void Reserve(int activeEffectCapacity, int stackingCapacity, int grantedTagCapacity, int abilityEffectCapacity)
        {
            if (activeEffectCapacity > _activeEffects.Capacity)
            {
                _activeEffects.Capacity = activeEffectCapacity;
            }

            if (activeEffectCapacity > 0)
            {
                _indexByEffect.EnsureCapacity(activeEffectCapacity);
                _effectByNetworkId.EnsureCapacity(activeEffectCapacity);
            }

            if (stackingCapacity > 0)
            {
                _stackingByTarget.EnsureCapacity(stackingCapacity);
                _stackingBySource.EnsureCapacity(stackingCapacity);
            }

            if (grantedTagCapacity > 0)
            {
                _effectsByGrantedTagIndex.EnsureCapacity(grantedTagCapacity);
            }

            if (abilityEffectCapacity > 0)
            {
                _effectsByAbility.EnsureCapacity(abilityEffectCapacity);
            }
        }

        public bool TryGetByNetworkId(int networkId, out ActiveGameplayEffect effect)
        {
            return _effectByNetworkId.TryGetValue(networkId, out effect);
        }

        public ActiveGameplayEffect FindByNetworkId(int networkId)
        {
            if (networkId == 0)
            {
                return null;
            }

            return _effectByNetworkId.TryGetValue(networkId, out var effect) &&
                effect != null &&
                !effect.IsExpired
                    ? effect
                    : null;
        }

        public bool AddEffect(ActiveGameplayEffect effect)
        {
            if (effect == null || _indexByEffect.ContainsKey(effect))
            {
                return false;
            }

            _indexByEffect[effect] = _activeEffects.Count;
            _activeEffects.Add(effect);
            if (effect.NetworkId != 0)
            {
                _effectByNetworkId[effect.NetworkId] = effect;
            }

            TrackStackingEffect(effect);
            return true;
        }

        public ActiveGameplayEffect RemoveAtSwapBack(int index)
        {
            if (index < 0 || index >= _activeEffects.Count)
            {
                return null;
            }

            var removedEffect = _activeEffects[index];
            if (removedEffect.NetworkId != 0 &&
                _effectByNetworkId.TryGetValue(removedEffect.NetworkId, out var indexed) &&
                ReferenceEquals(indexed, removedEffect))
            {
                _effectByNetworkId.Remove(removedEffect.NetworkId);
            }

            int lastIndex = _activeEffects.Count - 1;
            if (index != lastIndex)
            {
                var movedEffect = _activeEffects[lastIndex];
                _activeEffects[index] = movedEffect;
                _indexByEffect[movedEffect] = index;
            }

            _activeEffects.RemoveAt(lastIndex);
            _indexByEffect.Remove(removedEffect);
            return removedEffect;
        }

        public void RebuildNetworkIdIndex()
        {
            _effectByNetworkId.Clear();
            for (int i = 0; i < _activeEffects.Count; i++)
            {
                var effect = _activeEffects[i];
                if (effect != null && !effect.IsExpired && effect.NetworkId != 0)
                {
                    _effectByNetworkId[effect.NetworkId] = effect;
                }
            }
        }

        public bool TryFindIndex(ActiveGameplayEffect effect, out int index)
        {
            if (effect != null &&
                _indexByEffect.TryGetValue(effect, out index) &&
                index >= 0 &&
                index < _activeEffects.Count &&
                ReferenceEquals(_activeEffects[index], effect))
            {
                return true;
            }

            index = -1;
            return false;
        }

        public void SetNetworkId(ActiveGameplayEffect effect, int networkId)
        {
            if (effect == null)
            {
                return;
            }

            int oldNetworkId = effect.NetworkId;
            if (oldNetworkId == networkId)
            {
                if (networkId != 0)
                {
                    _effectByNetworkId[networkId] = effect;
                }
                return;
            }

            if (oldNetworkId != 0 &&
                _effectByNetworkId.TryGetValue(oldNetworkId, out var indexed) &&
                ReferenceEquals(indexed, effect))
            {
                _effectByNetworkId.Remove(oldNetworkId);
            }

            effect.NetworkId = networkId;
            if (networkId != 0)
            {
                _effectByNetworkId[networkId] = effect;
            }
        }

        public bool TryGetStackingEffect(GameplayEffectSpec spec, out ActiveGameplayEffect effect)
        {
            effect = null;
            if (spec == null || spec.Def == null || spec.Def.Stacking.Type == EGameplayEffectStackingType.None)
            {
                return false;
            }

            if (spec.Def.Stacking.Type == EGameplayEffectStackingType.AggregateByTarget)
            {
                _stackingByTarget.TryGetValue(spec.Def, out effect);
            }
            else if (spec.Def.Stacking.Type == EGameplayEffectStackingType.AggregateBySource)
            {
                _stackingBySource.TryGetValue((spec.Def, spec.Source), out effect);
            }

            if (effect == null)
            {
                return false;
            }

            if (effect.IsExpired || !_indexByEffect.ContainsKey(effect))
            {
                RemoveFromStackingIndex(effect);
                effect = null;
                return false;
            }

            return true;
        }

        public bool TrackStackingEffect(ActiveGameplayEffect effect)
        {
            var def = effect?.Spec?.Def;
            if (def == null || def.Stacking.Type == EGameplayEffectStackingType.None)
            {
                return false;
            }

            if (def.Stacking.Type == EGameplayEffectStackingType.AggregateByTarget)
            {
                _stackingByTarget[def] = effect;
                return true;
            }

            if (def.Stacking.Type == EGameplayEffectStackingType.AggregateBySource)
            {
                _stackingBySource[(def, effect.Spec.Source)] = effect;
                return true;
            }

            return false;
        }

        public bool RemoveFromStackingIndex(ActiveGameplayEffect effect)
        {
            var def = effect?.Spec?.Def;
            if (def == null)
            {
                return false;
            }

            if (def.Stacking.Type == EGameplayEffectStackingType.AggregateByTarget)
            {
                if (_stackingByTarget.TryGetValue(def, out var indexed) && ReferenceEquals(indexed, effect))
                {
                    _stackingByTarget.Remove(def);
                    return true;
                }
            }
            else if (def.Stacking.Type == EGameplayEffectStackingType.AggregateBySource)
            {
                var key = (def, effect.Spec.Source);
                if (_stackingBySource.TryGetValue(key, out var indexed) && ReferenceEquals(indexed, effect))
                {
                    _stackingBySource.Remove(key);
                    return true;
                }
            }

            return false;
        }

        public void TrackGrantedTags(ActiveGameplayEffect effect)
        {
            if (effect == null || effect.Spec == null)
            {
                return;
            }

            if (!effect.Spec.Def.GrantedTags.IsEmpty)
            {
                var en = effect.Spec.Def.GrantedTags.GetExplicitTags();
                while (en.MoveNext())
                {
                    AddGrantedTagIndexEntry(en.Current.RuntimeIndex, effect);
                }
            }

            if (!effect.Spec.DynamicGrantedTags.IsEmpty)
            {
                var en = effect.Spec.DynamicGrantedTags.GetExplicitTags();
                while (en.MoveNext())
                {
                    AddGrantedTagIndexEntry(en.Current.RuntimeIndex, effect);
                }
            }
        }

        public void UntrackGrantedTags(ActiveGameplayEffect effect)
        {
            if (effect == null || effect.Spec == null)
            {
                return;
            }

            if (!effect.Spec.Def.GrantedTags.IsEmpty)
            {
                var en = effect.Spec.Def.GrantedTags.GetExplicitTags();
                while (en.MoveNext())
                {
                    RemoveGrantedTagIndexEntry(en.Current.RuntimeIndex, effect);
                }
            }

            if (!effect.Spec.DynamicGrantedTags.IsEmpty)
            {
                var en = effect.Spec.DynamicGrantedTags.GetExplicitTags();
                while (en.MoveNext())
                {
                    RemoveGrantedTagIndexEntry(en.Current.RuntimeIndex, effect);
                }
            }
        }

        public bool TryGetGrantedTagEffects(GameplayTag tag, out List<ActiveGameplayEffect> effects)
        {
            effects = null;
            return tag.IsValid && !tag.IsNone && _effectsByGrantedTagIndex.TryGetValue(tag.RuntimeIndex, out effects);
        }

        public bool TrackAbilityAppliedEffect(GameplayAbility ability, ActiveGameplayEffect effect, System.Func<List<ActiveGameplayEffect>> rentList)
        {
            if (ability == null || effect == null)
            {
                return false;
            }

            if (!_effectsByAbility.TryGetValue(ability, out var effects))
            {
                effects = rentList != null ? rentList() : new List<ActiveGameplayEffect>(4);
                _effectsByAbility[ability] = effects;
            }

            effects.Add(effect);
            return true;
        }

        public bool TryGetAbilityAppliedEffects(GameplayAbility ability, out List<ActiveGameplayEffect> effects)
        {
            if (ability == null)
            {
                effects = null;
                return false;
            }

            return _effectsByAbility.TryGetValue(ability, out effects);
        }

        public bool UntrackAbilityAppliedEffectsForAbility(GameplayAbility ability, System.Action<List<ActiveGameplayEffect>> returnList)
        {
            if (ability == null || !_effectsByAbility.TryGetValue(ability, out var effects))
            {
                return false;
            }

            _effectsByAbility.Remove(ability);
            returnList?.Invoke(effects);
            return true;
        }

        public bool UntrackAppliedEffectFromAbilities(ActiveGameplayEffect effect, System.Action<List<ActiveGameplayEffect>> returnList)
        {
            if (effect == null || _effectsByAbility.Count == 0)
            {
                return false;
            }

            bool removedAny = false;
            _emptyAbilityEffectListOwners.Clear();
            foreach (var kvp in _effectsByAbility)
            {
                var effects = kvp.Value;
                if (effects == null || !RemoveEffectReference(effects, effect))
                {
                    continue;
                }

                removedAny = true;
                if (effects.Count == 0)
                {
                    _emptyAbilityEffectListOwners.Add(kvp.Key);
                }
            }

            for (int i = 0; i < _emptyAbilityEffectListOwners.Count; i++)
            {
                var ability = _emptyAbilityEffectListOwners[i];
                if (_effectsByAbility.TryGetValue(ability, out var effects))
                {
                    _effectsByAbility.Remove(ability);
                    returnList?.Invoke(effects);
                }
            }

            _emptyAbilityEffectListOwners.Clear();
            return removedAny;
        }

        public void ReturnAllAbilityAppliedEffectLists(System.Action<List<ActiveGameplayEffect>> returnList)
        {
            foreach (var kvp in _effectsByAbility)
            {
                returnList?.Invoke(kvp.Value);
            }

            _effectsByAbility.Clear();
        }

        public bool ValidateIndexes()
        {
            if (_indexByEffect.Count != _activeEffects.Count ||
                _effectByNetworkId.Count > _activeEffects.Count ||
                _effectsByAbility.Count > _activeEffects.Count)
            {
                return false;
            }

            for (int i = 0; i < _activeEffects.Count; i++)
            {
                var effect = _activeEffects[i];
                if (effect == null ||
                    !_indexByEffect.TryGetValue(effect, out int index) ||
                    index != i)
                {
                    return false;
                }

                if (effect.NetworkId != 0 &&
                    (!_effectByNetworkId.TryGetValue(effect.NetworkId, out var indexedByNetworkId) ||
                     !ReferenceEquals(indexedByNetworkId, effect)))
                {
                    return false;
                }
            }

            foreach (var kvp in _stackingByTarget)
            {
                if (kvp.Key == null || kvp.Value == null || !_indexByEffect.ContainsKey(kvp.Value))
                {
                    return false;
                }
            }

            foreach (var kvp in _stackingBySource)
            {
                if (kvp.Key.Item1 == null || kvp.Value == null || !_indexByEffect.ContainsKey(kvp.Value))
                {
                    return false;
                }
            }

            foreach (var kvp in _effectsByGrantedTagIndex)
            {
                if (kvp.Value == null)
                {
                    return false;
                }

                var effects = kvp.Value;
                for (int i = 0; i < effects.Count; i++)
                {
                    var effect = effects[i];
                    if (effect == null || !_indexByEffect.ContainsKey(effect))
                    {
                        return false;
                    }
                }
            }

            foreach (var kvp in _effectsByAbility)
            {
                if (kvp.Key == null || kvp.Value == null)
                {
                    return false;
                }

                var effects = kvp.Value;
                for (int i = 0; i < effects.Count; i++)
                {
                    var effect = effects[i];
                    if (effect == null || !_indexByEffect.ContainsKey(effect))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public void ClearIndexes()
        {
            _indexByEffect.Clear();
            _effectByNetworkId.Clear();
            _stackingByTarget.Clear();
            _stackingBySource.Clear();
            _effectsByGrantedTagIndex.Clear();
        }

        private static bool RemoveEffectReference(List<ActiveGameplayEffect> effects, ActiveGameplayEffect effect)
        {
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(effects[i], effect))
                {
                    continue;
                }

                int lastIndex = effects.Count - 1;
                if (i != lastIndex)
                {
                    effects[i] = effects[lastIndex];
                }

                effects.RemoveAt(lastIndex);
                return true;
            }

            return false;
        }

        private void AddGrantedTagIndexEntry(int runtimeIndex, ActiveGameplayEffect effect)
        {
            if (!_effectsByGrantedTagIndex.TryGetValue(runtimeIndex, out var effects))
            {
                effects = new List<ActiveGameplayEffect>(2);
                _effectsByGrantedTagIndex.Add(runtimeIndex, effects);
            }

            if (!effects.Contains(effect))
            {
                effects.Add(effect);
            }
        }

        private void RemoveGrantedTagIndexEntry(int runtimeIndex, ActiveGameplayEffect effect)
        {
            if (!_effectsByGrantedTagIndex.TryGetValue(runtimeIndex, out var effects))
            {
                return;
            }

            effects.Remove(effect);
            if (effects.Count == 0)
            {
                _effectsByGrantedTagIndex.Remove(runtimeIndex);
            }
        }
    }
}
