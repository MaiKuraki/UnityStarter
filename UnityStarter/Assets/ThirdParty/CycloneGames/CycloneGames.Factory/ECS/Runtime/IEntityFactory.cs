# if PRESENT_BURST && PRESENT_ECS
using Unity.Entities;

namespace CycloneGames.Factory.ECS.Runtime
{
    public interface IEntityFactory
    {
        Entity Create();
    }

    public interface IEntityFactory<in T> where T : unmanaged, IComponentData
    {
        Entity Create(T component);
        Entity Create(EntityCommandBuffer ecb, T component);
    }
}
#endif // PRESENT_BURST && PRESENT_ECS