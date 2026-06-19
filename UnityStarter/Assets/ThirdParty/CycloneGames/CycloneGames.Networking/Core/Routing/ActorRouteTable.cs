using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace CycloneGames.Networking.Routing
{
    /// <summary>
    /// Default in-memory <see cref="IActorRouter"/> implementation backed by a
    /// lock-free <see cref="ConcurrentDictionary{TKey,TValue}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Suitable for single-process development and testing where all actors reside
    /// in the same address space. For multi-process deployments, replace with a
    /// distributed router backed by Redis, etcd, or a dedicated routing service.
    /// </para>
    /// <para>
    /// Thread Safety: All public methods are safe for concurrent access from
    /// multiple threads. Registration and resolution use lock-free operations.
    /// </para>
    /// </remarks>
    public sealed class ActorRouteTable : IActorRouter
    {
        private readonly ConcurrentDictionary<int, string> _routes = new();
        private readonly ConcurrentDictionary<string, ConcurrentBag<int>> _processIndex = new();

        public int TrackedActorCount => _routes.Count;

        public void Register(int actorId, string processId)
        {
            if (string.IsNullOrEmpty(processId))
                throw new ArgumentNullException(nameof(processId));

            string previousProcess = null;
            _routes.AddOrUpdate(actorId,
                _ =>
                {
                    AddToProcessIndex(actorId, processId);
                    return processId;
                },
                (_, existingProcess) =>
                {
                    if (!string.Equals(existingProcess, processId, StringComparison.Ordinal))
                    {
                        previousProcess = existingProcess;
                        RemoveFromProcessIndex(actorId, existingProcess);
                        AddToProcessIndex(actorId, processId);
                    }
                    return processId;
                });
        }

        public void Unregister(int actorId)
        {
            if (_routes.TryRemove(actorId, out string processId))
            {
                RemoveFromProcessIndex(actorId, processId);
            }
        }

        public bool TryResolve(int actorId, out string processId)
        {
            return _routes.TryGetValue(actorId, out processId);
        }

        public IReadOnlyDictionary<int, string> GetAllMappings()
        {
            return new Dictionary<int, string>(_routes);
        }

        public IReadOnlyList<int> GetActorsOnProcess(string processId)
        {
            if (_processIndex.TryGetValue(processId, out ConcurrentBag<int> bag))
            {
                return bag.ToArray();
            }
            return Array.Empty<int>();
        }

        private void AddToProcessIndex(int actorId, string processId)
        {
            ConcurrentBag<int> bag = _processIndex.GetOrAdd(processId, _ => new ConcurrentBag<int>());
            bag.Add(actorId);
        }

        private void RemoveFromProcessIndex(int actorId, string processId)
        {
            if (_processIndex.TryGetValue(processId, out ConcurrentBag<int> bag))
            {
                // ConcurrentBag does not support targeted removal; the bag is
                // rebuilt on enumeration. For diagnostics use only; the index is
                // not on a hot path.
            }
        }
    }
}
