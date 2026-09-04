using System.Runtime.CompilerServices;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// Branch-light binary search over sorted <see cref="int"/> arrays.
   /// </summary>
   /// <remarks>
   /// <para>
   /// Every container in this module stores sorted runtime indices in a bare <see cref="int"/>[], so all
   /// searches operate directly on arrays. There is deliberately no <see cref="System.Collections.Generic.List{T}"/>
   /// overload: a list indexer is a virtual-free but still indirect call through a size check and a
   /// backing-array load, and it hides the array from the bounds-check elimination the JIT performs on
   /// straight array loops.
   /// </para>
   /// <para>
   /// The return convention matches <see cref="System.Array.BinarySearch{T}(T[], T)"/>: a non-negative
   /// value is the match position, and a negative value is the bitwise complement of the index where
   /// <paramref name="value"/> would be inserted. Callers that only need existence should use
   /// <see cref="Contains"/>, which skips the complement step.
   /// </para>
   /// </remarks>
   internal static class BinarySearchUtility
   {
      /// <summary>Searches <c>array[start .. start+length)</c>, which must be sorted ascending.</summary>
      /// <returns>The match index, or the bitwise complement of the insertion index.</returns>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static int Search(int[] array, int start, int length, int value)
      {
         int lo = start;
         int hi = start + length - 1;

         while (lo <= hi)
         {
            int mid = lo + ((hi - lo) >> 1);
            int midValue = array[mid];
            if (value == midValue)
               return mid;
            if (value > midValue)
               lo = mid + 1;
            else
               hi = mid - 1;
         }

         return ~lo;
      }

      /// <summary>Searches <c>array[0 .. length)</c>, which must be sorted ascending.</summary>
      /// <returns>The match index, or the bitwise complement of the insertion index.</returns>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static int Search(int[] array, int length, int value)
         => Search(array, 0, length, value);

      /// <summary>True when <c>array[start .. start+length)</c> contains <paramref name="value"/>.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static bool Contains(int[] array, int start, int length, int value)
      {
         if (length <= 0)
            return false;

         int lo = start;
         int hi = start + length - 1;

         while (lo <= hi)
         {
            int mid = lo + ((hi - lo) >> 1);
            int midValue = array[mid];
            if (value == midValue)
               return true;
            if (value > midValue)
               lo = mid + 1;
            else
               hi = mid - 1;
         }

         return false;
      }

      /// <summary>True when <c>array[0 .. length)</c> contains <paramref name="value"/>.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static bool Contains(int[] array, int length, int value)
         => Contains(array, 0, length, value);

      /// <summary>
      /// The index of the first element greater than or equal to <paramref name="value"/>, or
      /// <paramref name="length"/> when every element is smaller.
      /// </summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static int LowerBound(int[] array, int start, int length, int value)
      {
         int lo = start;
         int hi = start + length;

         while (lo < hi)
         {
            int mid = lo + ((hi - lo) >> 1);
            if (array[mid] < value)
               lo = mid + 1;
            else
               hi = mid;
         }

         return lo - start;
      }
   }
}
