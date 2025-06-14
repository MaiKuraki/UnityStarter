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
            gameObject.SetActive(false);
        }

        public void Tick()
        {
            // Move the bullet
            transform.position += _data.Direction * _data.Speed * Time.deltaTime;
        }

        private void Recycle()
        {
            _pool.Despawn(this);
        }

        public void Dispose()
        {
            // Called when the pool is destroyed
            Destroy(gameObject);
        }
    }
}
