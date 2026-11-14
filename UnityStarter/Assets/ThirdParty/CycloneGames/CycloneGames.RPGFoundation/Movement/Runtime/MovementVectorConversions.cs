using CycloneGames.RPGFoundation.Movement.Core;
using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    /// <summary>
    /// Bridges the engine-independent movement contracts with Unity vectors. Conversions live in the
    /// runtime layer so <see cref="MovementVector3"/> consumers stay free of Unity types.
    /// </summary>
    public static class MovementVectorConversions
    {
        public static MovementVector3 ToMovementVector3(this Vector3 value)
        {
            return new MovementVector3(value.x, value.y, value.z);
        }

        public static Vector3 ToVector3(this MovementVector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        public static MovementVector3 ToMovementVector3(this float3 value)
        {
            return new MovementVector3(value.x, value.y, value.z);
        }

        public static float3 ToFloat3(this MovementVector3 value)
        {
            return new float3(value.X, value.Y, value.Z);
        }
    }
}
