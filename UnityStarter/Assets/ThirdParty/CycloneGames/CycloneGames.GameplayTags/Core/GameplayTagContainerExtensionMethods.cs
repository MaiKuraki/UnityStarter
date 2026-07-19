using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Core
{
   public static class GameplayTagContainerExtensionMethods
   {
      public static bool HasTag<T>(this T container, GameplayTag gameplayTag) where T : IReadOnlyGameplayTagContainer
      {
         if (gameplayTag.IsNone || container == null || container.IsEmpty) return false;
         GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(container);
         return container.ContainsRuntimeIndex(gameplayTag.RuntimeIndex, explicitOnly: false);
      }

      public static bool HasTagExact<T>(this T container, GameplayTag gameplayTag) where T : IReadOnlyGameplayTagContainer
      {
         if (gameplayTag.IsNone || container == null || container.IsEmpty) return false;
         GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(container);
         return container.ContainsRuntimeIndex(gameplayTag.RuntimeIndex, explicitOnly: true);
      }

      public static bool HasAny<T, U>(this T container, in U other) where T : IReadOnlyGameplayTagContainer where U : IReadOnlyGameplayTagContainer
      {
         if (container == null || other == null) return false;
         GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(container);
         GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(other);
         return HasAnyInternal(container, explicitOnly: false, other.Indices.Explicit);
      }

      public static bool HasAnyExact<T, U>(this T container, in U other) where T : IReadOnlyGameplayTagContainer where U : IReadOnlyGameplayTagContainer
      {
         if (container == null || other == null) return false;
         GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(container);
         GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(other);
         return HasAnyInternal(container, explicitOnly: true, other.Indices.Explicit);
      }

      private static bool HasAnyInternal<T>(T container, bool explicitOnly, List<int> otherIndices) where T : IReadOnlyGameplayTagContainer
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

      public static bool HasAll<T, U>(this T container, in U other) where T : IReadOnlyGameplayTagContainer where U : IReadOnlyGameplayTagContainer
      {
         if (container == null) return false;
         if (other == null || other.IsEmpty) return true; // Has all of an empty set is true
         GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(container);
         GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(other);
         return HasAllInternal(container, explicitOnly: false, other.Indices.Explicit);
      }

      public static bool HasAll<T, U, V>(this T container, in U otherA, in V otherB) where T : IReadOnlyGameplayTagContainer where U : IReadOnlyGameplayTagContainer where V : IReadOnlyGameplayTagContainer
      {
         // The container must have all required tags from both other containers.
         return container.HasAll(otherA) && container.HasAll(otherB);
      }

      public static bool HasAllExact<T, U>(this T container, in U other) where T : IReadOnlyGameplayTagContainer where U : IReadOnlyGameplayTagContainer
      {
         if (container == null) return false;
         if (other == null || other.IsEmpty) return true;
         GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(container);
         GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(other);
         return HasAllInternal(container, explicitOnly: true, other.Indices.Explicit);
      }

      internal static bool HasAllInternal<T>(T container, bool explicitOnly, List<int> otherIndices) where T : IReadOnlyGameplayTagContainer
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
