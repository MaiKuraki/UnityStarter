# if PRESENT_BURST && PRESENT_ECS
using Unity.Entities;

namespace CycloneGames.Factory.ECS.Runtime
{
    public struct DespawnTag : IComponentData { }
}
#endif // PRESENT_BURST && PRESENT_ECS