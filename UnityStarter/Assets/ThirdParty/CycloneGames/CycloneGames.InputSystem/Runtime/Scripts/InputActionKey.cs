using System;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Zero-GC struct key for action lookups. Eliminates boxing from tuple-based keys.
    /// </summary>
    internal readonly struct InputActionKey : IEquatable<InputActionKey>
    {
        public readonly string MapName;
        public readonly string ActionName;

        public InputActionKey(string mapName, string actionName)
        {
            MapName = mapName;
            ActionName = actionName;
        }

        public bool Equals(InputActionKey other)
        {
            return string.Equals(MapName, other.MapName, StringComparison.Ordinal) &&
                   string.Equals(ActionName, other.ActionName, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is InputActionKey other && Equals(other);
        }

        /// <summary>
        /// Compound hash: hash = (hash * 31) + componentHash. Uses primes 17 and 31 for optimal distribution.
        /// 31 = 2^5 - 1 allows compiler bit-shift optimization.
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (MapName?.GetHashCode() ?? 0);
                hash = hash * 31 + (ActionName?.GetHashCode() ?? 0);
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