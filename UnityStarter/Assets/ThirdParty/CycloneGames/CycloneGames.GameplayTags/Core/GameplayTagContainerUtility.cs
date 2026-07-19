using System.Collections.Generic;

using System;

namespace CycloneGames.GameplayTags.Core
{
   public static class GameplayTagContainerUtility
   {
      private const int BitsetActivationTagCount = 64;
      private const int MaxBitsetWordsPerTag = 2;

      internal static bool ShouldUseBitset(int tagCount, int maxRuntimeIndex, out int wordCount)
      {
         wordCount = maxRuntimeIndex > 0 ? (maxRuntimeIndex >> 5) + 1 : 0;
         return tagCount >= BitsetActivationTagCount &&
                wordCount > 0 &&
                wordCount <= tagCount * MaxBitsetWordsPerTag;
      }

      public static bool HasAll<T, U, V>(this T containerA, in U containerB, in V other)
         where T : IReadOnlyGameplayTagContainer where U : IReadOnlyGameplayTagContainer where V : IReadOnlyGameplayTagContainer
      {
         EnsureCurrentRuntimeIndexEpoch(containerA);
         EnsureCurrentRuntimeIndexEpoch(containerB);
         EnsureCurrentRuntimeIndexEpoch(other);
         if (other is null || other.IsEmpty)
            return true;

         if (containerA is null || containerA.IsEmpty)
            return containerB != null && containerB.HasAll(other);

         if (containerB is null || containerB.IsEmpty)
            return containerA.HasAll(other);

         return HasAllUnion(containerA.Indices.Implicit, containerB.Indices.Implicit, other.Indices.Explicit);
      }

      public static bool HasAllExact<T, U, V>(this T containerA, in U containerB, in V other)
         where T : IReadOnlyGameplayTagContainer where U : IReadOnlyGameplayTagContainer where V : IReadOnlyGameplayTagContainer
      {
         EnsureCurrentRuntimeIndexEpoch(containerA);
         EnsureCurrentRuntimeIndexEpoch(containerB);
         EnsureCurrentRuntimeIndexEpoch(other);
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
         if (tagIndices == null || tagIndices.Count == 0)
            return;

         ReadOnlySpan<int> ancestorIndices = GameplayTagManager.GetParentRuntimeIndicesSpan(tag.RuntimeIndex);
         for (int i = ancestorIndices.Length - 1; i >= 0; i--)
         {
            int ancestorRuntimeIndex = ancestorIndices[i];
            if (BinarySearchUtility.Search(tagIndices, ancestorRuntimeIndex) >= 0)
               parentTags.Add(GameplayTagManager.GetTagFromRuntimeIndex(ancestorRuntimeIndex));
         }
      }

      internal static void GetChildTags(List<int> tagIndices, GameplayTag tag, List<GameplayTag> childTags)
      {
         if (tagIndices == null || tagIndices.Count == 0)
            return;

         ReadOnlySpan<int> descendantIndices = GameplayTagManager.GetChildRuntimeIndicesSpan(tag.RuntimeIndex);
         for (int i = 0; i < descendantIndices.Length; i++)
         {
            int descendantRuntimeIndex = descendantIndices[i];
            if (BinarySearchUtility.Search(tagIndices, descendantRuntimeIndex) >= 0)
               childTags.Add(GameplayTagManager.GetTagFromRuntimeIndex(descendantRuntimeIndex));
         }
      }

      internal static void EnsureCurrentRuntimeIndexEpoch<T>(in T container)
         where T : IReadOnlyGameplayTagContainer
      {
         if (container is IGameplayTagRuntimeIndexView runtimeIndexView &&
             runtimeIndexView.RuntimeIndexEpoch != GameplayTagManager.CurrentRuntimeIndexEpoch)
         {
            throw new InvalidOperationException(
               "Gameplay tag containers from different runtime-index epochs cannot be compared by runtime index.");
         }
      }
   }
}
