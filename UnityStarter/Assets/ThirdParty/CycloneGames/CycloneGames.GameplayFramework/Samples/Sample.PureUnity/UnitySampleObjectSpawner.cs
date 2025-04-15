using CycloneGames.Factory;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Sample.PureUnity
{
    public class UnitySampleObjectSpawner : IUnityObjectSpawner
    {
        public UnityEngine.Object Create(in UnityEngine.Object prefab)
        {
            if(prefab == null)
            {
                CLogger.LogError("Invalid prefab to spawn");
                return null;
            }

            return Object.Instantiate(prefab);
        }
    }
}