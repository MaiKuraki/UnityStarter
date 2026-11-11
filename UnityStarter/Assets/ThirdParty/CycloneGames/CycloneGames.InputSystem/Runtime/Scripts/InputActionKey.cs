using System;

namespace CycloneGames.InputSystem.Runtime
{
    internal readonly struct InputActionKey : IEquatable<InputActionKey>
    {
        public readonly string ContextName;
        public readonly string MapName;
        public readonly string ActionName;

        private readonly int _hash;

        public InputActionKey(string mapName, string actionName)
            : this(null, mapName, actionName)
        {
        }

        public InputActionKey(string contextName, string mapName, string actionName)
        {
            ContextName = contextName;
            MapName = mapName;
            ActionName = actionName;
            _hash = ComputeHash(contextName, mapName, actionName);
        }

        public bool Equals(InputActionKey other)
        {
            return string.Equals(ContextName, other.ContextName, StringComparison.Ordinal) &&
                   string.Equals(MapName, other.MapName, StringComparison.Ordinal) &&
                   string.Equals(ActionName, other.ActionName, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is InputActionKey other && Equals(other);
        }

        /// <summary>
        /// Returns the hash computed once during construction. Key identity only has to stay consistent inside a
        /// single process, so the runtime string hash is used: the combined value is stored once per key instead of
        /// being recomputed on every dictionary probe, and the per-name work uses the runtime's own string hashing
        /// implementation rather than a hand-written per-character loop.
        /// The value is not a content fingerprint and is not reproducible: never persist it, transmit it, compare
        /// it across processes, or log it for cross-run comparison. Deterministic cross-process identities remain
        /// available through <see cref="InputHashUtility.GetActionId(string, string, string)"/>, where identities
        /// are exported.
        /// </summary>
        public override int GetHashCode()
        {
            return _hash;
        }

        private static int ComputeHash(string contextName, string mapName, string actionName)
        {
            if (contextName == null && mapName == null && actionName == null)
            {
                // Keep the default instance and an explicitly all-null instance interchangeable, otherwise two
                // keys that compare equal would land in different buckets.
                return 0;
            }

            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (contextName == null ? 0 : contextName.GetHashCode());
                hash = hash * 31 + (mapName == null ? 0 : mapName.GetHashCode());
                hash = hash * 31 + (actionName == null ? 0 : actionName.GetHashCode());
                return hash;
            }
        }

        public static bool operator ==(InputActionKey left, InputActionKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(InputActionKey left, InputActionKey right)
        {
            return !left.Equals(right);
        }
    }
}
