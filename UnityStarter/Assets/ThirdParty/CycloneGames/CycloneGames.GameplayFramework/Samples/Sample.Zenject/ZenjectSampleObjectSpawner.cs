using Zenject;
using UnityEngine;
using CycloneGames.Logger;

namespace CycloneGames.GameplayFramework.Sample.Zenject
{
    public class ZenjectSampleObjectSpawner : CycloneGames.Factory.IFactory<MonoBehaviour, MonoBehaviour>
    {
        [Inject] DiContainer diContainer;

        public MonoBehaviour Create(MonoBehaviour prefab)
        {
            if (prefab == null)
            {
                CLogger.LogError("Invalid prefab to spawn");
                return null;
            }

            return diContainer.InstantiatePrefabForComponent<MonoBehaviour>(prefab);
        }
    }
}