using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Session
{
    /// <summary>
    /// Handles client reconnection to an ongoing game session.
    /// Supports any session-based multiplayer game requiring reconnect capability.
    /// 
    /// Workflow:
    /// 1. Client disconnects (network drop, crash, alt-F4)
    /// 2. Server keeps player slot reserved for reconnect window
    /// 3. Client reconnects, authenticates
    /// 4. Server streams full game state snapshot to client (catch-up)
    /// 5. Client applies snapshot and resumes real-time sync
    /// </summary>
    public interface IReconnectionManager
    {
        /// <summary>
        /// Maximum time (seconds) a disconnected client's slot is held.
        /// After this, the slot is released and reconnection is denied.
        /// </summary>
        float ReconnectWindow { get; set; }

        /// <summary>
        /// Whether a connection ID has an active reservation (is allowed to reconnect).
        /// </summary>
        bool HasReservation(int connectionId);

        /// <summary>
        /// Called by the server when a client disconnects.
        /// Starts the reconnect timer and preserves player state.
        /// </summary>
        void OnClientDisconnected(int connectionId);

        /// <summary>
        /// Called when a client attempts to reconnect.
        /// Returns true if the reconnection is accepted.
        /// </summary>
        bool TryReconnect(INetConnection newConnection, int originalConnectionId);

        /// <summary>
        /// Cancel a pending reconnection slot (e.g., player surrendered/abandoned).
        /// </summary>
        void CancelReservation(int connectionId);

        event Action<int, INetConnection> OnClientReconnected;     // (originalId, newConnection)
        event Action<int> OnReconnectWindowExpired;                 // (connectionId)
        event Action<int, float> OnCatchUpProgress;                // (connectionId, 0..1)
        event Action<int> OnCatchUpComplete;                       // (connectionId)
    }

    /// <summary>
    /// Server-side reconnection manager implementation.
    /// </summary>
    public sealed class ReconnectionManager : IReconnectionManager
    {
        private readonly Dictionary<int, ReconnectSlot> _reservedSlots =
            new Dictionary<int, ReconnectSlot>(16);

        private readonly IStateCatchUp _catchUp;

        public float ReconnectWindow { get; set; } = 300f; // 5 minutes default

        public event Action<int, INetConnection> OnClientReconnected;
        public event Action<int> OnReconnectWindowExpired;
        public event Action<int, float> OnCatchUpProgress;
        public event Action<int> OnCatchUpComplete;

        /// <param name="catchUp">Strategy for streaming game state to reconnecting client</param>
        public ReconnectionManager(IStateCatchUp catchUp = null)
        {
            _catchUp = catchUp;
        }

        public void OnClientDisconnected(int connectionId)
        {
            if (_reservedSlots.ContainsKey(connectionId)) return;

            _reservedSlots[connectionId] = new ReconnectSlot
            {
                OriginalConnectionId = connectionId,
                DisconnectTime = UnityEngine.Time.unscaledTime,
                State = ReconnectState.WaitingForReconnect
            };
        }

        public bool HasReservation(int connectionId) => _reservedSlots.ContainsKey(connectionId);

        public bool TryReconnect(INetConnection newConnection, int originalConnectionId)
        {
            if (!_reservedSlots.TryGetValue(originalConnectionId, out var slot))
                return false;

            if (slot.State != ReconnectState.WaitingForReconnect)
                return false;

            slot.State = ReconnectState.CatchingUp;
            slot.NewConnection = newConnection;
            _reservedSlots[originalConnectionId] = slot;

            // Begin state catch-up
            if (_catchUp != null)
            {
                _catchUp.BeginCatchUp(newConnection, originalConnectionId,
                    progress => OnCatchUpProgress?.Invoke(originalConnectionId, progress),
                    () =>
                    {
                        slot.State = ReconnectState.Reconnected;
                        _reservedSlots[originalConnectionId] = slot;
                        OnCatchUpComplete?.Invoke(originalConnectionId);
                        OnClientReconnected?.Invoke(originalConnectionId, newConnection);
                        _reservedSlots.Remove(originalConnectionId);
                    });
            }
            else
            {
                // No catch-up strategy: reconnect immediately
                slot.State = ReconnectState.Reconnected;
                _reservedSlots.Remove(originalConnectionId);
                OnClientReconnected?.Invoke(originalConnectionId, newConnection);
            }

            return true;
        }

        public void CancelReservation(int connectionId) => _reservedSlots.Remove(connectionId);

        /// <summary>
        /// Call each server tick to expire old reservations.
        /// </summary>
        public void Update()
        {
            float now = UnityEngine.Time.unscaledTime;
            List<int> expired = null;

            foreach (var pair in _reservedSlots)
            {
                if (pair.Value.State == ReconnectState.WaitingForReconnect &&
                    now - pair.Value.DisconnectTime >= ReconnectWindow)
                {
                    expired ??= new List<int>(4);
                    expired.Add(pair.Key);
                }
            }

            if (expired != null)
            {
                for (int i = 0; i < expired.Count; i++)
                {
                    _reservedSlots.Remove(expired[i]);
                    OnReconnectWindowExpired?.Invoke(expired[i]);
                }
            }
        }

        private struct ReconnectSlot
        {
            public int OriginalConnectionId;
            public float DisconnectTime;
            public ReconnectState State;
            public INetConnection NewConnection;
        }

        private enum ReconnectState : byte
        {
            WaitingForReconnect,
            CatchingUp,
            Reconnected
        }
    }

    /// <summary>
    /// Strategy for streaming game state to a reconnecting client.
    /// Implement for your specific game: send full world snapshot, replay recent events, etc.
    /// </summary>
    public interface IStateCatchUp
    {
        /// <summary>
        /// Begin streaming state to the reconnecting client.
        /// </summary>
        /// <param name="connection">The new connection to send data to</param>
        /// <param name="originalConnectionId">The player's original ID (to find their data)</param>
        /// <param name="onProgress">Report progress 0..1</param>
        /// <param name="onComplete">Called when catch-up is finished</param>
        void BeginCatchUp(INetConnection connection, int originalConnectionId,
            Action<float> onProgress, Action onComplete);
    }
}
