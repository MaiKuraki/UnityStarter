using System;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Runtime.Integrations.Spatial;
using CycloneGames.Spatial.Core;
using NUnit.Framework;

namespace CycloneGames.BehaviorTree.Integrations.Spatial.Tests.Editor
{
    public sealed class SpatialBlackboardQueryTests
    {
        private const int VisibleKey = 101;
        private const int DistanceKey = 102;
        private const int CountKey = 103;
        private const int OriginKey = 104;

        private SpatialHashGrid<int> _grid;

        [SetUp]
        public void SetUp()
        {
            _grid = new SpatialHashGrid<int>(4f);
        }

        [TearDown]
        public void TearDown()
        {
            _grid.Dispose();
        }

        [Test]
        public void QueryNearest_ReturnsItemsAscendingBySquaredDistance()
        {
            Insert(7, 10f, 0f, 0f);
            Insert(9, 3f, 4f, 0f);
            Insert(11, 0f, 0f, 1f);

            using (var query = new SpatialBlackboardQuery<int>(_grid, 4))
            {
                int count = query.QueryNearest(0f, 0f, 0f, 100f, SpatialLayers.All);

                Assert.AreEqual(3, count);
                Assert.AreEqual(11, query.NearestItem);
                Assert.AreEqual(1f, query.NearestSquaredDistance, 1e-5f);
                Assert.AreEqual(9, query.Results[1]);
                Assert.AreEqual(25f, query.SquaredDistances[1], 1e-5f);
                Assert.AreEqual(7, query.Results[2]);
                Assert.AreEqual(100f, query.SquaredDistances[2], 1e-5f);
            }
        }

        [Test]
        public void PublishNearest_WritesVisibilityAndDistanceSlots()
        {
            Insert(3, 0f, 0f, 2f);

            using (var blackboard = new RuntimeBlackboard())
            using (var query = new SpatialBlackboardQuery<int>(_grid, 2))
            {
                query.QueryNearest(0f, 0f, 0f, 10f, SpatialLayers.All);
                query.PublishNearest(blackboard, VisibleKey, DistanceKey);

                Assert.IsTrue(blackboard.GetBool(VisibleKey));
                Assert.AreEqual(4f, blackboard.GetFloat(DistanceKey), 1e-5f);
            }
        }

        [Test]
        public void PublishNearest_EmptyResult_WritesFalseAndZero()
        {
            Insert(3, 50f, 0f, 0f);

            using (var blackboard = new RuntimeBlackboard())
            using (var query = new SpatialBlackboardQuery<int>(_grid, 2))
            {
                query.QueryNearest(0f, 0f, 0f, 5f, SpatialLayers.All);
                query.PublishNearest(blackboard, VisibleKey, DistanceKey);

                Assert.IsFalse(blackboard.GetBool(VisibleKey));
                Assert.AreEqual(0f, blackboard.GetFloat(DistanceKey));
            }
        }

        [Test]
        public void QueryNearest_RespectsLayerMask()
        {
            Insert(1, 0f, 0f, 1f, layer: 2u);
            Insert(2, 0f, 0f, 2f, layer: 4u);

            using (var query = new SpatialBlackboardQuery<int>(_grid, 4))
            {
                Assert.AreEqual(1, query.QueryNearest(0f, 0f, 0f, 10f, 4u));
                Assert.AreEqual(2, query.NearestItem);

                Assert.AreEqual(0, query.QueryNearest(0f, 0f, 0f, 10f, 8u));
            }
        }

        [Test]
        public void QueryNearest_ClampsToMaxResults()
        {
            Insert(1, 0f, 0f, 1f);
            Insert(2, 0f, 0f, 2f);
            Insert(3, 0f, 0f, 3f);

            using (var query = new SpatialBlackboardQuery<int>(_grid, 2))
            {
                Assert.AreEqual(2, query.MaxResults);
                Assert.AreEqual(2, query.QueryNearest(0f, 0f, 0f, 10f, SpatialLayers.All));
                Assert.AreEqual(1, query.NearestItem);
                Assert.AreEqual(2, query.Results[1]);
            }
        }

        [Test]
        public void QueryNearest_NonPositiveRadiusReturnsNothing()
        {
            Insert(1, 0f, 0f, 0f);

            using (var query = new SpatialBlackboardQuery<int>(_grid, 2))
            {
                Assert.AreEqual(0, query.QueryNearest(0f, 0f, 0f, 0f, SpatialLayers.All));
                Assert.AreEqual(0, query.QueryNearest(0f, 0f, 0f, -1f, SpatialLayers.All));

                using (var blackboard = new RuntimeBlackboard())
                {
                    query.PublishCount(blackboard, CountKey);
                    Assert.AreEqual(0, blackboard.GetInt(CountKey));
                }
            }
        }

        [Test]
        public void QueryNearestFrom_ReadsOriginSlot()
        {
            Insert(5, 0f, 0f, 0f);

            using (var blackboard = new RuntimeBlackboard())
            using (var query = new SpatialBlackboardQuery<int>(_grid, 2))
            {
                blackboard.SetFloat3(OriginKey, new BTFloat3(5f, 0f, 0f));

                Assert.AreEqual(0, query.QueryNearestFrom(blackboard, OriginKey, 4f, SpatialLayers.All));
                Assert.AreEqual(1, query.QueryNearestFrom(blackboard, OriginKey, 6f, SpatialLayers.All));
            }
        }

        [Test]
        public void PublishCount_WritesResultCount()
        {
            Insert(1, 0f, 0f, 1f);
            Insert(2, 0f, 0f, 2f);

            using (var blackboard = new RuntimeBlackboard())
            using (var query = new SpatialBlackboardQuery<int>(_grid, 4))
            {
                query.QueryNearest(0f, 0f, 0f, 10f, SpatialLayers.All);
                query.PublishCount(blackboard, CountKey);

                Assert.AreEqual(2, blackboard.GetInt(CountKey));
            }
        }

        [Test]
        public void PublishNearest_UnchangedResult_DoesNotNotifyObservers()
        {
            Insert(3, 0f, 0f, 2f);

            using (var blackboard = new RuntimeBlackboard())
            using (var query = new SpatialBlackboardQuery<int>(_grid, 2))
            {
                int notifications = 0;
                blackboard.AddObserver(DistanceKey, (key, bb) => notifications++);

                query.QueryNearest(0f, 0f, 0f, 10f, SpatialLayers.All);
                query.PublishNearest(blackboard, VisibleKey, DistanceKey);
                Assert.AreEqual(1, notifications);

                query.QueryNearest(0f, 0f, 0f, 10f, SpatialLayers.All);
                query.PublishNearest(blackboard, VisibleKey, DistanceKey);
                Assert.AreEqual(1, notifications);
            }
        }

        [Test]
        public void QueryAndPublish_SteadyStateAllocatesNothing()
        {
            for (int i = 0; i < 64; i++)
            {
                Insert(i, i * 0.5f, 0f, 0f);
            }

            using (var blackboard = new RuntimeBlackboard())
            using (var query = new SpatialBlackboardQuery<int>(_grid, 8))
            {
                blackboard.SetBool(VisibleKey, true);
                blackboard.SetFloat(DistanceKey, 0f);

                for (int i = 0; i < 1000; i++)
                {
                    query.QueryNearest(0f, 0f, 0f, 40f, SpatialLayers.All);
                    query.PublishNearest(blackboard, VisibleKey, DistanceKey);
                }

                long before = GC.GetTotalMemory(true);

                for (int i = 0; i < 100000; i++)
                {
                    query.QueryNearest(0f, 0f, 0f, 40f, SpatialLayers.All);
                    query.PublishNearest(blackboard, VisibleKey, DistanceKey);
                }

                long after = GC.GetTotalMemory(true);
                Assert.Less(after - before, 65536, "Steady-state query and publish must not allocate.");
            }
        }

        [Test]
        public void Dispose_ReleasesBuffersAndRejectsFurtherQueries()
        {
            var query = new SpatialBlackboardQuery<int>(_grid, 4);
            query.Dispose();
            query.Dispose();

            Assert.Throws<ObjectDisposedException>(() => query.QueryNearest(0f, 0f, 0f, 1f, SpatialLayers.All));

            using (var blackboard = new RuntimeBlackboard())
            {
                Assert.Throws<ObjectDisposedException>(() => query.PublishNearest(blackboard, VisibleKey, DistanceKey));
            }
        }

        [Test]
        public void BindsInterfaceDispatchAndSnapshotBackends()
        {
            Insert(1, 0f, 0f, 1f);
            ISpatialReadOnlyIndex<int> index = _grid;

            using (var fromInterface = new SpatialBlackboardQuery<int>(index, 2))
            {
                Assert.AreEqual(1, fromInterface.QueryNearest(0f, 0f, 0f, 5f, SpatialLayers.All));
                Assert.AreEqual(1, fromInterface.NearestItem);
            }

            using (var snapshot = new SpatialSnapshot<int>(new GridSettings(4f)))
            {
                SpatialEntry<int>[] entries =
                {
                    new SpatialEntry<int>(0f, 0f, 3f, SpatialLayers.Default, 7),
                    new SpatialEntry<int>(0f, 0f, 9f, SpatialLayers.Default, 9),
                };
                snapshot.Build(entries);

                using (var fromSnapshot = new SpatialBlackboardQuery<int>(snapshot, 2))
                {
                    Assert.AreEqual(2, fromSnapshot.QueryNearest(0f, 0f, 0f, 10f, SpatialLayers.All));
                    Assert.AreEqual(7, fromSnapshot.NearestItem);
                    Assert.AreEqual(81f, fromSnapshot.SquaredDistances[1], 1e-5f);
                }
            }
        }

        [Test]
        public void AcceptsAnyIndexImplementation()
        {
            // A project-owned index that implements the read-only contract drives the adapter directly.
            var fake = new FixedProvider(new[] { 42 }, new[] { 6.25f });

            using (var blackboard = new RuntimeBlackboard())
            using (var query = new SpatialBlackboardQuery<int>(fake, 4))
            {
                Assert.AreEqual(1, query.QueryNearest(1f, 2f, 3f, 5f, 0xFFFFFFFFu));
                Assert.AreEqual(42, query.NearestItem);
                Assert.AreEqual(6.25f, query.NearestSquaredDistance, 1e-6f);
                Assert.AreEqual(1f, fake.LastX);
                Assert.AreEqual(4, fake.LastMaxResults);

                query.PublishNearest(blackboard, VisibleKey, DistanceKey);
                Assert.IsTrue(blackboard.GetBool(VisibleKey));
                Assert.AreEqual(6.25f, blackboard.GetFloat(DistanceKey), 1e-6f);
            }
        }

        [Test]
        public void Constructor_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new SpatialBlackboardQuery<int>(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialBlackboardQuery<int>(_grid, 0));
        }

        private void Insert(int item, float x, float y, float z, uint layer = SpatialLayers.Default)
        {
            Assert.IsTrue(_grid.TryInsert(item, x, y, z, layer, out _));
        }

        private sealed class FixedProvider : ISpatialReadOnlyIndex<int>
        {
            private readonly int[] _items;
            private readonly float[] _distances;

            public FixedProvider(int[] items, float[] distances)
            {
                _items = items;
                _distances = distances;
            }

            public float LastX { get; private set; }

            public int LastMaxResults { get; private set; }

            public int Count => _items.Length;

            public int QuerySphere(
                float centerX,
                float centerY,
                float centerZ,
                float radius,
                int[] results,
                int maxResults,
                uint layerMask)
            {
                return QueryNearest(centerX, centerY, centerZ, radius, maxResults, layerMask, results, new float[results.Length]);
            }

            public int QueryBox(
                float minX,
                float minY,
                float minZ,
                float maxX,
                float maxY,
                float maxZ,
                int[] results,
                int maxResults,
                uint layerMask)
            {
                return 0;
            }

            public int QueryNearest(
                float centerX,
                float centerY,
                float centerZ,
                float radius,
                int maxResults,
                uint layerMask,
                int[] results,
                float[] squaredDistances)
            {
                LastX = centerX;
                LastMaxResults = maxResults;

                int count = Math.Min(_items.Length, Math.Min(maxResults, results.Length));
                for (int i = 0; i < count; i++)
                {
                    results[i] = _items[i];
                    squaredDistances[i] = _distances[i];
                }

                return count;
            }
        }
    }
}
