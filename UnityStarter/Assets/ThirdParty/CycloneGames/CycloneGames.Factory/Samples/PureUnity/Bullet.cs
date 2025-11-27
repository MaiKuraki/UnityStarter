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

    public class Bullet : MonoBehaviour, IPoolable<BulletData, IMemoryPool>
    {
        private IMemoryPool _pool;
        private BulletData _data;
        private bool _isActive;

        public void OnSpawned(BulletData data, IMemoryPool pool)
        {
            _data = data;
            _pool = pool;
            _isActive = true;

            transform.position = _data.InitialPosition;
            gameObject.SetActive(true);

            CancelInvoke(nameof(Recycle));
            Invoke(nameof(Recycle), 3f);
        }

        public void OnDespawned()
        {
            _isActive = false;
            CancelInvoke(nameof(Recycle));
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!_isActive) return;

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
