namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// Receives lifecycle callbacks while an item moves between inactive and borrowed states.
    /// Resource destruction is independent; items may implement <see cref="System.IDisposable"/> when needed.
    /// </summary>
    public interface IPoolable
    {
        void OnSpawned();
        void OnDespawned();
    }

    public interface IPoolable<in TParam1>
    {
        void OnSpawned(TParam1 p1);
        void OnDespawned();
    }

    public interface IPoolable<in TParam1, TValue>
    {
        void OnSpawned(TParam1 p1, IDespawnableMemoryPool<TValue> pool);
        void OnDespawned();
    }

    public interface ITickable
    {
        void Tick();
    }
}
