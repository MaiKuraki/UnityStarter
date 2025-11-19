using CycloneGames.Factory.Runtime;

// A basic spawner for non-DI environments.
namespace CycloneGames.Factory.Samples.PureUnity
{
    public class SimpleUnitySpawner : IUnityObjectSpawner
    {
        public T Create<T>(T origin) where T : UnityEngine.Object
        {
            return UnityEngine.Object.Instantiate(origin);
        }

        public T Create<T>(T origin, UnityEngine.Transform parent) where T : UnityEngine.Object
        {
            return UnityEngine.Object.Instantiate(origin, parent);
        }
    }
}
