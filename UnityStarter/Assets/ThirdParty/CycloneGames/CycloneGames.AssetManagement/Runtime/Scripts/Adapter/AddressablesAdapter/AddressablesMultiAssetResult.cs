using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Validates an Addressables multi-asset result before it becomes caller-visible.
    /// A list that maps several entries onto one object is a failed load, not a shorter success.
    /// </summary>
    internal static class AddressablesMultiAssetResult
    {
        /// <summary>
        /// Identity comparer. UnityEngine.Object equality treats destroyed instances as equal to each other
        /// and to null, which would blur the difference between a repeated entry and a missing one.
        /// </summary>
        private sealed class ReferenceIdentityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        private static readonly ReferenceIdentityComparer Comparer = new ReferenceIdentityComparer();

        public static HashSet<object> CreateScratch()
        {
            return new HashSet<object>(Comparer);
        }

        /// <summary>
        /// Returns the failure for <paramref name="items"/> when two entries reference the same object;
        /// null counts as one object. Returns null when the list holds fewer than two entries or every entry is
        /// distinct. <paramref name="scratch"/> is cleared and reused. Main thread only.
        /// </summary>
        public static Exception Inspect<TAsset>(IList<TAsset> items, string location, HashSet<object> scratch)
        {
            int count = items != null ? items.Count : 0;
            if (count < 2)
            {
                return null;
            }

            // A list whose entries all alias one object is the common sub-asset shape. This pass reads one
            // reference per entry, stops at the first different one and never touches the scratch set.
            TAsset first = items[0];
            int index = 1;
            while (index < count && ReferenceEquals(items[index], first))
            {
                index++;
            }

            if (index == count)
            {
                return CreateFailure(location, count, 1);
            }

            // A mix of distinct and repeated entries cannot be mapped back to the requested entries either.
            // The scan continues to the end so the failure reports the true number of distinct objects.
            scratch.Clear();
            scratch.Add(first);
            int distinct = 1;
            // The leading run already holds `index` copies of the first entry.
            bool repeated = index > 1;
            for (; index < count; index++)
            {
                if (scratch.Add(items[index]))
                {
                    distinct++;
                }
                else
                {
                    repeated = true;
                }
            }

            return repeated ? CreateFailure(location, count, distinct) : null;
        }

        private static Exception CreateFailure(string location, int count, int distinct)
        {
            return new InvalidOperationException(
                $"Addressables resolved {count} entries of '{location}' to {distinct} distinct object(s). " +
                "The result cannot be mapped to the requested entries; " +
                "load sub-assets individually with LoadAssetAsync<T>(location[subAssetName]).");
        }
    }
}
