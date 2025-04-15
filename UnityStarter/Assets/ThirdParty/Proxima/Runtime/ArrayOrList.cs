using System;
using System.Collections;
using UnityEngine;

namespace Proxima
{
    internal class ArrayOrList
    {
        public static bool IsArrayOrList(Type arrayOrList)
        {
            return IsArray(arrayOrList) || IsList(arrayOrList);
        }

        public static bool IsList(Type arrayOrList)
        {
            return typeof(IList).IsAssignableFrom(arrayOrList) && arrayOrList.IsGenericType;
        }

        public static bool IsArray(Type arrayOrList)
        {
            return arrayOrList.IsArray;
        }

        public static bool IsArrayOrList(object arrayOrList)
        {
            return IsArray(arrayOrList) || IsList(arrayOrList);
        }

        public static bool IsList(object arrayOrList)
        {
            return IsList(arrayOrList.GetType());
        }

        public static bool IsArray(object arrayOrList)
        {
            return IsArray(arrayOrList.GetType());
        }

        public static int Count(object arrayOrList)
        {
            if (IsArray(arrayOrList))
            {
                return ((Array)arrayOrList).Length;
            }
            else if (IsList(arrayOrList))
            {
                return ((IList)arrayOrList).Count;
            }

            throw new ArgumentException("Type is not an array or list");
        }

        public static object Get(object arrayOrList, int index)
        {
            if (IsArray(arrayOrList))
            {
                return ((Array)arrayOrList).GetValue(index);
            }
            else if (IsList(arrayOrList))
            {
                return ((IList)arrayOrList)[index];
            }

            throw new ArgumentException("Type is not an array or list");
        }

        public static void Set(object arrayOrList, int index, object value)
        {
            if (IsArray(arrayOrList))
            {
                ((Array)arrayOrList).SetValue(value, index);
            }
            else if (IsList(arrayOrList))
            {
                ((IList)arrayOrList)[index] = value;
            }
            else
            {
                throw new ArgumentException("Type is not an array or list");
            }
        }

        public static Type GetElementType(Type arrayOrList)
        {
            if (IsArray(arrayOrList))
            {
                return arrayOrList.GetElementType();
            }
            else if (IsList(arrayOrList))
            {
                return arrayOrList.GetGenericArguments()[0];
            }

            throw new ArgumentException("Type is not an array or list");
        }

        private static object CreateDefault(Type type)
        {
            if (type.IsPrimitive)
            {
                return Activator.CreateInstance(type);
            }

            if (type == typeof(string))
            {
                return "";
            }

            if (type.GetConstructor(Type.EmptyTypes) != null)
            {
                return Activator.CreateInstance(type);
            }

            return null;
        }

        private static object Duplicate(object value)
        {
            if (value == null)
            {
                return null;
            }

            var type = value.GetType();
            if (type.IsValueType || type == typeof(string))
            {
                return value;
            }

            if (IsArray(type))
            {
                var array = (Array)value;
                var newArray = Array.CreateInstance(type.GetElementType(), array.Length);
                for (var i = 0; i < array.Length; i++)
                {
                    newArray.SetValue(Duplicate(array.GetValue(i)), i);
                }
            }

            if (IsList(type))
            {
                var list = (IList)value;
                var newList = (IList)Activator.CreateInstance(type);
                foreach (var item in list)
                {
                    newList.Add(Duplicate(item));
                }

                return newList;
            }

            if (type.IsSubclassOf(typeof(UnityEngine.Object)))
            {
                return value;
            }

            return null;
        }

        public static object Resize(object arrayOrList, int size)
        {
            var oldSize = Count(arrayOrList);
            if (oldSize == size)
            {
                return arrayOrList;
            }

            var type = arrayOrList.GetType();
            var elementType = GetElementType(type);

            if (IsArray(type))
            {
                var newArray = Array.CreateInstance(elementType, size);
                Array.Copy((Array)arrayOrList, newArray, Math.Min(oldSize, size));
                for (var i = oldSize; i < size; i++)
                {
                    newArray.SetValue(CreateDefault(elementType), i);
                }

                arrayOrList = newArray;
            }
            else if (IsList(type))
            {
                var list = ((IList)arrayOrList);
                while (list.Count > size)
                {
                    list.RemoveAt(list.Count - 1);
                }

                while (list.Count < size)
                {
                    list.Add(CreateDefault(elementType));
                }

                arrayOrList = list;
            }
            else
            {
                throw new ArgumentException("Type is not an array or list");
            }

            // If we're adding elements, and we already have at least one,
            // mimic the Unity behavior of duplicating the last element.
            if (size > oldSize && oldSize > 0)
            {
                var lastElement = Get(arrayOrList, oldSize - 1);
                for (var i = oldSize; i < size; i++)
                {
                    Set(arrayOrList, i, Duplicate(lastElement));
                }
            }

            return arrayOrList;
        }

        public static void MoveElement(object arrayOrList, int from, int to)
        {
            var temp = Get(arrayOrList, from);

            if (from < to)
            {
                for (int i = from; i < to; i++)
                {
                    Set(arrayOrList, i, Get(arrayOrList, i + 1));
                }
            }
            else
            {
                for (int i = from; i > to; i--)
                {
                    Set(arrayOrList, i, Get(arrayOrList, i - 1));
                }
            }

            Set(arrayOrList, to, temp);
        }

        public static object RemoveElement(object arrayOrList, int index)
        {
            var size = Count(arrayOrList);
            for (int i = index; i < size - 1; i++)
            {
                Set(arrayOrList, i, Get(arrayOrList, i + 1));
            }

            return Resize(arrayOrList, size - 1);
        }
    }
}