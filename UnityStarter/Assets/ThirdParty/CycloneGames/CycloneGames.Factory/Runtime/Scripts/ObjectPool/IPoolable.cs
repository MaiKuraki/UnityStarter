using System;

namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// Interface for an object that can be pooled. Provides lifecycle callbacks for when it's spawned or despawned.
    /// </summary>
    public interface IPoolable : IDisposable
    {
        void OnSpawned();
        void OnDespawned();
    }

    /// <summary>
    /// An object that can be pooled and requires one parameter upon spawning.
    /// </summary>
    public interface IPoolable<in TParam1> : IDisposable
    {
        void OnSpawned(TParam1 p1);
        void OnDespawned();
    }

    /// <summary>
    /// An object that can be pooled and requires two parameters upon spawning.
    /// The second parameter is typically the memory pool itself for self-despawning.
    /// </summary>
    public interface IPoolable<in TParam1, in TParam2> : IDisposable
    {
        void OnSpawned(TParam1 p1, TParam2 p2);
        void OnDespawned();
    }

    /// <summary>
    /// Represents an object that can be updated every frame.
    /// </summary>
    public interface ITickable
    {
        void Tick();
    }
}