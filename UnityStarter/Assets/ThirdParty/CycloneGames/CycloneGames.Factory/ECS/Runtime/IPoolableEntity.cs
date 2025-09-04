# if PRESENT_BURST && PRESENT_ECS
using Unity.Entities;

namespace CycloneGames.Factory.ECS.Runtime
{
    public interface IPoolableEntity
    {
        void OnSpawned(EntityManager entityManager, Entity entity);
        void OnDespawned(EntityManager entityManager, Entity entity);
    }

    public interface IPoolableEntity<in T> where T : unmanaged
    {
        void OnSpawned(EntityManager entityManager, Entity entity, T data);
        void OnDespawned(EntityManager entityManager, Entity entity);
    }
}
#endif // PRESENT_BURST && PRESENT_ECS