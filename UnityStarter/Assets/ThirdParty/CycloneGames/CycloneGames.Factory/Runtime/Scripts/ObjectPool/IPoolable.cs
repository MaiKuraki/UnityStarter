using System;

namespace CycloneGames.Factory
{
    public interface IPoolable : IDisposable
    {
        void OnDespawned();
        void OnSpawned();
    }
    public interface IPoolable<TParam1> : IDisposable
    {
        void OnDespawned();
        void OnSpawned(TParam1 p1);
    }
    public interface IPoolable<TParam1, TParam2> : IDisposable
    {
        void OnDespawned();
        void OnSpawned(TParam1 p1, TParam2 p2);
    }
    public interface ITickable
    {
        void Tick();
    }
}