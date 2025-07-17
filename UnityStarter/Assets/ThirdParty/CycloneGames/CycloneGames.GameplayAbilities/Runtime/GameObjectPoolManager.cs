using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Concrete implementation of the IGameObjectPoolManager.
    /// Manages pools of GameObjects keyed by their AssetReference.
    /// </summary>
    public class GameObjectPoolManager : IGameObjectPoolManager
    {
        private class PooledObjectComponent : MonoBehaviour { public AssetReferenceGameObject AssetRef; }

        private readonly IResourceLocator resourceLocator;
        private readonly Dictionary<AssetReferenceGameObject, Stack<GameObject>> poolRegistry = new Dictionary<AssetReferenceGameObject, Stack<GameObject>>();
        private readonly Transform poolRoot;

        public GameObjectPoolManager(IResourceLocator locator)
        {
            this.resourceLocator = locator;
            poolRoot = new GameObject("GameObjectPool_Root").transform;
            Object.DontDestroyOnLoad(poolRoot.gameObject);
        }

        public async UniTask<GameObject> GetAsync(AssetReferenceGameObject assetRef, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (!assetRef.RuntimeKeyIsValid()) return null;

            if (!poolRegistry.TryGetValue(assetRef, out var pool))
            {
                pool = new Stack<GameObject>();
                poolRegistry[assetRef] = pool;
            }

            GameObject instance;
            if (pool.Count > 0)
            {
                instance = pool.Pop();
                instance.transform.SetParent(parent, false);
                instance.transform.SetPositionAndRotation(position, rotation);
            }
            else
            {
                var prefab = await resourceLocator.LoadAssetAsync<GameObject>(assetRef);
                if (prefab == null) return null;
                instance = Object.Instantiate(prefab, position, rotation, parent);
                instance.AddComponent<PooledObjectComponent>().AssetRef = assetRef;
            }

            instance.SetActive(true);
            return instance;
        }

        public void Release(GameObject instance)
        {
            if (instance == null) return;
            var poolComponent = instance.GetComponent<PooledObjectComponent>();

            if (poolComponent == null || !poolRegistry.TryGetValue(poolComponent.AssetRef, out var pool))
            {
                Object.Destroy(instance);
                return;
            }

            instance.SetActive(false);
            instance.transform.SetParent(poolRoot);
            pool.Push(instance);
        }

        public async UniTask PrewarmPoolAsync(AssetReferenceGameObject assetRef, int count)
        {
            if (!assetRef.RuntimeKeyIsValid()) return;
            var prefab = await resourceLocator.LoadAssetAsync<GameObject>(assetRef);
            if (prefab == null) return;

            if (!poolRegistry.TryGetValue(assetRef, out var pool))
            {
                pool = new Stack<GameObject>();
                poolRegistry[assetRef] = pool;
            }

            while (pool.Count < count)
            {
                var instance = Object.Instantiate(prefab, poolRoot);
                instance.AddComponent<PooledObjectComponent>().AssetRef = assetRef;
                instance.SetActive(false);
                pool.Push(instance);
            }
        }

        public void Shutdown()
        {
            foreach (var pool in poolRegistry.Values)
            {
                foreach (var item in pool) Object.Destroy(item);
            }
            poolRegistry.Clear();
            if (poolRoot) Object.Destroy(poolRoot.gameObject);
        }
    }
}