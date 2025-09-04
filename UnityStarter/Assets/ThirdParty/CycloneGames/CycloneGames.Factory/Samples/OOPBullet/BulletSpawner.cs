using System;
using CycloneGames.Factory.Runtime;
using UnityEngine;

namespace CycloneGames.Factory.OOPBullet
{
    /// <summary>
    /// OOP version of the bullet spawner that manages bullet creation and pooling
    /// This provides the same functionality as the ECS BulletPoolManagerSystem but using traditional OOP patterns
    /// </summary>
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
        
        // Debug info
        private int _totalSpawned;
        private int _totalDespawned;

        private void Awake()
        {
            InitializeSpawner();
        }

        private void Start()
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                Debug.LogError("No main camera found! Bullet spawner needs a main camera for screen bounds calculation.");
                return;
            }
            
            UpdateScreenBounds();
        }

        private void Update()
        {
            if (autoSpawn)
            {
                Tick();
            }
        }

        /// <summary>
        /// Initialize the spawner with pool and factory
        /// </summary>
        private void InitializeSpawner()
        {
            if (bulletPrefab == null)
            {
                Debug.LogError("Bullet prefab is not assigned!");
                return;
            }

            // Create Unity object spawner
            _unitySpawner = new DefaultUnityObjectSpawner();
            
            // Create factory for bullets
            _bulletFactory = new MonoPrefabFactory<Bullet>(_unitySpawner, bulletPrefab, transform);
            
            // Create object pool
            _bulletPool = new ObjectPool<BulletData, Bullet>(
                _bulletFactory,
                initialPoolSize,
                expansionFactor: 0.5f,      // Expand by 50% when empty
                shrinkBufferFactor: 0.2f,    // Keep 20% buffer above peak usage
                shrinkCooldownTicks: 600     // Wait 10 seconds (at 60fps) before shrinking
            );
            
            // Calculate spawn rate
            _spawnRate = 1.0f / spawnsPerSecond;
            _nextSpawnTime = Time.time;
            
            Debug.Log($"BulletSpawner initialized with pool size: {initialPoolSize}, spawn rate: {spawnsPerSecond}/sec");
        }

        /// <summary>
        /// Update screen bounds for spawning bullets
        /// </summary>
        private void UpdateScreenBounds()
        {
            if (_mainCamera == null) return;
            
            // Convert screen corners to world space
            Vector3 bottomLeft = _mainCamera.ScreenToWorldPoint(new Vector3(0, 0, 10));
            Vector3 topRight = _mainCamera.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, 10));
            
            // Apply offset
            bottomLeft += (Vector3)screenBoundsOffset;
            topRight += (Vector3)screenBoundsOffset;
            
            _screenBounds = new Bounds(
                (bottomLeft + topRight) * 0.5f,
                topRight - bottomLeft
            );
        }

        /// <summary>
        /// Main update loop for spawning bullets
        /// </summary>
        public void Tick()
        {
            if (_bulletPool == null) return;
            
            // Update screen bounds if needed
            if (useScreenBounds)
            {
                UpdateScreenBounds();
            }
            
            // Update the pool (this calls Tick on all active bullets)
            _bulletPool.Tick();
            
            // Spawn bullets based on spawn rate
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

        /// <summary>
        /// Spawn a single bullet at a random position
        /// </summary>
        public void SpawnBullet()
        {
            if (_bulletPool == null) return;
            
            // Create bullet data
            var bulletData = new BulletData(defaultVelocity, defaultLifetime);
            
            // Spawn bullet from pool
            var bullet = _bulletPool.Spawn(bulletData);
            if (bullet != null)
            {
                // Set random position
                Vector3 spawnPosition = GetRandomSpawnPosition();
                bullet.SetPositionAndVelocity(spawnPosition, bulletData.Velocity);
                
                _totalSpawned++;
                
                // Debug log first bullet to avoid spam
                if (_totalSpawned == 1)
                {
                    Debug.Log($"First bullet spawned at position: {spawnPosition}");
                }
            }
        }

        /// <summary>
        /// Get a random spawn position within screen bounds
        /// </summary>
        /// <returns>Random world position</returns>
        private Vector3 GetRandomSpawnPosition()
        {
            if (useScreenBounds && _mainCamera != null)
            {
                // Random position within screen bounds
                float randomX = UnityEngine.Random.Range(_screenBounds.min.x, _screenBounds.max.x);
                float randomY = UnityEngine.Random.Range(_screenBounds.min.y, _screenBounds.max.y);
                return new Vector3(randomX, randomY, 0);
            }
            else
            {
                // Fallback to transform position
                return transform.position;
            }
        }

        /// <summary>
        /// Manually spawn a bullet at a specific position
        /// </summary>
        /// <param name="position">World position to spawn at</param>
        /// <param name="velocity">Initial velocity</param>
        /// <param name="lifetime">Bullet lifetime</param>
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

        /// <summary>
        /// Despawn all active bullets
        /// </summary>
        public void DespawnAllBullets()
        {
            if (_bulletPool != null)
            {
                _bulletPool.DespawnAllActive();
                Debug.Log("All bullets despawned");
            }
        }

        /// <summary>
        /// Get pool statistics
        /// </summary>
        /// <returns>Pool statistics string</returns>
        public string GetPoolStats()
        {
            if (_bulletPool == null) return "Pool not initialized";
            
            return $"Pool Stats - Total: {_bulletPool.NumTotal}, Active: {_bulletPool.NumActive}, Inactive: {_bulletPool.NumInactive}, Spawned: {_totalSpawned}";
        }

        /// <summary>
        /// Resize the pool
        /// </summary>
        /// <param name="newSize">New pool size</param>
        public void ResizePool(int newSize)
        {
            if (_bulletPool != null)
            {
                _bulletPool.Resize(newSize);
                Debug.Log($"Pool resized to: {newSize}");
            }
        }

        private void OnDestroy()
        {
            // Clean up the pool
            _bulletPool?.Dispose();
        }

        private void OnDrawGizmosSelected()
        {
            if (useScreenBounds && _mainCamera != null)
            {
                // Draw screen bounds
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(_screenBounds.center, _screenBounds.size);
            }
        }

        // Public properties for external access
        public int TotalSpawned => _totalSpawned;
        public int TotalDespawned => _totalDespawned;
        public int ActiveBullets => _bulletPool?.NumActive ?? 0;
        public int InactiveBullets => _bulletPool?.NumInactive ?? 0;
        public bool IsPoolInitialized => _bulletPool != null;
    }
}
