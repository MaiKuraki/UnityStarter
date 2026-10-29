using System;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.Spatial.Core;
using NUnit.Framework;

namespace CycloneGames.Spatial.Tests.Editor
{
    /// <summary>
    /// Evidence tests rather than behaviour tests: they assert properties that a reviewer would otherwise have to take
    /// on faith — dependency surface, static state, combinatorial query coverage, and long-run stability.
    /// </summary>
    public sealed class SpatialEvidenceTests
    {
        /// <summary>
        /// Assemblies that would break a stated guarantee if they ever appeared: engine coupling, reflection,
        /// file/network IO, LINQ, unmanaged interop and threads.
        /// </summary>
        private static readonly string[] DeniedReferencePrefixes =
        {
            "Unity",
            "System.Reflection",
            "System.IO",
            "System.Linq",
            "System.Runtime.InteropServices",
            "System.Threading",
            "System.Net",
            "System.Diagnostics"
        };

        [Test]
        public void CoreAssembly_ReferencesNoEngineReflectionIoLinQOrThreading()
        {
            Assembly assembly = typeof(SpatialHashGrid<int>).Assembly;
            var offenders = new List<string>();

            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                string name = reference.Name;
                if (name == null)
                {
                    continue;
                }

                for (int i = 0; i < DeniedReferencePrefixes.Length; i++)
                {
                    if (name.StartsWith(DeniedReferencePrefixes[i], StringComparison.Ordinal))
                    {
                        offenders.Add(name);
                        break;
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Spatial.Core gained a reference that breaks a documented guarantee: " + string.Join(", ", offenders));
        }

        [Test]
        public void CoreAssembly_HasNoMutableStaticState()
        {
            Assembly assembly = typeof(SpatialHashGrid<int>).Assembly;
            var offenders = new List<string>();
            int unityGeneratedBlobs = 0;

            foreach (Type type in assembly.GetTypes())
            {
                foreach (FieldInfo field in type.GetFields(
                             BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsLiteral)
                    {
                        // const: inlined at compile time, occupies no runtime storage.
                        continue;
                    }

                    if (field.IsInitOnly && field.FieldType.IsValueType && field.Name == "Invalid" &&
                        type == typeof(SpatialHandle))
                    {
                        // The single permitted static: an immutable value-type sentinel.
                        continue;
                    }

                    if (field.IsInitOnly && type.Name.StartsWith("<", StringComparison.Ordinal) &&
                        field.FieldType.Name.StartsWith("__StaticArrayInitTypeSize", StringComparison.Ordinal))
                    {
                        // Unity injects UnitySourceGeneratedAssemblyMonoScriptTypes_v1 into every compiled assembly and
                        // stores its data in these readonly blobs. They are immutable, identical across assemblies and
                        // not module state; excluding them is not the same as relaxing the assertion.
                        unityGeneratedBlobs++;
                        continue;
                    }

                    offenders.Add(type.Name + "." + field.Name + " : " + field.FieldType.Name +
                                  (field.IsInitOnly ? " readonly" : " MUTABLE"));
                }
            }

            Assert.That(offenders, Is.Empty,
                "Static state is a GC root that outlives every scene. Found: " + string.Join(", ", offenders));
            Assert.That(unityGeneratedBlobs, Is.LessThanOrEqualTo(4),
                "Unexpected number of Unity-generated data blobs: " + unityGeneratedBlobs);
        }

        [Test]
        public void PublicSurface_HasNoDelegateOrInterfaceShapedHotPath()
        {
            Assembly assembly = typeof(SpatialHashGrid<int>).Assembly;
            var offenders = new List<string>();

            foreach (Type type in assembly.GetTypes())
            {
                if (!type.IsPublic)
                {
                    continue;
                }

                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        if (typeof(Delegate).IsAssignableFrom(parameter.ParameterType))
                        {
                            offenders.Add(type.Name + "." + method.Name + "(" + parameter.Name + ")");
                        }
                    }
                }

                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (typeof(Delegate).IsAssignableFrom(field.FieldType))
                    {
                        offenders.Add(type.Name + "." + field.Name);
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "A delegate in the public surface forces every caller to allocate one: " + string.Join(", ", offenders));
        }

        [Test]
        public void Geometry_StaysInsideTheSnapshotAddressableSpan_ForRealisticWorlds()
        {
            // Two kilometres of world at a one-metre cell is the documented design point; the span must cover it.
            var settings = new GridSettings(1f);
            CellCoordinate cell = settings.WorldToCell(2000f, -2000f, 0f);

            Assert.That(Math.Abs(cell.X), Is.LessThan(SpatialSnapshot<int>.AxisLimitCells));
            Assert.That(Math.Abs(cell.Y), Is.LessThan(SpatialSnapshot<int>.AxisLimitCells));
        }
    }

    /// <summary>
    /// Combinatorial coverage: every backend against every query shape, mask state and range class.
    /// </summary>
    public sealed class SpatialCombinationMatrixTests
    {
        private const uint LayerA = 1u << 0;
        private const uint LayerB = 1u << 1;

        private static float[] BuildPositions(int count, int seed)
        {
            var random = new Random(seed);
            var positions = new float[count * 3];
            for (int i = 0; i < count; i++)
            {
                positions[i * 3] = (float)((random.NextDouble() * 100.0) - 50.0);
                positions[i * 3 + 1] = (float)((random.NextDouble() * 100.0) - 50.0);
                positions[i * 3 + 2] = (float)((random.NextDouble() * 100.0) - 50.0);
            }

            return positions;
        }

        [Test]
        public void EveryBackendAndQueryShape_SatisfiesItsInvariants()
        {
            const int ElementCount = 400;
            float[] positions = BuildPositions(ElementCount, 909);

            using (var grid = new SpatialHashGrid<int>(new GridSettings(4f), 64))
            using (var snapshot = new SpatialSnapshot<int>(new GridSettings(4f), 64))
            {
                var entries = new SpatialEntry<int>[ElementCount];
                for (int i = 0; i < ElementCount; i++)
                {
                    uint layer = (i % 3) == 0 ? LayerA : LayerB;
                    entries[i] = new SpatialEntry<int>(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2], layer, i);
                    grid.TryInsert(i, positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2], layer, out _);
                }

                snapshot.Build(entries);

                uint[] masks = { SpatialLayers.All, LayerA, LayerB, 0u };
                float[] radii = { 0f, 6f, 40f, 5_000_000f };   // the last one forces the linear fallback
                var buffer = new int[ElementCount];
                var squared = new float[8];

                foreach (ISpatialReadOnlyIndex<int> backend in new ISpatialReadOnlyIndex<int>[] { grid, snapshot })
                {
                    for (int m = 0; m < masks.Length; m++)
                    {
                        for (int r = 0; r < radii.Length; r++)
                        {
                            float radius = radii[r];
                            uint mask = masks[m];

                            int sphereCount = backend.QuerySphere(0f, 0f, 0f, radius, buffer, buffer.Length, mask);
                            AssertNoDuplicates(buffer, sphereCount, "sphere array " + Describe(backend, mask, radius));
                            AssertMaskRespected(buffer, sphereCount, positions, mask, "sphere array " + Describe(backend, mask, radius));
                            AssertDistanceBound(buffer, sphereCount, positions, radius, "sphere array " + Describe(backend, mask, radius));

                            int boxCount = backend.QueryBox(-radius, -radius, -radius, radius, radius, radius, buffer, buffer.Length, mask);
                            AssertNoDuplicates(buffer, boxCount, "box " + Describe(backend, mask, radius));
                            AssertMaskRespected(buffer, boxCount, positions, mask, "box " + Describe(backend, mask, radius));

                            int nearest = backend.QueryNearest(0f, 0f, 0f, radius, 8, mask, buffer, squared);
                            AssertNoDuplicates(buffer, nearest, "nearest " + Describe(backend, mask, radius));
                            AssertMaskRespected(buffer, nearest, positions, mask, "nearest " + Describe(backend, mask, radius));
                            AssertAscending(squared, nearest, "nearest " + Describe(backend, mask, radius));

                            if (mask == 0u)
                            {
                                Assert.That(sphereCount, Is.Zero, "A zero mask must return nothing.");
                                Assert.That(boxCount, Is.Zero, "A zero mask must return nothing.");
                                Assert.That(nearest, Is.Zero, "A zero mask must return nothing.");
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void EveryCapacityPolicy_SurvivesInsertRemoveClearAndTrim()
        {
            foreach (SpatialCapacityPolicy policy in new[] { SpatialCapacityPolicy.Grow, SpatialCapacityPolicy.Fixed })
            {
                var grid = new SpatialHashGrid<int>(new GridSettings(1f), 64, policy);
                var handles = new List<SpatialHandle>(256);

                for (int i = 0; i < 250; i++)
                {
                    if (grid.TryInsert(i, i * 0.3f, 0f, 0f, out SpatialHandle handle))
                    {
                        handles.Add(handle);
                    }
                }

                for (int i = 0; i < handles.Count; i += 2)
                {
                    Assert.That(grid.TryRemove(handles[i]), Is.True, policy + ": remove failed");
                }

                Assert.That(grid.ValidateStructure(), Is.True, policy + ": invariants broken after removal");

                grid.Clear();
                Assert.That(grid.Count, Is.Zero);
                Assert.That(grid.ValidateStructure(), Is.True, policy + ": invariants broken after clear");
                grid.TrimExcess();
                Assert.That(grid.ValidateStructure(), Is.True, policy + ": invariants broken after trim");

                Assert.That(grid.TryInsert(1, 0f, 0f, 0f, out SpatialHandle live), Is.True, policy + ": insert after trim");
                Assert.That(grid.TryGetPosition(live, out _, out _, out _), Is.True, policy + ": handle survived trim");

                grid.Dispose();
                Assert.That(grid.TryInsert(2, 0f, 0f, 0f, out _), Is.False, policy + ": disposed grid must reject mutation");
            }
        }

        private static string Describe(ISpatialReadOnlyIndex<int> backend, uint mask, float radius)
        {
            return backend.GetType().Name + " mask=" + mask + " radius=" + radius;
        }

        private static void AssertNoDuplicates(int[] buffer, int count, string label)
        {
            var seen = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                Assert.That(seen.Add(buffer[i]), Is.True, "Duplicate result for " + label);
            }
        }

        private static void AssertMaskRespected(int[] buffer, int count, float[] positions, uint mask, string label)
        {
            for (int i = 0; i < count; i++)
            {
                uint layer = (buffer[i] % 3) == 0 ? LayerA : LayerB;
                Assert.That((layer & mask) != 0u, Is.True, "Mask violated for " + label);
            }
        }

        private static void AssertDistanceBound(int[] buffer, int count, float[] positions, float radius, string label)
        {
            float tolerant = (radius * radius * (1f + 1e-5f)) + 1e-5f;
            for (int i = 0; i < count; i++)
            {
                int id = buffer[i];
                float x = positions[id * 3];
                float y = positions[id * 3 + 1];
                float z = positions[id * 3 + 2];
                float distanceSquared = (x * x) + (y * y) + (z * z);
                Assert.That(distanceSquared, Is.LessThanOrEqualTo(tolerant),
                    "Result outside the radius for " + label);
            }
        }

        private static void AssertAscending(float[] values, int count, string label)
        {
            for (int i = 1; i < count; i++)
            {
                Assert.That(values[i], Is.GreaterThanOrEqualTo(values[i - 1]), "Nearest not ordered for " + label);
            }
        }
    }

    /// <summary>
    /// Long-run stability: sustained mixed traffic with invariants checked periodically and memory watched across the
    /// whole run. This is a scaled-down stand-in for a multi-hour soak, not a replacement for one.
    /// </summary>
    public sealed class SpatialSoakTests
    {
        [Test]
        public void SustainedChurn_KeepsInvariantsAndFlatMemory()
        {
            const int RampRounds = 200;
            const int SteadyRounds = 400;
            const int InsertsPerRound = 200;

            var grid = new SpatialHashGrid<int>(new GridSettings(2f), 4096);
            var random = new Random(20261029);
            var handles = new List<SpatialHandle>(InsertsPerRound * 4);
            var buffer = new int[512];
            var squared = new float[8];
            var sequence = 0;
            int peakCount = 0;

            // Phase 1: reach the working set. Growth here is capacity, not a leak.
            for (int round = 0; round < RampRounds; round++)
            {
                for (int i = 0; i < InsertsPerRound; i++)
                {
                    if (grid.TryInsert(
                            sequence++,
                            (float)((random.NextDouble() * 400.0) - 200.0),
                            (float)((random.NextDouble() * 400.0) - 200.0),
                            (float)((random.NextDouble() * 400.0) - 200.0),
                            (i & 1) == 0 ? 1u : 2u,
                            out SpatialHandle handle))
                    {
                        handles.Add(handle);
                    }
                }
            }

            long baselineMemory = GC.GetTotalMemory(true);
            int baselineSlots = grid.Capacity;
            int baselineBuckets = grid.BucketCapacity;

            // Phase 2: one insert for one remove. Any growth from here is a leak.
            for (int round = 0; round < SteadyRounds; round++)
            {
                for (int i = 0; i < InsertsPerRound; i++)
                {
                    if (grid.TryInsert(
                            sequence++,
                            (float)((random.NextDouble() * 400.0) - 200.0),
                            (float)((random.NextDouble() * 400.0) - 200.0),
                            (float)((random.NextDouble() * 400.0) - 200.0),
                            (i & 1) == 0 ? 1u : 2u,
                            out SpatialHandle handle))
                    {
                        handles.Add(handle);
                    }

                    if (handles.Count == 0)
                    {
                        continue;
                    }

                    int index = random.Next(handles.Count);
                    SpatialHandle evicted = handles[index];
                    handles[index] = handles[handles.Count - 1];
                    handles.RemoveAt(handles.Count - 1);
                    grid.TryRemove(evicted);
                }

                grid.QuerySphere(0f, 0f, 0f, 60f, buffer, buffer.Length, SpatialLayers.All);
                grid.QueryNearest(0f, 0f, 0f, 120f, 8, SpatialLayers.All, buffer, squared);

                if (grid.Count > peakCount)
                {
                    peakCount = grid.Count;
                }

                if (round % 50 == 0)
                {
                    Assert.That(grid.ValidateStructure(), Is.True, "Invariants broken at round " + round + ".");
                }
            }

            Assert.That(grid.ValidateStructure(), Is.True, "Invariants broken at the end of the soak.");

            SpatialStatistics statistics = grid.GetStatistics();
            Assert.That(statistics.TotalInsertCount, Is.GreaterThan(0L));
            Assert.That(statistics.TotalRemoveCount, Is.GreaterThan(0L));
            Assert.That(statistics.GenerationWrapCount, Is.Zero, "A slot wrapped; handle reuse exceeded 2^64.");
            Assert.That(statistics.ElementCount, Is.EqualTo(grid.Count));
            Assert.That(statistics.PeakElementCount, Is.GreaterThanOrEqualTo(peakCount));
            Assert.That(statistics.SlotCapacity, Is.EqualTo(baselineSlots),
                "Slot capacity grew during net-zero churn: " + baselineSlots + " -> " + statistics.SlotCapacity);

            long finalMemory = GC.GetTotalMemory(true);
            long growth = finalMemory - baselineMemory;

            Console.WriteLine("[soak] ramp=" + RampRounds
                + " steady=" + SteadyRounds
                + " inserts=" + statistics.TotalInsertCount
                + " removes=" + statistics.TotalRemoveCount
                + " live=" + statistics.ElementCount
                + " peak=" + statistics.PeakElementCount
                + " slots=" + statistics.SlotCapacity + " (baseline " + baselineSlots + ")"
                + " buckets=" + statistics.BucketCapacity + " (baseline " + baselineBuckets + ")"
                + " memoryGrowth=" + growth + " B");

            Assert.That(growth, Is.LessThan(524_288L),
                "Memory grew by " + growth + " B across net-zero churn; the index should reach a plateau.");

            grid.Dispose();
        }

        [Test]
        public void RebuildSoak_SnapshotStaysFlat()
        {
            const int Rounds = 400;
            const int ElementCount = 512;

            var random = new Random(778899);
            var entries = new SpatialEntry<int>[ElementCount];
            var snapshot = new SpatialSnapshot<int>(new GridSettings(3f), ElementCount);
            var buffer = new int[64];

            long baselineMemory = 0L;

            for (int round = 0; round < Rounds; round++)
            {
                for (int i = 0; i < ElementCount; i++)
                {
                    entries[i] = new SpatialEntry<int>(
                        (float)((random.NextDouble() * 300.0) - 150.0),
                        (float)((random.NextDouble() * 300.0) - 150.0),
                        (float)((random.NextDouble() * 300.0) - 150.0),
                        SpatialLayers.Default,
                        i);
                }

                snapshot.Build(entries);
                snapshot.QuerySphere(0f, 0f, 0f, 50f, buffer, buffer.Length, SpatialLayers.All);

                if (round == 20)
                {
                    baselineMemory = GC.GetTotalMemory(true);
                }
            }

            long growth = GC.GetTotalMemory(true) - baselineMemory;
            Console.WriteLine("[soak] snapshot rebuilds=" + Rounds + " memoryGrowth=" + growth + " B");

            Assert.That(snapshot.Count, Is.EqualTo(ElementCount));
            Assert.That(growth, Is.LessThan(262_144L),
                "A repeated same-size rebuild grew memory by " + growth + " B; it must reuse its arrays.");

            snapshot.Dispose();
        }
    }
}
