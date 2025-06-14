using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Runtime
{
   public static class GameplayTagContainerExtensionMethods
   {
      public static bool HasTag<T>(this T container, GameplayTag gameplayTag) where T : IGameplayTagContainer
      {
         if (gameplayTag == GameplayTag.None || container == null || container.IsEmpty) return false;
         return container.Indices.Implicit != null && BinarySearchUtility.Search(container.Indices.Implicit, gameplayTag.RuntimeIndex) >= 0;
      }

      public static bool HasTagExact<T>(this T container, GameplayTag gameplayTag) where T : IGameplayTagContainer
      {
         if (gameplayTag == GameplayTag.None || container == null || container.IsEmpty) return false;
         return container.Indices.Explicit != null && BinarySearchUtility.Search(container.Indices.Explicit, gameplayTag.RuntimeIndex) >= 0;
      }

      public static bool HasAny<T, U>(this T container, in U other) where T : IGameplayTagContainer where U : IGameplayTagContainer
      {
         if (container == null || other == null) return false;
         return HasAnyInternal(container.Indices.Implicit, other.Indices.Explicit);
      }

      public static bool HasAnyExact<T, U>(this T container, in U other) where T : IGameplayTagContainer where U : IGameplayTagContainer
      {
         if (container == null || other == null) return false;
         return HasAnyInternal(container.Indices.Explicit, other.Indices.Explicit);
      }

      // OPTIMIZATION: highly efficient two-pointer algorithm
      // for checking intersection between two sorted lists. This is GC-free and easy to understand.
      private static bool HasAnyInternal(List<int> containerIndices, List<int> otherIndices)
      {
         if (containerIndices == null || containerIndices.Count == 0 || otherIndices == null || otherIndices.Count == 0)
         {
            return false;
         }

         int i = 0; // Pointer for containerIndices
         int j = 0; // Pointer for otherIndices

         while (i < containerIndices.Count && j < otherIndices.Count)
         {
            if (containerIndices[i] == otherIndices[j])
            {
               return true; // Found a common tag
            }

            if (containerIndices[i] < otherIndices[j])
            {
               i++;
            }
            else
            {
               j++;
            }
         }

         return false;
      }

      public static bool HasAll<T, U>(this T container, in U other) where T : IGameplayTagContainer where U : IGameplayTagContainer
      {
         if (container == null) return false;
         if (other == null || other.IsEmpty) return true; // Has all of an empty set is true
         return HasAllInternal(container.Indices.Implicit, other.Indices.Explicit);
      }

      public static bool HasAll<T, U, V>(this T container, in U otherA, in V otherB) where T : IGameplayTagContainer where U : IGameplayTagContainer where V : IGameplayTagContainer
      {
         // The container must have all required tags from both other containers.
         return container.HasAll(otherA) && container.HasAll(otherB);
      }

      public static bool HasAllExact<T, U>(this T container, in U other) where T : IGameplayTagContainer where U : IGameplayTagContainer
      {
         if (container == null) return false;
         if (other == null || other.IsEmpty) return true;
         return HasAllInternal(container.Indices.Explicit, other.Indices.Explicit);
      }

      // OPTIMIZATION: highly efficient two-pointer algorithm
      // for checking if one sorted list is a superset of another. This is GC-free and efficient.
      private static bool HasAllInternal(List<int> containerIndices, List<int> otherIndices)
      {
         if (otherIndices == null || otherIndices.Count == 0)
         {
            return true; // A container always has "all" tags of an empty set.
         }

         if (containerIndices == null || containerIndices.Count < otherIndices.Count)
         {
            return false; // Cannot contain all if it has fewer items.
         }

         int i = 0; // Pointer for containerIndices
         int j = 0; // Pointer for otherIndices

         while (i < containerIndices.Count && j < otherIndices.Count)
         {
            if (containerIndices[i] == otherIndices[j])
            {
               i++;
               j++;
            }
            else if (containerIndices[i] < otherIndices[j])
            {
               i++;
            }
            else // containerIndices[i] > otherIndices[j]
            {
               return false; // A required tag from otherIndices was missed.
            }
         }

         // If we have checked all of otherIndices's elements, it's a match.
         return j == otherIndices.Count;
      }
   }
}