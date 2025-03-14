using CycloneGames.Core;
using UnityEngine;
using strange.extensions.injector.api;

namespace CycloneGames.GameplayFramework.Sample.StrangeIoC
{
    public class StrangeIoCSampleObjectSpawner : IObjectSpawner
    {
        [Inject] public IInjectionBinder injectionBinder { get; set; }
        
        public Object SpawnObject(Object original)
        {
            var obj = UnityEngine.Object.Instantiate(original);
            injectionBinder.injector.Inject(obj);
            return obj;
        }

        public Object SpawnObject<T>(T original) where T : Object
        {
            var obj = UnityEngine.Object.Instantiate(original);
            injectionBinder.injector.Inject(obj);
            return obj;
        }

        public GameObject SpawnObjectOnNewGameObject<TComponent>(string name = "") where TComponent : Component
        {
            var go = new UnityEngine.GameObject(name);
            var component = go.AddComponent<TComponent>();
            injectionBinder.injector.Inject(component);
            return go;
        }
    }
}