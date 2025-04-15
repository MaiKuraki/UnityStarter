using CycloneGames.Factory;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Sample.PureUnity
{
    public class UnitySampleObjectSpawner : IFactory<MonoBehaviour, MonoBehaviour>
    {
        public MonoBehaviour Create(MonoBehaviour prefab)
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