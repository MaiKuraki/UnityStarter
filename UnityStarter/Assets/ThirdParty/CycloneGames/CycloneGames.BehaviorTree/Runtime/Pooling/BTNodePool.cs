using System;
using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Pooling
{
    public static class BTNodePool
    {
        private static readonly Dictionary<Type, Queue<BTNode>> _pools = new Dictionary<Type, Queue<BTNode>>(32);
        private static readonly Dictionary<Type, int> _poolSizes = new Dictionary<Type, int>(32);

        private static int _totalCreated = 0;
        private static int _totalReused = 0;
        private static int _totalReturned = 0;

        /// <summary>
        /// Gets a node instance from the pool. Creates a new instance if pool is empty.
        /// </summary>
        public static T Get<T>(T template) where T : BTNode
        {
            if (template == null)
            {
                Debug.LogError("[BTNodePool] Template is null");
                return null;
            }

            Type nodeType = typeof(T);

            if (_pools.TryGetValue(nodeType, out var pool) && pool.Count > 0)
            {
                var node = pool.Dequeue() as T;
                if (node != null)
                {
                    ResetNode(node);
                    _totalReused++;
                    return node;
                }
            }

            var newInstance = UnityEngine.Object.Instantiate(template) as T;
            if (newInstance != null)
            {
                newInstance.name = nodeType.Name;
                _totalCreated++;
            }

            return newInstance;
        }

        /// <summary>
        /// Returns a node instance to the pool for reuse.
        /// </summary>
        public static void Return(BTNode node)
        {
            if (node == null) return;

            Type nodeType = node.GetType();

            if (!_pools.TryGetValue(nodeType, out var pool))
            {
                pool = new Queue<BTNode>(8);
                _pools[nodeType] = pool;
            }

            int maxPoolSize = GetMaxPoolSize(nodeType);
            if (pool.Count < maxPoolSize)
            {
                pool.Enqueue(node);
                _totalReturned++;
            }
            else
            {
                UnityEngine.Object.Destroy(node);
            }
        }

        /// <summary>
        /// Pre-warms the pool by creating node instances in advance.
        /// </summary>
        public static void Warmup<T>(T template, int count) where T : BTNode
        {
            if (template == null || count <= 0) return;

            Type nodeType = typeof(T);
            if (!_pools.TryGetValue(nodeType, out var pool))
            {
                pool = new Queue<BTNode>(count);
                _pools[nodeType] = pool;
            }

            for (int i = 0; i < count; i++)
            {
                var node = UnityEngine.Object.Instantiate(template) as T;
                if (node != null)
                {
                    node.name = nodeType.Name;
                    ResetNode(node);
                    pool.Enqueue(node);
                    _totalCreated++;
                }
            }
        }

        /// <summary>
        /// Clears the pool for a specific node type.
        /// </summary>
        public static void ClearPool<T>() where T : BTNode
        {
            Type nodeType = typeof(T);
            if (_pools.TryGetValue(nodeType, out var pool))
            {
                while (pool.Count > 0)
                {
                    var node = pool.Dequeue();
                    if (node != null)
                    {
                        UnityEngine.Object.Destroy(node);
                    }
                }
                _pools.Remove(nodeType);
            }
        }

        /// <summary>
        /// Clears all pools and releases all pooled nodes.
        /// </summary>
        public static void ClearAllPools()
        {
            foreach (var pool in _pools.Values)
            {
                while (pool.Count > 0)
                {
                    var node = pool.Dequeue();
                    if (node != null)
                    {
                        UnityEngine.Object.Destroy(node);
                    }
                }
            }
            _pools.Clear();
            _poolSizes.Clear();
        }

        /// <summary>
        /// Sets the maximum pool size for a specific node type.
        /// </summary>
        public static void SetMaxPoolSize<T>(int maxSize) where T : BTNode
        {
            _poolSizes[typeof(T)] = Mathf.Max(1, maxSize);
        }

        /// <summary>
        /// Gets pool statistics for performance monitoring.
        /// </summary>
        public static PoolStatistics GetStatistics()
        {
            int totalPooled = 0;
            foreach (var pool in _pools.Values)
            {
                totalPooled += pool.Count;
            }

            return new PoolStatistics
            {
                TotalCreated = _totalCreated,
                TotalReused = _totalReused,
                TotalReturned = _totalReturned,
                TotalPooled = totalPooled,
                PoolCount = _pools.Count,
                ReuseRate = _totalCreated > 0 ? (float)_totalReused / (_totalCreated + _totalReused) : 0f
            };
        }

        /// <summary>
        /// Resets all statistics counters.
        /// </summary>
        public static void ResetStatistics()
        {
            _totalCreated = 0;
            _totalReused = 0;
            _totalReturned = 0;
        }

        private static void ResetNode(BTNode node)
        {
            if (node == null) return;

            node.State = Runtime.Data.BTState.NOT_ENTERED;
            node.IsStarted = false;

            if (node is IPoolableNode poolable)
            {
                poolable.OnReset();
            }
        }

        private static int GetMaxPoolSize(Type nodeType)
        {
            if (!_poolSizes.TryGetValue(nodeType, out var size))
            {
                size = GetDefaultPoolSize(nodeType);
                _poolSizes[nodeType] = size;
            }
            return size;
        }

        private static int GetDefaultPoolSize(Type nodeType)
        {
            if (nodeType.IsSubclassOf(typeof(Nodes.Actions.ActionNode)))
            {
                return 32;
            }
            if (nodeType.IsSubclassOf(typeof(Nodes.Compositors.CompositeNode)))
            {
                return 16;
            }
            if (nodeType.IsSubclassOf(typeof(Nodes.Decorators.DecoratorNode)))
            {
                return 16;
            }
            return 8;
        }

        /// <summary>
        /// Pool statistics for performance monitoring.
        /// </summary>
        public struct PoolStatistics
        {
            public int TotalCreated;
            public int TotalReused;
            public int TotalReturned;
            public int TotalPooled;
            public int PoolCount;
            public float ReuseRate;
        }
    }

    /// <summary>
    /// Interface for nodes that require custom reset logic when pooled.
    /// </summary>
    public interface IPoolableNode
    {
        void OnReset();
    }
}