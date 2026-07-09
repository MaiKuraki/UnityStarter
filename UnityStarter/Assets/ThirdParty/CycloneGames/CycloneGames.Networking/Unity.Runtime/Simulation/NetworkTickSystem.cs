using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CycloneGames.Networking.Simulation
{
    /// <summary>
    /// Fixed-timestep tick system for deterministic simulation.
    /// Drives all network-synchronized gameplay at a configurable tick rate.
    /// Critical for competitive games (PUBG, racing, fighting games) and
    /// large-scale sims (MMO, sandbox).
    /// </summary>
    public sealed class NetworkTickSystem : ITickSystem
    {
        private readonly List<ITickable> _tickables = new List<ITickable>(32);
        private readonly object _tickableLock = new object();
        private ITickable[] _tickableSnapshot = Array.Empty<ITickable>();

        private double _accumulator;
        private double _serverTimeOffset;
        private uint _tickValue;

        public NetworkTick CurrentTick => new NetworkTick(_tickValue);
        public int TickRate { get; private set; }
        public float TickInterval { get; private set; }
        public float ServerTime => (float)(_tickValue * (double)TickInterval + _serverTimeOffset);
        public float InterpolationDelay { get; set; }

        public event Action<NetworkTick> OnTick;
        public event Action<NetworkTick> OnPreTick;
        public event Action<NetworkTick> OnPostTick;

        public NetworkTickSystem(int tickRate = NetworkConstants.DefaultTickRate)
        {
            SetTickRate(tickRate);
            InterpolationDelay = TickInterval * 2f;
        }

        public void SetTickRate(int tickRate)
        {
            TickRate = Mathf.Clamp(tickRate, NetworkConstants.MinTickRate, NetworkConstants.MaxTickRate);
            TickInterval = 1f / TickRate;
        }

        // Call from MonoBehaviour.Update() or a custom game loop
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(float deltaTime)
        {
            if (deltaTime < 0f)
            {
                deltaTime = 0f;
            }

            _accumulator += deltaTime;

            // Clamp accumulator to prevent spiral-of-death on hitches
            const int maxTicksPerFrame = 5;
            double maxAccumulator = TickInterval * maxTicksPerFrame;
            if (_accumulator > maxAccumulator)
            {
                _accumulator = maxAccumulator;
            }

            while (_accumulator >= TickInterval)
            {
                _accumulator -= TickInterval;
                _tickValue++;

                var tick = new NetworkTick(_tickValue);
                ProcessTick(tick);
            }
        }

        private void ProcessTick(NetworkTick tick)
        {
            OnPreTick?.Invoke(tick);

            // Snapshot is rebuilt only when registration changes. This keeps the tick hot path stable
            // while still allowing callbacks to register or unregister without mutating the active pass.
            ITickable[] snapshot;
            lock (_tickableLock)
            {
                snapshot = _tickableSnapshot;
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].OnNetworkTick(tick, TickInterval);
            }

            OnTick?.Invoke(tick);
            OnPostTick?.Invoke(tick);
        }

        // Apply server time correction (from time sync packets)
        public void ApplyServerTimeOffset(double offset)
        {
            _serverTimeOffset = offset;
        }

        // Force-set tick (for server reconciliation)
        public void SetTick(uint tick)
        {
            _tickValue = tick;
            _accumulator = 0;
        }

        // Interpolation alpha: fraction of current tick elapsed (0..1)
        public float InterpolationAlpha => (float)(_accumulator / TickInterval);

        public void RegisterTickable(ITickable tickable)
        {
            if (tickable == null)
            {
                throw new ArgumentNullException(nameof(tickable));
            }

            lock (_tickableLock)
            {
                if (_tickables.Contains(tickable))
                {
                    return;
                }

                _tickables.Add(tickable);
                RebuildTickableSnapshotLocked();
            }
        }

        public void UnregisterTickable(ITickable tickable)
        {
            if (tickable == null)
            {
                return;
            }

            lock (_tickableLock)
            {
                if (_tickables.Remove(tickable))
                {
                    RebuildTickableSnapshotLocked();
                }
            }
        }

        public void Reset()
        {
            _tickValue = 0;
            _accumulator = 0;
            _serverTimeOffset = 0;
        }

        private void RebuildTickableSnapshotLocked()
        {
            _tickableSnapshot = _tickables.Count == 0
                ? Array.Empty<ITickable>()
                : _tickables.ToArray();
        }
    }
}
