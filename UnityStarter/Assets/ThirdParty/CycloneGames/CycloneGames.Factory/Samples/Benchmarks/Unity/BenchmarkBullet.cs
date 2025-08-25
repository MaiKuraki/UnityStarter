using UnityEngine;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Samples.Benchmarks.Unity
{
    /// <summary>
    /// Benchmark bullet implementation for testing Unity GameObject pooling performance.
    /// Implements the required interfaces for object pooling while providing realistic behavior.
    /// </summary>
    public class BenchmarkBullet : MonoBehaviour, IPoolable<BulletSpawnData, IMemoryPool>, ITickable
    {
        [Header("Bullet Configuration")]
        [SerializeField] private float baseSpeed = 10f;
        [SerializeField] private float baseLifetime = 5f;
        [SerializeField] private bool enableTrail = true;
        [SerializeField] private TrailRenderer trailRenderer;
        [SerializeField] private ParticleSystem impactEffect;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;

        private Vector2 _velocity;
        private float _remainingLifetime;
        private IMemoryPool _owningPool;
        private bool _isActive;
        private float _currentSpeed;

        // Performance tracking
        private int _tickCount;
        private float _spawnTime;

        #region IPoolable Implementation

        public void OnSpawned(BulletSpawnData data, IMemoryPool pool)
        {
            _owningPool = pool;
            _isActive = true;
            _tickCount = 0;
            _spawnTime = Time.realtimeSinceStartup;

            // Initialize bullet properties
            transform.position = data.Position;
            _currentSpeed = data.Speed > 0 ? data.Speed : baseSpeed;
            _velocity = data.Direction.normalized * _currentSpeed;
            _remainingLifetime = data.Lifetime > 0 ? data.Lifetime : baseLifetime;

            // Setup visual components
            gameObject.SetActive(true);
            
            if (trailRenderer != null)
            {
                trailRenderer.enabled = enableTrail;
                trailRenderer.Clear();
            }

            if (showDebugInfo)
            {
                Debug.Log($"Bullet spawned at {data.Position} with velocity {_velocity} and lifetime {_remainingLifetime}");
            }
        }

        public void OnDespawned()
        {
            _isActive = false;
            _owningPool = null;

            // Reset visual components
            if (trailRenderer != null)
            {
                trailRenderer.enabled = false;
                trailRenderer.Clear();
            }

            // Play impact effect if available
            if (impactEffect != null && impactEffect.gameObject.activeInHierarchy)
            {
                impactEffect.Play();
            }

            gameObject.SetActive(false);

            if (showDebugInfo)
            {
                Debug.Log($"Bullet despawned after {_tickCount} ticks and {Time.realtimeSinceStartup - _spawnTime:F3} seconds");
            }
        }

        #endregion

        #region ITickable Implementation

        public void Tick()
        {
            if (!_isActive || _owningPool == null) return;

            _tickCount++;
            float deltaTime = Time.deltaTime;

            // Update position
            Vector3 movement = _velocity * deltaTime;
            transform.position += movement;

            // Update remaining lifetime
            _remainingLifetime -= deltaTime;

            // Check for despawn conditions
            if (ShouldDespawn())
            {
                _owningPool.Despawn(this);
            }
        }

        #endregion

        #region Despawn Conditions

        private bool ShouldDespawn()
        {
            // Lifetime expired
            if (_remainingLifetime <= 0) return true;

            // Out of bounds (simple check)
            Vector3 pos = transform.position;
            if (Mathf.Abs(pos.x) > 50f || Mathf.Abs(pos.y) > 50f || Mathf.Abs(pos.z) > 50f)
                return true;

            return false;
        }

        #endregion

        #region Collision Detection (Optional)

        private void OnTriggerEnter(Collider other)
        {
            // Simple collision detection for more realistic behavior
            if (other.CompareTag("Target") || other.CompareTag("Wall"))
            {
                if (_owningPool != null && _isActive)
                {
                    _owningPool.Despawn(this);
                }
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // 2D collision detection
            if (other.CompareTag("Target") || other.CompareTag("Wall"))
            {
                if (_owningPool != null && _isActive)
                {
                    _owningPool.Despawn(this);
                }
            }
        }

        #endregion

        #region Debug and Utilities

        private void OnDrawGizmos()
        {
            if (!showDebugInfo || !_isActive) return;

            // Draw velocity vector
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, _velocity.normalized);

            // Draw remaining lifetime as a circle
            Gizmos.color = Color.yellow;
            float radius = _remainingLifetime * 0.1f;
            Gizmos.DrawWireSphere(transform.position, radius);
        }

        /// <summary>
        /// Get bullet performance info for debugging
        /// </summary>
        public BulletDebugInfo GetDebugInfo()
        {
            return new BulletDebugInfo
            {
                IsActive = _isActive,
                TickCount = _tickCount,
                RemainingLifetime = _remainingLifetime,
                CurrentSpeed = _currentSpeed,
                Position = transform.position,
                AliveTime = Time.realtimeSinceStartup - _spawnTime
            };
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            // Cleanup any managed resources if needed
            // For Unity GameObjects, this is typically handled by OnDestroy
            // This implementation satisfies the IPoolable<T1, T2> : IDisposable requirement
        }

        #endregion

        #region Unity Lifecycle (for direct instantiation testing)

        private void Start()
        {
            // If this bullet was instantiated directly (not through pool),
            // set up default behavior
            if (_owningPool == null && !_isActive)
            {
                var defaultData = new BulletSpawnData
                {
                    Position = transform.position,
                    Direction = transform.forward,
                    Speed = baseSpeed,
                    Lifetime = baseLifetime
                };

                // Initialize without pool (for benchmark comparison)
                _isActive = true;
                _velocity = defaultData.Direction.normalized * defaultData.Speed;
                _remainingLifetime = defaultData.Lifetime;
                _spawnTime = Time.realtimeSinceStartup;
            }
        }

        private void Update()
        {
            // Only use Unity's Update if not being managed by pool
            if (_owningPool == null && _isActive)
            {
                Tick();

                // Auto-destroy if lifetime expired (for direct instantiation)
                if (_remainingLifetime <= 0)
                {
                    Destroy(gameObject);
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Debug information for bullet performance analysis
    /// </summary>
    [System.Serializable]
    public struct BulletDebugInfo
    {
        public bool IsActive;
        public int TickCount;
        public float RemainingLifetime;
        public float CurrentSpeed;
        public Vector3 Position;
        public float AliveTime;
    }
}
