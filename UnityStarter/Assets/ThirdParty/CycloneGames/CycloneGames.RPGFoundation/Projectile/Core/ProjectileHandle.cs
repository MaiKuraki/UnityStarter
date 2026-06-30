using System;

namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public readonly struct ProjectileHandle : IEquatable<ProjectileHandle>
    {
        public readonly int Slot;
        public readonly int Generation;

        public ProjectileHandle(int slot, int generation)
        {
            Slot = slot;
            Generation = generation;
        }

        public bool IsValid
        {
            get
            {
                return Slot >= 0 && Generation > 0;
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
                return (Slot * 397) ^ Generation;
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
