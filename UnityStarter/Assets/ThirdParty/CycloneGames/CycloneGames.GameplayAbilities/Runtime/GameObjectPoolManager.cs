using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Statistics for a single pool, useful for profiling and tuning.
    /// </summary>
    public struct PoolStatistics
    {
        public string AssetKey;
        public int CurrentPoolSize;
        public int ActiveCount;
        public int PeakActive;
        public long TotalGets;
        public long TotalReturns;
        public long TotalCreated;
        public long TotalDestroyed;
        public float HitRate => TotalGets > 0 ? (float)(TotalGets - TotalCreated) / TotalGets : 0f;
    }

    /// <summary>
    /// Enhanced implementation of the IGameObjectPoolManager with:
    /// - Per-asset auto-scaling with max/min capacity limits
    /// - Smart shrinking based on peak usage
    /// - Pool statistics for profiling
    /// - Aggressive shrink mode for scene transitions
    /// - Pre-warming support
    /// </summary>
    public class GameObjectPoolManager : IGameObjectPoolManager
    {
        #region Pool Configuration
        
        /// <summary>
        /// Configuration for a specific asset pool.
        /// </summary>
        public struct PoolConfig
        {
            public int MaxCapacity;
            public int MinCapacity;
            public int InitialCapacity;
            
            public static PoolConfig Default => new PoolConfig
            {
                MaxCapacity = 64,
                MinCapacity = 4,
                InitialCapacity = 0
            };
            
            public static PoolConfig HighFrequency => new PoolConfig
            {
                MaxCapacity = 256,
                MinCapacity = 32,
                InitialCapacity = 32
            };
            
            public static PoolConfig LowEnd => new PoolConfig
            {
                MaxCapacity = 16,
                MinCapacity = 2,
                InitialCapacity = 0
            };
        }
        
        private static PoolConfig s_DefaultConfig = PoolConfig.Default;
        
        /// <summary>
        /// Sets the default configuration for all new pools.
        /// </summary>
        public static void SetDefaultConfig(PoolConfig config) => s_DefaultConfig = config;
        
        #endregion

        #region Internal Types
        
        private class PooledObjectComponent : MonoBehaviour 
        { 
            public string AssetRef; 
        }
        
        private class PoolData
        {
            public Stack<GameObject> Pool = new Stack<GameObject>();
            public PoolConfig Config;
            public int ActiveCount;
            public int PeakActiveSinceLastCheck;
            public int ReturnCounter;
            public long TotalGets;
            public long TotalReturns;
            public long TotalCreated;
            public long TotalDestroyed;
        }
        
        #endregion

        #region Constants
        
        private const int kShrinkCheckInterval = 32;
        private const int kMaxShrinkPerCheck = 4;
        private const float kBufferRatio = 1.5f;
        private const float kPeakDecayFactor = 0.6f;
        
        #endregion

        #region Fields
        
        private readonly IResourceLocator resourceLocator;
        private readonly Dictionary<string, PoolData> poolRegistry = new Dictionary<string, PoolData>();
        private readonly Dictionary<string, PoolConfig> customConfigs = new Dictionary<string, PoolConfig>();
        private readonly Transform poolRoot;
        
        #endregion

        #region Constructor
        
        public GameObjectPoolManager(IResourceLocator locator)
        {
            this.resourceLocator = locator;
            poolRoot = new GameObject("GameObjectPool_Root").transform;
            UnityEngine.Object.DontDestroyOnLoad(poolRoot.gameObject);
        }
        
        #endregion

        #region Configuration API
        
        /// <summary>
        /// Sets custom configuration for a specific asset.
        /// Call before the asset is first used.
        /// </summary>
        public void SetPoolConfig(string assetKey, PoolConfig config)
        {
            customConfigs[assetKey] = config;
            
            // Update existing pool if it exists
            if (poolRegistry.TryGetValue(assetKey, out var poolData))
            {
                poolData.Config = config;
            }
        }
        
        /// <summary>
        /// Configures a pool for high-frequency usage (e.g., bullets, particles).
        /// </summary>
        public void ConfigureForHighFrequency(string assetKey, int maxCapacity = 256, int minCapacity = 32)
        {
            SetPoolConfig(assetKey, new PoolConfig
            {
                MaxCapacity = maxCapacity,
                MinCapacity = minCapacity,
                InitialCapacity = minCapacity
            });
        }
        
        #endregion

        #region Core Pool Operations
        
        public async UniTask<GameObject> GetAsync(object assetRef, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (assetRef is not string assetKey || string.IsNullOrEmpty(assetKey)) return null;

            var poolData = GetOrCreatePoolData(assetKey);
            GameObject instance;
            
            if (poolData.Pool.Count > 0)
            {
                // Pool hit - reuse existing instance
                instance = poolData.Pool.Pop();
                
                // Validate instance (Unity may have destroyed it)
                if (instance == null)
                {
                    // Instance was destroyed externally, try again
                    return await GetAsync(assetRef, position, rotation, parent);
                }
                
                instance.transform.SetParent(parent, false);
                instance.transform.SetPositionAndRotation(position, rotation);
            }
            else
            {
                // Pool miss - create new instance
                var prefab = await resourceLocator.LoadAssetAsync<GameObject>(assetKey);
                if (prefab == null) return null;
                
                instance = UnityEngine.Object.Instantiate(prefab, position, rotation, parent);
                instance.AddComponent<PooledObjectComponent>().AssetRef = assetKey;
                poolData.TotalCreated++;
            }

            // Track statistics
            poolData.ActiveCount++;
            poolData.TotalGets++;
            if (poolData.ActiveCount > poolData.PeakActiveSinceLastCheck)
            {
                poolData.PeakActiveSinceLastCheck = poolData.ActiveCount;
            }
            
            instance.SetActive(true);
            return instance;
        }

        public void Release(GameObject instance)
        {
            if (instance == null) return;
            var poolComponent = instance.GetComponent<PooledObjectComponent>();

            if (poolComponent == null || !poolRegistry.TryGetValue(poolComponent.AssetRef, out var poolData))
            {
                UnityEngine.Object.Destroy(instance);
                return;
            }

            poolData.ActiveCount = Math.Max(0, poolData.ActiveCount - 1);
            poolData.TotalReturns++;
            
            // Check max capacity - destroy excess instances
            if (poolData.Config.MaxCapacity > 0 && poolData.Pool.Count >= poolData.Config.MaxCapacity)
            {
                UnityEngine.Object.Destroy(instance);
                poolData.TotalDestroyed++;
                return;
            }

            instance.SetActive(false);
            instance.transform.SetParent(poolRoot);
            poolData.Pool.Push(instance);
            
            // Periodic shrink check
            poolData.ReturnCounter++;
            if (poolData.ReturnCounter >= kShrinkCheckInterval)
            {
                poolData.ReturnCounter = 0;
                PerformSmartShrink(poolData);
            }
        }
        
        #endregion

        #region Pre-warming
        
        public async UniTask PrewarmPoolAsync(object assetRef, int count)
        {
            if (assetRef is not string assetKey || string.IsNullOrEmpty(assetKey)) return;
            var prefab = await resourceLocator.LoadAssetAsync<GameObject>(assetKey);
            if (prefab == null) return;

            var poolData = GetOrCreatePoolData(assetKey);
            int maxToCreate = poolData.Config.MaxCapacity > 0 
                ? Math.Min(count, poolData.Config.MaxCapacity) 
                : count;

            while (poolData.Pool.Count < maxToCreate)
            {
                var instance = UnityEngine.Object.Instantiate(prefab, poolRoot);
                instance.AddComponent<PooledObjectComponent>().AssetRef = assetKey;
                instance.SetActive(false);
                poolData.Pool.Push(instance);
                poolData.TotalCreated++;
            }
        }
        
        #endregion

        #region Shrinking & Cleanup
        
        private void PerformSmartShrink(PoolData poolData)
        {
            // Calculate target capacity based on peak usage with buffer
            int targetCapacity = (int)(poolData.PeakActiveSinceLastCheck * kBufferRatio);
            targetCapacity = Math.Max(targetCapacity, poolData.Config.MinCapacity);
            
            int currentTotal = poolData.ActiveCount + poolData.Pool.Count;
            if (currentTotal > targetCapacity && poolData.Pool.Count > poolData.Config.MinCapacity)
            {
                int excess = currentTotal - targetCapacity;
                int toRemove = Math.Min(excess, kMaxShrinkPerCheck);
                toRemove = Math.Min(toRemove, poolData.Pool.Count - poolData.Config.MinCapacity);
                
                for (int i = 0; i < toRemove && poolData.Pool.Count > poolData.Config.MinCapacity; i++)
                {
                    var instance = poolData.Pool.Pop();
                    if (instance != null)
                    {
                        UnityEngine.Object.Destroy(instance);
                        poolData.TotalDestroyed++;
                    }
                }
            }
            
            // Decay peak tracker
            poolData.PeakActiveSinceLastCheck = Math.Max(
                poolData.ActiveCount, 
                (int)(poolData.PeakActiveSinceLastCheck * kPeakDecayFactor)
            );
        }
        
        /// <summary>
        /// Aggressively shrinks all pools to their minimum capacity.
        /// Call during scene transitions or loading screens to free memory.
        /// </summary>
        public void AggressiveShrink()
        {
            foreach (var kvp in poolRegistry)
            {
                var poolData = kvp.Value;
                int targetSize = poolData.Config.MinCapacity;
                
                while (poolData.Pool.Count > targetSize)
                {
                    var instance = poolData.Pool.Pop();
                    if (instance != null)
                    {
                        UnityEngine.Object.Destroy(instance);
                        poolData.TotalDestroyed++;
                    }
                }
                
                // Reset peak tracker
                poolData.PeakActiveSinceLastCheck = poolData.ActiveCount;
            }
        }
        
        /// <summary>
        /// Shrinks a specific pool to its minimum capacity.
        /// </summary>
        public void AggressiveShrink(string assetKey)
        {
            if (!poolRegistry.TryGetValue(assetKey, out var poolData)) return;
            
            int targetSize = poolData.Config.MinCapacity;
            while (poolData.Pool.Count > targetSize)
            {
                var instance = poolData.Pool.Pop();
                if (instance != null)
                {
                    UnityEngine.Object.Destroy(instance);
                    poolData.TotalDestroyed++;
                }
            }
            poolData.PeakActiveSinceLastCheck = poolData.ActiveCount;
        }
        
        /// <summary>
        /// Clears a specific pool completely, destroying all pooled instances.
        /// Active instances are not affected.
        /// </summary>
        public void ClearPool(string assetKey)
        {
            if (!poolRegistry.TryGetValue(assetKey, out var poolData)) return;
            
            while (poolData.Pool.Count > 0)
            {
                var instance = poolData.Pool.Pop();
                if (instance != null)
                {
                    UnityEngine.Object.Destroy(instance);
                    poolData.TotalDestroyed++;
                }
            }
        }
        
        #endregion

        #region Statistics API
        
        /// <summary>
        /// Gets statistics for a specific pool.
        /// </summary>
        public PoolStatistics GetStatistics(string assetKey)
        {
            if (!poolRegistry.TryGetValue(assetKey, out var poolData))
            {
                return new PoolStatistics { AssetKey = assetKey };
            }
            
            return new PoolStatistics
            {
                AssetKey = assetKey,
                CurrentPoolSize = poolData.Pool.Count,
                ActiveCount = poolData.ActiveCount,
                PeakActive = poolData.PeakActiveSinceLastCheck,
                TotalGets = poolData.TotalGets,
                TotalReturns = poolData.TotalReturns,
                TotalCreated = poolData.TotalCreated,
                TotalDestroyed = poolData.TotalDestroyed
            };
        }
        
        /// <summary>
        /// Gets statistics for all pools.
        /// </summary>
        public List<PoolStatistics> GetAllStatistics()
        {
            var result = new List<PoolStatistics>(poolRegistry.Count);
            foreach (var kvp in poolRegistry)
            {
                result.Add(GetStatistics(kvp.Key));
            }
            return result;
        }
        
        /// <summary>
        /// Logs pool statistics to console (Editor/Development only).
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public void LogStatistics()
        {
            var stats = GetAllStatistics();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== GameObjectPool Statistics ===");
            
            foreach (var stat in stats)
            {
                sb.AppendLine($"[{stat.AssetKey}] Pool:{stat.CurrentPoolSize} Active:{stat.ActiveCount} Peak:{stat.PeakActive} HitRate:{stat.HitRate:P1} Gets:{stat.TotalGets} Created:{stat.TotalCreated}");
            }
            
            CLogger.LogInfo(sb.ToString(), "GameObjectPool");
        }
        
        /// <summary>
        /// Resets statistics for all pools.
        /// </summary>
        public void ResetStatistics()
        {
            foreach (var poolData in poolRegistry.Values)
            {
                poolData.TotalGets = 0;
                poolData.TotalReturns = 0;
                poolData.TotalCreated = 0;
                poolData.TotalDestroyed = 0;
                poolData.PeakActiveSinceLastCheck = poolData.ActiveCount;
            }
        }
        
        #endregion

        #region Lifecycle
        
        public void Shutdown()
        {
            foreach (var poolData in poolRegistry.Values)
            {
                foreach (var item in poolData.Pool)
                {
                    if (item != null) UnityEngine.Object.Destroy(item);
                }
            }
            poolRegistry.Clear();
            customConfigs.Clear();
            if (poolRoot) UnityEngine.Object.Destroy(poolRoot.gameObject);
        }
        
        #endregion

        #region Helpers
        
        private PoolData GetOrCreatePoolData(string assetKey)
        {
            if (!poolRegistry.TryGetValue(assetKey, out var poolData))
            {
                var config = customConfigs.TryGetValue(assetKey, out var customConfig) 
                    ? customConfig 
                    : s_DefaultConfig;
                    
                poolData = new PoolData
                {
                    Config = config,
                    Pool = new Stack<GameObject>(config.InitialCapacity > 0 ? config.InitialCapacity : 8)
                };
                poolRegistry[assetKey] = poolData;
            }
            return poolData;
        }
        
        #endregion
    }
}
