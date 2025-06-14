using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.GameplayTags.Runtime
{
   internal class BinarySearchUtility
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
            if (value == arr[mid])
            {
               return mid;
            }

            if (value > arr[mid])
            {
               lo = mid + 1;
            }
            else
            {
               hi = mid - 1;
            }
         }

         return ~lo;
      }
   }
}