using System;
using System.Collections;
using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Differential and cost verification for <see cref="InputActionBuffer"/>. The oracle is a list implementation of
    /// the same contract: append in tick order, prune expired heads, evict the oldest entry when full, deliver the
    /// newest unconsumed edge inside the window.
    /// </summary>
    public sealed class InputActionBufferFuzzTests
    {
        private const string File = "buffer-verify.csv";
        private const string Header = "runId,round,seed,capacity,retention,steps,metric,value,unit";

        private static readonly int SeedBase = ReadInt("CGIS_BUFFER_SEED", 410011);
        private static readonly int Rounds = ReadInt("CGIS_BUFFER_ROUNDS", 4);
        private static readonly int StepScale = ReadInt("CGIS_BUFFER_STEPS", 1);
        private static readonly int SoakSteps = ReadInt("CGIS_BUFFER_SOAK", 60000);

        private static readonly int[] Capacities = { 4, 8, 64 };
        private static readonly int[] Retentions = { 16, 64, 600 };

        private static int ReadInt(string name, int fallback)
        {
            string configured = Environment.GetEnvironmentVariable(name);
            return int.TryParse(configured, out int value) && value > 0 ? value : fallback;
        }

        [Test]
        public void Differential_MatchesTheReferenceModelAcrossSeedsAndCapacities()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            long totalSteps = 0;
            long totalViolations = 0;
            long totalRecords = 0;
            long totalConsumes = 0;

            for (int round = 0; round < Rounds; round++)
            {
                int seed = SeedBase + round;
                int capacity = Capacities[round % Capacities.Length];
                int retention = Retentions[round % Retentions.Length];
                int steps = (2000 << Math.Min(4, round)) * StepScale;

                var random = new System.Random(seed);
                var buffer = new InputActionBuffer(capacity, retention);
                var model = new ReferenceBuffer(capacity, retention);
                int violations = 0;
                long records = 0;
                long consumes = 0;

                for (int step = 0; step < steps; step++)
                {
                    int roll = random.Next(100);
                    if (roll < 55)
                    {
                        int actionId = 1 + random.Next(6);
                        bool actual = buffer.Record(actionId, model.Tick);
                        bool expected = model.Record(actionId, model.Tick);
                        if (actual != expected) violations++;
                        records++;
                    }
                    else if (roll < 80)
                    {
                        int actionId = 1 + random.Next(6);
                        int window = random.Next(0, 9);
                        bool actual = buffer.TryConsume(actionId, model.Tick, window);
                        bool expected = model.TryConsume(actionId, model.Tick, window);
                        if (actual != expected) violations++;
                        consumes++;
                    }
                    else if (roll < 92)
                    {
                        int actionId = 1 + random.Next(6);
                        int window = random.Next(0, 9);
                        bool actual = buffer.TryPeek(actionId, model.Tick, window);
                        bool expected = model.TryPeek(actionId, model.Tick, window);
                        if (actual != expected) violations++;
                        consumes++;
                    }
                    else if (roll < 97)
                    {
                        int actionId = 1 + random.Next(6);
                        int actual = buffer.Clear(actionId);
                        int expected = model.Clear(actionId);
                        if (actual != expected) violations++;
                    }
                    else
                    {
                        buffer.Clear();
                        model.Clear();
                    }

                    if (buffer.Count != model.Count ||
                        buffer.DroppedCount != model.Dropped ||
                        buffer.RecordedCount != model.Recorded)
                    {
                        violations++;
                    }

                    model.Advance(random.Next(0, 3) == 0 ? 0 : 1);
                }

                totalSteps += steps;
                totalViolations += violations;
                totalRecords += records;
                totalConsumes += consumes;

                PerfOutput.Row(File, Header, new[]
                {
                    PerfOutput.RunId, round.ToString(), seed.ToString(), capacity.ToString(), retention.ToString(),
                    steps.ToString(), "fuzzSteps", steps.ToString(), "count"
                });
                PerfOutput.Row(File, Header, new[]
                {
                    PerfOutput.RunId, round.ToString(), seed.ToString(), capacity.ToString(), retention.ToString(),
                    steps.ToString(), "fuzzViolations", violations.ToString(), "count"
                });
                PerfOutput.Row(File, Header, new[]
                {
                    PerfOutput.RunId, round.ToString(), seed.ToString(), capacity.ToString(), retention.ToString(),
                    steps.ToString(), "fuzzRecords", records.ToString(), "count"
                });
                PerfOutput.Row(File, Header, new[]
                {
                    PerfOutput.RunId, round.ToString(), seed.ToString(), capacity.ToString(), retention.ToString(),
                    steps.ToString(), "fuzzConsumes", consumes.ToString(), "count"
                });
            }

            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "all", SeedBase.ToString(), "-", "-", totalSteps.ToString(),
                "totalViolations", totalViolations.ToString(), "count"
            });
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "all", SeedBase.ToString(), "-", "-", totalSteps.ToString(),
                "totalRecords", totalRecords.ToString(), "count"
            });
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "all", SeedBase.ToString(), "-", "-", totalSteps.ToString(),
                "totalConsumes", totalConsumes.ToString(), "count"
            });

            Assert.That(totalViolations, Is.Zero);
        }

        [Test]
        public void WindowSemantics_SurviveAContinuousProducerConsumerMix()
        {
            var buffer = new InputActionBuffer(16, 600);
            var model = new ReferenceBuffer(16, 600);
            var random = new System.Random(SeedBase ^ 0x5F5F);

            for (int step = 0; step < 4000 * StepScale; step++)
            {
                int actionId = 1 + random.Next(3);
                if (random.Next(3) == 0)
                {
                    buffer.Record(actionId, model.Tick);
                    model.Record(actionId, model.Tick);
                }
                else
                {
                    int window = random.Next(1, 7);
                    Assert.That(buffer.TryConsume(actionId, model.Tick, window),
                        Is.EqualTo(model.TryConsume(actionId, model.Tick, window)),
                        "step " + step);
                }

                model.Advance(1);
            }
        }

        [Test]
        public void Soak_KeepsSteadyThroughputAndBoundedMemory()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            const int BlockSteps = 2000;
            var random = new System.Random(SeedBase ^ 0x1234);
            var buffer = new InputActionBuffer(InputActionBuffer.DefaultCapacity, InputActionBuffer.DefaultRetentionTicks);
            var blockMeans = new List<double>(SoakSteps / BlockSteps);
            long tick = 0;
            int consumed = 0;
            int blockOps = 0;

            for (int warmup = 0; warmup < 4000; warmup++)
            {
                int warmAction = 1 + (warmup & 7);
                if ((warmup & 3) == 0) buffer.Record(warmAction, tick);
                buffer.TryConsume(warmAction, tick, 6);
                tick++;
            }

            PerfMeter.ForceFullCollection();
            long heapBefore = GC.GetTotalMemory(true);
            int collectionsBefore = PerfMeter.TotalCollections();
            long blockStart = System.Diagnostics.Stopwatch.GetTimestamp();

            for (int step = 0; step < SoakSteps; step++)
            {
                int actionId = 1 + random.Next(8);
                if ((step & 3) == 0) buffer.Record(actionId, tick);
                if (buffer.TryConsume(actionId, tick, 6)) consumed++;
                tick++;
                blockOps += 2;

                if ((step + 1) % BlockSteps == 0)
                {
                    long now = System.Diagnostics.Stopwatch.GetTimestamp();
                    double microseconds = (now - blockStart) * PerfMeter.TicksToMicroseconds / blockOps;
                    blockMeans.Add(microseconds);
                    blockStart = now;
                    blockOps = 0;
                }
            }

            PerfMeter.ForceFullCollection();
            long heapAfter = GC.GetTotalMemory(true);
            int collectionsAfter = PerfMeter.TotalCollections();
            double drift = MedianDriftPercent(blockMeans);
            double p50 = Median(blockMeans);

            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "soak", (SeedBase ^ 0x1234).ToString(), InputActionBuffer.DefaultCapacity.ToString(),
                InputActionBuffer.DefaultRetentionTicks.ToString(), SoakSteps.ToString(),
                "soakOpsPerBlockMicroseconds", PerfOutput.Num(p50), "us"
            });
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "soak", (SeedBase ^ 0x1234).ToString(), InputActionBuffer.DefaultCapacity.ToString(),
                InputActionBuffer.DefaultRetentionTicks.ToString(), SoakSteps.ToString(),
                "soakDriftPercent", PerfOutput.Num(drift), "percent"
            });
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "soak", (SeedBase ^ 0x1234).ToString(), InputActionBuffer.DefaultCapacity.ToString(),
                InputActionBuffer.DefaultRetentionTicks.ToString(), SoakSteps.ToString(),
                "liveDelta", PerfOutput.Num(heapAfter - heapBefore), "byte"
            });
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "soak", (SeedBase ^ 0x1234).ToString(), InputActionBuffer.DefaultCapacity.ToString(),
                InputActionBuffer.DefaultRetentionTicks.ToString(), SoakSteps.ToString(),
                "collections", PerfOutput.Num(collectionsAfter - collectionsBefore), "count"
            });
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "soak", (SeedBase ^ 0x1234).ToString(), InputActionBuffer.DefaultCapacity.ToString(),
                InputActionBuffer.DefaultRetentionTicks.ToString(), SoakSteps.ToString(),
                "consumedEdges", PerfOutput.Num(consumed), "count"
            });

            Assert.That(drift, Is.LessThan(50d), "steady-state drift budget");
            Assert.That(Math.Abs(heapAfter - heapBefore), Is.LessThan(512 * 1024), "retained heap must stay flat");
        }

        [Test]
        public void CostMatrix_ReflectsCapacityAndPendingDensity()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            int[] capacities = { 8, 64, 512 };
            int[] densities = { 1, 8, 32 };

            for (int c = 0; c < capacities.Length; c++)
            {
                for (int d = 0; d < densities.Length; d++)
                {
                    int capacity = capacities[c];
                    int density = Math.Min(densities[d], capacity);
                    var buffer = new InputActionBuffer(capacity, 600);
                    long tick = 0;

                    for (int i = 0; i < density; i++)
                    {
                        buffer.Record(1000 + i, tick);
                    }

                    int cursor = 0;
                    double[] recordSamples = PerfMeter.SamplePerCallMicroseconds(24, 512, () =>
                    {
                        buffer.Record(2000 + (cursor & 7), tick);
                        cursor++;
                    });

                    buffer.Record(1000, tick);
                    double[] hitSamples = PerfMeter.SamplePerCallMicroseconds(24, 512, () =>
                    {
                        buffer.TryPeek(1000, tick, 600);
                    });

                    double[] missSamples = PerfMeter.SamplePerCallMicroseconds(24, 512, () =>
                    {
                        buffer.TryConsume(9999, tick, 600);
                    });

                    TimingStats recordStats = PerfMeter.Summarize(recordSamples);
                    TimingStats hitStats = PerfMeter.Summarize(hitSamples);
                    TimingStats missStats = PerfMeter.Summarize(missSamples);
                    string label = "C" + capacity + "D" + densities[d];

                    WriteCost(label + ".record", recordStats);
                    WriteCost(label + ".hit", hitStats);
                    WriteCost(label + ".miss", missStats);
                    tick++;
                }
            }
        }

        [UnityTest]
        public IEnumerator Allocation_RecordAndConsumeDoNotAllocate()
        {
            bool started = AllocationInstrument.TryStartFrameRecorder();
            Assert.That(started, Is.True);

            try
            {
                var buffer = new InputActionBuffer(InputActionBuffer.DefaultCapacity, InputActionBuffer.DefaultRetentionTicks);
                long tick = 0;
                const int Frames = 16;
                const int PerFrame = 2048;
                long operations = (long)Frames * PerFrame;

                for (int frame = 0; frame < 4; frame++)
                {
                    for (int op = 0; op < PerFrame; op++)
                    {
                        buffer.Record(1000 + (op & 7), tick);
                        buffer.TryConsume(1000 + (op & 7), tick, 6);
                    }

                    yield return null;
                }

                long idleStart = AllocationInstrument.FrameRecorderSampleSum();
                for (int frame = 0; frame < Frames; frame++) yield return null;
                long idle = AllocationInstrument.FrameRecorderSampleSum() - idleStart;

                long workStart = AllocationInstrument.FrameRecorderSampleSum();
                for (int frame = 0; frame < Frames; frame++)
                {
                    for (int op = 0; op < PerFrame; op++)
                    {
                        buffer.Record(1000 + (op & 7), tick);
                        buffer.TryConsume(1000 + (op & 7), tick, 6);
                    }

                    tick++;
                    yield return null;
                }

                long work = AllocationInstrument.FrameRecorderSampleSum() - workStart;
                double bytesPerOperation = Math.Max(0d, (work - idle) / (double)operations);

                PerfOutput.WriteEnvironmentHeader(File);
                PerfOutput.Row(File, Header, new[]
                {
                    PerfOutput.RunId, "alloc", "-", InputActionBuffer.DefaultCapacity.ToString(),
                    InputActionBuffer.DefaultRetentionTicks.ToString(), operations.ToString(),
                    "allocRecordConsumeBytesPerOperation", PerfOutput.Num(bytesPerOperation), "byte"
                });

                Assert.That(bytesPerOperation, Is.LessThan(1d), "record and consume must not allocate");
            }
            finally
            {
                AllocationInstrument.StopFrameRecorder();
            }
        }

        private static void WriteCost(string metric, TimingStats stats)
        {
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "cost", "-", metric, "-", stats.Count.ToString(),
                metric + ".p50Us", PerfOutput.Num(stats.MedianUs), "us"
            });
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "cost", "-", metric, "-", stats.Count.ToString(),
                metric + ".p95Us", PerfOutput.Num(stats.P95Us), "us"
            });
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "cost", "-", metric, "-", stats.Count.ToString(),
                metric + ".p99Us", PerfOutput.Num(stats.P99Us), "us"
            });
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "cost", "-", metric, "-", stats.Count.ToString(),
                metric + ".maxUs", PerfOutput.Num(stats.MaxUs), "us"
            });
        }

        private static double Median(List<double> values)
        {
            if (values.Count == 0) return 0d;
            var copy = new double[values.Count];
            values.CopyTo(copy);
            Array.Sort(copy);
            return copy[copy.Length / 2];
        }

        private static double MedianDriftPercent(List<double> blockMeans)
        {
            int quarter = Math.Max(1, blockMeans.Count / 4);
            var head = new double[quarter];
            var tail = new double[quarter];
            for (int i = 0; i < quarter; i++)
            {
                head[i] = blockMeans[i];
                tail[i] = blockMeans[blockMeans.Count - quarter + i];
            }

            Array.Sort(head);
            Array.Sort(tail);
            double first = head[quarter / 2];
            double last = tail[quarter / 2];
            return first <= 0d ? 0d : (last - first) / first * 100d;
        }

        /// <summary>Reference implementation. List storage and linear scans, independent of the ring arithmetic.</summary>
        private sealed class ReferenceBuffer
        {
            private struct Node
            {
                public int ActionId;
                public long Stamp;
                public bool Consumed;
            }

            private readonly List<Node> _nodes = new List<Node>();
            private readonly int _capacity;
            private readonly int _retention;

            public long Tick { get; private set; }
            public long Recorded { get; private set; }
            public long Dropped { get; private set; }
            public int Count => _nodes.Count;

            public ReferenceBuffer(int capacity, int retention)
            {
                _capacity = capacity;
                _retention = retention;
            }

            public void Advance(int steps)
            {
                Tick += steps;
            }

            public bool Record(int actionId, long tick)
            {
                if (actionId == 0) return false;

                for (int i = 0; i < _nodes.Count;)
                {
                    if (tick - _nodes[i].Stamp > _retention) _nodes.RemoveAt(i);
                    else i++;
                }

                if (_nodes.Count == _capacity)
                {
                    _nodes.RemoveAt(0);
                    Dropped++;
                }

                _nodes.Add(new Node { ActionId = actionId, Stamp = tick, Consumed = false });
                Recorded++;
                return true;
            }

            public bool TryConsume(int actionId, long tick, int window)
            {
                return TryTake(actionId, tick, window, true);
            }

            public bool TryPeek(int actionId, long tick, int window)
            {
                return TryTake(actionId, tick, window, false);
            }

            public int Clear(int actionId)
            {
                int cleared = 0;
                for (int i = 0; i < _nodes.Count; i++)
                {
                    Node node = _nodes[i];
                    if (node.ActionId != actionId || node.Consumed) continue;
                    node.Consumed = true;
                    _nodes[i] = node;
                    cleared++;
                }

                return cleared;
            }

            public void Clear()
            {
                _nodes.Clear();
            }

            private bool TryTake(int actionId, long tick, int window, bool consume)
            {
                if (actionId == 0 || window < 0) return false;

                for (int i = _nodes.Count - 1; i >= 0; i--)
                {
                    Node node = _nodes[i];
                    if (tick - node.Stamp > window) return false;
                    if (node.ActionId != actionId || node.Consumed) continue;
                    if (consume)
                    {
                        node.Consumed = true;
                        _nodes[i] = node;
                    }

                    return true;
                }

                return false;
            }
        }
    }
}
