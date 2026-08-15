using System;
using System.Collections;
using System.Collections.Generic;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Allocation-free live list view whose count, indexer, and enumerator enforce the owning
    /// gameplay thread on every access. Retaining the view or an enumerator never bypasses the
    /// thread-affinity contract of the mutable list behind it.
    /// </summary>
    public sealed class OwnerThreadReadOnlyList<T> : IReadOnlyList<T>
    {
        private readonly Action assertOwnerThread;
        private readonly List<T> items;

        internal OwnerThreadReadOnlyList(
            Action assertOwnerThread,
            List<T> items)
        {
            this.assertOwnerThread = assertOwnerThread ??
                throw new ArgumentNullException(nameof(assertOwnerThread));
            this.items = items ?? throw new ArgumentNullException(nameof(items));
        }

        public int Count
        {
            get
            {
                assertOwnerThread();
                return items.Count;
            }
        }

        public T this[int index]
        {
            get
            {
                assertOwnerThread();
                return items[index];
            }
        }

        public Enumerator GetEnumerator()
        {
            assertOwnerThread();
            return new Enumerator(assertOwnerThread, items.GetEnumerator());
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<T>
        {
            private readonly Action assertOwnerThread;
            private List<T>.Enumerator inner;

            internal Enumerator(
                Action assertOwnerThread,
                List<T>.Enumerator inner)
            {
                this.assertOwnerThread = assertOwnerThread;
                this.inner = inner;
            }

            public T Current
            {
                get
                {
                    assertOwnerThread();
                    return inner.Current;
                }
            }

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                assertOwnerThread();
                return inner.MoveNext();
            }

            public void Dispose()
            {
                inner.Dispose();
            }

            void IEnumerator.Reset()
            {
                throw new NotSupportedException();
            }
        }
    }
}
