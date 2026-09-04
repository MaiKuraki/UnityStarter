using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// A required-plus-forbidden tag pair, evaluated against one or two containers.
   /// </summary>
   /// <remarks>
   /// <para>
   /// "Required" uses the expanded-set test, so requiring "A.B" is satisfied by a container holding
   /// "A.B.C" - the same rule <see cref="GameplayTagContainerExtensionMethods.HasAll{T,U}"/> applies.
   /// "Forbidden" uses the same expanded rule: a container holding "A.B.C" is forbidden by "A.B".
   /// </para>
   /// <para>
   /// The two-container overload answers "do these two containers together satisfy the requirement", which
   /// is what an ability needs when part of its requirement is static (the ability's own tags) and part is
   /// dynamic (the target's tags). That combined test used to live as an extension method with the same
   /// signature as the ordinary <c>HasAll</c> but a different meaning, which made every call site ambiguous;
   /// it lives here now, where the semantics are explicit in the name.
   /// </para>
   /// </remarks>
   [Serializable]
   public struct GameplayTagRequirements
   {
      private GameplayTagContainer m_ForbiddenTags;
      private GameplayTagContainer m_RequiredTags;

      /// <summary>Tags that must not be present. Never null after construction.</summary>
      public GameplayTagContainer ForbiddenTags
      {
         get
         {
            m_ForbiddenTags ??= new GameplayTagContainer();
            return m_ForbiddenTags;
         }
      }

      /// <summary>Tags that must all be present. Never null after construction.</summary>
      public GameplayTagContainer RequiredTags
      {
         get
         {
            m_RequiredTags ??= new GameplayTagContainer();
            return m_RequiredTags;
         }
      }

      public bool IsEmpty
      {
         get
         {
            return (m_ForbiddenTags == null || m_ForbiddenTags.IsEmpty) &&
                   (m_RequiredTags == null || m_RequiredTags.IsEmpty);
         }
      }

      public GameplayTagRequirements(GameplayTagContainer forbiddenTags, GameplayTagContainer requiredTags)
      {
         m_ForbiddenTags = forbiddenTags;
         m_RequiredTags = requiredTags;
      }

      public readonly bool Matches<T>(in T container) where T : IReadOnlyGameplayTagContainer
      {
         return !container.HasAny(m_ForbiddenTags) && container.HasAll(m_RequiredTags);
      }

      /// <summary>
      /// True when neither container holds a forbidden tag and, for every required tag, at least one of
      /// the two containers holds it.
      /// </summary>
      public readonly bool Matches<T, U>(in T staticContainer, in U dynamicContainer)
         where T : IReadOnlyGameplayTagContainer
         where U : IReadOnlyGameplayTagContainer
      {
         if (staticContainer.HasAny(m_ForbiddenTags) || dynamicContainer.HasAny(m_ForbiddenTags))
            return false;

         return HasAllAcross(staticContainer, dynamicContainer, m_RequiredTags);
      }

      public readonly bool MeetsRequirements(GameplayTagCountContainer container)
      {
         return !container.HasAny(m_ForbiddenTags) && container.HasAll(m_RequiredTags);
      }

      /// <summary>
      /// True when, for every explicitly held tag in <paramref name="required"/>, at least one of the two
      /// containers holds it in the expanded sense.
      /// </summary>
      private readonly bool HasAllAcross<T, U>(in T first, in U second, GameplayTagContainer required)
         where T : IReadOnlyGameplayTagContainer
         where U : IReadOnlyGameplayTagContainer
      {
         if (required == null || required.IsEmpty)
            return true;

         List<GameplayTag> scratch = RequiredScratch;
         scratch.Clear();
         GameplayTagEnumerator enumerator = required.GetExplicitTags();
         while (enumerator.MoveNext())
            scratch.Add(enumerator.Current);

         for (int i = 0; i < scratch.Count; i++)
         {
            GameplayTag tag = scratch[i];
            if (!first.ContainsRuntimeIndex(tag.RuntimeIndex, explicitOnly: false) &&
                !second.ContainsRuntimeIndex(tag.RuntimeIndex, explicitOnly: false))
            {
               return false;
            }
         }

         return true;
      }

      // A requirements struct is a value type, so a per-call list would allocate on every test. One
      // static scratch buffer shared by all of them is safe here: Matches is not reentrant and never
      // crosses a thread boundary, and the buffer is drained before each use.
      [ThreadStatic]
      private static List<GameplayTag> s_RequiredScratch;

      private static List<GameplayTag> RequiredScratch
      {
         get
         {
            s_RequiredScratch ??= new List<GameplayTag>(8);
            return s_RequiredScratch;
         }
      }
   }
}
