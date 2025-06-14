using CycloneGames.Factory.Runtime;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Sample.PureUnity
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
    }
}