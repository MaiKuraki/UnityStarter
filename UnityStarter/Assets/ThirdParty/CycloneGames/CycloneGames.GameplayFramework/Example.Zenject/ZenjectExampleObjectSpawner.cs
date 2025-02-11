using CycloneGames.Core;
using Zenject;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Example.Zenject
{
    public class ZenjectExampleObjectSpawner : IObjectSpawner
    {
        [Inject] DiContainer diContainer;

        public Object SpawnObject(Object original)
        {
            var obj = diContainer.InstantiatePrefab(original);
            return obj;
        }

        public Object SpawnObject<T>(T original) where T : Object
        {
            var obj = diContainer.InstantiatePrefabForComponent<T>(original);
            return obj;
        }

        public GameObject SpawnObjectOnNewGameObject<TComponent>(string name = "") where TComponent : Component
        {
            var component = diContainer.InstantiateComponentOnNewGameObject<TComponent>();
            GameObject go = component.gameObject;
            go.name = name;
            return go;
        }
    }
}