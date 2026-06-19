using System;
using System.Collections.Generic;
using CycloneGames.Networking;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Networking
{
    public sealed class GameplayNetworkObserverRegistry : IGameplayNetworkObserverSource
    {
        private readonly Dictionary<int, NetworkInterestObserver> _observers;

        public GameplayNetworkObserverRegistry(int capacity = 16)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _observers = new Dictionary<int, NetworkInterestObserver>(capacity);
        }

        public int Count => _observers.Count;

        public void SetObserver(
            INetConnection connection,
            Vector3 position,
            float radius,
            uint layerMask = uint.MaxValue,
            int teamId = 0)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            SetObserver(
                connection.ConnectionId,
                new NetworkVector3(position.x, position.y, position.z),
                radius,
                layerMask,
                connection.PlayerId,
                teamId,
                connection);
        }

        public void SetObserver(
            int connectionId,
            NetworkVector3 position,
            float radius,
            uint layerMask = uint.MaxValue,
            ulong playerId = 0UL,
            int teamId = 0,
            INetConnection connection = null)
        {
            if (radius < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }

            if (!position.IsFinite())
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }

            _observers[connectionId] = new NetworkInterestObserver(
                connection,
                position,
                radius,
                layerMask,
                playerId,
                teamId);
        }

        public bool TryGetObserver(int connectionId, out NetworkInterestObserver observer)
        {
            return _observers.TryGetValue(connectionId, out observer);
        }

        public bool Remove(INetConnection connection)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            return Remove(connection.ConnectionId);
        }

        public bool Remove(int connectionId)
        {
            return _observers.Remove(connectionId);
        }

        public void Clear()
        {
            _observers.Clear();
        }
    }
}
