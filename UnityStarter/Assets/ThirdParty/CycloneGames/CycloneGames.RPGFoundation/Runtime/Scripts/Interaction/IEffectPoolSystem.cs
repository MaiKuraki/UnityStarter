using System;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public interface IEffectPoolSystem : IDisposable
    {
        void Initialize();
        void Spawn(GameObject prefab, Vector3 position, Quaternion rotation);
        void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float duration);
    }
}