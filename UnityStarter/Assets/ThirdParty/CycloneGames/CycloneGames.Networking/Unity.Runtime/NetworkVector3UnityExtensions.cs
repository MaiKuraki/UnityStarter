using UnityEngine;

namespace CycloneGames.Networking
{
    public static class NetworkVector3UnityExtensions
    {
        public static NetworkVector3 ToNetwork(this Vector3 v) => new(v.x, v.y, v.z);
        public static Vector3 ToUnity(this NetworkVector3 v) => new(v.X, v.Y, v.Z);
    }
}
