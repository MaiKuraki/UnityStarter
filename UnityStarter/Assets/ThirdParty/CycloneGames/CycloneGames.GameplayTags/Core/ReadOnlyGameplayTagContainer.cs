using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// An immutable, thread-safe snapshot of a GameplayTagContainer's state.
   /// Safe to pass to worker threads, parallel jobs, or cache across frames.
   /// Zero allocation after creation — all data captured at construction time.
   ///
   /// Usage:
   ///   var snapshot = container.CreateSnapshot();
   ///   // Pass snapshot to any thread — it's fully immutable.
   ///   bool has = snapshot.HasTag(someTag);
   /// </summary>
   public sealed class ReadOnlyGameplayTagContainer
   {
      private readonly int[] _explicitIndices;
      private readonly int[] _implicitIndices;
      private readonly int[] _bitset; // null if tag count < threshold

      private const int BitsetThreshold = 64;

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
      public ReadOnlyGameplayTagContainer(IGameplayTagContainer source)
      {
         if (source == null || source.IsEmpty)
         {
            _explicitIndices = Array.Empty<int>();
            _implicitIndices = Array.Empty<int>();
            _bitset = null;
            return;
         }

         var indices = source.Indices;
         _explicitIndices = indices.Explicit != null ? indices.Explicit.ToArray() : Array.Empty<int>();
         _implicitIndices = indices.Implicit != null ? indices.Implicit.ToArray() : Array.Empty<int>();

         // Build bitset for O(1) lookups if large enough
         if (_implicitIndices.Length >= BitsetThreshold && _implicitIndices.Length > 0)
         {
            int maxIndex = _implicitIndices[_implicitIndices.Length - 1];
            int bitsetLength = (maxIndex >> 5) + 1;
            _bitset = new int[bitsetLength];
            for (int i = 0; i < _implicitIndices.Length; i++)
            {
               int idx = _implicitIndices[i];
               _bitset[idx >> 5] |= 1 << (idx & 31);
            }
         }
      }

      /// <summary>Check if a tag (or any of its parents) is present. O(1) with bitset, O(log n) otherwise.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public bool HasTag(in GameplayTag tag)
      {
         int runtimeIndex = tag.RuntimeIndex;
         if (runtimeIndex <= 0) return false;

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
         return BinarySearchUtility.Search(_explicitIndices, tag.RuntimeIndex) >= 0;
      }

      /// <summary>Check if all tags in another container are present.</summary>
      public bool HasAll<T>(in T other) where T : IGameplayTagContainer
      {
         if (other == null || other.IsEmpty) return true;

         foreach (GameplayTag tag in other.GetTags())
         {
            if (!HasTag(tag)) return false;
         }
         return true;
      }

      /// <summary>Check if any tag in another container is present.</summary>
      public bool HasAny<T>(in T other) where T : IGameplayTagContainer
      {
         if (other == null || other.IsEmpty) return false;

         foreach (GameplayTag tag in other.GetTags())
         {
            if (HasTag(tag)) return true;
         }
         return false;
      }

      /// <summary>Check if all tags from another snapshot are present.</summary>
      public bool HasAll(ReadOnlyGameplayTagContainer other)
      {
         if (other == null || other.IsEmpty) return true;

         for (int i = 0; i < other._implicitIndices.Length; i++)
         {
            int idx = other._implicitIndices[i];
            if (_bitset != null)
            {
               int word = idx >> 5;
               if (word >= _bitset.Length || (_bitset[word] & (1 << (idx & 31))) == 0)
                  return false;
            }
            else
            {
               if (BinarySearchUtility.Search(_implicitIndices, idx) < 0)
                  return false;
            }
         }
         return true;
      }

      /// <summary>Check if any tag from another snapshot is present.</summary>
      public bool HasAny(ReadOnlyGameplayTagContainer other)
      {
         if (other == null || other.IsEmpty) return false;

         for (int i = 0; i < other._implicitIndices.Length; i++)
         {
            int idx = other._implicitIndices[i];
            if (_bitset != null)
            {
               int word = idx >> 5;
               if (word < _bitset.Length && (_bitset[word] & (1 << (idx & 31))) != 0)
                  return true;
            }
            else
            {
               if (BinarySearchUtility.Search(_implicitIndices, idx) >= 0)
                  return true;
            }
         }
         return false;
      }

      /// <summary>Get a span of all implicit (full hierarchy) runtime indices. Safe for parallel reads.</summary>
      public ReadOnlySpan<int> GetImplicitIndices() => _implicitIndices;

      /// <summary>Get a span of all explicit runtime indices. Safe for parallel reads.</summary>
      public ReadOnlySpan<int> GetExplicitIndices() => _explicitIndices;

      /// <summary>
      /// Serialize this snapshot for network transmission.
      /// </summary>
      public byte[] Serialize()
      {
         byte[] buffer = new byte[4 + _explicitIndices.Length * 2];
         int offset = 0;
         buffer[offset++] = 1; // version
         buffer[offset++] = 0xFE; // full marker
         buffer[offset++] = (byte)(_explicitIndices.Length & 0xFF);
         buffer[offset++] = (byte)(_explicitIndices.Length >> 8);
         for (int i = 0; i < _explicitIndices.Length; i++)
         {
            ushort idx = (ushort)_explicitIndices[i];
            buffer[offset++] = (byte)(idx & 0xFF);
            buffer[offset++] = (byte)(idx >> 8);
         }
         return buffer;
      }
   }

   /// <summary>
   /// Extension methods for creating thread-safe snapshots.
   /// </summary>
   public static class GameplayTagContainerSnapshotExtensions
   {
      /// <summary>
      /// Create an immutable, thread-safe snapshot of the container's current state.
      /// </summary>
      public static ReadOnlyGameplayTagContainer CreateSnapshot(this IGameplayTagContainer container)
      {
         return new ReadOnlyGameplayTagContainer(container);
      }
   }
}
