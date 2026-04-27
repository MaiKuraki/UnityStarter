#if PROTOBUF && UNITY_5_3_OR_NEWER
using CycloneGames.Networking.Serialization;
using UnityEngine;

namespace CycloneGames.Networking.Serializer.ProtoBuf
{
    internal static class ProtoBufAutoRegister
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            SerializerFactory.RegisterCreator(
                SerializerType.ProtoBuf,
                () => new ProtoBufSerializerAdapter());
        }
    }
}
#endif
