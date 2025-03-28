using System.Collections.Generic;
using UnityEngine.Pool;

namespace CycloneGames.GameplayTags
{
   public static class GameplayTagContainerExtensionMethods
   {
      public static bool HasTag<T>(this T container, GameplayTag gameplayTag) where T : IGameplayTagContainer
      {
         return container.Indices.Implicit != null && BinarySearchUtility.Search(container.Indices.Implicit, gameplayTag.RuntimeIndex) >= 0;
      }

      public static bool HasTagExact<T>(this T container, GameplayTag gameplayTag) where T : IGameplayTagContainer
      {
         return container.Indices.Explicit != null && BinarySearchUtility.Search(container.Indices.Explicit, gameplayTag.RuntimeIndex) >= 0;
      }

      public static bool HasAny<T, U>(this T container, in U other) where T : IGameplayTagContainer where U : IGameplayTagContainer
      {
         return HasAnyInternal(container.Indices.Implicit, other?.Indices.Explicit);
      }

      public static bool HasAnyExact<T, U>(this T container, in U other) where T : IGameplayTagContainer where U : IGameplayTagContainer
      {
         return HasAnyInternal(container.Indices.Explicit, other?.Indices.Explicit);
      }

      private static bool HasAnyInternal(List<int> tagIndexes, List<int> otherTagIndexes)
      {
         if (otherTagIndexes == null || otherTagIndexes.Count == 0 || tagIndexes == null || tagIndexes.Count == 0)
         {
            return false;
         }

         int start = BinarySearchUtility.Search(tagIndexes, otherTagIndexes[0], 0, tagIndexes.Count - 1);
         if (start >= 0)
         {
            return true;
         }

         start = ~start;

         int end = BinarySearchUtility.Search(tagIndexes, otherTagIndexes[^1], start, tagIndexes.Count - 1);
         if (end >= 0)
         {
            return true;
         }

         end = ~end;

         int j = 1;
         int i = start + 1;
         while (i < end && j < otherTagIndexes.Count)
         {
            if (otherTagIndexes[j] == tagIndexes[i])
            {
               return true;
            }

            if (tagIndexes[i] > otherTagIndexes[j])
            {
               i++;
               continue;
            }

            j++;
            while (otherTagIndexes[j] < tagIndexes[i])
            {
               j++;
               if (j == end)
               {
                  return false;
               }
            }
         }

         return false;
      }

      private static bool HasAllInternal(List<int> tagIndexes, List<int> otherTagIndexes)
      {
         if (otherTagIndexes == null || otherTagIndexes.Count == 0)
         {
            return true;
         }

         if (tagIndexes == null || tagIndexes.Count == 0)
         {
            return false;
         }

         int start = BinarySearchUtility.Search(tagIndexes, otherTagIndexes[0], 0, tagIndexes.Count - 1);
         if (start < 0)
         {
            return false;
         }

         if (otherTagIndexes.Count == 1)
         {
            return true;
         }

         int end = BinarySearchUtility.Search(tagIndexes, otherTagIndexes[^1], 0, tagIndexes.Count - 1);
         if (end < 0)
         {
            return false;
         }

         int j = 1;
         end--;
         for (int i = start + 1; i < end; i++)
         {
            if (otherTagIndexes[j] == tagIndexes[i])
            {
               j++;
               continue;
            }

            if (otherTagIndexes[j] > tagIndexes[i])
            {
               return false;
            }
         }

         return j == otherTagIndexes.Count - 1;
      }

      public static bool HasAll<T, U>(this T container, in U other) where T : IGameplayTagContainer where U : IGameplayTagContainer
      {
         return HasAllInternal(container.Indices.Implicit, other?.Indices.Explicit);
      }

      public static bool HasAll<T, U, V>(this T container, in U otherA, in V otherB) where T : IGameplayTagContainer where U : IGameplayTagContainer where V : IGameplayTagContainer
      {
         if (otherA.IsEmpty && otherB.IsEmpty)
         {
            return true;
         }

         if (otherA.IsEmpty)
         {
            return HasAll(container, otherB);
         }

         if (otherB.IsEmpty)
         {
            return HasAll(container, otherA);
         }

         using (GenericPool<GameplayTagContainer>.Get(out GameplayTagContainer intersection))
         {
            intersection.AddIntersection(otherA, otherB);
            bool hasAll = HasAll(container, intersection);
            intersection.Clear();

            return hasAll;
         }
      }

      public static bool HasAllExact<T, U>(this T container, in U other) where T : IGameplayTagContainer where U : IGameplayTagContainer
      {
         return HasAllInternal(container.Indices.Explicit, other?.Indices.Explicit);
      }
   }
}
