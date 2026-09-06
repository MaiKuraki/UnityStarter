using UnityEngine;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Samples.PureUnity
{
    public struct BulletData
    {
        public Vector3 InitialPosition;
        public Vector3 Direction;
        public float Speed;
    }

    public class Bullet : MonoBehaviour, IPoolable<BulletData, Bullet>
    {
        private const float LifetimeSeconds = 3f;

        private IDespawnableMemoryPool<Bullet> _pool;
        private BulletData _data;
        private bool _isActive;
        private float _remainingLifetime;

        public void OnSpawned(BulletData data, IDespawnableMemoryPool<Bullet> pool)
        {
            _data = data;
            _pool = pool;
            _isActive = true;
            _remainingLifetime = LifetimeSeconds;

            transform.position = _data.InitialPosition;
            gameObject.SetActive(true);
        }

        public void OnDespawned()
        {
            _isActive = false;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!_isActive) return;

            // Explicit lifetime accumulator instead of MonoBehaviour.Invoke: no string-based
            // method lookup, and no pending callback to cancel when the bullet is despawned early.
            _remainingLifetime -= Time.deltaTime;
            if (_remainingLifetime <= 0f)
            {
                Recycle();
                return;
            }

            transform.position += _data.Direction * _data.Speed * Time.deltaTime;
        }

        private void Recycle()
        {
            if (_pool != null)
            {
                _pool.Despawn(this);
            }
        }

        public void Dispose()
        {
            if (this != null)
            {
                Destroy(gameObject);
            }
        }
    }
}
