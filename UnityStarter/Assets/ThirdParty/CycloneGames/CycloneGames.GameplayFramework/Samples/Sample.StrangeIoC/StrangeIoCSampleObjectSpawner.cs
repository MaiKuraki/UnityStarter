using UnityEngine;
using strange.extensions.injector.api;
using CycloneGames.Factory;
using CycloneGames.Logger;

namespace CycloneGames.GameplayFramework.Sample.StrangeIoC
{
    public class StrangeIoCSampleObjectSpawner : IFactory<MonoBehaviour, MonoBehaviour>
    {
        [Inject] public IInjectionBinder injectionBinder { get; set; }

        public MonoBehaviour Create(MonoBehaviour prefab)
        {
            if (prefab == null)
            {
                CLogger.LogError($"Invalid Prefab to spawn");
                return null;
            }

            var obj = UnityEngine.Object.Instantiate(prefab);
            injectionBinder.injector.Inject(obj);
            return obj;
        }
    }
}