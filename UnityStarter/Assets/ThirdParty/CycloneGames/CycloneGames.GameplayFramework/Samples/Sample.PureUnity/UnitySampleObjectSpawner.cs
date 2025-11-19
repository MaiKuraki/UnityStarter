using CycloneGames.Factory.Runtime;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Sample.PureUnity
{
    public class UnitySampleObjectSpawner : IUnityObjectSpawner
    {
        public T Create<T>(T origin) where T : UnityEngine.Object
        {
            if (origin == null)
            {
                CLogger.LogError("Invalid prefab to spawn");
                return null;
            }

            return Object.Instantiate(origin);
        }

        public T Create<T>(T origin, UnityEngine.Transform parent) where T : UnityEngine.Object
        {
            if (origin == null)
            {
                CLogger.LogError("Invalid prefab to spawn");
                return null;
            }
            
            return UnityEngine.Object.Instantiate(origin, parent);
        }
    }
}