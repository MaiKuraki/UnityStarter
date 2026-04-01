using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Samples.PureUnity
{
    /// <summary>
    /// Example: implement IUnityObjectSpawner to add custom behavior (e.g. logging, analytics).
    /// If you don't need customization, use DefaultUnityObjectSpawner directly.
    /// </summary>
    public class SimpleUnitySpawner : IUnityObjectSpawner
    {
        private readonly DefaultUnityObjectSpawner _inner = new DefaultUnityObjectSpawner();

        public T Create<T>(T origin) where T : UnityEngine.Object
        {
            return _inner.Create(origin);
        }

        public T Create<T>(T origin, UnityEngine.Transform parent) where T : UnityEngine.Object
        {
            return _inner.Create(origin, parent);
        }
    }
}
