using System;

namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public sealed class ProjectileWorld
    {
        private const int NullDenseIndex = -1;
        private const float SURFACE_OFFSET = 0.001f;

        private readonly ProjectileSpaceProfile _space;
        private readonly ProjectileState[] _states;
        private readonly ProjectileDefinition[] _definitions;
        private readonly int[] _slotGenerations;
        private readonly int[] _slotToDense;
        private readonly int[] _denseToSlot;
        private readonly int[] _freeSlots;
        private readonly ProjectileCollisionHit[] _collisionHits;
        private readonly int _maxCollisionIterations;

        private int _activeCount;
        private int _slotHighWaterMark;
        private int _freeCount;
        private int _peakActiveCount;
        private int _stepCount;
        private int _lastTick;
        private float _lastDeltaTime;
        private int _lastHitEventCount;
        private int _lastCollisionQueryCount;
        private int _lastCollisionHitCount;
        private int _totalSpawnAcceptedCount;
        private int _totalSpawnRejectedInvalidCount;
        private int _totalSpawnRejectedCapacityCount;
        private int _totalDespawnCount;
        private int _totalHitEventCount;
        private int _totalHitEventOverflowCount;
        private int _totalCollisionIterationLimitCount;

        public ProjectileWorld(
            int capacity,
            int eventCapacity,
            int collisionHitCapacity,
            in ProjectileSpaceProfile space,
            int maxCollisionIterations = 4)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (collisionHitCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(collisionHitCapacity));
            }

            _space = space;
            _states = new ProjectileState[capacity];
            _definitions = new ProjectileDefinition[capacity];
            _slotGenerations = new int[capacity];
            _slotToDense = new int[capacity];
            _denseToSlot = new int[capacity];
            _freeSlots = new int[capacity];
            _collisionHits = new ProjectileCollisionHit[collisionHitCapacity];
            _maxCollisionIterations = Math.Max(1, maxCollisionIterations);
            HitEvents = new ProjectileEventBuffer(eventCapacity);

            for (int i = 0; i < capacity; i++)
            {
                _slotToDense[i] = NullDenseIndex;
                _denseToSlot[i] = NullDenseIndex;
            }
        }

        public int Capacity
        {
            get
            {
                return _states.Length;
            }
        }

        public int ActiveCount
        {
            get
            {
                return _activeCount;
            }
        }

        public ProjectileEventBuffer HitEvents { get; }

        public ProjectileWorldStats Stats
        {
            get
            {
                return new ProjectileWorldStats(
                    Capacity,
                    _activeCount,
                    _peakActiveCount,
                    _stepCount,
                    _lastTick,
                    _lastDeltaTime,
                    _lastHitEventCount,
                    _lastCollisionQueryCount,
                    _lastCollisionHitCount,
                    _totalSpawnAcceptedCount,
                    _totalSpawnRejectedInvalidCount,
                    _totalSpawnRejectedCapacityCount,
                    _totalDespawnCount,
                    _totalHitEventCount,
                    _totalHitEventOverflowCount,
                    _totalCollisionIterationLimitCount);
            }
        }

        public bool TrySpawn(in ProjectileSpawnRequest request, out ProjectileHandle handle)
        {
            handle = default;
            if (!request.IsValid)
            {
                _totalSpawnRejectedInvalidCount++;
                return false;
            }

            if (_activeCount >= _states.Length)
            {
                _totalSpawnRejectedCapacityCount++;
                return false;
            }

            int slot = AllocateSlot();
            if (slot < 0)
            {
                _totalSpawnRejectedCapacityCount++;
                return false;
            }

            int generation = _slotGenerations[slot];
            if (generation <= 0)
            {
                generation = 1;
                _slotGenerations[slot] = generation;
            }

            int denseIndex = _activeCount;
            _activeCount++;

            handle = new ProjectileHandle(slot, generation);
            _slotToDense[slot] = denseIndex;
            _denseToSlot[denseIndex] = slot;
            _definitions[denseIndex] = request.Definition;
            _states[denseIndex] = ProjectileState.Create(handle, in request, in _space);
            _totalSpawnAcceptedCount++;
            if (_activeCount > _peakActiveCount)
            {
                _peakActiveCount = _activeCount;
            }

            return true;
        }

        public bool Despawn(ProjectileHandle handle)
        {
            if (!TryGetDenseIndex(handle, out int denseIndex))
            {
                return false;
            }

            DespawnDense(denseIndex);
            return true;
        }

        public bool TryGetState(ProjectileHandle handle, out ProjectileState state)
        {
            if (!TryGetDenseIndex(handle, out int denseIndex))
            {
                state = default;
                return false;
            }

            state = _states[denseIndex];
            return true;
        }

        public bool TryGetSnapshot(ProjectileHandle handle, out ProjectileSnapshot snapshot)
        {
            if (!TryGetState(handle, out ProjectileState state))
            {
                snapshot = default;
                return false;
            }

            snapshot = ProjectileSnapshot.FromState(in state);
            return true;
        }

        public bool TryGetSnapshotByNetworkEntityId(
            ulong networkEntityId,
            out ProjectileSnapshot snapshot)
        {
            if (!TryGetStateByNetworkEntityId(networkEntityId, out ProjectileState state))
            {
                snapshot = default;
                return false;
            }

            snapshot = ProjectileSnapshot.FromState(in state);
            return true;
        }

        public bool TryGetStateByNetworkEntityId(
            ulong networkEntityId,
            out ProjectileState state)
        {
            if (networkEntityId == 0UL)
            {
                state = default;
                return false;
            }

            for (int i = 0; i < _activeCount; i++)
            {
                if (_states[i].NetworkEntityId != networkEntityId)
                {
                    continue;
                }

                state = _states[i];
                return true;
            }

            state = default;
            return false;
        }

        public void Step(
            float deltaTime,
            int tick,
            IProjectileCollisionWorld collisionWorld = null,
            IProjectileTargetProvider targetProvider = null)
        {
            if (deltaTime < 0f || !IsFinite(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            HitEvents.Clear();
            _stepCount++;
            _lastTick = tick;
            _lastDeltaTime = deltaTime;
            _lastHitEventCount = 0;
            _lastCollisionQueryCount = 0;
            _lastCollisionHitCount = 0;

            int denseIndex = 0;
            while (denseIndex < _activeCount)
            {
                ProjectileState state = _states[denseIndex];
                ProjectileDefinition definition = _definitions[denseIndex];

                bool hasTarget = TryGetTarget(
                    targetProvider,
                    state.TargetEntityId,
                    out ProjectileVector3 targetPosition,
                    out ProjectileVector3 targetVelocity);

                ProjectileSimulator.Step(
                    ref state,
                    in definition,
                    in _space,
                    deltaTime,
                    tick,
                    hasTarget,
                    targetPosition,
                    targetVelocity);

                bool despawn = state.Age >= definition.MaxLifetime;
                if (!despawn && collisionWorld != null && definition.CollisionLayerMask != 0)
                {
                    despawn = ResolveCollision(
                        ref state,
                        in definition,
                        collisionWorld);
                }

                if (despawn)
                {
                    DespawnDense(denseIndex);
                    continue;
                }

                _states[denseIndex] = state;
                denseIndex++;
            }
        }

        public void Clear()
        {
            Array.Clear(_states, 0, _states.Length);
            Array.Clear(_definitions, 0, _definitions.Length);

            for (int i = 0; i < _slotToDense.Length; i++)
            {
                _slotToDense[i] = NullDenseIndex;
                _denseToSlot[i] = NullDenseIndex;
            }

            _activeCount = 0;
            _slotHighWaterMark = 0;
            _freeCount = 0;
            ResetStats();
            HitEvents.Clear();
        }

        public void ResetStats()
        {
            _peakActiveCount = _activeCount;
            _stepCount = 0;
            _lastTick = 0;
            _lastDeltaTime = 0f;
            _lastHitEventCount = 0;
            _lastCollisionQueryCount = 0;
            _lastCollisionHitCount = 0;
            _totalSpawnAcceptedCount = 0;
            _totalSpawnRejectedInvalidCount = 0;
            _totalSpawnRejectedCapacityCount = 0;
            _totalDespawnCount = 0;
            _totalHitEventCount = 0;
            _totalHitEventOverflowCount = 0;
            _totalCollisionIterationLimitCount = 0;
        }

        private bool ResolveCollision(
            ref ProjectileState state,
            in ProjectileDefinition definition,
            IProjectileCollisionWorld collisionWorld)
        {
            ProjectileVector3 sweepFrom = state.PreviousPosition;
            ProjectileVector3 sweepTo = state.Position;

            for (int iteration = 0; iteration < _maxCollisionIterations; iteration++)
            {
                var query = new ProjectileCollisionQuery(
                    state.Handle,
                    state.OwnerEntityId,
                    state.NetworkEntityId,
                    definition.CollisionLayerMask,
                    state.Radius,
                    sweepFrom,
                    sweepTo);

                _lastCollisionQueryCount++;
                int count = collisionWorld.Cast(
                    in query,
                    _collisionHits,
                    _collisionHits.Length);
                if (count > 0)
                {
                    _lastCollisionHitCount += Math.Min(count, _collisionHits.Length);
                }

                if (count <= 0)
                {
                    return false;
                }

                ProjectileCollisionHit hit = SelectNearestHit(count);
                if (!hit.IsValid)
                {
                    return false;
                }

                bool canBounce = state.RemainingBounceCount > 0;
                bool canPierce = state.RemainingPierceCount > 0;
                bool terminal = ShouldDespawnOnHit(in definition) && !canBounce && !canPierce;
                if (HitEvents.TryAdd(new ProjectileHitEvent(
                    state.Handle,
                    state.NetworkEntityId,
                    state.OwnerEntityId,
                    hit.TargetEntityId,
                    hit.TargetObjectId,
                    state.DefinitionId,
                    definition.EffectPayloadId,
                    state.CurrentTick,
                    state.PredictionKey,
                    terminal,
                    hit.Position,
                    hit.Normal,
                    state.Velocity)))
                {
                    _lastHitEventCount++;
                    _totalHitEventCount++;
                }
                else
                {
                    _totalHitEventOverflowCount++;
                }

                if (terminal)
                {
                    state.Position = hit.Position;
                    return true;
                }

                if (canBounce)
                {
                    state.RemainingBounceCount--;
                    if (!TryContinueBounce(ref state, sweepFrom, sweepTo, in hit, out sweepFrom, out sweepTo))
                    {
                        return false;
                    }

                    continue;
                }

                if (canPierce)
                {
                    state.RemainingPierceCount--;
                    if (!TryContinuePierce(ref state, sweepFrom, sweepTo, in hit, out sweepFrom, out sweepTo))
                    {
                        return false;
                    }

                    continue;
                }

                return false;
            }

            _totalCollisionIterationLimitCount++;
            return false;
        }

        private static bool ShouldDespawnOnHit(in ProjectileDefinition definition)
        {
            return (definition.LifecycleFlags & ProjectileLifecycleFlags.DespawnOnHit) != ProjectileLifecycleFlags.None;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool TryContinueBounce(
            ref ProjectileState state,
            ProjectileVector3 sweepFrom,
            ProjectileVector3 sweepTo,
            in ProjectileCollisionHit hit,
            out ProjectileVector3 nextFrom,
            out ProjectileVector3 nextTo)
        {
            float remainingDistance = GetRemainingSweepDistance(sweepFrom, sweepTo, in hit);
            ProjectileVector3 normal = hit.Normal.NormalizedOrZero();
            ProjectileVector3 reflectedVelocity = ProjectileVector3.Reflect(state.Velocity, normal);
            ProjectileVector3 reflectedDirection = reflectedVelocity.NormalizedOrZero();

            state.Velocity = reflectedVelocity;
            state.Position = hit.Position + normal * SURFACE_OFFSET;
            nextFrom = state.Position;
            if (remainingDistance <= SURFACE_OFFSET || reflectedDirection.LengthSquared <= 0.000001f)
            {
                nextTo = state.Position;
                return false;
            }

            nextTo = state.Position + reflectedDirection * remainingDistance;
            state.Position = nextTo;
            return true;
        }

        private static bool TryContinuePierce(
            ref ProjectileState state,
            ProjectileVector3 sweepFrom,
            ProjectileVector3 sweepTo,
            in ProjectileCollisionHit hit,
            out ProjectileVector3 nextFrom,
            out ProjectileVector3 nextTo)
        {
            float remainingDistance = GetRemainingSweepDistance(sweepFrom, sweepTo, in hit);
            ProjectileVector3 direction = (sweepTo - sweepFrom).NormalizedOrFallback(state.Velocity.NormalizedOrZero());

            state.Position = hit.Position + direction * SURFACE_OFFSET;
            nextFrom = state.Position;
            if (remainingDistance <= SURFACE_OFFSET || direction.LengthSquared <= 0.000001f)
            {
                nextTo = state.Position;
                return false;
            }

            nextTo = state.Position + direction * remainingDistance;
            state.Position = nextTo;
            return true;
        }

        private static float GetRemainingSweepDistance(
            ProjectileVector3 sweepFrom,
            ProjectileVector3 sweepTo,
            in ProjectileCollisionHit hit)
        {
            float totalDistance = (sweepTo - sweepFrom).Length;
            float hitDistance = hit.Distance < 0f ? 0f : hit.Distance;
            float remainingDistance = totalDistance - hitDistance;
            return remainingDistance > 0f ? remainingDistance : 0f;
        }

        private ProjectileCollisionHit SelectNearestHit(int count)
        {
            ProjectileCollisionHit best = default;
            float bestDistance = float.PositiveInfinity;
            int max = Math.Min(count, _collisionHits.Length);
            for (int i = 0; i < max; i++)
            {
                ProjectileCollisionHit hit = _collisionHits[i];
                if (!hit.IsValid || hit.Distance >= bestDistance)
                {
                    continue;
                }

                best = hit;
                bestDistance = hit.Distance;
            }

            return best;
        }

        private static bool TryGetTarget(
            IProjectileTargetProvider targetProvider,
            ulong targetEntityId,
            out ProjectileVector3 targetPosition,
            out ProjectileVector3 targetVelocity)
        {
            targetPosition = default;
            targetVelocity = default;

            if (targetProvider == null || targetEntityId == 0UL)
            {
                return false;
            }

            if (!targetProvider.TryGetTargetPosition(targetEntityId, out targetPosition))
            {
                return false;
            }

            targetProvider.TryGetTargetVelocity(targetEntityId, out targetVelocity);
            return true;
        }

        private int AllocateSlot()
        {
            if (_freeCount > 0)
            {
                _freeCount--;
                return _freeSlots[_freeCount];
            }

            if (_slotHighWaterMark >= _states.Length)
            {
                return -1;
            }

            int slot = _slotHighWaterMark;
            _slotHighWaterMark++;
            return slot;
        }

        private bool TryGetDenseIndex(ProjectileHandle handle, out int denseIndex)
        {
            denseIndex = default;
            if (!handle.IsValid || (uint)handle.Slot >= (uint)_slotToDense.Length)
            {
                return false;
            }

            if (_slotGenerations[handle.Slot] != handle.Generation)
            {
                return false;
            }

            denseIndex = _slotToDense[handle.Slot];
            return denseIndex >= 0 && denseIndex < _activeCount;
        }

        private void DespawnDense(int denseIndex)
        {
            int slot = _denseToSlot[denseIndex];
            int lastDenseIndex = _activeCount - 1;

            if (denseIndex != lastDenseIndex)
            {
                _states[denseIndex] = _states[lastDenseIndex];
                _definitions[denseIndex] = _definitions[lastDenseIndex];

                int movedSlot = _denseToSlot[lastDenseIndex];
                _denseToSlot[denseIndex] = movedSlot;
                _slotToDense[movedSlot] = denseIndex;
            }

            _states[lastDenseIndex] = default;
            _definitions[lastDenseIndex] = default;
            _denseToSlot[lastDenseIndex] = NullDenseIndex;
            _slotToDense[slot] = NullDenseIndex;
            _activeCount--;

            _slotGenerations[slot]++;
            if (_slotGenerations[slot] <= 0)
            {
                _slotGenerations[slot] = 1;
            }

            _freeSlots[_freeCount] = slot;
            _freeCount++;
            _totalDespawnCount++;
        }
    }
}
