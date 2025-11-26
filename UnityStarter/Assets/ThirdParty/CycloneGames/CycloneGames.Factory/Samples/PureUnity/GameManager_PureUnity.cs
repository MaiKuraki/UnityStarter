using UnityEngine;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Samples.PureUnity
{
    public class GameManager_PureUnity : MonoBehaviour
    {
        public Bullet BulletPrefab; // Assign in Inspector
        private MonoFastPool<Bullet> _bulletPool;

        void Start()
        {
            _bulletPool = new MonoFastPool<Bullet>(BulletPrefab, 10);
            
            Debug.Log($"Pool Initialized. Inactive objects: {_bulletPool.NumInactive}");
        }

        void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                var bulletData = new BulletData
                {
                    InitialPosition = transform.position,
                    Direction = transform.forward,
                    Speed = 20f
                };
                
                var bullet = _bulletPool.Spawn();
                bullet.OnSpawned(bulletData, _bulletPool);
                
                Debug.Log($"Bullet spawned. Active: {_bulletPool.NumActive}, Inactive: {_bulletPool.NumInactive}");
            }
        }

        void OnDestroy()
        {
            _bulletPool?.Clear();
        }
    }
}