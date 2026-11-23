using System;
using System.Collections.Generic;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public readonly struct MovementDilationDiagnostics
    {
        public readonly int TrackedEntities;
        public readonly int MaxEntities;
        public readonly long AcceptedCommands;
        public readonly long RejectedCommands;
        public readonly long EntityEvictionCount;
        public readonly long ExpiredEntityCount;

        public MovementDilationDiagnostics(
            int trackedEntities, int maxEntities, long acceptedCommands,
            long rejectedCommands, long entityEvictionCount, long expiredEntityCount)
        {
            TrackedEntities = trackedEntities;
            MaxEntities = maxEntities;
            AcceptedCommands = acceptedCommands;
            RejectedCommands = rejectedCommands;
            EntityEvictionCount = entityEvictionCount;
            ExpiredEntityCount = expiredEntityCount;
        }
    }

    public sealed class MovementTimeDilationAuditor
    {
        private struct EntityClock
        {
            public ulong EntityId;
            public long WindowStartTick;
            public long LastSeenTick;
            public double AccumulatedClaimedSeconds;
            public long CommandCount;
            public bool InUse;
        }

        private readonly EntityClock[] _clocks;
        private readonly Dictionary<ulong, int> _index;
        private readonly int[] _free;
        private readonly int[] _lruQueue;
        private readonly int _lruMask;
        private readonly MovementDilationPolicy _policy;
        private readonly long _expiryTicks;
        private int _freeCount;
        private long _lruWrite;
        private int _lruCount;
        private int _tracked;
        private long _accepted;
        private long _rejected;
        private long _evictions;
        private long _expired;
        private long _operationsSinceSweep;

        public MovementTimeDilationAuditor(MovementDilationPolicy policy)
        {
            _policy = policy;
            _expiryTicks = (long)policy.WindowTicks * 4L;
            _clocks = new EntityClock[policy.MaxEntities];
            _index = new Dictionary<ulong, int>(policy.MaxEntities);
            _free = new int[policy.MaxEntities];
            _lruQueue = new int[NextPowerOfTwo(policy.MaxEntities * 2)];
            _lruMask = _lruQueue.Length - 1;
            for (int i = 0; i < policy.MaxEntities; i++)
                _free[i] = policy.MaxEntities - 1 - i;
            _freeCount = policy.MaxEntities;
        }

        public int TrackedEntityCount { get { return _tracked; } }

        public MovementDilationDiagnostics GetDiagnostics()
        {
            return new MovementDilationDiagnostics(
                _tracked, _policy.MaxEntities, _accepted, _rejected, _evictions, _expired);
        }

        // Budgets come from server ticks. A first-seen entity binds its window
        // start to that first tick so late joins cannot gain extra commands.
        public bool Audit(ulong entityId, double claimedDeltaSeconds, long serverTick)
        {
            bool accepted = Evaluate(entityId, claimedDeltaSeconds, serverTick);
            if (accepted) _accepted++; else _rejected++;

            _operationsSinceSweep++;
            if (_operationsSinceSweep >= _policy.SweepInterval)
            {
                _operationsSinceSweep = 0;
                Sweep(serverTick);
            }
            return accepted;
        }

        private bool Evaluate(ulong entityId, double claimedDeltaSeconds, long serverTick)
        {
            if (entityId == 0UL || serverTick < 0L) return false;
            if (double.IsNaN(claimedDeltaSeconds) || double.IsInfinity(claimedDeltaSeconds)
                || claimedDeltaSeconds < 0.0 || claimedDeltaSeconds > MovementWireLimits.MaxInputDeltaTime)
                return false;

            int clockIndex;
            if (!_index.TryGetValue(entityId, out clockIndex))
            {
                clockIndex = Acquire(entityId, serverTick);
                if (clockIndex < 0) return false;
            }

            EntityClock clock = _clocks[clockIndex];
            if (clock.LastSeenTick > serverTick) return false;

            long elapsed = serverTick - clock.WindowStartTick;
            if (elapsed > _policy.WindowTicks)
            {
                long shift = elapsed - _policy.WindowTicks;
                double consumed = shift / (double)_policy.TickRate * _policy.MaxDilationRatio;
                clock.WindowStartTick += shift;
                clock.AccumulatedClaimedSeconds -= consumed;
                if (clock.AccumulatedClaimedSeconds < 0.0) clock.AccumulatedClaimedSeconds = 0.0;
                elapsed = _policy.WindowTicks;
            }
            else if (elapsed < 0L)
            {
                clock.WindowStartTick = serverTick;
                clock.AccumulatedClaimedSeconds = 0.0;
                elapsed = 0L;
            }

            double elapsedSeconds = elapsed / (double)_policy.TickRate;
            double budget = elapsedSeconds * _policy.MaxDilationRatio + _policy.BurstAllowanceSeconds;
            double next = clock.AccumulatedClaimedSeconds + claimedDeltaSeconds;
            bool allowed = next <= budget;

            if (allowed) clock.AccumulatedClaimedSeconds = next;
            clock.CommandCount++;
            clock.LastSeenTick = serverTick;
            _clocks[clockIndex] = clock;
            TouchLru(clockIndex);
            return allowed;
        }

        public bool Remove(ulong entityId)
        {
            int clockIndex;
            if (!_index.TryGetValue(entityId, out clockIndex)) return false;
            Release(clockIndex);
            return true;
        }

        public void Clear()
        {
            Array.Clear(_clocks, 0, _clocks.Length);
            Array.Clear(_lruQueue, 0, _lruQueue.Length);
            _index.Clear();
            for (int i = 0; i < _clocks.Length; i++)
                _free[i] = _clocks.Length - 1 - i;
            _freeCount = _clocks.Length;
            _lruWrite = 0L;
            _lruCount = 0;
            _tracked = 0;
        }

        private int Acquire(ulong entityId, long serverTick)
        {
            if (_freeCount == 0)
            {
                int victim = NextVictim();
                if (victim < 0) return -1;
                Release(victim);
                _evictions++;
            }
            if (_freeCount == 0) return -1;

            int clockIndex = _free[--_freeCount];
            EntityClock clock = default;
            clock.EntityId = entityId;
            clock.InUse = true;
            clock.WindowStartTick = serverTick;
            clock.LastSeenTick = serverTick;
            _clocks[clockIndex] = clock;
            _index[entityId] = clockIndex;
            _tracked++;
            TouchLru(clockIndex);
            return clockIndex;
        }

        private void Release(int clockIndex)
        {
            if (clockIndex < 0 || clockIndex >= _clocks.Length) return;
            ulong entityId = _clocks[clockIndex].EntityId;
            _clocks[clockIndex] = default;
            if (entityId != 0UL && _index.ContainsKey(entityId)) _index.Remove(entityId);
            if (_freeCount >= _free.Length) return;
            _free[_freeCount++] = clockIndex;
            _tracked--;
            if (_tracked < 0) _tracked = 0;
        }

        private void Sweep(long serverTick)
        {
            for (int i = 0; i < _clocks.Length; i++)
            {
                EntityClock clock = _clocks[i];
                if (!clock.InUse) continue;
                if (clock.LastSeenTick > serverTick) continue;
                if (serverTick - clock.LastSeenTick <= _expiryTicks) continue;
                Release(i);
                _expired++;
            }
        }

        private void TouchLru(int index)
        {
            _lruQueue[(int)(_lruWrite & _lruMask)] = index;
            _lruWrite++;
            if (_lruCount < _lruQueue.Length) _lruCount++;
        }

        private int NextVictim()
        {
            long read = _lruWrite - _lruCount;
            for (int k = 0; k < _lruCount; k++)
            {
                int candidate = _lruQueue[(int)((read + k) & _lruMask)];
                if (candidate < 0 || candidate >= _clocks.Length) continue;
                if (_clocks[candidate].InUse) return candidate;
            }
            for (int i = 0; i < _clocks.Length; i++)
                if (_clocks[i].InUse) return i;
            return -1;
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value) result <<= 1;
            return result;
        }
    }
}
