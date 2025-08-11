using System;

namespace CycloneGames.Networking
{
    public interface INetSerializer
    {
        ArraySegment<byte> Serialize<T>(in T value) where T : struct;
        T Deserialize<T>(in ArraySegment<byte> data) where T : struct;
    }
}