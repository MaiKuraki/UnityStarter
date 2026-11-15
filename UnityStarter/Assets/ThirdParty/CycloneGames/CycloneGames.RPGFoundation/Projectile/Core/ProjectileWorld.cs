using System;

namespace CycloneGames.RPGFoundation.Projectile.Core
{
    /// <summary>
    /// Immutable construction contract for <see cref="ProjectileWorld"/>. All storage is
    /// allocated once at construction; no parameter can trigger later growth.
    /// </summary>
    public readonly struct ProjectileWorldConfig
    {
        // Bounds keep an authored or remote configuration from turning one frame
        // into an unbounded loop. They are deliberately generous for real workloads.
        private const int MaxCollisionIterationsLimit = 64;
        private const int MaxSubStepsLimit = 16;
        private const float MaxDeltaTimeLimit = 1f;

        public readonly int Capacity;
        public readonly int EventCapacity;
        public readonly int CollisionHitCapacity;
        public readonly int MaxCollisionIterations;
        public readonly float MaxDeltaTime;
        public readonly int MaxSubSteps;
        public readonly ProjectileSpaceProfile Space;

        public ProjectileWorldConfig(
            int capacity,
            int eventCapacity,
            int collisionHitCapacity,
            in ProjectileSpaceProfile space,
            int maxCollisionIterations = 4,
            float maxDeltaTime = 0f,
            int maxSubSteps = 1)
        {
            Capacity = capacity;
            EventCapacity = eventCapacity;
            CollisionHitCapacity = collisionHitCapacity;
            MaxCollisionIterations = maxCollisionIterations;
            MaxDeltaTime = maxDeltaTime;
            MaxSubSteps = maxSubSteps;
            Space = space;
        }

        /// <summary>
        /// Zero <see cref="MaxDeltaTime"/> disables sub-stepping. A positive value bounds
        /// each integration step; <see cref="MaxSubSteps"/> bounds the work spent on one
        /// <see cref="ProjectileWorld.Step"/> call.
        /// </summary>
        public bool IsValid
        {
            get
            {
                return Capacity > 0
                       && EventCapacity > 0
                       && CollisionHitCapacity > 0
                       && MaxCollisionIterations >= 1
                       && MaxCollisionIterations <= MaxCollisionIterationsLimit
                       && MaxSubSteps >= 1
                       && MaxSubSteps <= MaxSubStepsLimit
                       && !float.IsNaN(MaxDeltaTime)
                       && !float.IsInfinity(MaxDeltaTime)
                       && MaxDeltaTime >= 0f
                       && MaxDeltaTime <= MaxDeltaTimeLimit;
            }
        }
    }

    /// <summary>
    /// Fixed-capacity dense projectile store. Single-writer: one simulation thread owns
    /// all mutable state and feeds commands at a known simulation barrier. Every array
    /// is allocated at construction and never grows; the hot path performs no allocation.
    /// </summary>
    public sealed class ProjectileWorld
    {
        private const int NullDenseIndex = -1;
        private const float SURFACE_OFFSET = 0.001f;

        private readonly ProjectileSpaceProfile _space;
        private readonly ProjectileState[] _states;
        private readonly ProjectileDefinition[] _definitions;
        private readonly ulong[] _slotGenerations;
        private readonly int[] _slotToDense;
        private readonly int[] _denseToSlot;
        private readonly int[] _freeSlots;
        private readonly ProjectileCollisionHit[] _collisionHits;
        private readonly int _maxCollisionIterations;
        private readonly float _maxDeltaTime;
        private readonly int _maxSubSteps;

        private int _activeCount;
        private int _slotHighWaterMark;
        private int _freeCount;
        private int _peakActiveCount;
        private long _stepCount;
        private int _lastTick;
        private float _lastDeltaTime;
        private long _lastHitEventCount;
        private long _lastCollisionQueryCount;
        private long _lastCollisionHitCount;
        private long _totalSpawnAcceptedCount;
        private long _totalSpawnRejectedInvalidCount;
        private long _totalSpawnRejectedCapacityCount;
        private long _totalDespawnCount;
        private long _totalHitEventCount;
        private long _totalHitEventOverflowCount;
        private long _totalCollisionIterationLimitCount;
        private long _totalSubStepCount;
        private long _totalClampedFrameCount;

        public ProjectileWorld(in ProjectileWorldConfig config)
        {
            if (!config.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(config));
            }

            int capacity = config.Capacity;
            _space = config.Space;
            _states = new ProjectileState[capacity];
            _definitions = new ProjectileDefinition[capacity];
            _slotGenerations = new ulong[capacity];
            _slotToDense = new int[capacity];
            _denseToSlot = new int[capacity];
            _freeSlots = new int[capacity];
            _collisionHits = new ProjectileCollisionHit[config.CollisionHitCapacity];
            _maxCollisionIterations = config.MaxCollisionIterations;
            _maxDeltaTime = config.MaxDeltaTime;
            _maxSubSteps = config.MaxSubSteps;
            HitEvents = new ProjectileEventBuffer(config.EventCapacity);

            for (int i = 0; i < capacity; i++)
            {
                _slotToDense[i] = NullDenseIndex;
                _denseToSlot[i] = NullDenseIndex;
            }
        }

        public int Capacity => _states.Length;

        public int ActiveCount => _activeCount;

        public ProjectileEventBuffer HitEvents { get; }

        public ProjectileWorldStats Stats => new ProjectileWorldStats(
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
            _totalCollisionIterationLimitCount,
            _totalSubStepCount,
            _totalClampedFrameCount);

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

            ulong generation = _slotGenerations[slot];
            if (generation == 0UL)
            {
                generation = 1UL;
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

        /// <summary>
        /// Copies every active snapshot in dense order with one sequential pass. The
        /// destination is caller-owned contiguous storage; no allocation and no index
        /// structure is involved. A return value below <see cref="ActiveCount"/> means
        /// the destination was too small and the batch was truncated.
        /// </summary>
        public int CaptureSnapshots(ProjectileSnapshot[] destination, int startIndex = 0)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if ((uint)startIndex > (uint)destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }

            int available = destination.Length - startIndex;
            int count = _activeCount < available ? _activeCount : available;
            for (int i = 0; i < count; i++)
            {
                destination[startIndex + i] = ProjectileSnapshot.FromState(in _states[i]);
            }

            return count;
        }

        /// <summary>
        /// Low-frequency convenience lookup. <see cref="ProjectileSpawnRequest.NetworkEntityId"/>
        /// must be unique per active projectile; duplicates resolve to the first dense
        /// slot. Prefer <see cref="CaptureSnapshots"/> for per-frame authority broadcast.
        /// </summary>
        public bool TryGetStateByNetworkEntityId(ulong networkEntityId, out ProjectileState state)
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

        public bool TryGetSnapshotByNetworkEntityId(ulong networkEntityId, out ProjectileSnapshot snapshot)
        {
            if (!TryGetStateByNetworkEntityId(networkEntityId, out ProjectileState state))
            {
                snapshot = default;
                return false;
            }

            snapshot = ProjectileSnapshot.FromState(in state);
            return true;
        }

        /// <summary>
        /// Advances the world by <paramref name="deltaTime"/>. When a positive
        /// <see cref="ProjectileWorldConfig.MaxDeltaTime"/> is configured, the frame is
        /// split into equal sub-steps no larger than that value. A frame beyond
        /// <see cref="ProjectileWorldConfig.MaxSubSteps"/> is deterministically clamped,
        /// so oversized steps never tunnel through collision geometry and every device
        /// observes the same simulation boundary. Hit events accumulate across sub-steps
        /// and are cleared once per call.
        /// </summary>
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

            int subSteps = ComputeSubSteps(deltaTime);
            float maxFrame = _maxDeltaTime > 0f ? _maxDeltaTime * subSteps : deltaTime;
            float effectiveDelta = deltaTime;
            if (maxFrame > 0f && deltaTime > maxFrame)
            {
                // A frame beyond the catch-up budget is clamped rather than simulated
                // with oversized steps. The clamp is deterministic on every device, so
                // replay and rollback stay aligned even under a stalled CPU.
                effectiveDelta = maxFrame;
                _totalClampedFrameCount++;
            }

            float subDelta = effectiveDelta / subSteps;
            for (int s = 0; s < subSteps; s++)
            {
                StepCore(subDelta, tick, collisionWorld, targetProvider);
            }

            _totalSubStepCount += subSteps;
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

            // Advancing every issued generation keeps outstanding handles invalid
            // after slot reuse. Without this, Clear() produces handle aliasing.
            for (int i = 0; i < _slotHighWaterMark; i++)
            {
                _slotGenerations[i]++;
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
            _totalSubStepCount = 0;
            _totalClampedFrameCount = 0;
        }

        private int ComputeSubSteps(float deltaTime)
        {
            if (_maxDeltaTime <= 0f || _maxSubSteps <= 1 || deltaTime <= _maxDeltaTime)
            {
                return 1;
            }

            double ratio = (double)deltaTime / _maxDeltaTime;
            if (ratio >= _maxSubSteps)
            {
                return _maxSubSteps;
            }

            int needed = (int)Math.Ceiling(ratio);
            return needed < 1 ? 1 : needed;
        }

        private void StepCore(
            float deltaTime,
            int tick,
            IProjectileCollisionWorld collisionWorld,
            IProjectileTargetProvider targetProvider)
        {
            int denseIndex = 0;
            while (denseIndex < _activeCount)
            {
                ref ProjectileState state = ref _states[denseIndex];

                bool hasTarget = TryGetTarget(
                    targetProvider,
                    state.TargetEntityId,
                    out ProjectileVector3 targetPosition,
                    out ProjectileVector3 targetVelocity);

                ProjectileSimulator.Step(
                    ref state,
                    in _definitions[denseIndex],
                    in _space,
                    deltaTime,
                    tick,
                    hasTarget,
                    in targetPosition,
                    in targetVelocity);

                bool despawn = state.Age >= _definitions[denseIndex].MaxLifetime;
                if (!despawn && collisionWorld != null && _definitions[denseIndex].CollisionLayerMask != 0)
                {
                    despawn = ResolveCollision(ref state, in _definitions[denseIndex], collisionWorld);
                }

                if (despawn)
                {
                    DespawnDense(denseIndex);
                    continue;
                }

                denseIndex++;
            }
        }

        private bool ResolveCollision(
            ref ProjectileState state,
            in ProjectileDefinition definition,
            IProjectileCollisionWorld collisionWorld)
        {
            ProjectileVector3 sweepFrom = state.PreviousPosition;
            ProjectileVector3 sweepTo = state.Position;
            ulong lastHitTargetId = 0UL;
            ulong ownerToIgnore = (definition.LifecycleFlags & ProjectileLifecycleFlags.IgnoreOwner)
                                  != ProjectileLifecycleFlags.None
                ? state.OwnerEntityId
                : 0UL;

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
                int count = collisionWorld.Cast(in query, _collisionHits, _collisionHits.Length);
                if (count > 0)
                {
                    _lastCollisionHitCount += Math.Min(count, _collisionHits.Length);
                }

                if (count <= 0)
                {
                    return false;
                }

                ProjectileCollisionHit hit = SelectNearestHit(count, lastHitTargetId, ownerToIgnore);
                if (!hit.IsValid)
                {
                    return false;
                }

                if (hit.TargetEntityId != 0UL)
                {
                    lastHitTargetId = hit.TargetEntityId;
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

        /// <summary>
        /// Picks the nearest valid hit while suppressing the entity resolved on the
        /// previous iteration and the owner when the definition requests it. Entity ids
        /// of zero are unpopulated transport objects and are never filtered.
        /// </summary>
        private ProjectileCollisionHit SelectNearestHit(int count, ulong excludedTargetId, ulong ownerToIgnore)
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

                if (hit.TargetEntityId != 0UL
                    && (hit.TargetEntityId == excludedTargetId || hit.TargetEntityId == ownerToIgnore))
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
            _freeSlots[_freeCount] = slot;
            _freeCount++;
            _totalDespawnCount++;
        }
    }
}
