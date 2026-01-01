using System;
using CycloneGames.Factory.Runtime;
using UnityEngine;

namespace CycloneGames.Factory.OOPBullet
{
    public class Bullet : MonoBehaviour, IPoolable<BulletData, IMemoryPool>, ITickable
    {
        [Header("Bullet Settings")]
        [SerializeField] private float lifetime = 5f;
        [SerializeField] private float speed = 10f;

        private BulletData _bulletData;
        private IMemoryPool _pool;
        private float _despawnTime;
        private bool _isActive;
        private Rigidbody _rigidbody;

        public float Lifetime => lifetime;
        public float Speed => speed;
        public bool IsActive => _isActive;
        public BulletData Data => _bulletData;

        private void Awake()
        {
            gameObject.SetActive(false);

            _rigidbody = GetComponent<Rigidbody>();
            if (_rigidbody == null)
            {
                _rigidbody = gameObject.AddComponent<Rigidbody>();
            }

            _rigidbody.useGravity = false;

#if UNITY_6000_0_OR_NEWER
            _rigidbody.linearDamping = 0f;
#else
            _rigidbody.drag = 0f;
#endif
#if UNITY_6000_0_OR_NEWER
            _rigidbody.angularDamping = 0f;
#else
            _rigidbody.angularDrag = 0f;
#endif
            _rigidbody.mass = 0.1f;
            _rigidbody.isKinematic = false;
        }

        public void OnSpawned(BulletData bulletData, IMemoryPool pool)
        {
            _bulletData = bulletData;
            _pool = pool;
            _despawnTime = Time.time + bulletData.Lifetime;
            _isActive = true;

            gameObject.SetActive(true);

            if (_rigidbody != null)
            {
#if UNITY_6000_0_OR_NEWER
                _rigidbody.linearVelocity = bulletData.Velocity;
#else
                _rigidbody.velocity = bulletData.Velocity;
#endif
                _rigidbody.angularVelocity = Vector3.zero;
            }
            else
            {
                Debug.LogError("Bullet: Rigidbody component is missing! This should have been added in Awake().");
            }
        }

        public void OnDespawned()
        {
            _isActive = false;
            _bulletData = default;
            _pool = null;
            _despawnTime = 0f;

            if (_rigidbody != null)
            {
#if UNITY_6000_0_OR_NEWER
                _rigidbody.linearVelocity = Vector3.zero;
#else
                _rigidbody.velocity = Vector3.zero;
#endif
                _rigidbody.angularVelocity = Vector3.zero;
            }

            gameObject.SetActive(false);
        }

        public void Tick()
        {
            if (!_isActive) return;

            if (Time.time >= _despawnTime)
            {
                DespawnSelf();
            }
        }

        private void DespawnSelf()
        {
            if (_pool != null)
            {
                _pool.Despawn(this);
            }
        }

        public void Dispose()
        {

        }

        public void SetPositionAndVelocity(Vector3 position, Vector3 velocity)
        {
            transform.position = position;

            if (_rigidbody != null)
            {
#if UNITY_6000_0_OR_NEWER
                _rigidbody.linearVelocity = velocity;
#else
                _rigidbody.velocity = velocity;
#endif
                _rigidbody.angularVelocity = Vector3.zero;
            }
            else
            {
                Debug.LogError("Bullet: No Rigidbody component found when setting velocity!");
            }
        }

        public bool IsOutOfBounds(Bounds bounds)
        {
            return !bounds.Contains(transform.position);
        }
    }

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