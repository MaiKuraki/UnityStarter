using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Session
{
    public interface IReconnectionManager
    {
        double ReconnectWindow { get; set; }

        bool HasReservation(int connectionId);

        void OnClientDisconnected(int connectionId, double disconnectTime);
        bool TryReconnect(INetConnection newConnection, int originalConnectionId);
        void CancelReservation(int connectionId);
        void Update(double currentTime);

        event Action<int, INetConnection> OnClientReconnected;
        event Action<int> OnReconnectWindowExpired;
        event Action<int, float> OnCatchUpProgress;
        event Action<int> OnCatchUpComplete;
    }

    public sealed class ReconnectionManager : IReconnectionManager
    {
        private readonly Dictionary<int, ReconnectSlot> _reservedSlots =
            new Dictionary<int, ReconnectSlot>(16);

        private readonly IStateCatchUp _catchUp;

        public double ReconnectWindow { get; set; } = 300.0;

        public event Action<int, INetConnection> OnClientReconnected;
        public event Action<int> OnReconnectWindowExpired;
        public event Action<int, float> OnCatchUpProgress;
        public event Action<int> OnCatchUpComplete;

        public ReconnectionManager(IStateCatchUp catchUp = null)
        {
            _catchUp = catchUp;
        }

        public void OnClientDisconnected(int connectionId, double disconnectTime)
        {
            if (_reservedSlots.ContainsKey(connectionId)) return;

            _reservedSlots[connectionId] = new ReconnectSlot
            {
                OriginalConnectionId = connectionId,
                DisconnectTime = disconnectTime,
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
                slot.State = ReconnectState.Reconnected;
                _reservedSlots.Remove(originalConnectionId);
                OnClientReconnected?.Invoke(originalConnectionId, newConnection);
            }

            return true;
        }

        public void CancelReservation(int connectionId) => _reservedSlots.Remove(connectionId);

        public void Update(double currentTime)
        {
            List<int> expired = null;

            foreach (var pair in _reservedSlots)
            {
                if (pair.Value.State == ReconnectState.WaitingForReconnect &&
                    currentTime - pair.Value.DisconnectTime >= ReconnectWindow)
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
            public double DisconnectTime;
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

    public interface IStateCatchUp
    {
        void BeginCatchUp(INetConnection connection, int originalConnectionId,
            Action<float> onProgress, Action onComplete);
    }
}