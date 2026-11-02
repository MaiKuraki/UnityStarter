using CycloneGames.Factory.Runtime;
using UnityEngine;

namespace CycloneGames.Factory.Tests.Editor
{
    /// <summary>
    /// Engine object poolable used by the Unity adapter pool tests. A ScriptableObject can be created from
    /// an Editor-only test assembly, which a MonoBehaviour cannot: Unity refuses to attach an editor script
    /// to a GameObject, so a Component-based probe would need a runtime assembly of its own.
    /// </summary>
    public sealed class PooledProbe : ScriptableObject, IPoolable<int, PooledProbe>
    {
        public int LastPayload { get; private set; }

        public int SpawnCalls { get; private set; }

        public int DespawnCalls { get; private set; }

        public void OnSpawned(int data, IDespawnableMemoryPool<PooledProbe> pool)
        {
            LastPayload = data;
            SpawnCalls++;
        }

        public void OnDespawned()
        {
            LastPayload = 0;
            DespawnCalls++;
        }
    }
}
