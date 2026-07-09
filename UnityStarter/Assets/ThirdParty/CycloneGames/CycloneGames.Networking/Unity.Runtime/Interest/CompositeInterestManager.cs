using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Interest
{
    /// <summary>
    /// Composite interest manager that combines multiple strategies.
    /// For example: GridInterestManager for spatial culling + GroupInterestManager for instancing.
    /// Result is the UNION of all child managers' results.
    /// </summary>
    public sealed class CompositeInterestManager : IInterestManager
    {
        private readonly List<IInterestManager> _managers = new List<IInterestManager>(4);
        private readonly HashSet<uint> _tempResults = new HashSet<uint>();

        public void Add(IInterestManager manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            if (_managers.Contains(manager))
            {
                return;
            }

            _managers.Add(manager);
        }

        public bool Remove(IInterestManager manager) => _managers.Remove(manager);

        public void PreUpdate(IReadOnlyList<INetworkEntity> allEntities)
        {
            if (allEntities == null)
            {
                throw new ArgumentNullException(nameof(allEntities));
            }

            for (int i = 0; i < _managers.Count; i++)
            {
                _managers[i].PreUpdate(allEntities);
            }
        }

        public void RebuildForConnection(INetConnection connection, IReadOnlyList<INetworkEntity> allEntities, HashSet<uint> results)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (allEntities == null)
            {
                throw new ArgumentNullException(nameof(allEntities));
            }

            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();

            for (int i = 0; i < _managers.Count; i++)
            {
                _tempResults.Clear();
                _managers[i].RebuildForConnection(connection, allEntities, _tempResults);
                results.UnionWith(_tempResults);
            }
        }

        public bool IsVisible(INetConnection connection, INetworkEntity entity)
        {
            for (int i = 0; i < _managers.Count; i++)
            {
                if (_managers[i].IsVisible(connection, entity))
                    return true;
            }
            return false;
        }
    }
}
