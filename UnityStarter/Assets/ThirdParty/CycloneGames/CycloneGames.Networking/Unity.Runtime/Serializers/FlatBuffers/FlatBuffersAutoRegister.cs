#if FLATBUFFERS && UNITY_5_3_OR_NEWER
using CycloneGames.Networking.Serialization;
using UnityEngine;

namespace CycloneGames.Networking.Serializer.FlatBuffers
{
    internal static class FlatBuffersAutoRegister
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            SerializerFactory.RegisterCreator(
                SerializerType.FlatBuffers,
                () => new FlatBuffersSerializerAdapter());
        }
    }
}
#endif
