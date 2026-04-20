#if CYCLONE_HAS_COLLECTIONS
using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using CycloneGames.GameplayTags.Runtime;
#if CYCLONE_HAS_BURST
using Unity.Burst;
#endif

namespace CycloneGames.GameplayTags.Unity
{
   /// <summary>
   /// A NativeContainer-based tag mask compatible with Burst/Jobs.
   /// Allocates a NativeBitArray sized to the current tag count.
   /// Must be created on the main thread after GameplayTagManager is initialized.
   ///
   /// Usage:
   ///   var mask = new NativeGameplayTagMask(Allocator.TempJob);
   ///   mask.Set(someTag);
   ///   // Pass mask to IJobParallelFor, etc.
   ///   mask.Dispose();
   /// </summary>
#if CYCLONE_HAS_BURST
   [BurstCompile]
#endif
   public struct NativeGameplayTagMask : IDisposable
   {
      private NativeBitArray _bits;

      /// <summary>
      /// Create a native mask sized for all currently registered tags.
      /// </summary>
      public NativeGameplayTagMask(Allocator allocator)
      {
         int tagCount = GameplayTagManager.GetAllTags().Length;
         int bitCount = Math.Max(tagCount + 1, 64); // +1 because RuntimeIndex is 1-based
         _bits = new NativeBitArray(bitCount, allocator, NativeArrayOptions.ClearMemory);
      }

      /// <summary>
      /// Create a native mask with a specific capacity.
      /// </summary>
      public NativeGameplayTagMask(int capacity, Allocator allocator)
      {
         _bits = new NativeBitArray(Math.Max(capacity, 64), allocator, NativeArrayOptions.ClearMemory);
      }

      /// <summary>True if the mask has been allocated and not yet disposed.</summary>
      public readonly bool IsCreated
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => _bits.IsCreated;
      }

      /// <summary>Set a tag bit. Uses RuntimeIndex directly.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void Set(int runtimeIndex)
      {
         if (runtimeIndex > 0 && runtimeIndex < _bits.Length)
            _bits.Set(runtimeIndex, true);
      }

      /// <summary>Set a tag bit by GameplayTag.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void Set(in GameplayTag tag) => Set(tag.RuntimeIndex);

      /// <summary>Clear a tag bit.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void Clear(int runtimeIndex)
      {
         if (runtimeIndex > 0 && runtimeIndex < _bits.Length)
            _bits.Set(runtimeIndex, false);
      }

      /// <summary>Clear a tag bit by GameplayTag.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void Clear(in GameplayTag tag) => Clear(tag.RuntimeIndex);

      /// <summary>Test if a tag bit is set. O(1).</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly bool IsSet(int runtimeIndex)
      {
         return runtimeIndex > 0 && runtimeIndex < _bits.Length && _bits.IsSet(runtimeIndex);
      }

      /// <summary>Test if a tag bit is set by GameplayTag.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly bool IsSet(in GameplayTag tag) => IsSet(tag.RuntimeIndex);

      /// <summary>Clear all bits.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void ClearAll()
      {
         _bits.Clear();
      }

      /// <summary>Bitwise AND: result has only bits set in both masks.</summary>
#if CYCLONE_HAS_BURST
      [BurstCompile]
#endif
      public static void And(in NativeGameplayTagMask a, in NativeGameplayTagMask b, Allocator allocator, out NativeGameplayTagMask result)
      {
         int len = Math.Min(a._bits.Length, b._bits.Length);
         result = new NativeGameplayTagMask(len, allocator);
         for (int i = 0; i < len; i++)
         {
            if (a._bits.IsSet(i) && b._bits.IsSet(i))
               result._bits.Set(i, true);
         }
      }

      /// <summary>Bitwise OR: result has bits set in either mask.</summary>
#if CYCLONE_HAS_BURST
      [BurstCompile]
#endif
      public static void Or(in NativeGameplayTagMask a, in NativeGameplayTagMask b, Allocator allocator, out NativeGameplayTagMask result)
      {
         int len = Math.Max(a._bits.Length, b._bits.Length);
         result = new NativeGameplayTagMask(len, allocator);
         for (int i = 0; i < len; i++)
         {
            bool aSet = i < a._bits.Length && a._bits.IsSet(i);
            bool bSet = i < b._bits.Length && b._bits.IsSet(i);
            if (aSet || bSet)
               result._bits.Set(i, true);
         }
      }

      /// <summary>Check if this mask contains all bits set in another mask.</summary>
      public readonly bool ContainsAll(in NativeGameplayTagMask other)
      {
         for (int i = 0; i < other._bits.Length; i++)
         {
            if (other._bits.IsSet(i) && (i >= _bits.Length || !_bits.IsSet(i)))
               return false;
         }
         return true;
      }

      /// <summary>Check if this mask has any bit in common with another mask.</summary>
      public readonly bool ContainsAny(in NativeGameplayTagMask other)
      {
         int len = Math.Min(_bits.Length, other._bits.Length);
         for (int i = 0; i < len; i++)
         {
            if (_bits.IsSet(i) && other._bits.IsSet(i))
               return true;
         }
         return false;
      }

      /// <summary>
      /// Copy from a managed GameplayTagMask (256-bit) into this native mask.
      /// Call on main thread before scheduling jobs.
      /// </summary>
      public unsafe void CopyFrom(in GameplayTagMask mask)
      {
         ClearAll();
         fixed (GameplayTagMask* maskPtr = &mask)
         {
            ulong* words = (ulong*)maskPtr;
            for (int w = 0; w < 4; w++)
            {
               ulong word = words[w];
               if (word == 0) continue;
               int baseIdx = w * 64;
               while (word != 0)
               {
                  int bit = BitScanForward(word);
                  int idx = baseIdx + bit;
                  if (idx < _bits.Length)
                     _bits.Set(idx, true);
                  word &= word - 1; // clear lowest set bit
               }
            }
         }
      }

      /// <summary>
      /// Populate this mask from a container's implicit tags.
      /// Call on main thread before scheduling jobs.
      /// </summary>
      public void CopyFrom(IGameplayTagContainer container)
      {
         ClearAll();
         if (container == null || container.IsEmpty) return;

         foreach (GameplayTag tag in container.GetTags())
         {
            Set(tag.RuntimeIndex);
         }
      }

      /// <summary>
      /// Populate this mask from a ReadOnlyGameplayTagContainer (thread-safe snapshot).
      /// </summary>
      public void CopyFrom(ReadOnlyGameplayTagContainer snapshot)
      {
         ClearAll();
         if (snapshot == null || snapshot.IsEmpty) return;

         ReadOnlySpan<int> indices = snapshot.GetImplicitIndices();
         for (int i = 0; i < indices.Length; i++)
         {
            Set(indices[i]);
         }
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static int BitScanForward(ulong value)
      {
         // De Bruijn bit scan
         const ulong debruijn = 0x03F79D71B4CB0A89UL;
         return DeBruijnTable[((value & (ulong)(-(long)value)) * debruijn) >> 58];
      }

      private static readonly int[] DeBruijnTable = new int[64]
      {
          0,  1,  2,  7,  3, 13,  8, 19,  4, 25, 14, 28,  9, 34, 20, 40,
          5, 17, 26, 38, 15, 46, 29, 48, 10, 31, 35, 54, 21, 50, 41, 57,
         63,  6, 12, 18, 24, 27, 33, 39, 16, 37, 45, 47, 30, 53, 49, 56,
         62, 11, 23, 32, 36, 44, 52, 55, 61, 22, 43, 51, 60, 42, 59, 58
      };

      public void Dispose()
      {
         if (_bits.IsCreated)
            _bits.Dispose();
      }
   }
}
#endif
