using System;

namespace CycloneGames.InputSystem.Runtime
{
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
        /// Compound hash using deterministic FNV-1a for cross-platform consistency.
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + InputHashUtility.GetDeterministicHashCode(MapName ?? string.Empty);
                hash = hash * 31 + InputHashUtility.GetDeterministicHashCode(ActionName ?? string.Empty);
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