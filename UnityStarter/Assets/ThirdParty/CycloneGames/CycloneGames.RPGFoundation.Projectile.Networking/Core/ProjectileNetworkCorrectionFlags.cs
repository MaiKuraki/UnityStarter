using System;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    [Flags]
    public enum ProjectileNetworkCorrectionFlags : uint
    {
        None = 0u,
        Transform = 1u << 0,
        Velocity = 1u << 1,
        Timeline = 1u << 2,
        Lifecycle = 1u << 3,
        Target = 1u << 4,
        HardSnap = 1u << 15,
        FullReset = 1u << 31
    }
}
