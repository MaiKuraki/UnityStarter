# if PRESENT_BURST && PRESENT_ECS
using Unity.Entities;

namespace CycloneGames.Factory.ECS.Runtime
{
    public class PrefabEntityFactory : IEntityFactory
    {
        private readonly EntityManager entityManager;
        private readonly Entity prefab;

        public PrefabEntityFactory(EntityManager manager, Entity entityPrefab)
        {
            entityManager = manager;
            prefab = entityPrefab;
        }

        public Entity Create()
        {
            return entityManager.Instantiate(prefab);
        }
    }

    public class PrefabEntityFactory<T> : IEntityFactory<T> where T : unmanaged, IComponentData
    {
        private readonly EntityManager entityManager;
        private readonly Entity prefab;

        public PrefabEntityFactory(EntityManager manager, Entity entityPrefab)
        {
            entityManager = manager;
            prefab = entityPrefab;
        }

        public Entity Create(T component)
        {
            var entity = entityManager.Instantiate(prefab);
            entityManager.SetComponentData(entity, component);
            entityManager.SetEnabled(entity, false);
            return entity;
        }

        public Entity Create(EntityCommandBuffer ecb, T component)
        {
            var entity = ecb.Instantiate(prefab);
            ecb.SetComponent(entity, component);
            ecb.SetEnabled(entity, false);
            return entity;
        }
    }
}
#endif // PRESENT_BURST && PRESENT_ECS