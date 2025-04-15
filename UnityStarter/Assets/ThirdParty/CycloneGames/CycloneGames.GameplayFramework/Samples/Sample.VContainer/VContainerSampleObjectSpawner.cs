using UnityEngine;
using VContainer;
using CycloneGames.Logger;
using CycloneGames.Factory;

namespace CycloneGames.GameplayFramework.Sample.VContainer
{
    public class VContainerSampleObjectSpawner : IFactory<MonoBehaviour, MonoBehaviour>
    {
        [Inject] IObjectResolver objectResolver;

        public MonoBehaviour Create(MonoBehaviour prefab)
        {
            if(prefab == null)
            {
                CLogger.LogError($"Invalid prefab to spawn");
                return null;
            }

            var obj = UnityEngine.Object.Instantiate(prefab);
            objectResolver.Inject(obj);
            return obj;
        }
    }
}