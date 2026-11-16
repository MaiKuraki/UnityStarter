using System;

namespace CycloneGames.RPGFoundation.Projectile.Core
{
    /// <summary>
    /// Immutable construction contract for <see cref="ProjectileWorld"/>. All storage is
    /// allocated once at construction; no parameter can trigger later growth.
    /// </summary>
    public readonly struct ProjectileWorldConfig
    {
        // Upper bounds on per-frame work, so a bad configuration cannot turn one Step call
        // into an unbounded loop.
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

        /// <summary>
        /// Maintains an open-addressing <see cref="ulong"/> to slot index so
        /// <see cref="ProjectileWorld.TryGetStateByNetworkEntityId"/> is O(1) instead of a
        /// linear scan of active projectiles. Costs roughly 24 bytes per world slot
        /// (two tables sized at 2x capacity). Disable only when the product never resolves
        /// a projectile by network id, or when the extra memory is not affordable.
        /// </summary>
        public readonly bool EnableNetworkEntityIdIndex;

        /// <summary>
        /// Largest distance a projectile may travel in one integration step. Zero disables
        /// distance-based sub-stepping.
        ///
        /// Time-based sub-stepping alone does not bound displacement: at
        /// <see cref="MaxDeltaTime"/> = 1/60 a projectile at 500 m/s still moves 8.3 m per step,
        /// so any adapter that tests the endpoint instead of the segment will miss a thin wall.
        /// With this set, the frame is split until no projectile moves further than this value,
        /// and <see cref="MaxSubSteps"/> caps the work when the budget is insufficient.
        /// </summary>
        public readonly float MaxTravelPerStep;

        public ProjectileWorldConfig(
            int capacity,
            int eventCapacity,
            int collisionHitCapacity,
            in ProjectileSpaceProfile space,
            int maxCollisionIterations = 4,
            float maxDeltaTime = 0f,
            int maxSubSteps = 1,
            bool enableNetworkEntityIdIndex = true,
            float maxTravelPerStep = 0f)
        {
            Capacity = capacity;
            EventCapacity = eventCapacity;
            CollisionHitCapacity = collisionHitCapacity;
            MaxCollisionIterations = maxCollisionIterations;
            MaxDeltaTime = maxDeltaTime;
            MaxSubSteps = maxSubSteps;
            Space = space;
            EnableNetworkEntityIdIndex = enableNetworkEntityIdIndex;
            MaxTravelPerStep = maxTravelPerStep;
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
                       && MaxDeltaTime <= MaxDeltaTimeLimit
                       && !float.IsNaN(MaxTravelPerStep)
                       && !float.IsInfinity(MaxTravelPerStep)
                       && MaxTravelPerStep >= 0f
                       && (MaxTravelPerStep == 0f || MaxSubSteps > 1);
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

        /// <summary>
        /// Distinct collision objects suppressed inside one sweep. Bounce and pierce restart
        /// the sweep at the impact point, where the surface just hit is reported again at
        /// distance zero.
        /// </summary>
        private const int MaxSweepExclusions = 8;

        // Open addressing sentinels for the network-id index.
        private const int NetIdEmpty = -1;
        private const int NetIdTombstone = -2;

        private readonly ProjectileSpaceProfile _space;
        private readonly ProjectileState[] _states;

        // One definition per dense slot. Merging these fields into ProjectileState would
        // remove a memory stream; indexing a shared table does not, because the table still
        // has to be sized for capacity.
        private readonly ProjectileDefinition[] _definitions;

        private readonly ulong[] _slotGenerations;
        private readonly int[] _slotToDense;
        private readonly int[] _denseToSlot;
        private readonly int[] _freeSlots;
        private readonly ProjectileCollisionHit[] _collisionHits;
        private readonly ulong[] _sweepExcludedObjects;

        // Scratch for Replace-mode restores: which dense slots the batch still mentions.
        // Allocated once so a restore never allocates, even though it is not a hot path.
        private readonly bool[] _restoreKeepMask;
        private readonly int _maxCollisionIterations;
        private readonly float _maxDeltaTime;
        private readonly int _maxSubSteps;
        private readonly float _maxTravelPerStep;

        private readonly bool _hasNetIdIndex;
        private readonly ulong[] _netIdKeys;
        private readonly int[] _netIdValues;
        private readonly int _netIdMask;

        private int _activeCount;
        private int _slotHighWaterMark;
        private int _freeCount;
        private int _peakActiveCount;
        private int _netIdUsed;
        private int _netIdTombstones;
        private long _nonFiniteRejectedCount;
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
        private long _totalSubStepsCount;
        private long _totalClampedFrameCount;
        private long _subStepSaturationCount;
        private float _maxObservedSpeed;
        private float _stepMaxSpeedSqr;

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
            _sweepExcludedObjects = new ulong[MaxSweepExclusions];
            _restoreKeepMask = new bool[capacity];
            _maxCollisionIterations = config.MaxCollisionIterations;
            _maxDeltaTime = config.MaxDeltaTime;
            _maxSubSteps = config.MaxSubSteps;
            _maxTravelPerStep = config.MaxTravelPerStep;

            _hasNetIdIndex = config.EnableNetworkEntityIdIndex;
            if (_hasNetIdIndex)
            {
                // Two entries per slot keeps the load factor <= 0.5, so linear probing
                // stays short and a rebuild always leaves free slots behind.
                int tableSize = NextPowerOfTwo(capacity * 2);
                _netIdKeys = new ulong[tableSize];
                _netIdValues = new int[tableSize];
                _netIdMask = tableSize - 1;
                for (int i = 0; i < tableSize; i++)
                {
                    _netIdValues[i] = NetIdEmpty;
                }
            }
            else
            {
                _netIdKeys = Array.Empty<ulong>();
                _netIdValues = Array.Empty<int>();
                _netIdMask = 0;
            }

            HitEvents = new ProjectileEventBuffer(config.EventCapacity);

            for (int i = 0; i < capacity; i++)
            {
                _slotToDense[i] = NullDenseIndex;
                _denseToSlot[i] = NullDenseIndex;
            }
        }

        public int Capacity => _states.Length;

        public int ActiveCount => _activeCount;

        /// <summary>
        /// Projectiles removed because integration produced NaN or infinity. A non-zero value
        /// means an authored definition reaches the float limit.
        /// </summary>
        public long NonFiniteRejectedCount => _nonFiniteRejectedCount;

        /// <summary>
        /// Replay origin: the last tick passed to <see cref="Step"/>, or updated by a successful
        /// batch restore. Reading it does not build a <see cref="ProjectileWorldStats"/>.
        /// </summary>
        public int LatestTick => _lastTick;

        /// <summary>
        /// Frames where the displacement bound could not be honoured within
        /// <see cref="ProjectileWorldConfig.MaxSubSteps"/>. Non-zero means projectiles travelled
        /// further than <see cref="ProjectileWorldConfig.MaxTravelPerStep"/> in one step, so a
        /// discrete collision adapter may tunnel. Raise the sub-step budget or slow the projectile.
        /// </summary>
        public long SubStepSaturationCount => _subStepSaturationCount;

        public ProjectileEventBuffer HitEvents { get; }

        /// <summary>
        /// Read-only view over the dense active range, for presentation and serialization.
        /// Invalidated by spawn or despawn.
        /// </summary>
        public ReadOnlySpan<ProjectileState> ActiveStates
        {
            get { return new ReadOnlySpan<ProjectileState>(_states, 0, _activeCount); }
        }

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
            _totalSubStepsCount,
            _totalClampedFrameCount,
            _nonFiniteRejectedCount);

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
            if (_hasNetIdIndex)
            {
                NetIdIndexInsert(request.NetworkEntityId, slot);
            }

            float ceiling = request.Definition.MaxSpeed > 0f
                ? request.Definition.MaxSpeed
                : request.Definition.InitialSpeed;
            if (ceiling > _maxObservedSpeed)
            {
                _maxObservedSpeed = ceiling;
            }

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
        /// Resolves a projectile by its replicated network id. O(1) when
        /// <see cref="ProjectileWorldConfig.EnableNetworkEntityIdIndex"/> is on, otherwise a
        /// linear scan of active projectiles. <see cref="ProjectileSpawnRequest.NetworkEntityId"/>
        /// must be unique per active projectile; duplicates resolve to the first dense slot.
        /// </summary>
        public bool TryGetStateByNetworkEntityId(ulong networkEntityId, out ProjectileState state)
        {
            if (networkEntityId == 0UL)
            {
                state = default;
                return false;
            }

            if (_hasNetIdIndex)
            {
                if (TryNetIdIndexFind(networkEntityId, out int slot))
                {
                    int denseIndex = _slotToDense[slot];
                    if (denseIndex >= 0 && denseIndex < _activeCount && _states[denseIndex].NetworkEntityId == networkEntityId)
                    {
                        state = _states[denseIndex];
                        return true;
                    }
                }

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
        /// Removes a live projectile by its replicated network id and reports the handle it had.
        /// O(1) via the same index that serves <see cref="TryGetStateByNetworkEntityId"/>; it is
        /// the id-addressed counterpart of <see cref="Despawn"/> and exists so a despawn message
        /// can be landed without the receiver first having to hold a handle.
        ///
        /// Returns false when no live projectile owns the id, which is the normal outcome for a
        /// re-delivered despawn: unreliable delivery duplicates messages, it does not reorder
        /// them into a state where a second despawn is an error.
        /// </summary>
        public bool TryDespawnByNetworkEntityId(ulong networkEntityId, out ProjectileHandle handle)
        {
            handle = default;
            if (!TryGetDenseIndexByNetworkEntityId(networkEntityId, out int denseIndex))
            {
                return false;
            }

            handle = _states[denseIndex].Handle;
            DespawnDense(denseIndex);
            return true;
        }

        /// <summary>
        /// Overwrites a live projectile with an authoritative snapshot. Position, velocity,
        /// previous position, age and tick are replaced; handle, bounce and pierce counters are
        /// preserved so a correction does not hand the projectile a fresh budget.
        /// Returns <see cref="ProjectileSnapshotApplyResult.Missing"/> when no live projectile
        /// owns the id, and <see cref="ProjectileSnapshotApplyResult.Stale"/> when the snapshot's
        /// tick is older than the local one (out-of-order unreliable delivery).
        /// </summary>
        public ProjectileSnapshotApplyResult TryApplySnapshot(in ProjectileSnapshot snapshot)
        {
            return ApplySnapshot(in snapshot, allowRewind: false);
        }

        /// <summary>
        /// Shared apply path. <paramref name="allowRewind"/> exists because a Replace restore is
        /// by definition "go back to this tick": the out-of-order guard that protects incremental
        /// patches would otherwise reject every snapshot of a rollback and silently apply nothing.
        /// </summary>
        private ProjectileSnapshotApplyResult ApplySnapshot(in ProjectileSnapshot snapshot, bool allowRewind)
        {
            if (!IsSnapshotUsable(in snapshot))
            {
                return ProjectileSnapshotApplyResult.Invalid;
            }

            if (!TryGetDenseIndexByNetworkEntityId(snapshot.NetworkEntityId, out int denseIndex))
            {
                return ProjectileSnapshotApplyResult.Missing;
            }

            ref ProjectileState state = ref _states[denseIndex];
            if (!allowRewind && snapshot.Tick < state.CurrentTick)
            {
                return ProjectileSnapshotApplyResult.Stale;
            }

            state.Position = space_ProjectPosition(in snapshot.Position);
            state.PreviousPosition = space_ProjectPosition(in snapshot.PreviousPosition);
            state.Velocity = _space.ProjectVector(snapshot.Velocity);
            state.Age = snapshot.Age;
            state.CurrentTick = snapshot.Tick;
            return ProjectileSnapshotApplyResult.Applied;
        }

        /// <summary>
        /// Creates a projectile from an authoritative snapshot. Kinematics come from the local
        /// definition (the snapshot carries only the id), while age, tick and previous position
        /// are taken from the snapshot so the replica continues where the authority is.
        /// </summary>
        public bool TrySpawnFromSnapshot(
            in ProjectileSnapshot snapshot,
            in ProjectileDefinition definition,
            out ProjectileHandle handle)
        {
            handle = default;
            if (!IsSnapshotUsable(in snapshot) || !definition.IsValid)
            {
                return false;
            }

            ProjectileVector3 direction = snapshot.Velocity.NormalizedOrFallback(ProjectileVector3.Forward);
            var request = new ProjectileSpawnRequest(
                definition,
                snapshot.OwnerEntityId,
                snapshot.NetworkEntityId,
                snapshot.TargetEntityId,
                snapshot.Tick,
                snapshot.PredictionKey,
                0u,
                snapshot.Position,
                direction,
                snapshot.Velocity);

            if (!TrySpawn(in request, out handle))
            {
                return false;
            }

            if (TryGetDenseIndex(handle, out int denseIndex))
            {
                ref ProjectileState state = ref _states[denseIndex];
                state.Age = snapshot.Age;
                state.CurrentTick = snapshot.Tick;
                state.PreviousPosition = space_ProjectPosition(in snapshot.PreviousPosition);
            }

            return true;
        }

        /// <summary>
        /// Hard reset: drops any local projectile with this id and recreates it from the
        /// snapshot. Used when reconciliation reports a definition mismatch or a hard snap,
        /// where patching individual fields would leave counters and definition data wrong.
        /// </summary>
        public ProjectileSnapshotApplyResult TryResetFromSnapshot(
            in ProjectileSnapshot snapshot,
            in ProjectileDefinition definition,
            out ProjectileHandle handle)
        {
            handle = default;
            if (!IsSnapshotUsable(in snapshot) || !definition.IsValid)
            {
                return ProjectileSnapshotApplyResult.Invalid;
            }

            if (TryGetDenseIndexByNetworkEntityId(snapshot.NetworkEntityId, out int denseIndex))
            {
                DespawnDense(denseIndex);
            }

            return TrySpawnFromSnapshot(in snapshot, in definition, out handle)
                ? ProjectileSnapshotApplyResult.Spawned
                : ProjectileSnapshotApplyResult.Failed;
        }

        /// <summary>
        /// Replaces the world with a whole authoritative state: patches known ids, creates
        /// unknown ones through <paramref name="catalog"/>, and in
        /// <see cref="ProjectileRestoreMode.Replace"/> removes everything the batch omits.
        ///
        /// Atomic. The batch is validated, its definitions resolved and the capacity checked
        /// before any state changes, so a rejected batch leaves the world untouched. A patch
        /// keeps the handle, bounce and pierce budgets; only a created projectile gets a fresh
        /// budget.
        ///
        /// <paramref name="catalog"/> may be null only when the batch mentions no unknown id.
        /// Allocation free, O(batch + active).
        /// </summary>
        public ProjectileRestoreResult TryRestoreStates(
            ReadOnlySpan<ProjectileSnapshot> snapshots,
            ProjectileRestoreMode mode,
            IProjectileDefinitionCatalog catalog = null)
        {
            int batchTick = 0;
            int needSpawn = 0;
            int alreadyKnown = 0;

            // Pass 1: validate, resolve definitions, and mark what survives. No mutation yet.
            Array.Clear(_restoreKeepMask, 0, _activeCount);

            for (int i = 0; i < snapshots.Length; i++)
            {
                ref readonly ProjectileSnapshot snapshot = ref snapshots[i];
                if (!IsSnapshotUsable(in snapshot))
                {
                    return Rejected(ProjectileRestoreRejection.InvalidSnapshot, in snapshot);
                }

                if (snapshot.Tick > batchTick)
                {
                    batchTick = snapshot.Tick;
                }

                if (TryGetDenseIndexByNetworkEntityId(snapshot.NetworkEntityId, out int denseIndex))
                {
                    _restoreKeepMask[denseIndex] = true;
                    alreadyKnown++;
                    continue;
                }

                needSpawn++;
                if (catalog == null
                    || !catalog.TryGetDefinition(snapshot.DefinitionId.Value, out ProjectileDefinition definition)
                    || !definition.IsValid)
                {
                    return Rejected(ProjectileRestoreRejection.UnknownDefinition, in snapshot);
                }
            }

            int toRemove = 0;
            if (mode == ProjectileRestoreMode.Replace)
            {
                for (int denseIndex = 0; denseIndex < _activeCount; denseIndex++)
                {
                    if (!_restoreKeepMask[denseIndex])
                    {
                        toRemove++;
                    }
                }
            }

            int projected = _activeCount - toRemove + needSpawn;
            if (projected > _states.Length)
            {
                return new ProjectileRestoreResult(
                    0, 0, 0, 0, projected - _states.Length, 0, 0, _lastTick, false,
                    ProjectileRestoreRejection.Capacity);
            }

            // Pass 2: mutate. Removals run descending so the swap-remove source is always an
            // index already decided.
            int despawned = 0;
            if (mode == ProjectileRestoreMode.Replace)
            {
                for (int denseIndex = _activeCount - 1; denseIndex >= 0; denseIndex--)
                {
                    if (_restoreKeepMask[denseIndex])
                    {
                        continue;
                    }

                    DespawnDense(denseIndex);
                    despawned++;
                }
            }

            int applied = 0;
            int spawned = 0;
            for (int i = 0; i < snapshots.Length; i++)
            {
                ref readonly ProjectileSnapshot snapshot = ref snapshots[i];
                bool rewind = mode == ProjectileRestoreMode.Replace;
                if (ApplySnapshot(in snapshot, rewind) == ProjectileSnapshotApplyResult.Applied)
                {
                    applied++;
                    continue;
                }

                // Missing: pass 1 proved a definition exists, and capacity was checked.
                catalog.TryGetDefinition(snapshot.DefinitionId.Value, out ProjectileDefinition definition);
                if (TrySpawnFromSnapshot(in snapshot, in definition, out _))
                {
                    spawned++;
                }
            }

            // Replace means "this batch IS the world at that tick", so the anchor may move
            // backwards: that is exactly what a rollback needs. Merge only ever advances.
            // Recompute the displacement driver from restored state: it feeds ComputeSubSteps, and
            // a stale value would make the replay after a rollback pick a different sub-step count
            // than the original run.
            float restoredMaxSpeed = 0f;
            for (int i = 0; i < _activeCount; i++)
            {
                float speed = _states[i].Velocity.Length;
                if (speed > restoredMaxSpeed)
                {
                    restoredMaxSpeed = speed;
                }
            }

            _maxObservedSpeed = restoredMaxSpeed;

            _lastTick = mode == ProjectileRestoreMode.Replace
                ? batchTick
                : (batchTick > _lastTick ? batchTick : _lastTick);
            int tick = _lastTick;

            int duplicates = applied - alreadyKnown;

            return new ProjectileRestoreResult(
                applied,
                spawned,
                despawned,
                0,
                0,
                0,
                duplicates < 0 ? 0 : duplicates,
                tick,
                true,
                ProjectileRestoreRejection.None);
        }

        private ProjectileRestoreResult Rejected(ProjectileRestoreRejection reason, in ProjectileSnapshot snapshot)
        {
            int invalid = reason == ProjectileRestoreRejection.InvalidSnapshot ? 1 : 0;
            int unknown = reason == ProjectileRestoreRejection.UnknownDefinition ? 1 : 0;
            return new ProjectileRestoreResult(
                0, 0, 0, invalid, 0, unknown, 0, _lastTick, false, reason);
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
            if (deltaTime < 0f || !IsFiniteValue(deltaTime))
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

            _stepMaxSpeedSqr = 0f;
            float subDelta = effectiveDelta / subSteps;
            for (int s = 0; s < subSteps; s++)
            {
                StepCore(subDelta, tick, collisionWorld, targetProvider);
            }

            // Observed speed drives next frame's displacement bound. It is sampled during the
            // dense pass that already touches velocity, so no extra traversal is paid. Spawn also
            // raises it, which keeps the first frame after a fast projectile from running
            // under-substepped.
            _maxObservedSpeed = (float)Math.Sqrt(_stepMaxSpeedSqr);

            _totalSubStepsCount += subSteps;
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

            if (_hasNetIdIndex)
            {
                for (int i = 0; i < _netIdValues.Length; i++)
                {
                    _netIdValues[i] = NetIdEmpty;
                }

                _netIdUsed = 0;
                _netIdTombstones = 0;
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
            _totalSubStepsCount = 0;
            _totalClampedFrameCount = 0;
            _nonFiniteRejectedCount = 0;
            _subStepSaturationCount = 0;
        }

        /// <summary>
        /// Sub-step count for one frame, driven by both a time bound and a displacement bound and
        /// clamped by <see cref="ProjectileWorldConfig.MaxSubSteps"/>.
        /// </summary>
        private int ComputeSubSteps(float deltaTime)
        {
            if (_maxSubSteps <= 1)
            {
                return 1;
            }

            int needed = 1;

            if (_maxDeltaTime > 0f && deltaTime > _maxDeltaTime)
            {
                double ratio = (double)deltaTime / _maxDeltaTime;
                needed = ratio >= _maxSubSteps ? _maxSubSteps : (int)Math.Ceiling(ratio);
            }

            if (_maxTravelPerStep > 0f && _maxObservedSpeed > 0f)
            {
                double displacement = (double)_maxObservedSpeed * deltaTime;
                if (displacement > _maxTravelPerStep)
                {
                    double ratio = displacement / _maxTravelPerStep;
                    if (ratio > _maxSubSteps)
                    {
                        // The budget cannot honour MaxTravelPerStep for this frame. The counter is
                        // the only signal a caller gets that the bound is not being met.
                        _subStepSaturationCount++;
                        needed = _maxSubSteps;
                    }
                    else
                    {
                        int byDistance = (int)Math.Ceiling(ratio);
                        if (byDistance > needed)
                        {
                            needed = byDistance;
                        }
                    }
                }
            }

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
                ref readonly ProjectileDefinition definition = ref _definitions[denseIndex];

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
                    in targetPosition,
                    in targetVelocity);

                // Sampled here rather than in a separate pass: the dense loop already holds the
                // state, and the maximum feeds next frame's displacement bound.
                float speedSqr = state.Velocity.X * state.Velocity.X
                                 + state.Velocity.Y * state.Velocity.Y
                                 + state.Velocity.Z * state.Velocity.Z;
                if (speedSqr > _stepMaxSpeedSqr)
                {
                    _stepMaxSpeedSqr = speedSqr;
                }

                // Integration is unguarded by design (it must stay branch-free and
                // allocation-free), so the one place non-finite state can be observed
                // cheaply is here. Without this a legal-but-overflowing definition keeps
                // producing NaN/Infinity that silently fails snapshot validation later.
                if (state.MayOverflow && !state.IsFinite)
                {
                    _nonFiniteRejectedCount++;
                    DespawnDense(denseIndex);
                    continue;
                }

                bool despawn = state.Age >= definition.MaxLifetime;
                if (!despawn
                    && collisionWorld != null
                    && definition.CollisionLayerMask != 0)
                {
                    despawn = ResolveCollision(ref state, in definition, collisionWorld);
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

            // Bounce and pierce restart the sweep from the impact point. PhysX reports the
            // collider the sweep starts inside again, at distance ~0, so suppressing the
            // already-consumed object ids is what stops one wall from eating the whole
            // bounce budget. Stack allocated: no GC, no shared mutable state.
            ulong[] excludedObjects = _sweepExcludedObjects;
            int excludedCount = 0;

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

                ProjectileCollisionHit hit = SelectNearestHit(
                    count,
                    lastHitTargetId,
                    ownerToIgnore,
                    excludedObjects,
                    excludedCount);
                if (!hit.IsValid)
                {
                    return false;
                }

                if (hit.TargetEntityId != 0UL)
                {
                    lastHitTargetId = hit.TargetEntityId;
                }

                if (excludedCount < MaxSweepExclusions && hit.TargetObjectId != 0UL)
                {
                    excludedObjects[excludedCount] = hit.TargetObjectId;
                    excludedCount++;
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

        private static bool IsFiniteValue(float value)
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
        /// Picks the nearest valid hit while suppressing the entity and the collision object
        /// already resolved during this sweep, plus the owner when the definition requests
        /// it. Entity ids of zero are unpopulated transport objects and are never filtered
        /// by entity id; object ids of zero are never filtered by object id.
        /// </summary>
        private ProjectileCollisionHit SelectNearestHit(
            int count,
            ulong excludedTargetId,
            ulong ownerToIgnore,
            ulong[] excludedObjectIds,
            int excludedObjectCount)
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

                if (hit.TargetObjectId != 0UL && IsExcludedObject(hit.TargetObjectId, excludedObjectIds, excludedObjectCount))
                {
                    continue;
                }

                best = hit;
                bestDistance = hit.Distance;
            }

            return best;
        }

        private static bool IsExcludedObject(ulong objectId, ulong[] excludedObjectIds, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (excludedObjectIds[i] == objectId)
                {
                    return true;
                }
            }

            return false;
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

        private bool TryGetDenseIndexByNetworkEntityId(ulong networkEntityId, out int denseIndex)
        {
            denseIndex = default;
            if (networkEntityId == 0UL)
            {
                return false;
            }

            if (_hasNetIdIndex)
            {
                if (!TryNetIdIndexFind(networkEntityId, out int slot))
                {
                    return false;
                }

                int candidate = _slotToDense[slot];
                if (candidate < 0 || candidate >= _activeCount
                    || _states[candidate].NetworkEntityId != networkEntityId)
                {
                    return false;
                }

                denseIndex = candidate;
                return true;
            }

            for (int i = 0; i < _activeCount; i++)
            {
                if (_states[i].NetworkEntityId != networkEntityId)
                {
                    continue;
                }

                denseIndex = i;
                return true;
            }

            return false;
        }

        private ProjectileVector3 space_ProjectPosition(in ProjectileVector3 value)
        {
            return _space.ProjectPosition(value);
        }

        private static bool IsSnapshotUsable(in ProjectileSnapshot snapshot)
        {
            return snapshot.NetworkEntityId != 0UL
                   && snapshot.Age >= 0f
                   && !float.IsNaN(snapshot.Age)
                   && !float.IsInfinity(snapshot.Age)
                   && snapshot.Position.IsFinite
                   && snapshot.PreviousPosition.IsFinite
                   && snapshot.Velocity.IsFinite;
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
            ulong removedNetworkEntityId = _states[denseIndex].NetworkEntityId;
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

            // Removal runs last: a rebuild triggered here must not observe this slot as live.
            if (_hasNetIdIndex && removedNetworkEntityId != 0UL)
            {
                NetIdIndexRemove(removedNetworkEntityId);
            }
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
            {
                result <<= 1;
            }

            return result;
        }

        private static int NetIdHash(ulong key)
        {
            // 64-bit finalizer (MurmurHash3 fmix64). Deterministic on every platform:
            // pure integer arithmetic, no floating point, no culture dependency.
            ulong x = key;
            x ^= x >> 33;
            x *= 0xFF51AFD7ED558CCDUL;
            x ^= x >> 33;
            x *= 0xC4CEB9FE1A85EC53UL;
            x ^= x >> 33;
            return (int)(x & 0x7FFFFFFFUL);
        }

        private void NetIdIndexInsert(ulong key, int slot)
        {
            // Keep the load factor (live + tombstones) at or below 0.5. Churn turns live
            // entries into tombstones without freeing probe space, so without this the
            // probe length would grow even though the live count stays flat.
            if ((_netIdUsed + _netIdTombstones + 1) * 2 > _netIdValues.Length)
            {
                NetIdIndexRebuild();
            }

            if (!TryNetIdIndexInsertRaw(key, slot))
            {
                NetIdIndexRebuild();
                TryNetIdIndexInsertRaw(key, slot);
            }
        }

        private bool TryNetIdIndexInsertRaw(ulong key, int slot)
        {
            int index = NetIdHash(key) & _netIdMask;
            int firstTombstone = -1;
            for (int probe = 0; probe < _netIdValues.Length; probe++)
            {
                int value = _netIdValues[index];
                if (value == NetIdEmpty)
                {
                    if (firstTombstone >= 0)
                    {
                        index = firstTombstone;
                        _netIdTombstones--;
                    }

                    _netIdKeys[index] = key;
                    _netIdValues[index] = slot;
                    _netIdUsed++;
                    return true;
                }

                if (value == NetIdTombstone)
                {
                    if (firstTombstone < 0)
                    {
                        firstTombstone = index;
                    }
                }
                else if (_netIdKeys[index] == key)
                {
                    _netIdValues[index] = slot;
                    return true;
                }

                index = (index + 1) & _netIdMask;
            }

            return false;
        }

        private void NetIdIndexRemove(ulong key)
        {
            int index = NetIdHash(key) & _netIdMask;
            for (int probe = 0; probe < _netIdValues.Length; probe++)
            {
                int value = _netIdValues[index];
                if (value == NetIdEmpty)
                {
                    return;
                }

                if (value != NetIdTombstone && _netIdKeys[index] == key)
                {
                    _netIdValues[index] = NetIdTombstone;
                    _netIdUsed--;
                    _netIdTombstones++;
                    return;
                }

                index = (index + 1) & _netIdMask;
            }
        }

        private bool TryNetIdIndexFind(ulong key, out int slot)
        {
            int index = NetIdHash(key) & _netIdMask;
            for (int probe = 0; probe < _netIdValues.Length; probe++)
            {
                int value = _netIdValues[index];
                if (value == NetIdEmpty)
                {
                    slot = -1;
                    return false;
                }

                if (value != NetIdTombstone && _netIdKeys[index] == key)
                {
                    slot = value;
                    return true;
                }

                index = (index + 1) & _netIdMask;
            }

            slot = -1;
            return false;
        }

        /// <summary>
        /// Rebuilds the index from live slot state. Triggered only when tombstones from
        /// churn degrade probe length; cost is O(active), amortised over many despawns.
        /// </summary>
        private void NetIdIndexRebuild()
        {
            for (int i = 0; i < _netIdValues.Length; i++)
            {
                _netIdValues[i] = NetIdEmpty;
            }

            _netIdUsed = 0;
            _netIdTombstones = 0;

            for (int slot = 0; slot < _slotHighWaterMark; slot++)
            {
                int denseIndex = _slotToDense[slot];
                if (denseIndex < 0 || denseIndex >= _activeCount)
                {
                    continue;
                }

                ulong key = _states[denseIndex].NetworkEntityId;
                if (key != 0UL)
                {
                    TryNetIdIndexInsertRaw(key, slot);
                }
            }
        }
    }
}
