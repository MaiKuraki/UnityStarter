using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.SceneManagement;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public static class EffectPoolSystem
    {
        private static readonly ConcurrentDictionary<int, ObjectPool<PooledEffectSpawnData, PooledEffect>> s_pools = new();
        private static readonly object s_poolCreationLock = new();
        private static readonly DefaultUnityObjectSpawner s_spawner = new();

        private static Transform s_root;
        private static bool s_initialized;
        private static UnityEngine.SceneManagement.Scene s_ownerScene;

        public static void Initialize()
        {
            if (s_initialized) return;

            var go = new GameObject("[EffectPoolSystem]");
            Object.DontDestroyOnLoad(go);
            s_root = go.transform;
            s_ownerScene = SceneManager.GetActiveScene();
            s_initialized = true;

            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private static void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
        {
            if (scene == s_ownerScene && s_initialized)
                Dispose();
        }

        public static void Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            Spawn(prefab, position, rotation, -1f);
        }

        public static void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float duration)
        {
            if (prefab == null) return;
            if (!s_initialized) Initialize();

            if (!prefab.TryGetComponent<PooledEffect>(out var template))
            {
                Object.Instantiate(prefab, position, rotation);
                return;
            }

            int key = prefab.GetInstanceID();

            if (!s_pools.TryGetValue(key, out var pool))
            {
                lock (s_poolCreationLock)
                {
                    if (!s_pools.TryGetValue(key, out pool))
                    {
                        var factory = new MonoPrefabFactory<PooledEffect>(s_spawner, template, s_root);
                        pool = new ObjectPool<PooledEffectSpawnData, PooledEffect>(
                            factory: factory,
                            initialCapacity: 8,
                            expansionFactor: 0.5f,
                            shrinkBufferFactor: 0.2f
                        );
                        s_pools.TryAdd(key, pool);
                    }
                }
            }

            var spawnData = new PooledEffectSpawnData(position, rotation, duration);
            pool.Spawn(spawnData);
        }

        public static void Dispose()
        {
            if (!s_initialized) return;

            SceneManager.sceneUnloaded -= OnSceneUnloaded;

            foreach (var pool in s_pools.Values)
                pool.Dispose();
            s_pools.Clear();

            if (s_root != null)
            {
                Object.Destroy(s_root.gameObject);
                s_root = null;
            }

            s_initialized = false;
            s_ownerScene = default;
        }
    }

    public readonly struct PooledEffectSpawnData
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly float Duration;

        public PooledEffectSpawnData(Vector3 position, Quaternion rotation, float duration)
        {
            Position = position;
            Rotation = rotation;
            Duration = duration;
        }
    }
}