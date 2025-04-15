using Zenject;
using UnityEngine;
using CycloneGames.Logger;
using CycloneGames.Factory;

namespace CycloneGames.GameplayFramework.Sample.Zenject
{
    public class ZenjectSampleObjectSpawner : IUnityObjectSpawner
    {
        [Inject] DiContainer diContainer;

        public T Create<T>(T origin) where T : Object
        {
            if (origin == null)
            {
                CLogger.LogError("Invalid prefab to spawn");
                return null;
            }

            return diContainer.InstantiatePrefabForComponent<T>(origin);
        }
    }
}