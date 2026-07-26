using System;
using System.Collections.Generic;
using CycloneGames.Networking;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Networking
{
    public sealed class GameplayNetworkObserverRegistry : IGameplayNetworkObserverSource
    {
        /// <summary>
        /// Implementation safety ceiling for observers retained by one registry. Product
        /// interest-management budgets should normally be lower than this value.
        /// </summary>
        public const int MaximumObserverCount = 65_536;

        private readonly Dictionary<int, NetworkInterestObserver> _observers;
        private long _rejectedObserverAdmissionCount;

        public GameplayNetworkObserverRegistry(int capacity = 16)
        {
            if (capacity < 0 || capacity > MaximumObserverCount)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _observers = new Dictionary<int, NetworkInterestObserver>(capacity);
        }

        public int Count => _observers.Count;
        public long RejectedObserverAdmissionCount => _rejectedObserverAdmissionCount;

        public void SetObserver(
            INetConnection connection,
            Vector3 position,
            float radius,
            uint layerMask = uint.MaxValue,
            int teamId = 0)
        {
            if (!TrySetObserver(connection, position, radius, layerMask, teamId))
            {
                throw CreateObserverCapacityException();
            }
        }

        /// <summary>
        /// Attempts to add or update an observer. Returns false only when a new observer would
        /// exceed the implementation ceiling; updates remain valid at capacity.
        /// </summary>
        public bool TrySetObserver(
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

            return TrySetObserver(
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
            if (!TrySetObserver(connectionId, position, radius, layerMask, playerId, teamId, connection))
            {
                throw CreateObserverCapacityException();
            }
        }

        /// <summary>
        /// Attempts to add or update an observer. Returns false only for new admission at the
        /// implementation ceiling.
        /// </summary>
        public bool TrySetObserver(
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

            if (!_observers.ContainsKey(connectionId) && _observers.Count >= MaximumObserverCount)
            {
                if (_rejectedObserverAdmissionCount < long.MaxValue)
                {
                    _rejectedObserverAdmissionCount++;
                }

                return false;
            }

            _observers[connectionId] = new NetworkInterestObserver(
                connection,
                position,
                radius,
                layerMask,
                playerId,
                teamId);
            return true;
        }

        /// <summary>Returns an allocation-free O(1) observer admission snapshot.</summary>
        public GameplayNetworkObserverAdmissionSnapshot GetAdmissionSnapshot()
        {
            return new GameplayNetworkObserverAdmissionSnapshot(
                _observers.Count,
                MaximumObserverCount,
                _rejectedObserverAdmissionCount);
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

        private static InvalidOperationException CreateObserverCapacityException()
        {
            return new InvalidOperationException(
                $"Observer capacity reached the implementation ceiling of {MaximumObserverCount}.");
        }
    }
}
