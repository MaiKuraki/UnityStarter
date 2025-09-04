# if PRESENT_BURST && PRESENT_ECS
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace CycloneGames.Factory.ECS.Samples
{
    public struct BulletComponent : IComponentData
    {
        public float3 Velocity;
        public float Lifetime;
    }

    public struct BulletPrefab : IComponentData
    {
        public Entity Value;
    }

    public class BulletAuthoring : MonoBehaviour
    {
        public float3 Velocity = new float3(0, 0, 10f);
        public float Lifetime = 5f;

        public class Baker : Baker<BulletAuthoring>
        {
            public override void Bake(BulletAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new BulletComponent
                {
                    Velocity = authoring.Velocity,
                    Lifetime = authoring.Lifetime
                });

                AddComponent(entity, LocalTransform.Identity);
            }
        }
    }
}
#endif // PRESENT_BURST && PRESENT_ECS