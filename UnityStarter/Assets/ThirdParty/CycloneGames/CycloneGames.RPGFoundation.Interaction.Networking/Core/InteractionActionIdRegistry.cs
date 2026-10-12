using System;
using System.Collections.Generic;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Networking
{
    /// <summary>
    /// Bidirectional registry between authoring-time string action IDs and the
    /// deterministic 64-bit hashes carried on the wire. Registration happens at
    /// composition time from the shared action catalog on both peers; lookups on
    /// the receive path are dictionary reads with no allocation.
    /// </summary>
    /// <remarks>
    /// Collision policy: two different action IDs producing the same hash abort
    /// registration with an exception. Wire values that resolve to no registered
    /// action are rejected by the caller; a hash is never trusted without a
    /// local registration.
    /// </remarks>
    public sealed class InteractionActionIdRegistry
    {
        private readonly Dictionary<ulong, string> _hashToId;
        private readonly Dictionary<string, ulong> _idToHash;

        public InteractionActionIdRegistry(int capacity = 64)
        {
            _hashToId = new Dictionary<ulong, string>(capacity);
            _idToHash = new Dictionary<string, ulong>(capacity, StringComparer.Ordinal);
        }

        public int Count => _hashToId.Count;

        public ulong Register(string actionId)
        {
            if (string.IsNullOrEmpty(actionId))
                throw new ArgumentNullException(nameof(actionId));
            if (actionId.Length > InteractionNetworkProtocol.MAX_ACTION_ID_LENGTH)
                throw new ArgumentOutOfRangeException(nameof(actionId));

            ulong hash = InteractionStableId.Hash64(actionId);
            if (_idToHash.TryGetValue(actionId, out ulong existing))
                return existing;

            if (_hashToId.TryGetValue(hash, out string other) && !string.Equals(other, actionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Action ID hash collision between '{other}' and '{actionId}'. Rename one action.");
            }

            _idToHash.Add(actionId, hash);
            _hashToId.Add(hash, actionId);
            return hash;
        }

        public bool TryResolve(ulong actionIdHash, out string actionId)
        {
            return _hashToId.TryGetValue(actionIdHash, out actionId);
        }

        public bool TryGetHash(string actionId, out ulong hash)
        {
            if (actionId == null)
            {
                hash = 0UL;
                return false;
            }

            return _idToHash.TryGetValue(actionId, out hash);
        }
    }

    /// <summary>
    /// Process-wide default registry used by <see cref="InteractionNetworkRequest"/>
    /// mapping helpers when no explicit registry is supplied. Compose roots that
    /// isolate registries per world should pass their own instance instead.
    /// </summary>
    public static class InteractionActionIds
    {
        private static readonly InteractionActionIdRegistry s_default = new InteractionActionIdRegistry();

        public static InteractionActionIdRegistry Default => s_default;

        public static ulong ComputeHash(string actionId)
        {
            return string.IsNullOrEmpty(actionId) ? InteractionStableId.None : InteractionStableId.Hash64(actionId);
        }
    }
}
