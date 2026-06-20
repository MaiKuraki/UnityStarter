using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

using CycloneGames.AIPerception.Runtime;

namespace CycloneGames.AIPerception.Tests.Editor
{
    public sealed class AIPerceptionCoreTests
    {
        [TearDown]
        public void TearDown()
        {
            PerceptibleRegistry.ResetInstance();
        }

        [Test]
        public void PerceptibleHandle_Equality_UsesIdAndGeneration()
        {
            var first = new PerceptibleHandle(7, 3);
            var same = new PerceptibleHandle(7, 3);
            var differentGeneration = new PerceptibleHandle(7, 4);
            var differentId = new PerceptibleHandle(8, 3);

            Assert.That(first, Is.EqualTo(same));
            Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
            Assert.That(first, Is.Not.EqualTo(differentGeneration));
            Assert.That(first, Is.Not.EqualTo(differentId));
            Assert.That(PerceptibleHandle.Invalid.IsValid, Is.False);
        }

        [Test]
        public void PerceptibleData_IsDetectable_TogglesOnlyDetectableFlag()
        {
            var data = new PerceptibleData
            {
                Flags = 0b_1010
            };

            data.IsDetectable = true;

            Assert.That(data.Flags, Is.EqualTo((byte)0b_1011));

            data.IsDetectable = false;

            Assert.That(data.Flags, Is.EqualTo((byte)0b_1010));
        }

        [Test]
        public void SpatialGrid_CreateFilteredCopy_ReturnsOnlyTargetsInsideRange()
        {
            var source = new[]
            {
                CreateData(id: 1, position: new float3(0f, 0f, 0f)),
                CreateData(id: 2, position: new float3(5f, 0f, 0f)),
                CreateData(id: 3, position: new float3(30f, 0f, 0f))
            };
            var grid = new SpatialGrid(10f);
            grid.Rebuild(source, source.Length);

            using (NativeArray<PerceptibleData> filtered = grid.CreateFilteredCopy(
                source,
                source.Length,
                new float3(0f, 0f, 0f),
                6f,
                Allocator.Temp))
            {
                Assert.That(filtered.Length, Is.EqualTo(2));
                Assert.That(ContainsId(filtered, 1), Is.True);
                Assert.That(ContainsId(filtered, 2), Is.True);
                Assert.That(ContainsId(filtered, 3), Is.False);
            }

            grid.Clear();
        }

        [Test]
        public void PerceptibleRegistry_ReusedSlot_InvalidatesOldHandle()
        {
            using var registry = new PerceptibleRegistry();
            var first = new TestPerceptible(perceptibleId: 10, typeId: PerceptibleTypes.Player);
            var second = new TestPerceptible(perceptibleId: 11, typeId: PerceptibleTypes.Enemy);

            PerceptibleHandle firstHandle = registry.Register(first);
            registry.Unregister(firstHandle);
            PerceptibleHandle secondHandle = registry.Register(second);

            Assert.That(secondHandle.Id, Is.EqualTo(firstHandle.Id));
            Assert.That(secondHandle.Generation, Is.Not.EqualTo(firstHandle.Generation));
            Assert.That(registry.IsValid(firstHandle), Is.False);
            Assert.That(registry.IsValid(secondHandle), Is.True);
            Assert.That(registry.Get(secondHandle), Is.SameAs(second));
        }

        [Test]
        public void PerceptibleRegistry_RebuildData_ExportsOnlyDetectablePerceptibles()
        {
            using var registry = new PerceptibleRegistry();
            registry.Register(new TestPerceptible(perceptibleId: 10, typeId: PerceptibleTypes.Player, isDetectable: true));
            registry.Register(new TestPerceptible(perceptibleId: 11, typeId: PerceptibleTypes.Enemy, isDetectable: false));

            registry.RebuildData();

            Assert.That(registry.GetDataCount(), Is.EqualTo(1));
            using (NativeArray<PerceptibleData> data = registry.CreateNativeDataCopy(Allocator.Temp))
            {
                Assert.That(data.Length, Is.EqualTo(1));
                Assert.That(data[0].TypeId, Is.EqualTo(PerceptibleTypes.Player));
                Assert.That(data[0].IsDetectable, Is.True);
            }
        }

        private static PerceptibleData CreateData(int id, float3 position)
        {
            return new PerceptibleData
            {
                Id = id,
                Generation = 1,
                TypeId = PerceptibleTypes.Default,
                Flags = 1,
                DetectionRadius = 1f,
                Loudness = 1f,
                Position = position,
                LOSPoint = position
            };
        }

        private static bool ContainsId(NativeArray<PerceptibleData> data, int id)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i].Id == id)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class TestPerceptible : IPerceptible
        {
            public TestPerceptible(int perceptibleId, int typeId, bool isDetectable = true)
            {
                PerceptibleId = perceptibleId;
                PerceptibleTypeId = typeId;
                IsDetectable = isDetectable;
                Position = new float3(perceptibleId, 1f, 2f);
                DetectionRadius = 4f;
                Loudness = 0.5f;
                Tag = "Test";
            }

            public int PerceptibleId { get; }
            public int PerceptibleTypeId { get; }
            public bool IsDetectable { get; }
            public float3 Position { get; }
            public float DetectionRadius { get; }
            public float Loudness { get; }
            public string Tag { get; }

            public float3 GetLOSPoint()
            {
                return Position + new float3(0f, 1f, 0f);
            }
        }
    }
}

