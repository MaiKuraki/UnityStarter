using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// A frozen, allocation-free view of a container's tag set.
   /// </summary>
   /// <remarks>
   /// <para>
   /// This is the type to hand across a thread, into a job, or into a cache: it owns its index arrays, it
   /// is immutable, and every query is a pure index operation. Contrast
   /// <see cref="GameplayTagContainer"/>, which is owner-thread mutable state.
   /// </para>
   /// <para>
   /// The view is bound to one registry epoch by construction. <see cref="IsCompatibleWithCurrentRegistry"/>
   /// reports whether that epoch is still current; the queries themselves never check, because they would
   /// otherwise tax every read for a condition that never arises during play.
   /// </para>
   /// <para>
   /// <see cref="HasTag"/> and friends test the expanded set - the explicit tags plus all of their
   /// ancestors - matching <see cref="GameplayTagContainer"/> semantics.
   /// </para>
   /// </remarks>
   public sealed class ReadOnlyGameplayTagContainer : IReadOnlyGameplayTagContainer, IEnumerable<GameplayTag>
   {
      private readonly int[] m_Explicit;
      private readonly int[] m_Implicit;
      private readonly int[] m_Bitset;
      private readonly int m_RuntimeIndexEpoch;
      private readonly TagDataSnapshot m_Snapshot;

      /// <summary>Builds a frozen view of another container's current tag set.</summary>
      public ReadOnlyGameplayTagContainer(IReadOnlyGameplayTagContainer source)
      {
         if (source == null)
            throw new ArgumentNullException(nameof(source));

         // Read by index: GetTags/GetExplicitTags return a struct, and reading them through this
         // interface would box it twice per snapshot.
         int explicitCount = source.ExplicitTagCount;
         m_Explicit = new int[explicitCount];
         for (int i = 0; i < explicitCount; i++)
            m_Explicit[i] = source.GetExplicitTag(i).RuntimeIndex;

         int implicitCount = source.TagCount;
         m_Implicit = new int[implicitCount];
         for (int i = 0; i < implicitCount; i++)
            m_Implicit[i] = source.GetTag(i).RuntimeIndex;

         m_Snapshot = GameplayTagManager.Snapshot;
         m_RuntimeIndexEpoch = m_Snapshot.RuntimeIndexEpoch;
         m_Bitset = BuildBitset(m_Explicit, m_Implicit, m_Snapshot.TagCount);
      }

      private ReadOnlyGameplayTagContainer(
         int[] explicitIndices,
         int[] implicitIndices,
         int[] bitset,
         TagDataSnapshot snapshot)
      {
         m_Explicit = explicitIndices;
         m_Implicit = implicitIndices;
         m_Bitset = bitset;
         m_Snapshot = snapshot;
         m_RuntimeIndexEpoch = snapshot.RuntimeIndexEpoch;
      }

      /// <summary>True when the registry epoch this view was built against is still current.</summary>
      public bool IsCompatibleWithCurrentRegistry
         => m_RuntimeIndexEpoch == GameplayTagManager.Snapshot.RuntimeIndexEpoch;

      /// <summary>The registry epoch this view is bound to.</summary>
      public int RuntimeIndexEpoch => m_RuntimeIndexEpoch;

      public int ExplicitTagCount => m_Explicit.Length;

      public int TagCount => m_Implicit.Length;

      public bool IsEmpty => m_Explicit.Length == 0;

      public GameplayTag GetTag(int index)
      {
         if ((uint)index >= (uint)m_Implicit.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

         return new GameplayTag(m_Implicit[index]);
      }

      public GameplayTag GetExplicitTag(int index)
      {
         if ((uint)index >= (uint)m_Explicit.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

         return new GameplayTag(m_Explicit[index]);
      }

      public bool HasTag(in GameplayTag tag)
         => !tag.IsNone && m_Implicit.Length > 0 && ContainsRuntimeIndex(tag.RuntimeIndex, explicitOnly: false);

      public bool HasTagExact(in GameplayTag tag)
         => !tag.IsNone && m_Explicit.Length > 0 && ContainsRuntimeIndex(tag.RuntimeIndex, explicitOnly: true);

      public bool HasAll<T>(in T other) where T : IReadOnlyGameplayTagContainer
         => GameplayTagContainerExtensionMethods.HasAll(this, other);

      public bool HasAny<T>(in T other) where T : IReadOnlyGameplayTagContainer
         => GameplayTagContainerExtensionMethods.HasAny(this, other);

      /// <summary>
      /// The raw explicit indices, ascending. The span aliases this view's storage, which never changes.
      /// </summary>
      public ReadOnlySpan<int> GetExplicitIndices() => m_Explicit;

      /// <summary>
      /// The raw expanded indices, ascending. The span aliases this view's storage, which never changes.
      /// </summary>
      public ReadOnlySpan<int> GetImplicitIndices() => m_Implicit;

      public GameplayTagEnumerator GetTags() => new(m_Implicit, m_Implicit.Length);

      public GameplayTagEnumerator GetExplicitTags() => new(m_Explicit, m_Explicit.Length);

      public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags)
         => FillAncestors(m_Implicit, tag, parentTags);

      public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags)
         => FillDescendants(m_Implicit, tag, childTags);

      public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags)
         => FillAncestors(m_Explicit, tag, parentTags);

      public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags)
         => FillDescendants(m_Explicit, tag, childTags);

      public bool ContainsRuntimeIndex(int runtimeIndex, bool explicitOnly)
      {
         if (runtimeIndex <= 0)
            return false;

         if (m_Bitset.Length > 0)
            return HasBit(m_Bitset, runtimeIndex, explicitOnly);

         return explicitOnly
            ? BinarySearchUtility.Contains(m_Explicit, m_Explicit.Length, runtimeIndex)
            : BinarySearchUtility.Contains(m_Implicit, m_Implicit.Length, runtimeIndex);
      }

      private void FillAncestors(int[] indices, GameplayTag tag, List<GameplayTag> destination)
      {
         if (indices.Length == 0 || destination == null)
            return;
         if ((uint)tag.RuntimeIndex >= (uint)m_Snapshot.TotalTagCount)
            return;

         // The pool slice is root-first, so walking it backwards yields nearest-ancestor-first.
         int start = m_Snapshot.HierarchyOffsets[tag.RuntimeIndex];
         int end = m_Snapshot.HierarchyOffsets[tag.RuntimeIndex + 1] - 1;
         for (int i = end - 1; i >= start; i--)
         {
            int ancestor = m_Snapshot.HierarchyPool[i];
            if (BinarySearchUtility.Contains(indices, indices.Length, ancestor))
               destination.Add(new GameplayTag(ancestor));
         }
      }

      private void FillDescendants(int[] indices, GameplayTag tag, List<GameplayTag> destination)
      {
         if (indices.Length == 0 || destination == null)
            return;
         if ((uint)tag.RuntimeIndex >= (uint)m_Snapshot.TotalTagCount)
            return;

         int start = m_Snapshot.ChildOffsets[tag.RuntimeIndex];
         int end = m_Snapshot.ChildOffsets[tag.RuntimeIndex + 1];
         for (int i = start; i < end; i++)
         {
            int child = m_Snapshot.ChildPool[i];
            if (BinarySearchUtility.Contains(indices, indices.Length, child))
               destination.Add(new GameplayTag(child));
         }
      }

      private static int[] BuildBitset(int[] explicitIndices, int[] implicitIndices, int tagCount)
      {
         if (implicitIndices.Length == 0)
            return Array.Empty<int>();

         int maxIndex = Math.Min(implicitIndices[implicitIndices.Length - 1], tagCount);
         int wordCount = (maxIndex / GameplayTagContainer.IndicesPerWord) + 1;
         if (implicitIndices.Length < GameplayTagContainer.BitsetActivationTagCount ||
             wordCount > implicitIndices.Length * GameplayTagContainer.MaxBitsetWordsPerTag)
         {
            return Array.Empty<int>();
         }

         int[] bitset = new int[wordCount];
         for (int i = 0; i < explicitIndices.Length; i++)
            SetBit(bitset, explicitIndices[i], explicitOnly: true);
         for (int i = 0; i < implicitIndices.Length; i++)
            SetBit(bitset, implicitIndices[i], explicitOnly: false);

         return bitset;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static bool HasBit(int[] bitset, int runtimeIndex, bool explicitOnly)
      {
         int word = runtimeIndex / GameplayTagContainer.IndicesPerWord;
         if ((uint)word >= (uint)bitset.Length)
            return false;

         int bit = 1 << (((runtimeIndex % GameplayTagContainer.IndicesPerWord)
             * GameplayTagContainer.BitsPerIndex) + (explicitOnly ? 0 : 1));
         return (bitset[word] & bit) != 0;
      }

      private static void SetBit(int[] bitset, int runtimeIndex, bool explicitOnly)
      {
         int word = runtimeIndex / GameplayTagContainer.IndicesPerWord;
         if ((uint)word >= (uint)bitset.Length)
            return;

         bitset[word] |= 1 << (((runtimeIndex % GameplayTagContainer.IndicesPerWord)
             * GameplayTagContainer.BitsPerIndex) + (explicitOnly ? 0 : 1));
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public GameplayTagEnumerator GetEnumerator() => new(m_Implicit, m_Implicit.Length);

      IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => GetEnumerator();

      IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
   }

   public static class GameplayTagContainerSnapshotExtensions
   {
      /// <summary>Freezes a container's current tag set into an immutable view.</summary>
      public static ReadOnlyGameplayTagContainer CreateSnapshot(this IReadOnlyGameplayTagContainer container)
         => new(container);
   }
}
