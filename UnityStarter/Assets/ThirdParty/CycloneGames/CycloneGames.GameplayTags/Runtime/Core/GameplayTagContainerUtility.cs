using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Core
{
   public static class GameplayTagContainerUtility
   {
      public static bool HasAll<T, U, V>(this T containerA, in U containerB, in V other)
         where T : IGameplayTagContainer where U : IGameplayTagContainer where V : IGameplayTagContainer
      {
         if (other is null || other.IsEmpty)
            return true;

         if (containerA is null || containerA.IsEmpty)
            return containerB != null && containerB.HasAll(other);

         if (containerB is null || containerB.IsEmpty)
            return containerA.HasAll(other);

         return HasAllUnion(containerA.Indices.Implicit, containerB.Indices.Implicit, other.Indices.Explicit);
      }

      public static bool HasAllExact<T, U, V>(this T containerA, in U containerB, in V other)
         where T : IGameplayTagContainer where U : IGameplayTagContainer where V : IGameplayTagContainer
      {
         if (other is null || other.IsEmpty)
            return true;

         if (containerA is null || containerA.IsEmpty)
            return containerB != null && containerB.HasAllExact(other);

         if (containerB is null || containerB.IsEmpty)
            return containerA.HasAllExact(other);

         return HasAllUnion(containerA.Indices.Explicit, containerB.Indices.Explicit, other.Indices.Explicit);
      }

      private static bool HasAllUnion(List<int> containerAIndices, List<int> containerBIndices, List<int> otherIndices)
      {
         if (otherIndices == null || otherIndices.Count == 0)
            return true;

         int aCount = containerAIndices?.Count ?? 0;
         int bCount = containerBIndices?.Count ?? 0;
         if (aCount == 0)
            return HasAllInList(containerBIndices, otherIndices);

         if (bCount == 0)
            return HasAllInList(containerAIndices, otherIndices);

         int i = 0;
         int j = 0;

         for (int k = 0; k < otherIndices.Count; k++)
         {
            int required = otherIndices[k];

            while (i < aCount && containerAIndices[i] < required)
               i++;

            while (j < bCount && containerBIndices[j] < required)
               j++;

            bool foundInA = i < aCount && containerAIndices[i] == required;
            bool foundInB = j < bCount && containerBIndices[j] == required;
            if (!foundInA && !foundInB)
               return false;
         }

         return true;
      }

      private static bool HasAllInList(List<int> containerIndices, List<int> otherIndices)
      {
         if (otherIndices == null || otherIndices.Count == 0)
            return true;

         if (containerIndices == null || containerIndices.Count == 0)
            return false;

         for (int i = 0; i < otherIndices.Count; i++)
         {
            if (BinarySearchUtility.Search(containerIndices, otherIndices[i]) < 0)
            {
               return false;
            }
         }

         return true;
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
            int otherRuntimeIndex = tagIndices[i];
            if (!GameplayTagManager.IsParentOf(otherRuntimeIndex, tag.RuntimeIndex))
            {
               break;
            }

            parentTags.Add(GameplayTagManager.GetTagFromRuntimeIndex(otherRuntimeIndex));
         }
      }

      internal static void GetChildTags(List<int> tagIndices, GameplayTag tag, List<GameplayTag> childTags)
      {
         int index = tagIndices.BinarySearch(tag.RuntimeIndex);
         index = index < 0 ? ~index : index + 1;

         for (int i = index; i < tagIndices.Count; i++)
         {
            int otherRuntimeIndex = tagIndices[i];
            if (!GameplayTagManager.IsChildOf(otherRuntimeIndex, tag.RuntimeIndex))
            {
               break;
            }

            childTags.Add(GameplayTagManager.GetTagFromRuntimeIndex(otherRuntimeIndex));
         }
      }
   }
}
