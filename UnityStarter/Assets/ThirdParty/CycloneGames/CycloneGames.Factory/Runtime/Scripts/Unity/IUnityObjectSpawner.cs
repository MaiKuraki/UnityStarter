using UnityEngine;

namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// Creates Unity objects on the Unity main thread.
    /// </summary>
    public interface IUnityObjectSpawner : IFactory
    {
        T Create<T>(T origin) where T : Object;

        T Create<T>(T origin, Transform parent) where T : Object;
    }
}
