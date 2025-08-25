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

    public class Bullet : MonoBehaviour, IPoolable<BulletData, IMemoryPool>, ITickable
    {
        private IMemoryPool _pool;
        private BulletData _data;

        public void OnSpawned(BulletData data, IMemoryPool pool)
        {
            _data = data;
            _pool = pool;
            transform.position = _data.InitialPosition;
            gameObject.SetActive(true);
            // Despawn after 3 seconds
            Invoke(nameof(Recycle), 3f);
        }

        public void OnDespawned()
        {
            // This prevents the bullet from trying to despawn itself again
            // if it's recycled by some other means before the 3-second timer is up.
            CancelInvoke(nameof(Recycle));
            gameObject.SetActive(false);
        }

        public void Tick()
        {
            // Move the bullet
            transform.position += _data.Direction * _data.Speed * Time.deltaTime;
        }

        private void Recycle()
        {
            // Ensure pool still exists to avoid errors during shutdown
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
