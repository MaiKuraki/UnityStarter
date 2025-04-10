using System;
using System.Collections.Generic;

namespace CycloneGames.Factory.Samples
{
    public struct PureCSharpBulletData
    {
        public float InitialSpeed { get; set; }
        public List<float> InitialPosition { get; set; }
        public List<float> InitialRotation { get; set; }
        public List<float> InitialDirection { get; set; }
    }

    public class PureCSharpBullet : IPoolable<PureCSharpBulletData, IMemoryPool>, ITickable, IDisposable
    {
        private PureCSharpBulletData _data;
        private IMemoryPool _pool;

        public void OnSpawned(PureCSharpBulletData data, IMemoryPool pool)
        {
            _data = data;
            _pool = pool;
            System.Console.WriteLine($"Bullet spawned with speed: {_data.InitialSpeed}");
        }

        public void OnDespawned()
        {
            System.Console.WriteLine("Bullet despawned");
        }

        public void Tick()
        {
            System.Console.WriteLine($"Bullet moving at speed: {_data.InitialSpeed}");
            // Simulate bullet logic here 
        }

        public void Hit() => System.Console.WriteLine("Bullet Hit!");

        public void Dispose()
        {
            System.Console.WriteLine("Bullet disposed");
        }
    }

    public class DefaultFactory<T> : IFactory<T> where T : new()
    {
        public T Create() => new T();
    }

    public class PureCSharpGame
    {
        private ObjectPool<PureCSharpBulletData, PureCSharpBullet> _bulletPool;

        public void Initialize()
        {
            // Create a factory (could also use a parameterized factory)
            var factory = new DefaultFactory<PureCSharpBullet>();
            _bulletPool = new ObjectPool<PureCSharpBulletData, PureCSharpBullet>(factory, initialSize: 10);
        }

        public void FireBullet(PureCSharpBulletData speed)
        {
            PureCSharpBullet bullet = _bulletPool.Spawn(speed);
        }

        public void RecycleBullet(PureCSharpBullet bullet)
        {
            bullet.Hit();
            _bulletPool.Despawn(bullet);
        }

        public void Update()
        {
            _bulletPool.Tick();
        }
    }
}

