using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Provides high-performance, thread-safe, and GC-optimized utility methods for various collection types.
    /// Focuses on minimizing allocations and providing robust, exception-safe access.
    /// All methods are designed to be 0GC (zero garbage collection) unless explicitly stated otherwise.
    /// </summary>
    public static class CollectionUtils
    {
        // Shared RNG for Shuffle, avoids allocation per call.
        [ThreadStatic] private static Random _sharedRng;

        private static Random SharedRng => _sharedRng ?? (_sharedRng = new Random());

        // --- List<T> ---

        /// <summary>
        /// Attempts to retrieve an element at a specific index from a List<T>.
        /// WARNING: NOT thread-safe. If another thread may be writing, use TryGetElementAtIndexThreadSafe instead.
        /// It is O(1) for List<T> and is 0GC.
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
        /// Attempts to retrieve an element from a List<T> in a thread-safe manner using an external lock.
        /// The lock must be acquired by the caller to ensure thread safety during concurrent modifications.
        /// It is O(1) and 0GC.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetElementAtIndexThreadSafe<T>(this List<T> list, int index, object lockObject, out T element)
        {
            if (lockObject == null) throw new ArgumentNullException(nameof(lockObject));

            lock (lockObject)
            {
                if (list != null && (uint)index < (uint)list.Count)
                {
                    element = list[index];
                    return true;
                }
            }
            element = default;
            return false;
        }

        /// <summary>
        /// Adds a collection of items to a List<T> within a single lock, improving performance over locking per item.
        /// This method is designed for scenarios requiring thread-safe bulk additions.
        /// </summary>
        public static void ThreadSafeAddRange<T>(this List<T> list, IEnumerable<T> collection, object lockObject)
        {
            if (lockObject == null) throw new ArgumentNullException(nameof(lockObject));
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            lock (lockObject)
            {
                list.AddRange(collection);
            }
        }

        /// <summary>
        /// Clears a List<T> and optionally sets its capacity to avoid future re-allocations.
        /// This is a 0GC operation useful for reusing lists in performance-sensitive code.
        /// </summary>
        public static void ClearAndResize<T>(this List<T> list, int capacity)
        {
            if (list == null) return;

            list.Clear();
            if (list.Capacity < capacity)
            {
                list.Capacity = capacity;
            }
        }

        /// <summary>
        /// Attempts to remove and return the last element of the List<T>.
        /// This is an O(1) operation and is 0GC.
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
            if (list == null || list.Count == 0) throw new InvalidOperationException("List is empty");
            int lastIndex = list.Count - 1;
            T item = list[lastIndex];
            list.RemoveAt(lastIndex);
            return item;
        }

        /// <summary>
        /// Removes an element at the given index by swapping it with the last element, then removing the last.
        /// This is an O(1) operation but does NOT preserve order.
        /// Ideal for game loops where order doesn't matter (e.g., entity lists, projectile pools).
        /// 0GC.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SwapRemoveAt<T>(this List<T> list, int index)
        {
            int lastIndex = list.Count - 1;
            if (index < lastIndex)
            {
                list[index] = list[lastIndex];
            }
            list.RemoveAt(lastIndex);
        }

        /// <summary>
        /// Attempts to get the first element of a List<T>.
        /// O(1), 0GC.
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
        /// O(1), 0GC.
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
        /// In-place Fisher-Yates shuffle. O(N), 0GC.
        /// Uses System.Random by default. For deterministic results, pass a seeded Random instance.
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

        /// <summary>Checks if a List is null or empty. 0GC.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrEmpty<T>(this List<T> list) => list == null || list.Count == 0;

        /// <summary>Checks if an array is null or empty. 0GC.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrEmpty<T>(this T[] array) => array == null || array.Length == 0;

        /// <summary>Checks if a Dictionary is null or empty. 0GC.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrEmpty<TKey, TValue>(this Dictionary<TKey, TValue> dict) => dict == null || dict.Count == 0;

        /// <summary>Checks if a HashSet is null or empty. 0GC.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrEmpty<T>(this HashSet<T> set) => set == null || set.Count == 0;

        // --- T[] (Array) ---

        /// <summary>
        /// Attempts to retrieve an element at a specific index from a T[] array.
        /// This method is highly optimized and avoids exceptions for out-of-range indices.
        /// It is O(1) and 0GC.
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
        /// 0GC.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SwapRemoveAt<T>(this T[] array, int index, int count)
        {
            int lastIndex = count - 1;
            if (index < lastIndex)
            {
                array[index] = array[lastIndex];
            }
            array[lastIndex] = default;
            return lastIndex;
        }

        /// <summary>
        /// In-place Fisher-Yates shuffle for arrays. O(N), 0GC.
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

        // --- HashSet<T> ---

        /// <summary>
        /// Attempts to add an element to a HashSet<T> in a thread-safe manner.
        /// This is an O(1) operation on average.
        /// </summary>
        public static bool ThreadSafeTryAdd<T>(this HashSet<T> hashSet, T item, object lockObject)
        {
            if (lockObject == null) throw new ArgumentNullException(nameof(lockObject));
            if (hashSet == null) return false;

            lock (lockObject)
            {
                return hashSet.Add(item);
            }
        }

        /// <summary>
        /// Checks if an element exists in a HashSet<T> in a thread-safe manner.
        /// This is an O(1) operation on average.
        /// </summary>
        public static bool ThreadSafeContains<T>(this HashSet<T> hashSet, T item, object lockObject)
        {
            if (lockObject == null) throw new ArgumentNullException(nameof(lockObject));
            if (hashSet == null) return false;

            lock (lockObject)
            {
                return hashSet.Contains(item);
            }
        }

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
        /// O(1), 0GC.
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

        /// <summary>
        /// Attempts to retrieve an element at a specific index from a Dictionary<TKey, TValue>.
        /// WARNING: Dictionaries are unordered. "Index" refers to the unstable enumeration order.
        /// This operation is O(N) due to the need for enumeration and is 0GC
        /// (Dictionary.Enumerator is a struct, no heap allocation).
        /// It is not thread-safe for writes. For concurrent access, use a ConcurrentDictionary.
        /// </summary>
        public static bool TryGetElementAtIndex<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, int index, out KeyValuePair<TKey, TValue> element)
        {
            if (dictionary != null && (uint)index < (uint)dictionary.Count)
            {
                int currentIndex = 0;
                // Dictionary enumerator is a struct, making this allocation-free.
                foreach (var pair in dictionary)
                {
                    if (currentIndex == index)
                    {
                        element = pair;
                        return true;
                    }
                    currentIndex++;
                }
            }
            element = default;
            return false;
        }

        // --- SortedList<TKey, TValue> ---

        /// <summary>
        /// Attempts to retrieve a value at a specific index from a SortedList<TKey, TValue>.
        /// This is a highly efficient operation.
        /// It is O(1) because SortedList is backed by arrays, not O(log N). It is also 0GC.
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
        /// It is O(1) because SortedList is backed by arrays, not O(log N). It is also 0GC.
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

        // --- ConcurrentDictionary<TKey, TValue> ---
        // NOTE: ConcurrentDictionary.TryAdd / TryRemove / TryGetValue are built-in instance methods.
        // Extension methods with the same signature are DEAD CODE (instance methods always win in C# overload resolution).
        // Only provide helpers that add genuinely new functionality.

        /// <summary>
        /// Null-safe wrapper for ConcurrentDictionary.GetOrAdd.
        /// This operation is thread-safe. The valueFactory may be called more than once under contention,
        /// but only one result will be stored. 0GC when the key already exists.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TValue SafeGetOrAdd<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key, Func<TKey, TValue> valueFactory)
        {
            if (dictionary == null) return default;
            return dictionary.GetOrAdd(key, valueFactory);
        }

        // --- Span<T> & ReadOnlySpan<T> ---

        /// <summary>
        /// Attempts to retrieve an element at a specific index from a ReadOnlySpan<T>.
        /// This method is highly optimized, 0GC, and avoids exceptions for out-of-range indices.
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
        /// This method is highly optimized, 0GC, and avoids exceptions for out-of-range indices.
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