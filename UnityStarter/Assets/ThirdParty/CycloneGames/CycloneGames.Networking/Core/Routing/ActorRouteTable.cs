using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace CycloneGames.Networking.Routing
{
    /// <summary>
    /// Default in-memory <see cref="IActorRouter"/> implementation backed by
    /// concurrent actor and process indexes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Suitable for single-process development and testing where all actors reside
    /// in the same address space. For multi-process deployments, replace with a
    /// distributed router backed by Redis, etcd, or a dedicated routing service.
    /// </para>
    /// <para>
    /// Concurrent dictionary operations protect each index from corruption. The actor and process
    /// indexes are updated separately, so concurrent snapshots can observe a transient migration state.
    /// Use one routing owner when cross-index consistency is required. This helper has no actor-count
    /// capacity policy; a production composition must bound registrations before calling it.
    /// </para>
    /// </remarks>
    public sealed class ActorRouteTable : IActorRouter
    {
        private readonly ConcurrentDictionary<int, string> _routes = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, byte>> _processIndex =
            new ConcurrentDictionary<string, ConcurrentDictionary<int, byte>>(StringComparer.Ordinal);

        public int TrackedActorCount => _routes.Count;

        public void Register(int actorId, string processId)
        {
            if (string.IsNullOrEmpty(processId))
            {
                throw new ArgumentNullException(nameof(processId));
            }

            while (true)
            {
                if (!_routes.TryGetValue(actorId, out string existingProcess))
                {
                    if (_routes.TryAdd(actorId, processId))
                    {
                        AddToProcessIndex(actorId, processId);
                        return;
                    }

                    continue;
                }

                if (string.Equals(existingProcess, processId, StringComparison.Ordinal))
                {
                    AddToProcessIndex(actorId, processId);
                    return;
                }

                if (_routes.TryUpdate(actorId, processId, existingProcess))
                {
                    RemoveFromProcessIndex(actorId, existingProcess);
                    AddToProcessIndex(actorId, processId);
                    return;
                }
            }
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
            if (string.IsNullOrEmpty(processId))
            {
                return Array.Empty<int>();
            }

            if (!_processIndex.TryGetValue(processId, out ConcurrentDictionary<int, byte> actors) || actors.IsEmpty)
            {
                return Array.Empty<int>();
            }

            int[] result = new int[Math.Max(actors.Count, 1)];
            int count = 0;
            foreach (int actorId in actors.Keys)
            {
                if (count == result.Length)
                {
                    Array.Resize(ref result, result.Length * 2);
                }

                result[count++] = actorId;
            }

            if (count == 0)
            {
                return Array.Empty<int>();
            }

            if (count != result.Length)
            {
                Array.Resize(ref result, count);
            }

            return result;
        }

        private void AddToProcessIndex(int actorId, string processId)
        {
            ConcurrentDictionary<int, byte> actors = _processIndex.GetOrAdd(
                processId,
                _ => new ConcurrentDictionary<int, byte>());
            actors.TryAdd(actorId, 0);
        }

        private void RemoveFromProcessIndex(int actorId, string processId)
        {
            if (_processIndex.TryGetValue(processId, out ConcurrentDictionary<int, byte> actors))
            {
                actors.TryRemove(actorId, out _);
            }
        }
    }
}
