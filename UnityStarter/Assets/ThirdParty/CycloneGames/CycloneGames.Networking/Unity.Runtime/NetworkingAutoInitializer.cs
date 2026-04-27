using CycloneGames.Networking.Serialization;
using CycloneGames.Networking.Unity.Runtime.Serialization;
using UnityEngine;

namespace CycloneGames.Networking.Unity.Runtime
{
    internal static class NetworkingAutoInitializer
    {
#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#endif
        private static void Initialize()
        {
            SerializerFactory.RegisterCreator(SerializerType.Json, () => UnityJsonSerializerAdapter.Instance);
            SerializerFactory.SetDefaultCreator(() => UnityJsonSerializerAdapter.Instance);
        }
    }
}