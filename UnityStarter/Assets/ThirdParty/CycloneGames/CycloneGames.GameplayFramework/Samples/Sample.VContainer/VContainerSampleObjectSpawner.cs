using UnityEngine;
using VContainer;
using CycloneGames.Logger;
using CycloneGames.Factory;

namespace CycloneGames.GameplayFramework.Sample.VContainer
{
    public class VContainerSampleObjectSpawner : IUnityObjectSpawner
    {
        [Inject] IObjectResolver objectResolver;

        public UnityEngine.Object Create(in UnityEngine.Object prefab)
        {
            if (prefab == null)
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