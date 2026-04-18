using System;

namespace CycloneGames.Factory.Runtime
{
    public interface IPoolable : IDisposable
    {
        void OnSpawned();
        void OnDespawned();
    }

    public interface IPoolable<in TParam1> : IDisposable
    {
        void OnSpawned(TParam1 p1);
        void OnDespawned();
    }

    public interface IPoolable<in TParam1, TValue> : IDisposable
    {
        void OnSpawned(TParam1 p1, IDespawnableMemoryPool<TValue> pool);
        void OnDespawned();
    }

    public interface ITickable
    {
        void Tick();
    }
}
