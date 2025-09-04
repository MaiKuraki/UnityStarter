using System;
using CycloneGames.Factory.Runtime;
using UnityEngine;

namespace CycloneGames.Factory.OOPBullet
{
    /// <summary>
    /// OOP version of the bullet that implements IPoolable and ITickable interfaces
    /// This provides the same functionality as the ECS version but using traditional OOP patterns
    /// </summary>
    public class Bullet : MonoBehaviour, IPoolable<BulletData, IMemoryPool>, ITickable
    {
        [Header("Bullet Settings")]
        [SerializeField] private float lifetime = 5f;
        [SerializeField] private float speed = 10f;
        
        private BulletData _bulletData;
        private IMemoryPool _pool;
        private float _currentLifetime;
        private bool _isActive;
        
        // Properties for external access
        public float Lifetime => lifetime;
        public float Speed => speed;
        public bool IsActive => _isActive;
        public BulletData Data => _bulletData;

        private void Awake()
        {
            // Ensure the bullet starts inactive
            gameObject.SetActive(false);
            
            // Ensure we have a Rigidbody component
            EnsureRigidbodyComponent();
        }
        
        /// <summary>
        /// Ensure the bullet has a properly configured Rigidbody component
        /// </summary>
        private void EnsureRigidbodyComponent()
        {
            var rigidbody = GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                rigidbody = gameObject.AddComponent<Rigidbody>();
                Debug.Log("Bullet: Added missing Rigidbody component");
            }
            
            // Configure Rigidbody for bullet behavior
            rigidbody.useGravity = false; // Bullets shouldn't fall
            rigidbody.drag = 0f; // No air resistance
            rigidbody.angularDrag = 0f; // No angular resistance
            rigidbody.mass = 0.1f; // Light mass for fast movement
            rigidbody.isKinematic = false; // Enable physics
        }

        private void Update()
        {
            if (_isActive)
            {
                // Debug: Log position every 60 frames to avoid spam
                if (Time.frameCount % 60 == 0)
                {
                    var rigidbody = GetComponent<Rigidbody>();
                    if (rigidbody != null)
                    {
                        Debug.Log($"Bullet Update - Position: {transform.position}, Velocity: {rigidbody.velocity}, Speed: {rigidbody.velocity.magnitude}");
                    }
                }
            }
        }

        /// <summary>
        /// Called when the bullet is spawned from the pool
        /// </summary>
        /// <param name="bulletData">The data containing velocity and lifetime</param>
        /// <param name="pool">The memory pool for self-despawning</param>
        public void OnSpawned(BulletData bulletData, IMemoryPool pool)
        {
            _bulletData = bulletData;
            _pool = pool;
            _currentLifetime = bulletData.Lifetime;
            _isActive = true;
            
            // Activate the game object first
            gameObject.SetActive(true);
            
            // Get the Rigidbody (should already be configured in Awake)
            var rigidbody = GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                // Set the velocity from the bullet data
                rigidbody.velocity = bulletData.Velocity;
                rigidbody.angularVelocity = Vector3.zero; // Stop any rotation
                
                Debug.Log($"Bullet spawned at position: {transform.position} with velocity: {bulletData.Velocity}, rigidbody velocity: {rigidbody.velocity}");
            }
            else
            {
                Debug.LogError("Bullet: Rigidbody component is missing! This should have been added in Awake().");
            }
        }

        /// <summary>
        /// Called when the bullet is returned to the pool
        /// </summary>
        public void OnDespawned()
        {
            _isActive = false;
            _bulletData = default;
            _pool = null;
            _currentLifetime = 0f;
            
            // Reset physics
            var rigidbody = GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.velocity = Vector3.zero;
                rigidbody.angularVelocity = Vector3.zero;
            }
            
            // Deactivate the game object
            gameObject.SetActive(false);
            
            Debug.Log("Bullet despawned and returned to pool");
        }

        /// <summary>
        /// Called every frame to update the bullet's state
        /// </summary>
        public void Tick()
        {
            if (!_isActive) return;
            
            // Update lifetime
            _currentLifetime -= Time.deltaTime;
            
            // Check if bullet should be despawned
            if (_currentLifetime <= 0f)
            {
                DespawnSelf();
            }
        }

        /// <summary>
        /// Despawns this bullet back to the pool
        /// </summary>
        private void DespawnSelf()
        {
            if (_pool != null)
            {
                _pool.Despawn(this);
            }
        }

        /// <summary>
        /// Dispose implementation for IDisposable
        /// </summary>
        public void Dispose()
        {
            // Clean up any resources if needed
            // The pool will handle the actual destruction
        }

        /// <summary>
        /// Set the bullet's position and velocity
        /// </summary>
        /// <param name="position">World position</param>
        /// <param name="velocity">Initial velocity</param>
        public void SetPositionAndVelocity(Vector3 position, Vector3 velocity)
        {
            transform.position = position;
            
            var rigidbody = GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.velocity = velocity;
                rigidbody.angularVelocity = Vector3.zero; // Stop any rotation
            }
            else
            {
                Debug.LogError("Bullet: No Rigidbody component found when setting velocity!");
            }
        }

        /// <summary>
        /// Check if the bullet is out of bounds (optional utility method)
        /// </summary>
        /// <param name="bounds">The screen bounds to check against</param>
        /// <returns>True if the bullet is out of bounds</returns>
        public bool IsOutOfBounds(Bounds bounds)
        {
            return !bounds.Contains(transform.position);
        }
    }

    /// <summary>
    /// Data structure containing bullet properties
    /// Similar to the ECS BulletComponent but for OOP use
    /// </summary>
    [Serializable]
    public struct BulletData
    {
        public Vector3 Velocity;
        public float Lifetime;
        
        public BulletData(Vector3 velocity, float lifetime)
        {
            Velocity = velocity;
            Lifetime = lifetime;
        }
    }
}
