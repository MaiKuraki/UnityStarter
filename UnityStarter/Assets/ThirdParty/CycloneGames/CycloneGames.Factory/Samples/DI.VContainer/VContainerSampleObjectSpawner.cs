using CycloneGames.Factory.Runtime;
using VContainer;

namespace CycloneGames.Factory.Samples.VContainer
{
    public class VContainerSampleObjectSpawner : IUnityObjectSpawner
    {
        // VContainer injects the resolver for us
        [Inject]
        readonly IObjectResolver _objectResolver;

        public T Create<T>(T origin) where T : UnityEngine.Object
        {
            var obj = UnityEngine.Object.Instantiate(origin);
            _objectResolver.Inject(obj); // The critical DI step
            return obj;
        }
    }
}