using System;

namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public readonly struct ProjectileDefinitionId : IEquatable<ProjectileDefinitionId>
    {
        public readonly int Value;

        public ProjectileDefinitionId(int value)
        {
            Value = value;
        }

        public bool IsValid
        {
            get
            {
                return Value != 0;
            }
        }

        public bool Equals(ProjectileDefinitionId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is ProjectileDefinitionId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(ProjectileDefinitionId left, ProjectileDefinitionId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ProjectileDefinitionId left, ProjectileDefinitionId right)
        {
            return !left.Equals(right);
        }
    }
}
