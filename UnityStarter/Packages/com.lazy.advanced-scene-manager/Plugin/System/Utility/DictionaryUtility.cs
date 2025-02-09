using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedSceneManager.Utility
{

    /// <summary>Contains utility functions for working with dictionaries.</summary>
    public static class DictionaryUtility
    {

        public static TValue Set<TKey, TValue>(this Dictionary<TKey, TValue> d, TKey key, TValue value)
        {
            Add(d, key, value);
            return value;
        }

        public static void Set(this Hashtable d, object key, object value)
        {

            if (d == null)
                return;

            if (key == null) return;

            if (d.ContainsKey(key))
                d[key] = value;
            else
                d.Add(key, value);

        }

        /// <summary>Adds or sets the value of a key.</summary>
        public static void Add<TKey, TValue>(this Dictionary<TKey, TValue> d, TKey key, TValue value)
        {

            if (d == null)
                return;

            if (key == null) return;

            if (d.ContainsKey(key))
                d[key] = value;
            else
                d.Add(key, value);

        }

        /// <summary>Adds the value to the list with the specified key. Creates list automatically if null and adds key if necessary.</summary>
        public static void Add<TKey, TList, TItem>(this Dictionary<TKey, TList> d, TKey key, TItem item) where TList : IList<TItem>, new() =>
            AddRange(d, key, item);

        /// <summary>Adds the values to the list with the specified key. Creates list automatically if null and adds key if necessary.</summary>
        public static void AddRange<TKey, TList, TItem>(this Dictionary<TKey, TList> d, TKey key, IEnumerable<TItem> items) where TList : IList<TItem>, new() =>
            AddRange(d, key, items.ToArray());

        /// <summary>Adds the values to the list with the specified key. Creates list automatically if null and adds key if necessary.</summary>
        public static void AddRange<TKey, TList, TItem>(this Dictionary<TKey, TList> d, TKey key, params TItem[] items) where TList : IList<TItem>, new()
        {

            if (d == null)
                return;

            if (key == null) return;

            if (!d.ContainsKey(key))
                d.Add(key, new TList());
            else if (d[key] == null)
                d[key] = new TList();

            foreach (var item in items)
                d[key].Add(item);

        }

        /// <summary>Removes the value to the list with the specified key.</summary>
        public static void Remove<TKey, TList, TItem>(this Dictionary<TKey, TList> d, TKey key, TItem value) where TList : IList<TItem>, new()
        {

            if (d == null)
                return;

            if (key == null) return;

            if (d.ContainsKey(key))
                d[key]?.Remove(value);

        }

        /// <summary>Gets the value of the specified key, returns default if it does not exist.</summary>
        public static TValue GetValue<TKey, TValue>(this Dictionary<TKey, TValue> d, TKey key, TValue defaultValue = default)
        {

            if (d == null)
                return defaultValue;

            if (key == null) return defaultValue;

            if (d.ContainsKey(key))
                return d[key];
            else
                return defaultValue;
        }

    }

}
