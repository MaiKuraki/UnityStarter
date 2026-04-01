using UnityEngine;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Samples.PureUnity
{
    /// <summary>
    /// Demonstrates MonoFastPool usage — lightweight, main-thread only, no lifecycle callbacks.
    /// For pools with automatic OnSpawned/OnDespawned, see AdvancedObjectPoolSample (ObjectPool).
    /// </summary>
    public class GameManager_PureUnity : MonoBehaviour
    {
        public Bullet BulletPrefab;
        [SerializeField] private int initialPoolSize = 32;
        [SerializeField] private int maxPoolCapacity = 200;

        private MonoFastPool<Bullet> _bulletPool;

        void Start()
        {
            _bulletPool = new MonoFastPool<Bullet>(BulletPrefab, 0, transform);
            _bulletPool.MaxCapacity = maxPoolCapacity;

            // Spread warmup across frames to avoid spike on low-end devices
            StartCoroutine(_bulletPool.WarmupCoroutine(initialPoolSize, batchSize: 8));
        }

        void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                var bullet = _bulletPool.Spawn();

                // MonoFastPool does not auto-invoke IPoolable callbacks.
                // The caller is responsible for initialization — this is by design for lightweight scenarios.
                bullet.OnSpawned(new BulletData
                {
                    InitialPosition = transform.position,
                    Direction = transform.forward,
                    Speed = 20f
                }, _bulletPool);
            }
        }

        void OnDestroy()
        {
            _bulletPool?.Dispose();
        }
    }
}