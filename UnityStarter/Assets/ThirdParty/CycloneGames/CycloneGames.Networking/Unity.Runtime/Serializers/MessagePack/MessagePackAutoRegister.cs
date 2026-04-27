#if MESSAGEPACK && UNITY_5_3_OR_NEWER
using CycloneGames.Networking.Serialization;
using UnityEngine;

namespace CycloneGames.Networking.Serializer.MessagePack
{
    internal static class MessagePackAutoRegister
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            SerializerFactory.RegisterCreator(
                SerializerType.MessagePack,
                () => new MessagePackSerializerAdapter());
        }
    }
}
#endif
