using UnityEngine;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public class PooledEffect : MonoBehaviour, IPoolable<PooledEffectSpawnData, IMemoryPool>
    {
        [SerializeField] private float defaultDuration = 2f;

        private IMemoryPool _pool;
        private float _timer;
        private bool _isPooled;

        public void OnSpawned(PooledEffectSpawnData data, IMemoryPool pool)
        {
            _pool = pool;
            _isPooled = true;
            _timer = data.Duration > 0f ? data.Duration : defaultDuration;

            Transform t = transform;
            t.SetPositionAndRotation(data.Position, data.Rotation);
            gameObject.SetActive(true);
        }

        public void OnDespawned()
        {
            _isPooled = false;
            _pool = null;
            gameObject.SetActive(false);
        }

        public void Dispose()
        {
            if (this != null && gameObject != null)
                Destroy(gameObject);
        }

        private void Update()
        {
            if (!_isPooled) return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
                ReturnToPool();
        }

        public void ReturnToPool()
        {
            if (_isPooled && _pool != null)
                _pool.Despawn(this);
        }
    }
}