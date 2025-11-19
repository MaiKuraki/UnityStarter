using UnityEngine;
using strange.extensions.injector.api;
using CycloneGames.Factory.Runtime;
using CycloneGames.Logger;

namespace CycloneGames.GameplayFramework.Sample.StrangeIoC
{
    public class StrangeIoCSampleObjectSpawner : IUnityObjectSpawner
    {
        [Inject] public IInjectionBinder injectionBinder { get; set; }

        public T Create<T>(T origin) where T : Object
        {
            if (origin == null)
            {
                CLogger.LogError($"Invalid Prefab to spawn");
                return null;
            }

            var obj = UnityEngine.Object.Instantiate(origin);
            injectionBinder.injector.Inject(obj);
            return obj;
        }

        public T Create<T>(T origin, Transform parent) where T : Object
        {
            if (origin == null)
            {
                CLogger.LogError($"Invalid Prefab to spawn");
                return null;
            }

            var obj = UnityEngine.Object.Instantiate(origin, parent);
            injectionBinder.injector.Inject(obj);
            return obj;
        }
    }
}