#if CYCLONE_HAS_ENTITIES
using System.Runtime.CompilerServices;
using Unity.Entities;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayTags.Unity
{
   /// <summary>
   /// ECS-compatible gameplay tag component using the blittable 256-bit GameplayTagMask.
   /// Can be used directly with SystemBase, IJobEntity, and Burst-compiled systems.
   ///
   /// Usage:
   ///   EntityManager.AddComponentData(entity, new GameplayTagMaskComponent { Mask = mask });
   ///
   ///   // In a system:
   ///   foreach (var (tags, entity) in SystemAPI.Query<RefRW<GameplayTagMaskComponent>>())
   ///   {
   ///       if (tags.ValueRO.HasTag(stunTagIndex))
   ///           // apply stun logic
   ///   }
   /// </summary>
   public struct GameplayTagMaskComponent : IComponentData
   {
      /// <summary>The 256-bit tag mask (blittable, Burst-compatible).</summary>
      public GameplayTagMask Mask;

      /// <summary>Test if a tag bit is set by runtime index. O(1).</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly bool HasTag(in GameplayTag tag) => Mask.HasTag(in tag);

      /// <summary>Set a tag bit by GameplayTag.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void SetTag(in GameplayTag tag) => Mask.AddTag(in tag);

      /// <summary>Clear a tag bit by GameplayTag.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void ClearTag(in GameplayTag tag) => Mask.RemoveTag(in tag);

      /// <summary>Check if all tags in another mask are present.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly bool HasAll(in GameplayTagMask other) => Mask.HasAll(in other);

      /// <summary>Check if any tag in another mask is present.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly bool HasAny(in GameplayTagMask other) => Mask.HasAny(in other);
   }

   /// <summary>
   /// Optional: A cleanup component that stores the "previous frame" mask
   /// for delta-based replication or change detection in ECS systems.
   /// </summary>
   public struct GameplayTagMaskPrevious : IComponentData
   {
      public GameplayTagMask Mask;
   }

   /// <summary>
   /// Tag component to mark entities that have dirty gameplay tags needing replication.
   /// </summary>
   public struct GameplayTagsDirty : IComponentData, IEnableableComponent
   {
   }
}
#endif
