using System;
using System.Threading;
using System.Threading.Tasks;

using CycloneGames.Utility.Runtime;

using NUnit.Framework;

namespace CycloneGames.Utility.Tests.Editor
{
    [TestFixture]
    public sealed class SingletonTests
    {
        [Test]
        public void Instance_SupportsDirectAndInheritedLegacyShapes()
        {
            DirectSingleton directFirst = Singleton<DirectSingleton>.Instance;
            DirectSingleton directSecond = Singleton<DirectSingleton>.Instance;
            InheritedSingleton inheritedFirst = InheritedSingleton.Instance;
            InheritedSingleton inheritedSecond = InheritedSingleton.Instance;

            Assert.That(directFirst, Is.SameAs(directSecond));
            Assert.That(inheritedFirst, Is.SameAs(inheritedSecond));
        }

        [Test]
        public void Instance_ConcurrentFirstAccess_ConstructsExactlyOnce()
        {
            var instances = new ConcurrentSingleton[16];

            Parallel.For(0, instances.Length, index =>
            {
                instances[index] = ConcurrentSingleton.Instance;
            });

            Assert.That(ConcurrentSingleton.ConstructionCount, Is.EqualTo(1));
            for (int i = 1; i < instances.Length; i++)
            {
                Assert.That(instances[i], Is.SameAs(instances[0]));
            }
        }

        [Test]
        public void Instance_ConstructorFailure_IsCachedByTypeInitialization()
        {
            Assert.Throws<TypeInitializationException>(() => _ = FailingSingleton.Instance);
            Assert.Throws<TypeInitializationException>(() => _ = FailingSingleton.Instance);
            Assert.That(FailingSingleton.ConstructionAttempts, Is.EqualTo(1));
        }

        [Test]
        public void Instance_WarmAccess_DoesNotAllocateManagedMemory()
        {
            AllocationSingleton current = AllocationSingleton.Instance;
            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 100_000; i++)
            {
                current = AllocationSingleton.Instance;
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(current);
            Assert.That(allocated, Is.Zero);
        }

        private sealed class DirectSingleton
        {
            public DirectSingleton()
            {
            }
        }

        private sealed class InheritedSingleton : Singleton<InheritedSingleton>
        {
            public InheritedSingleton()
            {
            }
        }

        private sealed class ConcurrentSingleton : Singleton<ConcurrentSingleton>
        {
            private static int _constructionCount;

            public ConcurrentSingleton()
            {
                Interlocked.Increment(ref _constructionCount);
            }

            internal static int ConstructionCount => Volatile.Read(ref _constructionCount);
        }

        private sealed class FailingSingleton : Singleton<FailingSingleton>
        {
            private static int _constructionAttempts;

            public FailingSingleton()
            {
                Interlocked.Increment(ref _constructionAttempts);
                throw new InvalidOperationException("Expected singleton construction failure.");
            }

            internal static int ConstructionAttempts => Volatile.Read(ref _constructionAttempts);
        }

        private sealed class AllocationSingleton : Singleton<AllocationSingleton>
        {
            public AllocationSingleton()
            {
            }
        }
    }
}
