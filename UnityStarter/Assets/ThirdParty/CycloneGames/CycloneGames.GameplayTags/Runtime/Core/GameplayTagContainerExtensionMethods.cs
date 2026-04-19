using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Runtime
{
   public static class GameplayTagContainerExtensionMethods
   {
      public static bool HasTag<T>(this T container, GameplayTag gameplayTag) where T : IGameplayTagContainer
      {
         if (gameplayTag.IsNone || container == null || container.IsEmpty) return false;
         return container.ContainsRuntimeIndex(gameplayTag.RuntimeIndex, explicitOnly: false);
      }

      public static bool HasTagExact<T>(this T container, GameplayTag gameplayTag) where T : IGameplayTagContainer
      {
         if (gameplayTag.IsNone || container == null || container.IsEmpty) return false;
         return container.ContainsRuntimeIndex(gameplayTag.RuntimeIndex, explicitOnly: true);
      }

      public static bool HasAny<T, U>(this T container, in U other) where T : IGameplayTagContainer where U : IGameplayTagContainer
      {
         if (container == null || other == null) return false;
         return HasAnyInternal(container, explicitOnly: false, other.Indices.Explicit);
      }

      public static bool HasAnyExact<T, U>(this T container, in U other) where T : IGameplayTagContainer where U : IGameplayTagContainer
      {
         if (container == null || other == null) return false;
         return HasAnyInternal(container, explicitOnly: true, other.Indices.Explicit);
      }

      // OPTIMIZATION: highly efficient two-pointer algorithm
      // for checking intersection between two sorted lists. This is GC-free and easy to understand.
      private static bool HasAnyInternal<T>(T container, bool explicitOnly, List<int> otherIndices) where T : IGameplayTagContainer
      {
         if (container == null || container.IsEmpty || otherIndices == null || otherIndices.Count == 0)
         {
            return false;
         }

         for (int i = 0; i < otherIndices.Count; i++)
         {
            if (container.ContainsRuntimeIndex(otherIndices[i], explicitOnly))
            {
               return true;
            }
         }

         return false;
      }

      public static bool HasAll<T, U>(this T container, in U other) where T : IGameplayTagContainer where U : IGameplayTagContainer
      {
         if (container == null) return false;
         if (other == null || other.IsEmpty) return true; // Has all of an empty set is true
         return HasAllInternal(container, explicitOnly: false, other.Indices.Explicit);
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
         return HasAllInternal(container, explicitOnly: true, other.Indices.Explicit);
      }

      // OPTIMIZATION: highly efficient two-pointer algorithm
      // for checking if one sorted list is a superset of another. This is GC-free and efficient.
      internal static bool HasAllInternal<T>(T container, bool explicitOnly, List<int> otherIndices) where T : IGameplayTagContainer
      {
         if (otherIndices == null || otherIndices.Count == 0)
         {
            return true; // A container always has "all" tags of an empty set.
         }

         if (container == null || container.IsEmpty)
         {
            return false;
         }

         for (int i = 0; i < otherIndices.Count; i++)
         {
            if (!container.ContainsRuntimeIndex(otherIndices[i], explicitOnly))
            {
               return false;
            }
         }

         return true;
      }
   }
}
