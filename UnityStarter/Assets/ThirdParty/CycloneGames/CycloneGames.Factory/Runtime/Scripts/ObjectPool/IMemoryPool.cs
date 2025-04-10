namespace CycloneGames.Factory
{
    public interface IMemoryPool
    {
        int NumTotal { get; }
        int NumActive { get; }
        int NumInactive { get; }
        System.Type ItemType { get; }
        void Resize(int desiredPoolSize);
        void Clear();
        void ExpandBy(int numToAdd);
        void ShrinkBy(int numToRemove);
        void Despawn(object obj);
    }
    public interface IDespawnableMemoryPool<TValue> : IMemoryPool
    {
        void Despawn(TValue item);
    }
    public interface IMemoryPool<TValue> : IDespawnableMemoryPool<TValue>
    {
        TValue Spawn();
    }
    public interface IMemoryPool<in TParam1, TValue> : IDespawnableMemoryPool<TValue>
    {
        TValue Spawn(TParam1 param);
    }
}