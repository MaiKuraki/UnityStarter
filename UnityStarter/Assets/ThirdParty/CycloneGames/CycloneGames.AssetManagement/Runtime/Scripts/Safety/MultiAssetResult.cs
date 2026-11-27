using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Enforces the <see cref="IMultiAssetLoader"/> result contract for every provider adapter.
    /// A list that maps several entries onto one object is a failed load, not a shorter success.
    /// </summary>
    internal static class MultiAssetResult
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
        /// Returns the failure when <paramref name="items"/> repeats an object reference or holds a null
        /// entry. Provider adapters run a Unity-lifetime scan first so destroyed instances are rejected too.
        /// Returns null for an empty list or a list of distinct objects.
        /// <paramref name="scratch"/> is cleared and reused. Main thread only.
        /// </summary>
        public static Exception Inspect<T>(IReadOnlyList<T> items, string provider, string location, HashSet<object> scratch)
            where T : class
        {
            int count = items != null ? items.Count : 0;
            if (count == 0)
            {
                return null;
            }

            T first = items[0];
            if (ReferenceEquals(first, null))
            {
                return CreateDanglingFailure(provider, location, 0);
            }

            if (count == 1)
            {
                return null;
            }

            // A list whose entries all alias one object is the common sub-asset shape. This pass reads one
            // reference per entry, stops at the first different one and never touches the scratch set.
            int index = 1;
            while (index < count && ReferenceEquals(items[index], first))
            {
                index++;
            }

            if (index == count)
            {
                return CreateFailure(provider, location, count, 1);
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
                T item = items[index];
                if (ReferenceEquals(item, null))
                {
                    return CreateDanglingFailure(provider, location, index);
                }

                if (scratch.Add(item))
                {
                    distinct++;
                }
                else
                {
                    repeated = true;
                }
            }

            return repeated ? CreateFailure(provider, location, count, distinct) : null;
        }

        public static Exception CreateTypeError(string provider, string location, int index, Type expected)
        {
            return new InvalidOperationException(
                $"{provider} returned an entry at index {index} of '{location}' that is not a {expected.Name}.");
        }

        /// <summary>Reports an entry that is null or a destroyed UnityEngine.Object.</summary>
        public static Exception CreateDanglingFailure(string provider, string location, int index)
        {
            return new InvalidOperationException(
                $"{provider} returned a null or destroyed entry at index {index} of '{location}'.");
        }

        private static Exception CreateFailure(string provider, string location, int count, int distinct)
        {
            return new InvalidOperationException(
                $"{provider} resolved {count} entries of '{location}' to {distinct} distinct object(s). " +
                "The result cannot be mapped to the requested entries; " +
                "load the entries individually with LoadAssetAsync<T>.");
        }
    }
}
