using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public static class ProjectileNetworkVectorExtensions
    {
        public static NetworkVector3 ToNetworkVector3(this ProjectileVector3 value)
        {
            return new NetworkVector3(value.X, value.Y, value.Z);
        }

        public static ProjectileVector3 ToProjectileVector3(this NetworkVector3 value)
        {
            return new ProjectileVector3(value.X, value.Y, value.Z);
        }
    }
}
