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
        // --- List<T> ---

        /// <summary>
        /// Attempts to retrieve an element at a specific index from a List<T>.
        /// This method is thread-safe for reads and avoids throwing exceptions for out-of-range indices.
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

        // --- Stack<T> ---

        /// <summary>
        /// Attempts to return the object at the top of the Stack<T> without removing it.
        /// This method is 0GC and avoids the exception thrown by Peek() on an empty stack.
        /// </summary>
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

        /// <summary>
        /// Attempts to remove and return the object at the top of the Stack<T>.
        /// This method is 0GC and avoids the exception thrown by Pop() on an empty stack.
        /// </summary>
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

        // --- Queue<T> ---

        /// <summary>
        /// Attempts to return the object at the beginning of the Queue<T> without removing it.
        /// This method is 0GC and avoids the exception thrown by Peek() on an empty queue.
        /// </summary>
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

        /// <summary>
        /// Attempts to remove and return the object at the beginning of the Queue<T>.
        /// This method is 0GC and avoids the exception thrown by Dequeue() on an empty queue.
        /// </summary>
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
        /// </summary>
        public static bool TryGetNodeAtIndex<T>(this LinkedList<T> list, int index, out LinkedListNode<T> node)
        {
            if (list != null && (uint)index < (uint)list.Count)
            {
                var currentNode = list.First;
                for (int i = 0; i < index; i++)
                {
                    currentNode = currentNode.Next;
                }
                node = currentNode;
                return true;
            }
            node = null;
            return false;
        }

        // --- Dictionary<TKey, TValue> ---

        /// <summary>
        /// Attempts to retrieve an element at a specific index from a Dictionary<TKey, TValue>.
        /// WARNING: Dictionaries are unordered. "Index" refers to the unstable enumeration order.
        /// This operation is O(N) due to the need for enumeration and is 0GC.
        /// It is not thread-safe for writes. For concurrent access, use a ConcurrentDictionary.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        /// <summary>
        /// Creates a thread-safe snapshot of the dictionary's key-value pairs as an array.
        /// This allows for safe enumeration without locking the collection for the duration of the loop.
        /// WARNING: This method allocates memory for the new array and is NOT a 0GC operation.
        /// </summary>
        public static KeyValuePair<TKey, TValue>[] ToArraySnapshot<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary)
        {
            if (dictionary == null)
            {
                return Array.Empty<KeyValuePair<TKey, TValue>>();
            }
            return dictionary.ToArray();
        }

        /// <summary>
        /// Provides a consistent, null-safe wrapper for ConcurrentDictionary.TryAdd.
        /// This operation is thread-safe and 0GC.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryAdd<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            return dictionary != null && dictionary.TryAdd(key, value);
        }

        /// <summary>
        /// Provides a consistent, null-safe wrapper for ConcurrentDictionary.TryRemove.
        /// This operation is thread-safe and 0GC.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRemove<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key, out TValue value)
        {
            if (dictionary == null)
            {
                value = default;
                return false;
            }
            return dictionary.TryRemove(key, out value);
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