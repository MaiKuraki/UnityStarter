using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Owns granted ability specs, ticking specs, and ability specs granted by active effects.
    /// This mirrors Unreal GAS' dedicated ability spec container while keeping Unity runtime code allocation-aware.
    /// </summary>
    public sealed class AbilitySpecContainer
    {
        private readonly List<GameplayAbilitySpec> _activatableAbilities;
        private readonly Dictionary<int, GameplayAbilitySpec> _specByHandle;
        private readonly Dictionary<GameplayAbilitySpec, int> _indexBySpec;
        private readonly Dictionary<ActiveGameplayEffect, List<GameplayAbilitySpec>> _specsByGrantingEffect;
        private readonly Stack<List<GameplayAbilitySpec>> _grantedSpecListPool;
        private readonly List<GameplayAbilitySpec> _tickingAbilities;
        private readonly Dictionary<GameplayAbilitySpec, int> _tickingIndexBySpec;

        public AbilitySpecContainer(
            int abilityCapacity = 16,
            int grantingEffectCapacity = 8,
            int pooledListCapacity = 4)
        {
            _activatableAbilities = new List<GameplayAbilitySpec>(abilityCapacity);
            _specByHandle = new Dictionary<int, GameplayAbilitySpec>(abilityCapacity);
            _indexBySpec = new Dictionary<GameplayAbilitySpec, int>(abilityCapacity);
            _specsByGrantingEffect = new Dictionary<ActiveGameplayEffect, List<GameplayAbilitySpec>>(grantingEffectCapacity);
            _grantedSpecListPool = new Stack<List<GameplayAbilitySpec>>(pooledListCapacity);
            _tickingAbilities = new List<GameplayAbilitySpec>(abilityCapacity);
            _tickingIndexBySpec = new Dictionary<GameplayAbilitySpec, int>(abilityCapacity);
        }

        public IReadOnlyList<GameplayAbilitySpec> ActivatableAbilities => _activatableAbilities;
        public IReadOnlyList<GameplayAbilitySpec> TickingAbilities => _tickingAbilities;
        public int Count => _activatableAbilities.Count;
        public int TickingCount => _tickingAbilities.Count;
        public int SpecByHandleCount => _specByHandle.Count;
        public int IndexBySpecCount => _indexBySpec.Count;
        public int SpecsByGrantingEffectCount => _specsByGrantingEffect.Count;
        public int GrantedSpecListPoolSize => _grantedSpecListPool.Count;

        internal List<GameplayAbilitySpec> MutableActivatableAbilities => _activatableAbilities;
        internal Dictionary<int, GameplayAbilitySpec> MutableSpecByHandle => _specByHandle;
        internal Dictionary<GameplayAbilitySpec, int> MutableIndexBySpec => _indexBySpec;
        internal Dictionary<ActiveGameplayEffect, List<GameplayAbilitySpec>> MutableSpecsByGrantingEffect => _specsByGrantingEffect;
        internal Stack<List<GameplayAbilitySpec>> MutableGrantedSpecListPool => _grantedSpecListPool;
        internal List<GameplayAbilitySpec> MutableTickingAbilities => _tickingAbilities;
        internal Dictionary<GameplayAbilitySpec, int> MutableTickingIndexBySpec => _tickingIndexBySpec;

        public void Reserve(int abilityCapacity, int grantingEffectCapacity)
        {
            if (abilityCapacity > _activatableAbilities.Capacity)
            {
                _activatableAbilities.Capacity = abilityCapacity;
            }

            if (abilityCapacity > _tickingAbilities.Capacity)
            {
                _tickingAbilities.Capacity = abilityCapacity;
            }

            if (abilityCapacity > 0)
            {
                _specByHandle.EnsureCapacity(abilityCapacity);
                _indexBySpec.EnsureCapacity(abilityCapacity);
                _tickingIndexBySpec.EnsureCapacity(abilityCapacity);
            }

            if (grantingEffectCapacity > 0)
            {
                _specsByGrantingEffect.EnsureCapacity(grantingEffectCapacity);
            }
        }

        public bool TryGetSpecByHandle(int handle, out GameplayAbilitySpec spec)
        {
            return _specByHandle.TryGetValue(handle, out spec);
        }

        public bool AddSpec(GameplayAbilitySpec spec)
        {
            if (spec == null || _indexBySpec.ContainsKey(spec))
            {
                return false;
            }

            _indexBySpec[spec] = _activatableAbilities.Count;
            _activatableAbilities.Add(spec);
            _specByHandle[spec.Handle] = spec;
            return true;
        }

        public bool RemoveSpec(GameplayAbilitySpec spec)
        {
            if (spec == null)
            {
                return false;
            }

            if (!_indexBySpec.TryGetValue(spec, out int index) ||
                index < 0 ||
                index >= _activatableAbilities.Count ||
                !ReferenceEquals(_activatableAbilities[index], spec))
            {
                _specByHandle.Remove(spec.Handle);
                _indexBySpec.Remove(spec);
                return false;
            }

            int lastIndex = _activatableAbilities.Count - 1;
            if (index != lastIndex)
            {
                var movedSpec = _activatableAbilities[lastIndex];
                _activatableAbilities[index] = movedSpec;
                _indexBySpec[movedSpec] = index;
            }

            _activatableAbilities.RemoveAt(lastIndex);
            _specByHandle.Remove(spec.Handle);
            _indexBySpec.Remove(spec);
            return true;
        }

        public bool AddTickingSpec(GameplayAbilitySpec spec)
        {
            if (spec == null || _tickingIndexBySpec.ContainsKey(spec))
            {
                return false;
            }

            _tickingIndexBySpec[spec] = _tickingAbilities.Count;
            _tickingAbilities.Add(spec);
            return true;
        }

        public bool RemoveTickingSpec(GameplayAbilitySpec spec)
        {
            if (spec == null ||
                !_tickingIndexBySpec.TryGetValue(spec, out int index) ||
                index < 0 ||
                index >= _tickingAbilities.Count ||
                !ReferenceEquals(_tickingAbilities[index], spec))
            {
                return false;
            }

            int lastIndex = _tickingAbilities.Count - 1;
            if (index != lastIndex)
            {
                var movedSpec = _tickingAbilities[lastIndex];
                _tickingAbilities[index] = movedSpec;
                _tickingIndexBySpec[movedSpec] = index;
            }

            _tickingAbilities.RemoveAt(lastIndex);
            _tickingIndexBySpec.Remove(spec);
            return true;
        }

        public bool RegisterGrantedByEffect(ActiveGameplayEffect effect, GameplayAbilitySpec spec, Func<List<GameplayAbilitySpec>> rentList)
        {
            if (effect == null || spec == null)
            {
                return false;
            }

            spec.GrantingEffect = effect;
            if (!_specsByGrantingEffect.TryGetValue(effect, out var specs))
            {
                specs = rentList != null ? rentList() : new List<GameplayAbilitySpec>(4);
                _specsByGrantingEffect[effect] = specs;
            }

            specs.Add(spec);
            return true;
        }

        public bool UnregisterGrantedByEffect(GameplayAbilitySpec spec, Action<List<GameplayAbilitySpec>> returnList)
        {
            var effect = spec?.GrantingEffect;
            if (effect == null)
            {
                return false;
            }

            bool removed = false;
            if (_specsByGrantingEffect.TryGetValue(effect, out var specs))
            {
                for (int i = specs.Count - 1; i >= 0; i--)
                {
                    if (!ReferenceEquals(specs[i], spec))
                    {
                        continue;
                    }

                    int lastIndex = specs.Count - 1;
                    if (i != lastIndex)
                    {
                        specs[i] = specs[lastIndex];
                    }

                    specs.RemoveAt(lastIndex);
                    removed = true;
                    break;
                }

                if (specs.Count == 0)
                {
                    _specsByGrantingEffect.Remove(effect);
                    returnList?.Invoke(specs);
                }
            }

            spec.GrantingEffect = null;
            return removed;
        }

        public bool TryGetGrantedSpecs(ActiveGameplayEffect effect, out List<GameplayAbilitySpec> specs)
        {
            if (effect == null)
            {
                specs = null;
                return false;
            }

            return _specsByGrantingEffect.TryGetValue(effect, out specs);
        }

        public void ReturnAllGrantedSpecLists(Action<List<GameplayAbilitySpec>> returnList)
        {
            foreach (var kvp in _specsByGrantingEffect)
            {
                returnList?.Invoke(kvp.Value);
            }

            _specsByGrantingEffect.Clear();
        }

        public bool ValidateIndexes()
        {
            if (_specByHandle.Count != _activatableAbilities.Count ||
                _indexBySpec.Count != _activatableAbilities.Count ||
                _tickingIndexBySpec.Count != _tickingAbilities.Count)
            {
                return false;
            }

            for (int i = 0; i < _activatableAbilities.Count; i++)
            {
                var spec = _activatableAbilities[i];
                if (spec == null ||
                    !_specByHandle.TryGetValue(spec.Handle, out var indexedSpec) ||
                    !ReferenceEquals(indexedSpec, spec) ||
                    !_indexBySpec.TryGetValue(spec, out int index) ||
                    index != i ||
                    (spec.GrantingEffect != null && !ValidateGrantedAbilityEffectIndex(spec)))
                {
                    return false;
                }
            }

            for (int i = 0; i < _tickingAbilities.Count; i++)
            {
                var spec = _tickingAbilities[i];
                if (spec == null ||
                    !_tickingIndexBySpec.TryGetValue(spec, out int index) ||
                    index != i)
                {
                    return false;
                }
            }

            foreach (var kvp in _specsByGrantingEffect)
            {
                if (kvp.Key == null || kvp.Value == null)
                {
                    return false;
                }

                var specs = kvp.Value;
                for (int i = 0; i < specs.Count; i++)
                {
                    var spec = specs[i];
                    if (spec == null ||
                        !ReferenceEquals(spec.GrantingEffect, kvp.Key) ||
                        !_indexBySpec.ContainsKey(spec))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public void ClearIndexes()
        {
            _specByHandle.Clear();
            _indexBySpec.Clear();
            _tickingIndexBySpec.Clear();
        }

        private bool ValidateGrantedAbilityEffectIndex(GameplayAbilitySpec spec)
        {
            if (spec == null || spec.GrantingEffect == null)
            {
                return true;
            }

            if (!_specsByGrantingEffect.TryGetValue(spec.GrantingEffect, out var specs))
            {
                return false;
            }

            for (int i = 0; i < specs.Count; i++)
            {
                if (ReferenceEquals(specs[i], spec))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
