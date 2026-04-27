using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// A fixed-size 256-bit bitmask for O(1) tag operations.
   /// Designed for ECS/DOD: blittable, no heap allocation, 32 bytes inline.
   /// Supports up to 256 registered tags by RuntimeIndex.
   /// For projects with more tags, use <see cref="GameplayTagMaskLarge"/> or <see cref="GameplayTagContainer"/>.
   /// </summary>
   [StructLayout(LayoutKind.Sequential)]
   public struct GameplayTagMask : IEquatable<GameplayTagMask>
   {
      public const int MaxTags = 256;
      private const int WordCount = 4;

      private ulong _w0, _w1, _w2, _w3;

      public static readonly GameplayTagMask Empty = default;

      /// <summary>True if no tags are set.</summary>
      public readonly bool IsEmpty
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => (_w0 | _w1 | _w2 | _w3) == 0;
      }

      /// <summary>Number of tags currently set.</summary>
      public readonly int Count
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => PopCount(_w0) + PopCount(_w1) + PopCount(_w2) + PopCount(_w3);
      }

      /// <summary>Add a tag to the mask. O(1).</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void AddTag(in GameplayTag tag)
      {
         int index = tag.RuntimeIndex;
         if ((uint)index >= MaxTags) return;
         SetBit(index);
      }

      /// <summary>
      /// Add a tag and automatically expand to include all hierarchy (parent) tags.
      /// O(h) where h is hierarchy depth.
      /// </summary>
      public void AddTagWithHierarchy(in GameplayTag tag)
      {
         var snap = GameplayTagManager.Snapshot;
         if (snap == null) return;
         ReadOnlySpan<int> hierarchy = snap.GetHierarchyRuntimeIndicesSpan(tag.RuntimeIndex);
         for (int i = 0; i < hierarchy.Length; i++)
         {
            int idx = hierarchy[i];
            if ((uint)idx < MaxTags)
            {
               SetBit(idx);
            }
         }
      }

      /// <summary>Remove a tag from the mask. O(1).</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void RemoveTag(in GameplayTag tag)
      {
         int index = tag.RuntimeIndex;
         if ((uint)index >= MaxTags) return;
         ClearBit(index);
      }

      /// <summary>Check if a tag is present. O(1).</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly bool HasTag(in GameplayTag tag)
      {
         int index = tag.RuntimeIndex;
         if ((uint)index >= MaxTags) return false;
         return (GetWord(index >> 6) & (1UL << (index & 63))) != 0;
      }

      /// <summary>
      /// Check if a tag is present, including hierarchy (parent match).
      /// A tag "A.B" matches if "A.B" or any child like "A.B.C" is set.
      /// O(1) per tag check.
      /// </summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly bool HasTagHierarchical(in GameplayTag tag)
      {
         // Direct check first
         if (HasTag(tag)) return true;

         // Check if any child of this tag is present
         var snap = GameplayTagManager.Snapshot;
         if (snap == null) return false;
         ReadOnlySpan<int> children = snap.GetChildRuntimeIndicesSpan(tag.RuntimeIndex);
         for (int i = 0; i < children.Length; i++)
         {
            int idx = children[i];
            if ((uint)idx < MaxTags && (GetWord(idx >> 6) & (1UL << (idx & 63))) != 0)
               return true;
         }

         return false;
      }

      /// <summary>Check if all tags from another mask are present. O(1) — 4 AND operations.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly bool HasAll(in GameplayTagMask other)
      {
         return (other._w0 & ~_w0) == 0 &&
                (other._w1 & ~_w1) == 0 &&
                (other._w2 & ~_w2) == 0 &&
                (other._w3 & ~_w3) == 0;
      }

      /// <summary>Check if any tag from another mask is present. O(1) — 4 AND operations.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly bool HasAny(in GameplayTagMask other)
      {
         return ((_w0 & other._w0) | (_w1 & other._w1) | (_w2 & other._w2) | (_w3 & other._w3)) != 0;
      }

      /// <summary>Check if no tags from another mask are present. O(1).</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly bool HasNone(in GameplayTagMask other)
      {
         return !HasAny(other);
      }

      /// <summary>Bitwise OR — union of two masks. O(1).</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static GameplayTagMask Union(in GameplayTagMask a, in GameplayTagMask b)
      {
         GameplayTagMask result;
         result._w0 = a._w0 | b._w0;
         result._w1 = a._w1 | b._w1;
         result._w2 = a._w2 | b._w2;
         result._w3 = a._w3 | b._w3;
         return result;
      }

      /// <summary>Bitwise AND — intersection of two masks. O(1).</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static GameplayTagMask Intersection(in GameplayTagMask a, in GameplayTagMask b)
      {
         GameplayTagMask result;
         result._w0 = a._w0 & b._w0;
         result._w1 = a._w1 & b._w1;
         result._w2 = a._w2 & b._w2;
         result._w3 = a._w3 & b._w3;
         return result;
      }

      /// <summary>Bitwise AND-NOT — tags in a that are not in b. O(1).</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static GameplayTagMask Difference(in GameplayTagMask a, in GameplayTagMask b)
      {
         GameplayTagMask result;
         result._w0 = a._w0 & ~b._w0;
         result._w1 = a._w1 & ~b._w1;
         result._w2 = a._w2 & ~b._w2;
         result._w3 = a._w3 & ~b._w3;
         return result;
      }

      /// <summary>Clear all tags.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void Clear()
      {
         _w0 = _w1 = _w2 = _w3 = 0;
      }

      /// <summary>Create a mask from a single tag.</summary>
      public static GameplayTagMask FromTag(in GameplayTag tag)
      {
         GameplayTagMask mask = default;
         mask.AddTag(tag);
         return mask;
      }

      /// <summary>Create a mask from a tag with full hierarchy expansion.</summary>
      public static GameplayTagMask FromTagWithHierarchy(in GameplayTag tag)
      {
         GameplayTagMask mask = default;
         mask.AddTagWithHierarchy(tag);
         return mask;
      }

      /// <summary>Create a mask from a container.</summary>
      public static GameplayTagMask FromContainer<T>(in T container) where T : IGameplayTagContainer
      {
         GameplayTagMask mask = default;
         if (container == null || container.IsEmpty) return mask;

         foreach (GameplayTag tag in container.GetTags())
         {
            mask.AddTag(tag);
         }

         return mask;
      }

      /// <summary>Iterate all set tag RuntimeIndices.</summary>
      public readonly Enumerator GetEnumerator() => new(this);

      public struct Enumerator
      {
         private readonly GameplayTagMask _mask;
         private int _wordIndex;
         private ulong _currentWord;
         private int _bitOffset;

         internal Enumerator(in GameplayTagMask mask)
         {
            _mask = mask;
            _wordIndex = 0;
            _currentWord = mask._w0;
            _bitOffset = 0;
         }

         public GameplayTag Current
         {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GameplayTagManager.GetTagFromRuntimeIndex(_bitOffset);
         }

         public bool MoveNext()
         {
            while (_wordIndex < WordCount)
            {
               if (_currentWord != 0)
               {
                  int bit = BitScanForward(_currentWord);
                  _bitOffset = (_wordIndex << 6) + bit;
                  _currentWord &= _currentWord - 1; // clear lowest set bit
                  return true;
               }

               _wordIndex++;
               if (_wordIndex < WordCount)
                  _currentWord = _mask.GetWord(_wordIndex);
            }

            return false;
         }
      }

      // --- IEquatable ---

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly bool Equals(GameplayTagMask other)
      {
         return _w0 == other._w0 && _w1 == other._w1 && _w2 == other._w2 && _w3 == other._w3;
      }

      public override readonly bool Equals(object obj) => obj is GameplayTagMask other && Equals(other);

      public override readonly int GetHashCode()
      {
         return HashCode.Combine(_w0, _w1, _w2, _w3);
      }

      public static bool operator ==(in GameplayTagMask lhs, in GameplayTagMask rhs) => lhs.Equals(rhs);
      public static bool operator !=(in GameplayTagMask lhs, in GameplayTagMask rhs) => !lhs.Equals(rhs);

      public override readonly string ToString()
      {
         int count = Count;
         if (count == 0) return "<Empty>";
         return $"GameplayTagMask({count} tags)";
      }

      // --- Bit-level helpers (unsafe pointer arithmetic, zero branch overhead) ---

      /// <summary>Set a bit at the given index. No bounds check.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public unsafe void SetBit(int index)
      {
         fixed (ulong* ptr = &_w0)
         {
            ptr[index >> 6] |= 1UL << (index & 63);
         }
      }

      /// <summary>Clear a bit at the given index. No bounds check.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public unsafe void ClearBit(int index)
      {
         fixed (ulong* ptr = &_w0)
         {
            ptr[index >> 6] &= ~(1UL << (index & 63));
         }
      }

      /// <summary>Check if a bit at the given index is set. O(1).</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly bool IsSet(int index)
      {
         if ((uint)index >= MaxTags) return false;
         return (GetWord(index >> 6) & (1UL << (index & 63))) != 0;
      }

      /// <summary>Get the raw ulong word at the given word index (0-3). No bounds check.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public unsafe readonly ulong GetWord(int wordIndex)
      {
         fixed (ulong* ptr = &_w0)
         {
            return ptr[wordIndex];
         }
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static int PopCount(ulong value)
      {
#if NETCOREAPP3_0_OR_GREATER
         return System.Numerics.BitOperations.PopCount(value);
#else
         // Hamming weight
         value -= (value >> 1) & 0x5555555555555555UL;
         value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
         value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
         return (int)((value * 0x0101010101010101UL) >> 56);
#endif
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static int BitScanForward(ulong value)
      {
#if NETCOREAPP3_0_OR_GREATER
         return System.Numerics.BitOperations.TrailingZeroCount(value);
#else
         if (value == 0) return 64;
         int n = 0;
         if ((value & 0xFFFFFFFF) == 0) { n += 32; value >>= 32; }
         if ((value & 0xFFFF) == 0) { n += 16; value >>= 16; }
         if ((value & 0xFF) == 0) { n += 8; value >>= 8; }
         if ((value & 0xF) == 0) { n += 4; value >>= 4; }
         if ((value & 0x3) == 0) { n += 2; value >>= 2; }
         if ((value & 0x1) == 0) { n += 1; }
         return n;
#endif
      }
   }

   /// <summary>
   /// A dynamically-sized bitmask for projects with more than 256 tag types.
   /// Heap-allocated but still O(1) per-tag operations and O(N/64) for HasAll/HasAny.
   /// Use <see cref="GameplayTagMask"/> (256-bit blittable) for ECS/DOD hot paths where tag count fits.
   /// </summary>
   public sealed class GameplayTagMaskLarge : IEquatable<GameplayTagMaskLarge>
   {
      private ulong[] _words;
      private int _capacity; // max tag index + 1

      /// <summary>Current capacity in tag indices.</summary>
      public int Capacity => _capacity;

      /// <summary>True if no tags are set.</summary>
      public bool IsEmpty
      {
         get
         {
            for (int i = 0; i < _words.Length; i++)
               if (_words[i] != 0) return false;
            return true;
         }
      }

      /// <summary>Number of tags set.</summary>
      public int Count
      {
         get
         {
            int count = 0;
            for (int i = 0; i < _words.Length; i++)
               count += PopCount(_words[i]);
            return count;
         }
      }

      public GameplayTagMaskLarge(int capacity = 1024)
      {
         _capacity = capacity > 0 ? capacity : 1024;
         _words = new ulong[(_capacity + 63) >> 6];
      }

      /// <summary>Add a tag. O(1), auto-grows if needed.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void AddTag(in GameplayTag tag)
      {
         int index = tag.RuntimeIndex;
         if (index < 0) return;
         EnsureCapacity(index);
         _words[index >> 6] |= 1UL << (index & 63);
      }

      /// <summary>Add a tag with full hierarchy expansion.</summary>
      public void AddTagWithHierarchy(in GameplayTag tag)
      {
         var snap = GameplayTagManager.Snapshot;
         if (snap == null) return;
         ReadOnlySpan<int> hierarchy = snap.GetHierarchyRuntimeIndicesSpan(tag.RuntimeIndex);
         for (int i = 0; i < hierarchy.Length; i++)
         {
            int idx = hierarchy[i];
            if (idx >= 0)
            {
               EnsureCapacity(idx);
               _words[idx >> 6] |= 1UL << (idx & 63);
            }
         }
      }

      /// <summary>Remove a tag. O(1).</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void RemoveTag(in GameplayTag tag)
      {
         int index = tag.RuntimeIndex;
         if (index < 0 || index >= _capacity) return;
         _words[index >> 6] &= ~(1UL << (index & 63));
      }

      /// <summary>Check if a tag is present. O(1).</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public bool HasTag(in GameplayTag tag)
      {
         int index = tag.RuntimeIndex;
         if ((uint)index >= (uint)_capacity) return false;
         return (_words[index >> 6] & (1UL << (index & 63))) != 0;
      }

      /// <summary>Check if all tags from another mask are present. O(N/64).</summary>
      public bool HasAll(GameplayTagMaskLarge other)
      {
         if (other == null) return true;
         int minLen = Math.Min(_words.Length, other._words.Length);
         for (int i = 0; i < minLen; i++)
         {
            if ((other._words[i] & ~_words[i]) != 0) return false;
         }
         for (int i = minLen; i < other._words.Length; i++)
         {
            if (other._words[i] != 0) return false;
         }
         return true;
      }

      /// <summary>Check if any tag from another mask is present. O(N/64).</summary>
      public bool HasAny(GameplayTagMaskLarge other)
      {
         if (other == null) return false;
         int minLen = Math.Min(_words.Length, other._words.Length);
         for (int i = 0; i < minLen; i++)
         {
            if ((_words[i] & other._words[i]) != 0) return true;
         }
         return false;
      }

      /// <summary>Clear all tags.</summary>
      public void Clear()
      {
         Array.Clear(_words, 0, _words.Length);
      }

      /// <summary>Create from a container.</summary>
      public static GameplayTagMaskLarge FromContainer<T>(in T container) where T : IGameplayTagContainer
      {
         var mask = new GameplayTagMaskLarge();
         if (container == null || container.IsEmpty) return mask;
         foreach (GameplayTag tag in container.GetTags())
            mask.AddTag(tag);
         return mask;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private void EnsureCapacity(int index)
      {
         if (index < _capacity) return;
         int newCap = Math.Max(index + 1, _capacity * 2);
         int newWordCount = (newCap + 63) >> 6;
         Array.Resize(ref _words, newWordCount);
         _capacity = newCap;
      }

      public bool Equals(GameplayTagMaskLarge other)
      {
         if (other == null) return false;
         int maxLen = Math.Max(_words.Length, other._words.Length);
         for (int i = 0; i < maxLen; i++)
         {
            ulong a = i < _words.Length ? _words[i] : 0;
            ulong b = i < other._words.Length ? other._words[i] : 0;
            if (a != b) return false;
         }
         return true;
      }

      public override bool Equals(object obj) => obj is GameplayTagMaskLarge other && Equals(other);

      public override int GetHashCode()
      {
         unchecked
         {
            int hash = 17;
            for (int i = 0; i < _words.Length; i++)
               hash = hash * 31 + _words[i].GetHashCode();
            return hash;
         }
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static int PopCount(ulong value)
      {
#if NETCOREAPP3_0_OR_GREATER
         return System.Numerics.BitOperations.PopCount(value);
#else
         value -= (value >> 1) & 0x5555555555555555UL;
         value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
         value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
         return (int)((value * 0x0101010101010101UL) >> 56);
#endif
      }
   }
}
