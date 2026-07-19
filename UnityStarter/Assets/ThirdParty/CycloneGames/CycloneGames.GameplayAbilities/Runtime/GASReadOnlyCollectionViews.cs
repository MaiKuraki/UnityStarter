using System;
using System.Collections;
using System.Collections.Generic;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Stable live view over an owned list. The backing list cannot be recovered or mutated through this API.
    /// Enumerating the concrete view uses a value-type enumerator and does not allocate.
    /// </summary>
    public sealed class GASReadOnlyListView<T> : IReadOnlyList<T>
    {
        private readonly List<T> source;
        private readonly Action assertAccess;

        internal GASReadOnlyListView(List<T> source, Action assertAccess)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.assertAccess = assertAccess ?? throw new ArgumentNullException(nameof(assertAccess));
        }

        public int Count
        {
            get
            {
                assertAccess();
                return source.Count;
            }
        }

        public T this[int index]
        {
            get
            {
                assertAccess();
                return source[index];
            }
        }

        public Enumerator GetEnumerator()
        {
            assertAccess();
            return new Enumerator(source, assertAccess);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            assertAccess();
            return new Enumerator(source, assertAccess);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            assertAccess();
            return new Enumerator(source, assertAccess);
        }

        public struct Enumerator : IEnumerator<T>
        {
            private List<T>.Enumerator enumerator;
            private readonly Action assertAccess;

            internal Enumerator(List<T> source, Action assertAccess)
            {
                enumerator = source.GetEnumerator();
                this.assertAccess = assertAccess;
            }

            public T Current
            {
                get
                {
                    assertAccess();
                    return enumerator.Current;
                }
            }

            object IEnumerator.Current => Current;

            public Enumerator GetEnumerator()
            {
                assertAccess();
                return this;
            }

            public bool MoveNext()
            {
                assertAccess();
                return enumerator.MoveNext();
            }

            void IEnumerator.Reset()
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
                enumerator.Dispose();
            }
        }
    }

    /// <summary>
    /// Stable live view over an owned set. The backing set cannot be recovered or mutated through this API.
    /// Enumerating the concrete view uses a value-type enumerator and does not allocate.
    /// </summary>
    public sealed class GASReadOnlySetView<T> : IReadOnlyCollection<T>
    {
        private readonly HashSet<T> source;
        private readonly Action assertAccess;

        internal GASReadOnlySetView(HashSet<T> source, Action assertAccess)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.assertAccess = assertAccess ?? throw new ArgumentNullException(nameof(assertAccess));
        }

        public int Count
        {
            get
            {
                assertAccess();
                return source.Count;
            }
        }

        public bool Contains(T item)
        {
            assertAccess();
            return source.Contains(item);
        }

        public Enumerator GetEnumerator()
        {
            assertAccess();
            return new Enumerator(source, assertAccess);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            assertAccess();
            return new Enumerator(source, assertAccess);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            assertAccess();
            return new Enumerator(source, assertAccess);
        }

        public struct Enumerator : IEnumerator<T>
        {
            private HashSet<T>.Enumerator enumerator;
            private readonly Action assertAccess;

            internal Enumerator(HashSet<T> source, Action assertAccess)
            {
                enumerator = source.GetEnumerator();
                this.assertAccess = assertAccess;
            }

            public T Current
            {
                get
                {
                    assertAccess();
                    return enumerator.Current;
                }
            }

            object IEnumerator.Current => Current;

            public Enumerator GetEnumerator()
            {
                assertAccess();
                return this;
            }

            public bool MoveNext()
            {
                assertAccess();
                return enumerator.MoveNext();
            }

            void IEnumerator.Reset()
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
                enumerator.Dispose();
            }
        }
    }

    /// <summary>
    /// Owner-thread-confined live query view over gameplay tags. It intentionally exposes no mutation,
    /// callback registration, backing indices, or raw container access.
    /// </summary>
    public sealed class GASReadOnlyTagView
    {
        private readonly IReadOnlyGameplayTagContainer source;
        private readonly IGameplayTagCountContainer countSource;
        private readonly Action assertAccess;

        internal GASReadOnlyTagView(IReadOnlyGameplayTagContainer source, Action assertAccess)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            countSource = source as IGameplayTagCountContainer;
            this.assertAccess = assertAccess ?? throw new ArgumentNullException(nameof(assertAccess));
        }

        public bool IsEmpty
        {
            get
            {
                assertAccess();
                return source.IsEmpty;
            }
        }

        public int ExplicitTagCount
        {
            get
            {
                assertAccess();
                return source.ExplicitTagCount;
            }
        }

        public int TagCount
        {
            get
            {
                assertAccess();
                return source.TagCount;
            }
        }

        public bool Contains(GameplayTag tag)
        {
            assertAccess();
            return source.ContainsRuntimeIndex(tag.RuntimeIndex, false);
        }

        public bool HasTag(GameplayTag tag)
        {
            return Contains(tag);
        }

        public bool HasTagExact(GameplayTag tag)
        {
            assertAccess();
            return source.ContainsRuntimeIndex(tag.RuntimeIndex, true);
        }

        public int GetTagCount(GameplayTag tag)
        {
            assertAccess();
            return countSource != null
                ? countSource.GetTagCount(tag)
                : (source.ContainsRuntimeIndex(tag.RuntimeIndex, false) ? 1 : 0);
        }

        public int GetExplicitTagCount(GameplayTag tag)
        {
            assertAccess();
            return countSource != null
                ? countSource.GetExplicitTagCount(tag)
                : (source.ContainsRuntimeIndex(tag.RuntimeIndex, true) ? 1 : 0);
        }

        public Enumerator GetEnumerator()
        {
            return GetTags();
        }

        public Enumerator GetTags()
        {
            assertAccess();
            return new Enumerator(source.GetTags(), assertAccess);
        }

        public Enumerator GetExplicitTags()
        {
            assertAccess();
            return new Enumerator(source.GetExplicitTags(), assertAccess);
        }

        public struct Enumerator : IEnumerator<GameplayTag>
        {
            private GameplayTagEnumerator enumerator;
            private readonly Action assertAccess;

            internal Enumerator(GameplayTagEnumerator enumerator, Action assertAccess)
            {
                this.enumerator = enumerator;
                this.assertAccess = assertAccess;
            }

            public GameplayTag Current
            {
                get
                {
                    assertAccess();
                    return enumerator.Current;
                }
            }

            object IEnumerator.Current => Current;

            public Enumerator GetEnumerator()
            {
                assertAccess();
                return this;
            }

            public bool MoveNext()
            {
                assertAccess();
                return enumerator.MoveNext();
            }

            void IEnumerator.Reset()
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
                enumerator.Dispose();
            }
        }
    }
}
