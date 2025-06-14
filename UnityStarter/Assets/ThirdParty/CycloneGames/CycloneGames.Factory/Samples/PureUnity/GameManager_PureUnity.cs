using UnityEngine;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Samples.PureUnity
{
    public class GameManager_PureUnity : MonoBehaviour
    {
        public Bullet BulletPrefab; // Assign in Inspector
        private ObjectPool<BulletData, Bullet> _bulletPool;
        private IFactory<Bullet> _bulletFactory;

        void Start()
        {
            // 1. Manually create the dependencies
            var spawner = new SimpleUnitySpawner();
            _bulletFactory = new MonoPrefabFactory<Bullet>(spawner, BulletPrefab, null);
            _bulletPool = new ObjectPool<BulletData, Bullet>(_bulletFactory, 10);
            Debug.Log($"Pool Initialized. Inactive objects: {_bulletPool.NumInactive}");
        }

        void Update()
        {
            // 3. Spawn a bullet on mouse click
            if (Input.GetMouseButtonDown(0))
            {
                var bulletData = new BulletData
                {
                    InitialPosition = transform.position,
                    Direction = transform.forward,
                    Speed = 20f
                };
                _bulletPool.Spawn(bulletData);
                Debug.Log($"Bullet spawned. Active: {_bulletPool.NumActive}, Inactive: {_bulletPool.NumInactive}");
            }

            // 4. Update all active bullets
            _bulletPool.Tick();
        }

        void OnDestroy()
        {
            // 5. Clean up the pool when this manager is destroyed
            _bulletPool.Dispose();
        }
    }
}
