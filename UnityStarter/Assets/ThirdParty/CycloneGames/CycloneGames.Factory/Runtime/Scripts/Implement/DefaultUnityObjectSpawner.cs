namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// A default implementation of <see cref="IUnityObjectSpawner"/> that uses Unity's Instantiate.
    /// This is safe for DI or manual wiring and generates no GC allocations beyond the Instantiate itself.
    /// </summary>
    public sealed class DefaultUnityObjectSpawner : IUnityObjectSpawner
    {
        public T Create<T>(T origin) where T : UnityEngine.Object
        {
            if (origin == null) return null;
            return UnityEngine.Object.Instantiate(origin);
        }

        public T Create<T>(T origin, UnityEngine.Transform parent) where T : UnityEngine.Object
        {
            if (origin == null) return null;
            return UnityEngine.Object.Instantiate(origin, parent);
        }
    }
}