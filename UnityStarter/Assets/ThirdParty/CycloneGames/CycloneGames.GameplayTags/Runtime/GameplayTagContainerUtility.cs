using System.Collections.Generic;
using UnityEngine.Pool;

namespace CycloneGames.GameplayTags
{
   public static class GameplayTagContainerUtility
   {
      public static bool HasAll<T, U, V>(this T containerA, in U containerB, in V other)
         where T : IGameplayTagContainer where U : IGameplayTagContainer where V : IGameplayTagContainer
      {
         if (containerA.IsEmpty && containerB.IsEmpty)
         {
            return true;
         }

         if (containerA.IsEmpty)
         {
            return containerB.HasAll(other);
         }

         if (containerB.IsEmpty)
         {
            return containerA.HasAll(other);
         }

         using (GenericPool<GameplayTagContainer>.Get(out GameplayTagContainer intersection))
         {
            intersection.AddIntersection(containerA, containerB);
            bool hasAll = intersection.HasAll(other);
            intersection.Clear();

            return hasAll;
         }
      }

      public static bool HasAllExact<T, U, V>(this T containerA, in U containerB, in V other)
         where T : IGameplayTagContainer where U : IGameplayTagContainer where V : IGameplayTagContainer
      {
         if (containerA.IsEmpty && containerB.IsEmpty)
         {
            return true;
         }

         if (containerA.IsEmpty)
         {
            return containerB.HasAllExact(other);
         }

         if (containerB.IsEmpty)
         {
            return containerA.HasAllExact(other);
         }

         using (GenericPool<GameplayTagContainer>.Get(out GameplayTagContainer intersection))
         {
            intersection.AddIntersection(containerA, containerB);
            bool hasAllExact = intersection.HasAllExact(other);
            intersection.Clear();

            return hasAllExact;
         }
      }

      internal static void GetParentTags(List<int> tagIndices, GameplayTag tag, List<GameplayTag> parentTags)
      {
         int index = tagIndices.BinarySearch(tag.RuntimeIndex);
         if (index < 0)
         {
            index = ~index;
         }

         for (int i = index - 1; i >= 0; i--)
         {
            GameplayTagDefinition otherTagDefinition = GameplayTagManager.GetDefinitionFromRuntimeIndex(tagIndices[i]);

            if (!otherTagDefinition.IsParentOf(tag))
            {
               break;
            }

            parentTags.Add(otherTagDefinition.Tag);
         }
      }

      internal static void GetChildTags(List<int> tagIndices, GameplayTag tag, List<GameplayTag> childTags)
      {
         int index = tagIndices.BinarySearch(tag.RuntimeIndex);
         index = index < 0 ? ~index : index + 1;

         for (int i = index; i < tagIndices.Count; i++)
         {
            GameplayTagDefinition otherTagDefinition = GameplayTagManager.GetDefinitionFromRuntimeIndex(tagIndices[i]);

            if (!otherTagDefinition.IsChildOf(tag))
            {
               break;
            }

            childTags.Add(otherTagDefinition.Tag);
         }
      }
   }
}
