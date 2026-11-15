using System;

namespace CycloneGames.RPGFoundation.Projectile.Core
{
    /// <summary>
    /// Slot plus generation handle. The generation is monotonic per slot and wide enough
    /// that a released handle can never alias a later occupant, including after Clear().
    /// </summary>
    public readonly struct ProjectileHandle : IEquatable<ProjectileHandle>
    {
        public readonly int Slot;
        public readonly ulong Generation;

        public ProjectileHandle(int slot, ulong generation)
        {
            Slot = slot;
            Generation = generation;
        }

        public bool IsValid
        {
            get
            {
                return Slot >= 0 && Generation > 0UL;
            }
        }

        public bool Equals(ProjectileHandle other)
        {
            return Slot == other.Slot && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is ProjectileHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                ulong folded = Generation ^ (Generation >> 32);
                return (Slot * 397) ^ (int)folded;
            }
        }

        public override string ToString()
        {
            return IsValid ? $"{Slot}:{Generation}" : "Invalid";
        }

        public static bool operator ==(ProjectileHandle left, ProjectileHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ProjectileHandle left, ProjectileHandle right)
        {
            return !left.Equals(right);
        }
    }
}
