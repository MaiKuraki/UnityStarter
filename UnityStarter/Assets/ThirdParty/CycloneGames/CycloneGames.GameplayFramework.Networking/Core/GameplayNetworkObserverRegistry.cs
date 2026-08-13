using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Networking;

namespace CycloneGames.GameplayFramework.Networking
{
    public sealed class GameplayNetworkObserverRegistry : IGameplayNetworkObserverSource
    {
        /// <summary>
        /// Implementation safety ceiling for observers retained by one registry. Product
        /// interest-management budgets should normally be lower than this value.
        /// </summary>
        public const int MaximumSupportedObserverCount = 65_536;

        private readonly Dictionary<int, NetworkInterestObserver> _observers;
        private readonly int _maximumObserverCount;
        private readonly int _ownerThreadId;
        private long _rejectedObserverAdmissionCount;

        public GameplayNetworkObserverRegistry(
            int initialCapacity = 16,
            int maximumObserverCount = MaximumSupportedObserverCount)
        {
            if (maximumObserverCount < 0 || maximumObserverCount > MaximumSupportedObserverCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumObserverCount));
            }

            if (initialCapacity < 0 || initialCapacity > maximumObserverCount)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _maximumObserverCount = maximumObserverCount;
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            _observers = new Dictionary<int, NetworkInterestObserver>(initialCapacity);
        }

        public int Count
        {
            get { AssertOwnerThread(); return _observers.Count; }
        }
        public int MaximumObserverCount => _maximumObserverCount;
        public long RejectedObserverAdmissionCount
        {
            get { AssertOwnerThread(); return _rejectedObserverAdmissionCount; }
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
            AssertOwnerThread();
            if (!TrySetObserver(connectionId, position, radius, layerMask, playerId, teamId, connection))
            {
                throw CreateObserverLimitException();
            }
        }

        /// <summary>
        /// Attempts to add or update an observer. Returns false only for new admission at the
        /// configured product budget.
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
            AssertOwnerThread();
            ValidateConnectionId(connectionId);
            if (radius < 0f || float.IsNaN(radius) || float.IsInfinity(radius))
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }

            if (!position.IsFinite())
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }

            if (!_observers.ContainsKey(connectionId) && _observers.Count >= _maximumObserverCount)
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
            AssertOwnerThread();
            return new GameplayNetworkObserverAdmissionSnapshot(
                _observers.Count,
                _maximumObserverCount,
                _rejectedObserverAdmissionCount);
        }

        public bool TryGetObserver(int connectionId, out NetworkInterestObserver observer)
        {
            AssertOwnerThread();
            ValidateConnectionId(connectionId);
            return _observers.TryGetValue(connectionId, out observer);
        }

        public bool Remove(INetConnection connection)
        {
            AssertOwnerThread();
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            return Remove(connection.ConnectionId);
        }

        public bool Remove(int connectionId)
        {
            AssertOwnerThread();
            ValidateConnectionId(connectionId);
            return _observers.Remove(connectionId);
        }

        public void Clear()
        {
            AssertOwnerThread();
            _observers.Clear();
        }

        private void AssertOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "GameplayNetworkObserverRegistry must be accessed on its owning thread.");
            }
        }

        private static void ValidateConnectionId(int connectionId)
        {
            if (connectionId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(connectionId),
                    "ConnectionId must be positive.");
            }
        }

        private InvalidOperationException CreateObserverLimitException()
        {
            return new InvalidOperationException(
                $"Observer capacity reached the configured maximum of {_maximumObserverCount}.");
        }
    }
}
