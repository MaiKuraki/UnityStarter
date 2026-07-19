using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// An immutable snapshot of a GameplayTagContainer's state bound to the registry snapshot
   /// captured during construction. Its read methods do not reinterpret stored runtime indices
   /// through a later registry snapshot.
   ///
   /// Usage:
   ///   var snapshot = container.CreateSnapshot();
   ///   bool has = snapshot.HasTag(someTag);
   /// </summary>
   public sealed class ReadOnlyGameplayTagContainer : IReadOnlyGameplayTagContainer, IGameplayTagRuntimeIndexView
   {
      private readonly int[] _explicitIndices;
      private readonly int[] _implicitIndices;
      private readonly int[] _bitset;
      private readonly GameplayTagContainerIndices _indices;
      private readonly TagDataSnapshot _snapshot;

      public bool IsCompatibleWithCurrentRegistry =>
         ReferenceEquals(_snapshot, GameplayTagManager.Snapshot) ||
         _snapshot.RuntimeIndexEpoch == GameplayTagManager.CurrentRuntimeIndexEpoch;

      public GameplayTagContainerIndices Indices
      {
         get
         {
            EnsureCompatible();
            return _indices;
         }
      }

      int IGameplayTagRuntimeIndexView.RuntimeIndexEpoch => _snapshot.RuntimeIndexEpoch;

      /// <summary>Number of explicitly added tags.</summary>
      public int ExplicitTagCount
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => _explicitIndices.Length;
      }

      /// <summary>Total tag count (explicit + implicit hierarchy).</summary>
      public int TagCount
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => _implicitIndices.Length;
      }

      /// <summary>True if no tags are present.</summary>
      public bool IsEmpty
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => _explicitIndices.Length == 0;
      }

      /// <summary>
      /// Create an immutable snapshot from a container's current state.
      /// </summary>
      public ReadOnlyGameplayTagContainer(IReadOnlyGameplayTagContainer source)
      {
         if (source is ReadOnlyGameplayTagContainer immutableSource)
         {
            _snapshot = immutableSource._snapshot;
            _explicitIndices = immutableSource._explicitIndices;
            _implicitIndices = immutableSource._implicitIndices;
         }
         else
         {
            TagDataSnapshot capturedSnapshot;
            int[] explicitIndices;
            int[] implicitIndices;
            do
            {
               capturedSnapshot = GameplayTagManager.Snapshot;
               if (source == null || source.IsEmpty)
               {
                  explicitIndices = Array.Empty<int>();
                  implicitIndices = Array.Empty<int>();
               }
               else
               {
                  GameplayTagContainerIndices sourceIndices = source.Indices;
                  explicitIndices = sourceIndices.Explicit != null
                     ? sourceIndices.Explicit.ToArray()
                     : Array.Empty<int>();
                  implicitIndices = sourceIndices.Implicit != null
                     ? sourceIndices.Implicit.ToArray()
                     : Array.Empty<int>();
               }
            }
            while (!ReferenceEquals(capturedSnapshot, GameplayTagManager.Snapshot));

            _snapshot = capturedSnapshot;
            _explicitIndices = explicitIndices;
            _implicitIndices = implicitIndices;
         }

         _indices = GameplayTagContainerIndices.Create();
         _indices.Explicit.AddRange(_explicitIndices);
         _indices.Implicit.AddRange(_implicitIndices);

         // Dense snapshots use a bitset; sparse snapshots retain binary-search storage.
         if (_implicitIndices.Length > 0 &&
             GameplayTagContainerUtility.ShouldUseBitset(
                _implicitIndices.Length,
                _implicitIndices[_implicitIndices.Length - 1],
                out int bitsetLength))
         {
            _bitset = new int[bitsetLength];
            for (int i = 0; i < _implicitIndices.Length; i++)
            {
               int idx = _implicitIndices[i];
               _bitset[idx >> 5] |= 1 << (idx & 31);
            }
         }
         else
         {
            _bitset = null;
         }
      }

      /// <summary>Check if a tag (or any of its parents) is present. O(1) with bitset, O(log n) otherwise.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public bool HasTag(in GameplayTag tag)
      {
         EnsureCompatible();
         if (!TryResolveRuntimeIndex(tag, out int runtimeIndex))
            return false;

         if (_bitset != null)
         {
            int word = runtimeIndex >> 5;
            return word < _bitset.Length && (_bitset[word] & (1 << (runtimeIndex & 31))) != 0;
         }

         return BinarySearchUtility.Search(_implicitIndices, runtimeIndex) >= 0;
      }

      /// <summary>Check if a tag is explicitly present (not inherited). O(log n).</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public bool HasTagExact(in GameplayTag tag)
      {
         EnsureCompatible();
         return TryResolveRuntimeIndex(tag, out int runtimeIndex) &&
                BinarySearchUtility.Search(_explicitIndices, runtimeIndex) >= 0;
      }

      /// <summary>Check if all tags in another container are present.</summary>
      public bool HasAll<T>(in T other) where T : IReadOnlyGameplayTagContainer
      {
         EnsureCompatible();
         if (other == null || other.IsEmpty) return true;

         foreach (GameplayTag tag in other.GetExplicitTags())
         {
            if (!HasTag(tag)) return false;
         }
         return true;
      }

      /// <summary>Check if any tag in another container is present.</summary>
      public bool HasAny<T>(in T other) where T : IReadOnlyGameplayTagContainer
      {
         EnsureCompatible();
         if (other == null || other.IsEmpty) return false;

         foreach (GameplayTag tag in other.GetExplicitTags())
         {
            if (HasTag(tag)) return true;
         }
         return false;
      }

      /// <summary>Check if all tags from another snapshot are present.</summary>
      public bool HasAll(ReadOnlyGameplayTagContainer other)
      {
         EnsureCompatible();
         other?.EnsureCompatible();
         if (other == null || other.IsEmpty) return true;

         foreach (GameplayTag tag in other.GetExplicitTags())
         {
            if (!HasTag(tag))
               return false;
         }
         return true;
      }

      /// <summary>Check if any tag from another snapshot is present.</summary>
      public bool HasAny(ReadOnlyGameplayTagContainer other)
      {
         EnsureCompatible();
         other?.EnsureCompatible();
         if (other == null || other.IsEmpty) return false;

         foreach (GameplayTag tag in other.GetExplicitTags())
         {
            if (HasTag(tag))
               return true;
         }
         return false;
      }

      /// <summary>Get a span of all implicit (full hierarchy) snapshot-local runtime indices.</summary>
      public ReadOnlySpan<int> GetImplicitIndices()
      {
         EnsureCompatible();
         return _implicitIndices;
      }

      /// <summary>Get a span of all explicit snapshot-local runtime indices.</summary>
      public ReadOnlySpan<int> GetExplicitIndices()
      {
         EnsureCompatible();
         return _explicitIndices;
      }

      public GameplayTagEnumerator GetTags()
      {
         EnsureCompatible();
         return new GameplayTagEnumerator(_indices.Implicit, _snapshot);
      }

      public GameplayTagEnumerator GetExplicitTags()
      {
         EnsureCompatible();
         return new GameplayTagEnumerator(_indices.Explicit, _snapshot);
      }

      public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags)
      {
         EnsureCompatible();
         CollectParents(_implicitIndices, tag, parentTags);
      }

      public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags)
      {
         EnsureCompatible();
         CollectChildren(_implicitIndices, tag, childTags);
      }

      public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags)
      {
         EnsureCompatible();
         CollectParents(_explicitIndices, tag, parentTags);
      }

      public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags)
      {
         EnsureCompatible();
         CollectChildren(_explicitIndices, tag, childTags);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public bool ContainsRuntimeIndex(int runtimeIndex, bool explicitOnly)
      {
         EnsureCompatible();
         if (runtimeIndex <= 0)
            return false;

         if (!explicitOnly && _bitset != null)
         {
            int word = runtimeIndex >> 5;
            return word < _bitset.Length && (_bitset[word] & (1 << (runtimeIndex & 31))) != 0;
         }

         int[] indices = explicitOnly ? _explicitIndices : _implicitIndices;
         return BinarySearchUtility.Search(indices, runtimeIndex) >= 0;
      }

      public GameplayTagEnumerator GetEnumerator()
      {
         return GetTags();
      }

      IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator()
      {
         return GetTags();
      }

      IEnumerator IEnumerable.GetEnumerator()
      {
         return GetTags();
      }

      private void CollectParents(int[] containerIndices, GameplayTag tag, List<GameplayTag> output)
      {
         if (output == null)
            throw new ArgumentNullException(nameof(output));
         if (!TryResolveRuntimeIndex(tag, out int runtimeIndex))
            return;

         ReadOnlySpan<int> ancestorIndices = _snapshot.GetParentRuntimeIndicesSpan(runtimeIndex);
         for (int i = ancestorIndices.Length - 1; i >= 0; i--)
         {
            int ancestorRuntimeIndex = ancestorIndices[i];
            if (BinarySearchUtility.Search(containerIndices, ancestorRuntimeIndex) >= 0)
               output.Add(_snapshot.GetTagFromRuntimeIndex(ancestorRuntimeIndex));
         }
      }

      private void CollectChildren(int[] containerIndices, GameplayTag tag, List<GameplayTag> output)
      {
         if (output == null)
            throw new ArgumentNullException(nameof(output));
         if (!TryResolveRuntimeIndex(tag, out int runtimeIndex))
            return;

         ReadOnlySpan<int> descendantIndices = _snapshot.GetChildRuntimeIndicesSpan(runtimeIndex);
         for (int i = 0; i < descendantIndices.Length; i++)
         {
            int descendantRuntimeIndex = descendantIndices[i];
            if (BinarySearchUtility.Search(containerIndices, descendantRuntimeIndex) >= 0)
               output.Add(_snapshot.GetTagFromRuntimeIndex(descendantRuntimeIndex));
         }
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private bool TryResolveRuntimeIndex(in GameplayTag tag, out int runtimeIndex)
      {
         string name = tag.m_Name;
         if (!string.IsNullOrEmpty(name))
            return _snapshot.TryGetRuntimeIndex(name, out runtimeIndex);

         runtimeIndex = 0;
         return false;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private void EnsureCompatible()
      {
         if (!IsCompatibleWithCurrentRegistry)
         {
            throw new InvalidOperationException(
               "ReadOnlyGameplayTagContainer belongs to an incompatible gameplay tag runtime-index epoch.");
         }
      }
   }

   /// <summary>
   /// Extension methods for creating immutable snapshots.
   /// </summary>
   public static class GameplayTagContainerSnapshotExtensions
   {
      /// <summary>
      /// Create an immutable snapshot of the container's current state.
      /// </summary>
      public static ReadOnlyGameplayTagContainer CreateSnapshot(this IReadOnlyGameplayTagContainer container)
      {
         return new ReadOnlyGameplayTagContainer(container);
      }
   }
}
