using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Proxima
{
    internal class CircularList<T> : IEnumerable<T>
    {
        private T[] _array;
        private int _itemsAdded = 0;
        public int ItemsAdded => _itemsAdded;

        public CircularList(int capacity)
        {
            _array = new T[capacity];
        }

        public void Add(T t)
        {
            var index = _itemsAdded % _array.Length;
            _array[index] = t;
            _itemsAdded++;
        }

        public IEnumerable<T> GetRange(int index)
        {
            index = Mathf.Max(_itemsAdded - _array.Length, index);
            var count = _itemsAdded - index;
            var start = index % _array.Length;
            for (int i = 0; i < count; i++)
            {
                yield return _array[(i + start) % _array.Length];
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            return GetRange(0).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}