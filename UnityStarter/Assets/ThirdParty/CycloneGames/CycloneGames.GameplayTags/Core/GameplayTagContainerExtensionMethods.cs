using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// Tag membership tests over any <see cref="IReadOnlyGameplayTagContainer"/>.
   /// </summary>
   /// <remarks>
   /// <para>
   /// These are the only query entry points downstream code should use, because they work uniformly on a
   /// mutable <see cref="GameplayTagContainer"/>, a frozen <see cref="ReadOnlyGameplayTagContainer"/>, and
   /// a <see cref="GameplayTagCountContainer"/>.
   /// </para>
   /// <para>
   /// <b>Semantics.</b> The non-Exact forms test against the expanded set - the container's tags plus
   /// every ancestor of each - matching Unreal's <c>FGameplayTagContainer</c>. The Exact forms test only
   /// the explicitly held tags. "Has all of an empty set" is true, "has any of an empty set" is false.
   /// </para>
   /// <para>
   /// There was previously a second <see cref="HasAll{T,U,V}"/> overload in this namespace with the same
   /// signature but the meaning "the union of two containers has all of the third". Same signature and
   /// different meaning is a call-site ambiguity, so that variant is gone; the combined-requirement case
   /// belongs to <see cref="GameplayTagRequirements"/>.
   /// </para>
   /// <para>
   /// None of these allocate and none check the registry epoch. See
   /// <see cref="IGameplayTagContainer"/> for the epoch contract.
   /// </para>
   /// </remarks>
   public static class GameplayTagContainerExtensionMethods
   {
      public static bool HasTag<T>(this T container, GameplayTag gameplayTag)
         where T : IReadOnlyGameplayTagContainer
      {
         return !gameplayTag.IsNone
            && container != null
            && !container.IsEmpty
            && container.ContainsRuntimeIndex(gameplayTag.RuntimeIndex, explicitOnly: false);
      }

      public static bool HasTagExact<T>(this T container, GameplayTag gameplayTag)
         where T : IReadOnlyGameplayTagContainer
      {
         return !gameplayTag.IsNone
            && container != null
            && !container.IsEmpty
            && container.ContainsRuntimeIndex(gameplayTag.RuntimeIndex, explicitOnly: true);
      }

      public static bool HasAny<T, U>(this T container, in U other)
         where T : IReadOnlyGameplayTagContainer
         where U : IReadOnlyGameplayTagContainer
         => HasAnyInternal(container, other, explicitOnly: false);

      public static bool HasAnyExact<T, U>(this T container, in U other)
         where T : IReadOnlyGameplayTagContainer
         where U : IReadOnlyGameplayTagContainer
         => HasAnyInternal(container, other, explicitOnly: true);

      public static bool HasAll<T, U>(this T container, in U other)
         where T : IReadOnlyGameplayTagContainer
         where U : IReadOnlyGameplayTagContainer
         => HasAllInternal(container, other, explicitOnly: false);

      public static bool HasAllExact<T, U>(this T container, in U other)
         where T : IReadOnlyGameplayTagContainer
         where U : IReadOnlyGameplayTagContainer
         => HasAllInternal(container, other, explicitOnly: true);

      private static bool HasAnyInternal<T, U>(T container, in U other, bool explicitOnly)
         where T : IReadOnlyGameplayTagContainer
         where U : IReadOnlyGameplayTagContainer
      {
         if (container == null || container.IsEmpty || other == null || other.IsEmpty)
            return false;

         // Iterating by index rather than through GetExplicitTags matters here. That member returns a
         // struct, and calling a struct-returning member through an interface boxes the result - so the
         // generic form would allocate on every HasAny/HasAll, which is the one thing these helpers must
         // never do. GetExplicitTag(i) is an interface dispatch but returns nothing to box.
         int requiredCount = other.ExplicitTagCount;
         for (int i = 0; i < requiredCount; i++)
         {
            if (container.ContainsRuntimeIndex(other.GetExplicitTag(i).RuntimeIndex, explicitOnly))
               return true;
         }

         return false;
      }

      private static bool HasAllInternal<T, U>(T container, in U other, bool explicitOnly)
         where T : IReadOnlyGameplayTagContainer
         where U : IReadOnlyGameplayTagContainer
      {
         if (other == null || other.IsEmpty)
            return true;
         if (container == null || container.IsEmpty)
            return false;

         int requiredCount = other.ExplicitTagCount;
         for (int i = 0; i < requiredCount; i++)
         {
            if (!container.ContainsRuntimeIndex(other.GetExplicitTag(i).RuntimeIndex, explicitOnly))
               return false;
         }

         return true;
      }
   }
}
