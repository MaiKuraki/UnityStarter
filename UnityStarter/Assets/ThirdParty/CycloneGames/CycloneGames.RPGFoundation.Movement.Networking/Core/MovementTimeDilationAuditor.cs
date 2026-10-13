using System;
using System.Collections.Generic;

using CycloneGames.Networking;
using CycloneGames.Networking.Simulation;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    /// <summary>
    /// Detects time-dilation abuse in client-reported input streams. Per-message
    /// DeltaTime caps (<see cref="MovementNetworkProtocol.MAX_INPUT_DELTA_TIME"/>)
    /// only bound a single command: a client sending 30 commands per second, each
    /// claiming 1 second of simulation time, would run its entity at 30x speed
    /// while passing every per-message check. This auditor compares the
    /// ACCUMULATED claimed simulation time against the server wall clock (derived
    /// from server ticks and the configured tick rate) over a sliding window per
    /// entity and rejects streams whose claimed time outruns real time beyond
    /// the tolerance ratio.
    ///
    /// Tolerance absorbs legitimate jitter, burst catch-up after lag spikes and
    /// tick quantization; it must not absorb sustained speed-up, which is exactly
    /// what a windowed ratio measures. State is bounded by
    /// <paramref name="maxEntities"/> with fail-closed eviction: when the table
    /// is full, audits for new entities are rejected rather than letting memory
    /// grow or silently disabling the check.
    ///
    /// Single owner thread; not concurrent. Allocation-free after warm-up.
    /// </summary>
    public sealed class MovementTimeDilationAuditor
    {
        private struct EntityWindow
        {
            public int WindowStartServerTick;
            public float AccumulatedClaimedSeconds;
            public int CommandCount;
        }

        private readonly Dictionary<ulong, EntityWindow> _windows;
        private readonly int _tickRate;
        private readonly int _windowCommands;
        private readonly float _maxDilationRatio;
        private readonly int _maxEntities;

        public MovementTimeDilationAuditor(
            int tickRate,
            int windowCommands = 64,
            float maxDilationRatio = 1.25f,
            int maxEntities = 1024)
        {
            if (tickRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (windowCommands <= 1)
                throw new ArgumentOutOfRangeException(nameof(windowCommands));
            if (!float.IsFinite(maxDilationRatio) || maxDilationRatio < 1f)
                throw new ArgumentOutOfRangeException(nameof(maxDilationRatio));
            if (maxEntities <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxEntities));

            _windows = new Dictionary<ulong, EntityWindow>(Math.Min(maxEntities, 64));
            _tickRate = tickRate;
            _windowCommands = windowCommands;
            _maxDilationRatio = maxDilationRatio;
            _maxEntities = maxEntities;
        }

        public int TrackedEntityCount
        {
            get { return _windows.Count; }
        }

        /// <summary>
        /// Audits one accepted-shape input command. Returns true when the stream
        /// stays within the dilation tolerance; false when the entity is claiming
        /// simulation time faster than the server wall clock permits.
        /// </summary>
        public bool Audit(ulong entityId, float claimedDeltaSeconds, int serverTick)
        {
            if (!_windows.TryGetValue(entityId, out EntityWindow window))
            {
                if (_windows.Count >= _maxEntities)
                    return false; // Fail-closed: no untracked entity bypasses the audit.

                window = new EntityWindow { WindowStartServerTick = serverTick };
            }

            window.AccumulatedClaimedSeconds += claimedDeltaSeconds;
            window.CommandCount++;

            if (window.CommandCount < _windowCommands)
            {
                _windows[entityId] = window;
                return true;
            }

            int elapsedTicks = serverTick - window.WindowStartServerTick;
            float elapsedServerSeconds = elapsedTicks > 0 ? (float)elapsedTicks / _tickRate : 0f;

            bool accept = true;
            if (elapsedServerSeconds > 0f)
            {
                float ratio = window.AccumulatedClaimedSeconds / elapsedServerSeconds;
                accept = ratio <= _maxDilationRatio;
            }

            // Slide the window regardless of verdict so a rejected entity re-earns
            // trust only through sustained compliant behavior.
            _windows[entityId] = new EntityWindow { WindowStartServerTick = serverTick };
            return accept;
        }

        public bool Remove(ulong entityId)
        {
            return _windows.Remove(entityId);
        }

        public void Clear()
        {
            _windows.Clear();
        }
    }
}
