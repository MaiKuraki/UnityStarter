using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedSceneManager.Utility
{

    internal static class LinqUtility
    {

        public static IEnumerable<T> Concat<T>(this IEnumerable<T> list, T item, bool checkContains = true)
        {
            if (!checkContains || !list.Contains(item))
                list = list.Concat(new[] { item });
            return list;
        }

        public static IEnumerable<T> Except<T>(this IEnumerable<T> list, T item) =>
            list.Except(new[] { item });

        public static IEnumerable<T> NonNull<T>(this IEnumerable<T> list) where T : UnityEngine.Object =>
            list.Where(o => o);

        public static void ForEach<T>(this T[] list, Action<T, int> action)
        {
            for (int i = 0; i < list.Length; i++)
                action?.Invoke(list[i], i);
        }

        public static void ForEach<T>(this IEnumerable<T> list, Action<T, int> action)
        {
            for (int i = 0; i < list.Count(); i++)
                action?.Invoke(list.ElementAt(i), i);
        }

        public static void ForEach<T>(this IEnumerable<T> list, Action<T> action)
        {
            for (int i = 0; i < list.Count(); i++)
                action?.Invoke(list.ElementAt(i));
        }

        public static void ForEach<T>(this T[] list, Action<T> action)
        {
            for (int i = 0; i < list.Length; i++)
                action?.Invoke(list[i]);
        }

        public static IEnumerable<T> Flatten<T>(this IEnumerable<T> list, Func<T, IEnumerable<T>> getSubList)
        {
            foreach (var item in list)
            {
                yield return item;
                foreach (var subItem in Flatten(getSubList?.Invoke(item), getSubList))
                    yield return subItem;
            }
        }

        public static IEnumerable<IEnumerable<T>> GroupConsecutive<T>(this IEnumerable<T> list, Func<T, T, bool> compare)
        {
            if (list.Count() > 1)
            {
                var prevItem = list.First();
                var l = new List<T>();
                l.Add(prevItem);
                foreach (var item in list.Skip(1))
                {
                    if (!compare.Invoke(prevItem, item))
                    {
                        yield return l;
                        l = new List<T>();
                        l.Add(item);
                    }
                    else
                        l.Add(item);
                    prevItem = item;
                }
                yield return l;
            }

        }

    }

}
