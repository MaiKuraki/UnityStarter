#if NEWTONSOFT_JSON && UNITY_5_3_OR_NEWER
using CycloneGames.Networking.Serialization;
using UnityEngine;

namespace CycloneGames.Networking.Serializer.NewtonsoftJson
{
    internal static class NewtonsoftJsonAutoRegister
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            SerializerFactory.RegisterCreator(
                SerializerType.NewtonsoftJson,
                () => NewtonsoftJsonSerializerAdapter.Instance);
        }
    }
}
#endif
