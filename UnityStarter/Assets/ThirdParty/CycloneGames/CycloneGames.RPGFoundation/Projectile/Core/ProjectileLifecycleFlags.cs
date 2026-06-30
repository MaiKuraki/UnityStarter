using System;

namespace CycloneGames.RPGFoundation.Projectile.Core
{
    [Flags]
    public enum ProjectileLifecycleFlags : uint
    {
        None = 0u,
        Predicted = 1u << 0,
        Authoritative = 1u << 1,
        DespawnOnHit = 1u << 2,
        IgnoreOwner = 1u << 3,
        HasVisualArc = 1u << 4
    }
}
