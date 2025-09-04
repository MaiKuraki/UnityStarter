using UnityEngine;

namespace CycloneGames.Factory.OOPBullet
{
    /// <summary>
    /// Sample script demonstrating how to use the OOP bullet system
    /// This shows the same functionality as the ECS version but with traditional Unity patterns
    /// </summary>
    public class OOPBulletSample : MonoBehaviour
    {
        [Header("Sample Settings")]
        [SerializeField] private BulletSpawner bulletSpawner;
        [SerializeField] private bool enableDebugLogging = true;
        [SerializeField] private float statsLogInterval = 5f;
        
        private float _lastStatsLogTime;
        private int _lastSpawnedCount;
        private int _lastActiveCount;

        private void Start()
        {
            // Find the bullet spawner if not assigned
            if (bulletSpawner == null)
            {
                bulletSpawner = FindObjectOfType<BulletSpawner>();
                if (bulletSpawner == null)
                {
                    Debug.LogError("OOPBulletSample: No BulletSpawner found in the scene!");
                    return;
                }
            }

            if (enableDebugLogging)
            {
                Debug.Log("OOPBulletSample: Starting bullet system demonstration");
                LogInitialStats();
            }
        }

        private void Update()
        {
            if (bulletSpawner == null) return;

            // Log statistics periodically
            if (enableDebugLogging && Time.time - _lastStatsLogTime >= statsLogInterval)
            {
                LogPeriodicStats();
                _lastStatsLogTime = Time.time;
            }

            // Example: Spawn extra bullets on space key
            if (Input.GetKeyDown(KeyCode.Space))
            {
                SpawnBurst();
            }

            // Example: Despawn all bullets on escape key
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                DespawnAll();
            }

            // Example: Resize pool on R key
            if (Input.GetKeyDown(KeyCode.R))
            {
                ResizePool();
            }
        }

        /// <summary>
        /// Log initial statistics
        /// </summary>
        private void LogInitialStats()
        {
            Debug.Log($"OOPBulletSample: Initial pool stats - {bulletSpawner.GetPoolStats()}");
            Debug.Log($"OOPBulletSample: Controls - Space: Spawn Burst, Escape: Despawn All, R: Resize Pool");
        }

        /// <summary>
        /// Log periodic statistics
        /// </summary>
        private void LogPeriodicStats()
        {
            int currentSpawned = bulletSpawner.TotalSpawned;
            int currentActive = bulletSpawner.ActiveBullets;
            
            int spawnedDelta = currentSpawned - _lastSpawnedCount;
            int activeDelta = currentActive - _lastActiveCount;
            
            Debug.Log($"OOPBulletSample: Stats Update - " +
                     $"Spawned: {currentSpawned} (+{spawnedDelta}), " +
                     $"Active: {currentActive} ({activeDelta:+0;-0;0}), " +
                     $"Pool: {bulletSpawner.GetPoolStats()}");
            
            _lastSpawnedCount = currentSpawned;
            _lastActiveCount = currentActive;
        }

        /// <summary>
        /// Spawn a burst of bullets at random positions
        /// </summary>
        private void SpawnBurst()
        {
            int burstCount = Random.Range(5, 15);
            Debug.Log($"OOPBulletSample: Spawning burst of {burstCount} bullets");
            
            for (int i = 0; i < burstCount; i++)
            {
                Vector3 randomPosition = new Vector3(
                    Random.Range(-10f, 10f),
                    Random.Range(-5f, 5f),
                    0f
                );
                
                Vector3 randomVelocity = new Vector3(
                    Random.Range(-5f, 5f),
                    Random.Range(-5f, 5f),
                    Random.Range(5f, 15f)
                );
                
                float randomLifetime = Random.Range(3f, 8f);
                
                bulletSpawner.SpawnBulletAt(randomPosition, randomVelocity, randomLifetime);
            }
        }

        /// <summary>
        /// Despawn all active bullets
        /// </summary>
        private void DespawnAll()
        {
            Debug.Log("OOPBulletSample: Despawning all bullets");
            bulletSpawner.DespawnAllBullets();
        }

        /// <summary>
        /// Resize the pool to a random size
        /// </summary>
        private void ResizePool()
        {
            int newSize = Random.Range(50, 300);
            Debug.Log($"OOPBulletSample: Resizing pool to {newSize}");
            bulletSpawner.ResizePool(newSize);
        }

        /// <summary>
        /// Example of how to create a custom bullet spawner configuration
        /// </summary>
        [ContextMenu("Configure Spawner")]
        private void ConfigureSpawner()
        {
            if (bulletSpawner == null) return;
            
            // This would require public setters in BulletSpawner
            // For demonstration purposes, we'll just log the current configuration
            Debug.Log($"OOPBulletSample: Current spawner configuration - " +
                     $"Spawn Rate: {bulletSpawner.TotalSpawned}, " +
                     $"Pool Size: {bulletSpawner.ActiveBullets + bulletSpawner.InactiveBullets}");
        }

        private void OnGUI()
        {
            if (bulletSpawner == null) return;
            
            // Simple on-screen display
            GUILayout.BeginArea(new Rect(10, 10, 300, 200));
            GUILayout.Label("OOP Bullet System Demo", GUI.skin.box);
            GUILayout.Label($"Total Spawned: {bulletSpawner.TotalSpawned}");
            GUILayout.Label($"Active Bullets: {bulletSpawner.ActiveBullets}");
            GUILayout.Label($"Inactive Bullets: {bulletSpawner.InactiveBullets}");
            GUILayout.Space(10);
            GUILayout.Label("Controls:", GUI.skin.box);
            GUILayout.Label("Space - Spawn Burst");
            GUILayout.Label("Escape - Despawn All");
            GUILayout.Label("R - Resize Pool");
            GUILayout.EndArea();
        }
    }
}
