using System.Collections.Generic;
using CycloneGames.Factory.Runtime;
using UnityEngine;

namespace CycloneGames.Factory.OOPBullet
{
    public class BulletSpawner : MonoBehaviour
    {
        [Header("Spawner Settings")]
        [SerializeField] private Bullet bulletPrefab;
        [SerializeField] private float spawnsPerSecond = 200f;
        [SerializeField] private int initialPoolSize = 100;
        [SerializeField] private bool autoSpawn = true;

        [Header("Bullet Settings")]
        [SerializeField] private Vector3 defaultVelocity = new Vector3(0, 0, 10f);
        [SerializeField] private float defaultLifetime = 5f;

        [Header("Screen Bounds")]
        [SerializeField] private bool useScreenBounds = true;
        [SerializeField] private Vector2 screenBoundsOffset = Vector2.zero;

        private MonoFastPool<Bullet> _bulletPool;

        private List<Bullet> _activeBullets;

        // Spawning state
        private float _nextSpawnTime;
        private float _spawnRate;
        private Camera _mainCamera;
        private Bounds _screenBounds;

        private int _totalSpawned;

        private void Awake()
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                Debug.LogError("No main camera found! Bullet spawner needs a main camera for screen bounds calculation.");
            }

            InitializeSpawner();
        }

        private void Start()
        {
            if (useScreenBounds)
            {
                UpdateScreenBounds();
            }
        }

        private void Update()
        {
            // Spawn logic
            if (autoSpawn)
            {
                float currentTime = Time.time;
                while (currentTime >= _nextSpawnTime)
                {
                    SpawnBullet();
                    _nextSpawnTime += _spawnRate;
                }
            }

            for (int i = _activeBullets.Count - 1; i >= 0; i--)
            {
                var bullet = _activeBullets[i];

                // If bullet returned itself to the pool, remove it from our active list
                if (!bullet.IsActive)
                {
                    // Swap-back removal (O(1))
                    int lastIndex = _activeBullets.Count - 1;
                    _activeBullets[i] = _activeBullets[lastIndex];
                    _activeBullets.RemoveAt(lastIndex);
                    continue;
                }

                bullet.Tick();
            }
        }

        private void InitializeSpawner()
        {
            if (bulletPrefab == null)
            {
                Debug.LogError("Bullet prefab is not assigned!");
                return;
            }

            _bulletPool = new MonoFastPool<Bullet>(bulletPrefab, initialPoolSize, transform);

            _activeBullets = new List<Bullet>(initialPoolSize);

            _spawnRate = 1.0f / spawnsPerSecond;
            _nextSpawnTime = Time.time;
        }

        private void UpdateScreenBounds()
        {
            if (_mainCamera == null) return;

            Vector3 bottomLeft = _mainCamera.ScreenToWorldPoint(new Vector3(0, 0, 10));
            Vector3 topRight = _mainCamera.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, 10));

            bottomLeft += (Vector3)screenBoundsOffset;
            topRight += (Vector3)screenBoundsOffset;

            _screenBounds = new Bounds(
                (bottomLeft + topRight) * 0.5f,
                topRight - bottomLeft
            );
        }

        public void SpawnBullet()
        {
            if (_bulletPool == null) return;

            var bulletData = new BulletData(defaultVelocity, defaultLifetime);

            var bullet = _bulletPool.Spawn();
            bullet.OnSpawned(bulletData, _bulletPool);
            Vector3 spawnPosition = GetRandomSpawnPosition();
            bullet.SetPositionAndVelocity(spawnPosition, bulletData.Velocity);

            _activeBullets.Add(bullet);
            _totalSpawned++;
        }

        private Vector3 GetRandomSpawnPosition()
        {
            if (useScreenBounds && _mainCamera != null)
            {
                float randomX = UnityEngine.Random.Range(_screenBounds.min.x, _screenBounds.max.x);
                float randomY = UnityEngine.Random.Range(_screenBounds.min.y, _screenBounds.max.y);
                return new Vector3(randomX, randomY, 0);
            }

            return transform.position;
        }

        public void DespawnAllBullets()
        {
            foreach (var bullet in _activeBullets)
            {
                if (bullet.IsActive) _bulletPool.Despawn(bullet);
            }
            _activeBullets.Clear();
        }

        public string GetPoolStats()
        {
            if (_bulletPool == null) return "Pool not initialized";
            return $"Pool Stats - Active: {_bulletPool.NumActive}, Inactive: {_bulletPool.NumInactive}, Spawned: {_totalSpawned}";
        }

        private void OnDestroy()
        {
            _bulletPool?.Clear();
        }

        private void OnDrawGizmosSelected()
        {
            if (useScreenBounds && _mainCamera != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(_screenBounds.center, _screenBounds.size);
            }
        }

        public int TotalSpawned => _totalSpawned;
        public int ActiveBullets => _bulletPool?.NumActive ?? 0;
        public int InactiveBullets => _bulletPool?.NumInactive ?? 0;
    }
}