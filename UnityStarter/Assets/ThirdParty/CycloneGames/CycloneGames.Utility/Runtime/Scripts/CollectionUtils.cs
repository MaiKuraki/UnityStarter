using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Provides focused helpers for common collection operations.
    /// Mutable collections remain owned by the caller and are not made thread-safe by these extensions.
    /// </summary>
    public static class CollectionUtils
    {
        // The default shuffle path lazily creates one RNG per calling thread. Pass an explicit
        // seeded Random when deterministic output or explicit lifetime control is required.
        [ThreadStatic] private static Random _sharedRng;

        private static Random SharedRng => _sharedRng ?? (_sharedRng = new Random());

        // --- List<T> ---

        /// <summary>
        /// Attempts to retrieve an element at a specific index from a List<T>.
        /// This is O(1). The caller must prevent concurrent mutation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetElementAtIndex<T>(this List<T> list, int index, out T element)
        {
            if (list != null && (uint)index < (uint)list.Count) // Use unsigned trick for a single bounds check
            {
                element = list[index];
                return true;
            }
            element = default;
            return false;
        }

        /// <summary>
        /// Clears a List<T> and grows its capacity when necessary.
        /// Growing capacity allocates a new backing array; capacity is never reduced.
        /// </summary>
        public static void ClearAndResize<T>(this List<T> list, int capacity)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            list.Clear();
            if (list.Capacity < capacity)
            {
                list.Capacity = capacity;
            }
        }

        /// <summary>
        /// Attempts to remove and return the last element of the List<T>.
        /// This is an O(1) operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryPop<T>(this List<T> list, out T result)
        {
            if (list != null && list.Count > 0)
            {
                int lastIndex = list.Count - 1;
                result = list[lastIndex];
                list.RemoveAt(lastIndex);
                return true;
            }
            result = default;
            return false;
        }

        /// <summary>
        /// Removes and returns the last element of the List<T>.
        /// Throws InvalidOperationException if the list is empty.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Pop<T>(this List<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (list.Count == 0) throw new InvalidOperationException("List is empty.");
            int lastIndex = list.Count - 1;
            T item = list[lastIndex];
            list.RemoveAt(lastIndex);
            return item;
        }

        /// <summary>
        /// Removes an element at the given index by swapping it with the last element, then removing the last.
        /// This is an O(1) operation but does NOT preserve order.
        /// Ideal for game loops where order doesn't matter (e.g., entity lists, projectile pools).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SwapRemoveAt<T>(this List<T> list, int index)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if ((uint)index >= (uint)list.Count) throw new ArgumentOutOfRangeException(nameof(index));

            int lastIndex = list.Count - 1;
            if (index < lastIndex)
            {
                list[index] = list[lastIndex];
            }
            list.RemoveAt(lastIndex);
        }

        /// <summary>
        /// Attempts an unordered O(1) removal. Returns false for a null list or invalid index.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TrySwapRemoveAt<T>(this List<T> list, int index)
        {
            if (list == null || (uint)index >= (uint)list.Count)
            {
                return false;
            }

            int lastIndex = list.Count - 1;
            if (index < lastIndex)
            {
                list[index] = list[lastIndex];
            }
            list.RemoveAt(lastIndex);
            return true;
        }

        /// <summary>
        /// Attempts to get the first element of a List<T>.
        /// O(1).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryFirst<T>(this List<T> list, out T result)
        {
            if (list != null && list.Count > 0)
            {
                result = list[0];
                return true;
            }
            result = default;
            return false;
        }

        /// <summary>
        /// Attempts to get the last element of a List<T>.
        /// O(1).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryLast<T>(this List<T> list, out T result)
        {
            if (list != null && list.Count > 0)
            {
                result = list[list.Count - 1];
                return true;
            }
            result = default;
            return false;
        }

        /// <summary>
        /// In-place Fisher-Yates shuffle. O(N).
        /// The default RNG is lazily allocated per thread. Pass a seeded Random instance for deterministic results.
        /// </summary>
        public static void Shuffle<T>(this List<T> list, Random rng = null)
        {
            if (list == null || list.Count <= 1) return;
            rng = rng ?? SharedRng;
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }

        // --- IsNullOrEmpty ---

        /// <summary>Checks if a List is null or empty.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrEmpty<T>(this List<T> list) => list == null || list.Count == 0;

        /// <summary>Checks if an array is null or empty.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrEmpty<T>(this T[] array) => array == null || array.Length == 0;

        /// <summary>Checks if a Dictionary is null or empty.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrEmpty<TKey, TValue>(this Dictionary<TKey, TValue> dict) => dict == null || dict.Count == 0;

        /// <summary>Checks if a HashSet is null or empty.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrEmpty<T>(this HashSet<T> set) => set == null || set.Count == 0;

        // --- T[] (Array) ---

        /// <summary>
        /// Attempts to retrieve an element at a specific index from a T[] array.
        /// This method is highly optimized and avoids exceptions for out-of-range indices.
        /// It is O(1).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetElementAtIndex<T>(this T[] array, int index, out T element)
        {
            if (array != null && (uint)index < (uint)array.Length)
            {
                element = array[index];
                return true;
            }
            element = default;
            return false;
        }

        /// <summary>
        /// Removes an element at the given index by swapping it with the last element, then setting the last to default.
        /// Returns the new logical length (count - 1). O(1), does NOT preserve order and does NOT resize the array.
        /// The caller must track the logical length separately.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SwapRemoveAt<T>(this T[] array, int index, int count)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if ((uint)count > (uint)array.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index));

            int lastIndex = count - 1;
            if (index < lastIndex)
            {
                array[index] = array[lastIndex];
            }
            array[lastIndex] = default;
            return lastIndex;
        }

        /// <summary>
        /// Attempts an unordered O(1) removal within the array's logical <paramref name="count"/>.
        /// Returns false when the array, count, or index is invalid.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TrySwapRemoveAt<T>(this T[] array, int index, int count, out int newCount)
        {
            if (array == null || (uint)count > (uint)array.Length || (uint)index >= (uint)count)
            {
                newCount = count;
                return false;
            }

            int lastIndex = count - 1;
            if (index < lastIndex)
            {
                array[index] = array[lastIndex];
            }
            array[lastIndex] = default;
            newCount = lastIndex;
            return true;
        }

        /// <summary>
        /// In-place Fisher-Yates shuffle for arrays. O(N).
        /// The default RNG is lazily allocated per thread. Pass a seeded Random instance for deterministic results.
        /// </summary>
        public static void Shuffle<T>(this T[] array, Random rng = null)
        {
            if (array == null || array.Length <= 1) return;
            rng = rng ?? SharedRng;
            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T temp = array[i];
                array[i] = array[j];
                array[j] = temp;
            }
        }

        // --- Stack<T> ---
        // NOTE: Stack<T>.TryPeek / TryPop are built-in since .NET Standard 2.1 (Unity 2021+).
        // Extension methods with the same signature would be dead code (instance methods always win).
        // If you need to support Unity 2020 or earlier, uncomment the block below.

#if !NET_STANDARD_2_1 && !NETSTANDARD2_1 && !NET5_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryPeek<T>(this Stack<T> stack, out T result)
        {
            if (stack != null && stack.Count > 0)
            {
                result = stack.Peek();
                return true;
            }
            result = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryPop<T>(this Stack<T> stack, out T result)
        {
            if (stack != null && stack.Count > 0)
            {
                result = stack.Pop();
                return true;
            }
            result = default;
            return false;
        }
#endif

        // --- Queue<T> ---
        // NOTE: Queue<T>.TryPeek / TryDequeue are built-in since .NET Standard 2.1 (Unity 2021+).

#if !NET_STANDARD_2_1 && !NETSTANDARD2_1 && !NET5_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryPeek<T>(this Queue<T> queue, out T result)
        {
            if (queue != null && queue.Count > 0)
            {
                result = queue.Peek();
                return true;
            }
            result = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryDequeue<T>(this Queue<T> queue, out T result)
        {
            if (queue != null && queue.Count > 0)
            {
                result = queue.Dequeue();
                return true;
            }
            result = default;
            return false;
        }
#endif

        // --- LinkedList<T> ---

        /// <summary>
        /// Attempts to get the first node of the LinkedList<T>.
        /// This is an O(1) operation and avoids exceptions on an empty list.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetFirst<T>(this LinkedList<T> list, out LinkedListNode<T> node)
        {
            if (list != null)
            {
                node = list.First;
                return node != null;
            }
            node = null;
            return false;
        }

        /// <summary>
        /// Attempts to get the last node of the LinkedList<T>.
        /// This is an O(1) operation and avoids exceptions on an empty list.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetLast<T>(this LinkedList<T> list, out LinkedListNode<T> node)
        {
            if (list != null)
            {
                node = list.Last;
                return node != null;
            }
            node = null;
            return false;
        }

        /// <summary>
        /// Attempts to get the node at a specific index in a LinkedList<T>.
        /// WARNING: This is an O(N) operation and should be used with caution.
        /// Optimized to traverse from the closer end (head or tail).
        /// </summary>
        public static bool TryGetNodeAtIndex<T>(this LinkedList<T> list, int index, out LinkedListNode<T> node)
        {
            if (list != null && (uint)index < (uint)list.Count)
            {
                int count = list.Count;
                if (index <= count / 2)
                {
                    // Traverse from head
                    var currentNode = list.First;
                    for (int i = 0; i < index; i++)
                    {
                        currentNode = currentNode.Next;
                    }
                    node = currentNode;
                }
                else
                {
                    // Traverse from tail (closer)
                    var currentNode = list.Last;
                    for (int i = count - 1; i > index; i--)
                    {
                        currentNode = currentNode.Previous;
                    }
                    node = currentNode;
                }
                return true;
            }
            node = null;
            return false;
        }

        // --- Dictionary<TKey, TValue> ---

        /// <summary>
        /// Returns the value for the given key, or default(TValue) if the key does not exist.
        /// More concise than TryGetValue for the common "get or fallback" pattern.
        /// O(1).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TValue GetOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue fallback = default)
        {
            if (dictionary != null && dictionary.TryGetValue(key, out var value))
            {
                return value;
            }
            return fallback;
        }

        // --- SortedList<TKey, TValue> ---

        /// <summary>
        /// Attempts to retrieve a value at a specific index from a SortedList<TKey, TValue>.
        /// This is a highly efficient operation.
        /// It is O(1) because SortedList is backed by arrays, not O(log N).
        /// Not thread-safe for writes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetValueAtIndex<TKey, TValue>(this SortedList<TKey, TValue> sortedList, int index, out TValue value)
        {
            if (sortedList != null && (uint)index < (uint)sortedList.Count)
            {
                value = sortedList.Values[index];
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Attempts to retrieve a KeyValuePair at a specific index from a SortedList<TKey, TValue>.
        /// It is O(1) because SortedList is backed by arrays, not O(log N).
        /// Not thread-safe for writes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetElementAtIndex<TKey, TValue>(this SortedList<TKey, TValue> sortedList, int index, out KeyValuePair<TKey, TValue> element)
        {
            if (sortedList != null && (uint)index < (uint)sortedList.Count)
            {
                element = new KeyValuePair<TKey, TValue>(sortedList.Keys[index], sortedList.Values[index]);
                return true;
            }
            element = default;
            return false;
        }

        // --- Span<T> & ReadOnlySpan<T> ---

        /// <summary>
        /// Attempts to retrieve an element at a specific index from a ReadOnlySpan<T>.
        /// This method avoids exceptions for out-of-range indices.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetElementAtIndex<T>(this ReadOnlySpan<T> span, int index, out T element)
        {
            if ((uint)index < (uint)span.Length)
            {
                element = span[index];
                return true;
            }
            element = default;
            return false;
        }

        /// <summary>
        /// Attempts to retrieve an element at a specific index from a Span<T>.
        /// This method avoids exceptions for out-of-range indices.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetElementAtIndex<T>(this Span<T> span, int index, out T element)
        {
            if ((uint)index < (uint)span.Length)
            {
                element = span[index];
                return true;
            }
            element = default;
            return false;
        }
    }
}
