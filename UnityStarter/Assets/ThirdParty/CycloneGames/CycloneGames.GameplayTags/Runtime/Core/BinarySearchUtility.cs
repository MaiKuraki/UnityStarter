using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.GameplayTags.Core
{
   internal static class BinarySearchUtility
   {
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static int Search(List<int> arr, int value)
      {
         return Search(arr, value, 0, arr.Count - 1);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static int Search(List<int> arr, int value, int start, int end)
      {
         int lo = start;
         int hi = end;

         while (lo <= hi)
         {
            int mid = lo + ((hi - lo) >> 1);
            int midVal = arr[mid];
            if (value == midVal)
               return mid;
            if (value > midVal)
               lo = mid + 1;
            else
               hi = mid - 1;
         }

         return ~lo;
      }

      /// <summary>
      /// Binary search on a ReadOnlySpan. Used by TagDataSnapshot for lock-free hierarchy queries.
      /// </summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static int SearchSpan(ReadOnlySpan<int> span, int value)
      {
         int lo = 0;
         int hi = span.Length - 1;

         while (lo <= hi)
         {
            int mid = lo + ((hi - lo) >> 1);
            int midVal = span[mid];
            if (value == midVal)
               return mid;
            if (value > midVal)
               lo = mid + 1;
            else
               hi = mid - 1;
         }

         return ~lo;
      }

      /// <summary>
      /// Binary search on a sorted int array. Used by ReadOnlyGameplayTagContainer.
      /// </summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static int Search(int[] arr, int value)
      {
         int lo = 0;
         int hi = arr.Length - 1;

         while (lo <= hi)
         {
            int mid = lo + ((hi - lo) >> 1);
            int midVal = arr[mid];
            if (value == midVal)
               return mid;
            if (value > midVal)
               lo = mid + 1;
            else
               hi = mid - 1;
         }

         return ~lo;
      }
   }
}