using CycloneGames.Factory.Runtime;
using UnityEngine;

namespace CycloneGames.Factory.OOPBullet
{
    public class BulletSpawner : MonoBehaviour, ITickable
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
        
        // Pool and factory
        private ObjectPool<BulletData, Bullet> _bulletPool;
        private MonoPrefabFactory<Bullet> _bulletFactory;
        private DefaultUnityObjectSpawner _unitySpawner;
        
        // Spawning state
        private float _nextSpawnTime;
        private float _spawnRate;
        private Camera _mainCamera;
        private Bounds _screenBounds;
        
        private int _totalSpawned;
        private int _totalDespawned;

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

            // Pre-warm the pool to prevent runtime GC allocation from pool expansion.
            // Calculate the maximum number of bullets that could be active at any time.
            int requiredPoolSize = Mathf.CeilToInt(spawnsPerSecond * defaultLifetime * 1.1f); // 10% buffer
            if (_bulletPool.NumTotal < requiredPoolSize)
            {
                _bulletPool.Resize(requiredPoolSize);
                Debug.Log($"Pool pre-warmed to {requiredPoolSize} to prevent runtime allocations.");
            }
        }

        private void Update()
        {
            if (autoSpawn)
            {
                Tick();
            }
        }

        private void InitializeSpawner()
        {
            if (bulletPrefab == null)
            {
                Debug.LogError("Bullet prefab is not assigned!");
                return;
            }

            _unitySpawner = new DefaultUnityObjectSpawner();
            _bulletFactory = new MonoPrefabFactory<Bullet>(_unitySpawner, bulletPrefab, transform);
            
            _bulletPool = new ObjectPool<BulletData, Bullet>(
                _bulletFactory,
                initialPoolSize,
                expansionFactor: 0.5f,
                shrinkBufferFactor: 0.2f,
                shrinkCooldownTicks: 600
            );
            
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

        public void Tick()
        {
            if (_bulletPool == null) return;
            
            _bulletPool.UpdateActiveItems(b => b.Tick());
            _bulletPool.Maintenance();
            
            float currentTime = Time.time;
            int bulletsToSpawn = 0;
            
            while (currentTime >= _nextSpawnTime)
            {
                bulletsToSpawn++;
                _nextSpawnTime += _spawnRate;
            }
            
            // Spawn the calculated number of bullets
            for (int i = 0; i < bulletsToSpawn; i++)
            {
                SpawnBullet();
            }
        }

        public void SpawnBullet()
        {
            if (_bulletPool == null) return;
            
            var bulletData = new BulletData(defaultVelocity, defaultLifetime);
            
            var bullet = _bulletPool.Spawn(bulletData);
            if (bullet != null)
            {
                Vector3 spawnPosition = GetRandomSpawnPosition();
                bullet.SetPositionAndVelocity(spawnPosition, bulletData.Velocity);
                
                _totalSpawned++;
            }
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

        public void SpawnBulletAt(Vector3 position, Vector3 velocity, float lifetime)
        {
            if (_bulletPool == null) return;
            
            var bulletData = new BulletData(velocity, lifetime);
            var bullet = _bulletPool.Spawn(bulletData);
            if (bullet != null)
            {
                bullet.SetPositionAndVelocity(position, velocity);
                _totalSpawned++;
            }
        }

        public void DespawnAllBullets()
        {
            _bulletPool?.DespawnAllActive();
        }

        public string GetPoolStats()
        {
            if (_bulletPool == null) return "Pool not initialized";
            
            // NOTE: This string formatting causes GC allocation. Avoid calling it in performance-critical code.
            return $"Pool Stats - Total: {_bulletPool.NumTotal}, Active: {_bulletPool.NumActive}, Inactive: {_bulletPool.NumInactive}, Spawned: {_totalSpawned}";
        }

        public void ResizePool(int newSize)
        {
            _bulletPool?.Resize(newSize);
        }

        private void OnDestroy()
        {
            _bulletPool?.Dispose();
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
        public int TotalDespawned => _totalDespawned;
        public int ActiveBullets => _bulletPool?.NumActive ?? 0;
        public int InactiveBullets => _bulletPool?.NumInactive ?? 0;
        public bool IsPoolInitialized => _bulletPool != null;
    }
}
