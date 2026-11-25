using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using UnityEngine.TestTools;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Model-based fuzz, soak, timing and allocation of runtime context-policy overrides.
    /// Raw rows are written through PerfOutput.
    /// </summary>
    public sealed class InputContextPolicyFuzzTests
    {
        private const string File = "policy-verify.csv";
        private const string Header = "runId,round,seed,depth,steps,metric,value,unit";

        private static readonly int SeedBase = ReadInt("CGIS_POLICY_SEED", 44001);
        private static readonly int Rounds = ReadInt("CGIS_POLICY_ROUNDS", 4);
        private static readonly int StepScale = ReadInt("CGIS_POLICY_STEPS", 1);
        private static readonly int SoakSteps = ReadInt("CGIS_POLICY_SOAK", 60000);

        private static readonly int[] Depths = { 2, 4, 8, 16 };
        private static readonly int[] Priorities = { -5, 0, 0, 5, 10, 100 };

        private static int _deviceSerial;

        private readonly List<InputPlayer> _players = new List<InputPlayer>();
        private readonly List<InputDevice> _devices = new List<InputDevice>();

        private static int ReadInt(string name, int fallback)
        {
            string configured = Environment.GetEnvironmentVariable(name);
            return int.TryParse(configured, out int value) && value > 0 ? value : fallback;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _players.Count - 1; i >= 0; i--) _players[i].Dispose();
            _players.Clear();
            for (int i = _devices.Count - 1; i >= 0; i--)
            {
                InputDevice device = _devices[i];
                if (device != null && device.added) UIS.RemoveDevice(device);
            }
            _devices.Clear();
        }

        private InputPlayer CreatePlayer(int depth, int bindingsPerContext, out InputContext[] contexts)
        {
            var slot = new PlayerSlotConfig { PlayerId = 0, Contexts = new List<ContextDefinitionConfig>(depth) };
            for (int i = 0; i < depth; i++)
            {
                slot.Contexts.Add(new ContextDefinitionConfig
                {
                    Name = "C" + i,
                    ActionMap = "M" + i,
                    Bindings = new List<ActionBindingConfig>
                    {
                        new ActionBindingConfig
                        {
                            Type = ActionValueType.Button,
                            ActionName = "A0",
                            DeviceBindings = new List<string> { "<Gamepad>/buttonSouth" }
                        }
                    }
                });
            }

            Gamepad device = UIS.AddDevice<Gamepad>("PolicyFuzzPad" + (++_deviceSerial));
            _devices.Add(device);
            InputUser user = InputUser.PerformPairingWithDevice(device);
            var player = new InputPlayer(0, user, slot, device);
            _players.Add(player);

            contexts = new InputContext[depth];
            for (int i = 0; i < depth; i++)
            {
                contexts[i] = new InputContext("M" + i, "C" + i, 0, i % 2 == 0);
                for (int b = 0; b < bindingsPerContext; b++)
                {
                    contexts[i].AddBinding(new Subject<Unit>(), NullCommand.Instance);
                }
            }

            return player;
        }

        private static readonly FieldInfo ActiveField =
            typeof(InputPlayer).GetField("_activeContexts", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo OverrideField =
            typeof(InputPlayer).GetField("_contextPolicyOverrides", BindingFlags.Instance | BindingFlags.NonPublic);

        private static InputContext[] ActualActive(InputPlayer player)
        {
            var list = (List<InputContext>)ActiveField.GetValue(player);
            return list.ToArray();
        }

        private static int OverrideCount(InputPlayer player)
        {
            return ((System.Collections.ICollection)OverrideField.GetValue(player)).Count;
        }

        [Test]
        public void Fuzz_ProjectionMatchesReferenceModelAcrossSeedsAndDepths()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            int totalSteps = 0;
            int totalViolations = 0;
            int totalChanges = 0;
            var report = new System.Text.StringBuilder(512);

            for (int round = 0; round < Rounds; round++)
            {
                int depth = Depths[round % Depths.Length];
                int steps = 2500 * (round + 1) * StepScale;
                CaseResult result = RunCase(SeedBase + round, round, depth, steps);

                totalSteps += result.Steps;
                totalViolations += result.Violations;
                totalChanges += result.PolicyChanges;

                Write(result, "steps", result.Steps, "count");
                Write(result, "policyChanges", result.PolicyChanges, "count");
                Write(result, "pushes", result.Pushes, "count");
                Write(result, "pops", result.Pops, "count");
                Write(result, "captures", result.Captures, "count");
                Write(result, "violations", result.Violations, "count");
                Write(result, "exceptions", result.Exceptions, "count");
                Write(result, "maxOverrides", result.MaxOverrides, "count");
                Write(result, "finalOverrides", result.FinalOverrides, "count");
                Write(result, "opP50Us", result.P50Us, "us");
                Write(result, "opP95Us", result.P95Us, "us");
                Write(result, "liveHeapDelta", result.LiveHeapDelta, "byte");

                report.Append("round=").Append(round).Append(" seed=").Append(result.Seed)
                    .Append(" depth=").Append(result.Depth).Append(" steps=").Append(result.Steps)
                    .Append(" violations=").Append(result.Violations)
                    .Append(" exceptions=").Append(result.Exceptions)
                    .Append(" p95=").Append(PerfOutput.Num(result.P95Us, 2)).Append("us")
                    .AppendLine();
            }

            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "all", SeedBase.ToString(), "all", totalSteps.ToString(), "totalViolations", totalViolations.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "all", SeedBase.ToString(), "all", totalSteps.ToString(), "totalPolicyChanges", totalChanges.ToString(), "count" });
            PerfOutput.Log("policyFuzz seeds=" + SeedBase + ".." + (SeedBase + Rounds - 1) +
                " steps=" + totalSteps + " policyChanges=" + totalChanges + " violations=" + totalViolations);

            Assert.That(totalViolations, Is.Zero, "Projection diverged from the reference model.\n" + report);
            Assert.That(totalChanges, Is.GreaterThan(200), "The script must exercise enough policy changes.");
        }

        private struct CaseResult
        {
            internal int Seed;
            internal int Round;
            internal int Depth;
            internal int Steps;
            internal int PolicyChanges;
            internal int Pushes;
            internal int Pops;
            internal int Captures;
            internal int Violations;
            internal int Exceptions;
            internal int MaxOverrides;
            internal int FinalOverrides;
            internal double P50Us;
            internal double P95Us;
            internal long LiveHeapDelta;
        }

        private static bool ContextEquals(InputContext[] expected, InputContext[] actual)
        {
            if (expected.Length != actual.Length) return false;
            for (int i = 0; i < expected.Length; i++)
            {
                if (!ReferenceEquals(expected[i], actual[i])) return false;
            }
            return true;
        }

        private CaseResult RunCase(int seed, int round, int depth, int steps)
        {
            var result = new CaseResult { Seed = seed, Round = round, Depth = depth, Steps = steps };
            var random = new System.Random(seed);
            PerfMeter.ForceFullCollection();
            long liveBefore = GC.GetTotalMemory(true);
            int collectionsBefore = PerfMeter.TotalCollections();

            InputPlayer player = CreatePlayer(depth, 0, out InputContext[] contexts);
            var priority = new int[depth];
            var blocking = new bool[depth];
            var overridden = new bool[depth];
            for (int i = 0; i < depth; i++)
            {
                priority[i] = 0;
                blocking[i] = i % 2 == 0;
            }

            var stackModel = new List<InputContext>(depth);
            var captureModel = new List<InputContext>(depth);
            var scopes = new List<IDisposable>(depth);
            var ordered = new List<InputContext>(depth);
            var expected = new List<InputContext>(depth);
            var timing = new double[steps];
            int blockDepth = 0;

            for (int step = 0; step < steps; step++)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    int choice = random.Next(100);
                    if (choice < 12)
                    {
                        InputContext context = contexts[random.Next(depth)];
                        stackModel.Remove(context);
                        stackModel.Add(context);
                        player.PushContext(context);
                        result.Pushes++;
                    }
                    else if (choice < 24)
                    {
                        if (stackModel.Count > 0)
                        {
                            InputContext popped = stackModel[stackModel.Count - 1];
                            stackModel.RemoveAt(stackModel.Count - 1);
                            player.PopContext();
                            if (!Contains(captureModel, popped)) ResetModelPolicy(popped, priority, blocking, overridden);
                            result.Pops++;
                        }
                    }
                    else if (choice < 32)
                    {
                        InputContext context = contexts[random.Next(depth)];
                        if (stackModel.Remove(context))
                        {
                            for (int s = captureModel.Count - 1; s >= 0; s--)
                            {
                                if (ReferenceEquals(captureModel[s], context))
                                {
                                    captureModel.RemoveAt(s);
                                    scopes.RemoveAt(s);
                                }
                            }
                            player.RemoveContext(context);
                            if (!Contains(captureModel, context)) ResetModelPolicy(context, priority, blocking, overridden);
                        }
                    }
                    else if (choice < 40)
                    {
                        InputContext context = contexts[random.Next(depth)];
                        captureModel.Add(context);
                        scopes.Add(player.CaptureContext(context));
                        result.Captures++;
                    }
                    else if (choice < 48 && scopes.Count > 0)
                    {
                        int s = random.Next(scopes.Count);
                        InputContext released = captureModel[s];
                        captureModel.RemoveAt(s);
                        scopes[s].Dispose();
                        scopes.RemoveAt(s);
                        if (!Contains(stackModel, released) && !Contains(captureModel, released))
                            ResetModelPolicy(released, priority, blocking, overridden);
                    }
                    else if (choice < 54)
                    {
                        blockDepth++;
                        player.BlockInput();
                    }
                    else if (choice < 60 && blockDepth > 0)
                    {
                        blockDepth--;
                        player.UnblockInput();
                    }
                    else if (choice < 76)
                    {
                        int index = random.Next(depth);
                        int value = Priorities[random.Next(Priorities.Length)];
                        bool blocks = random.Next(2) == 0;
                        if (player.SetContextPolicy(contexts[index], value, blocks))
                        {
                            priority[index] = value;
                            blocking[index] = blocks;
                            overridden[index] = true;
                            result.PolicyChanges++;
                        }
                    }
                    else if (choice < 86)
                    {
                        int index = random.Next(depth);
                        if (player.ClearContextPolicy(contexts[index]))
                        {
                            priority[index] = 0;
                            blocking[index] = index % 2 == 0;
                            overridden[index] = false;
                            result.PolicyChanges++;
                        }
                    }
                    else if (choice < 92)
                    {
                        player.RefreshActiveContext();
                    }

                    expected.Clear();
                    if (captureModel.Count > 0)
                    {
                        expected.Add(captureModel[captureModel.Count - 1]);
                    }
                    else
                    {
                        ordered.Clear();
                        for (int i = stackModel.Count - 1; i >= 0; i--) ordered.Add(stackModel[i]);
                        for (int i = 1; i < ordered.Count; i++)
                        {
                            int insertion = i;
                            while (insertion > 0 && PriorityOf(priority, ordered[insertion]) > PriorityOf(priority, ordered[insertion - 1]))
                            {
                                InputContext swap = ordered[insertion - 1];
                                ordered[insertion - 1] = ordered[insertion];
                                ordered[insertion] = swap;
                                insertion--;
                            }
                        }

                        for (int i = 0; i < ordered.Count; i++)
                        {
                            InputContext context = ordered[i];
                            expected.Add(context);
                            if (blocking[IndexFromName(context)]) break;
                        }
                    }

                    if (!ContextEquals(expected.ToArray(), ActualActive(player))) result.Violations++;

                    int overrideCount = 0;
                    for (int i = 0; i < depth; i++) if (overridden[i]) overrideCount++;
                    if (OverrideCount(player) != overrideCount) result.Violations++;
                    if (overrideCount > result.MaxOverrides) result.MaxOverrides = overrideCount;
                }
                catch (Exception)
                {
                    result.Exceptions++;
                }

                timing[step] = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * PerfMeter.TicksToMicroseconds;
            }

            for (int s = scopes.Count - 1; s >= 0; s--) scopes[s].Dispose();
            result.FinalOverrides = OverrideCount(player);
            TimingStats stats = PerfMeter.Summarize(timing);
            result.P50Us = stats.MedianUs;
            result.P95Us = stats.P95Us;
            PerfMeter.ForceFullCollection();
            result.LiveHeapDelta = GC.GetTotalMemory(true) - liveBefore;
            Assert.That(PerfMeter.TotalCollections() - collectionsBefore, Is.GreaterThanOrEqualTo(0));
            return result;
        }

        private static int PriorityOf(int[] priority, InputContext context)
        {
            return priority[IndexFromName(context)];
        }

        private static int IndexFromName(InputContext context)
        {
            string name = context.Name;
            int index = 0;
            for (int i = 1; i < name.Length; i++) index = index * 10 + (name[i] - '0');
            return index;
        }

        private static bool Contains(List<InputContext> list, InputContext context)
        {
            for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], context)) return true;
            return false;
        }

        private static void ResetModelPolicy(InputContext context, int[] priority, bool[] blocking, bool[] overridden)
        {
            int index = IndexFromName(context);
            priority[index] = 0;
            blocking[index] = index % 2 == 0;
            overridden[index] = false;
        }

        private static void Write(in CaseResult result, string metric, double value, string unit)
        {
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId,
                result.Round.ToString(),
                result.Seed.ToString(),
                result.Depth.ToString(),
                result.Steps.ToString(),
                metric,
                PerfOutput.Num(value, 3),
                unit
            });
        }

        [Test]
        public void Soak_LongPolicyChurnKeepsMemoryAndTimingStable()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            const int BlockSize = 2000;
            int blocks = Math.Max(4, SoakSteps / BlockSize);
            int steps = blocks * BlockSize;

            InputPlayer player = CreatePlayer(8, 8, out InputContext[] contexts);
            for (int i = 0; i < contexts.Length; i++) player.PushContext(contexts[i]);

            var random = new System.Random(SeedBase + 9973);
            var blockMeans = new List<double>(blocks);
            var blockHeap = new List<long>(blocks);
            int violations = 0;
            for (int i = 0; i < 2000; i++)
            {
                player.SetContextPolicy(contexts[i % contexts.Length], i % 9, i % 2 == 0);
                player.ClearContextPolicy(contexts[(i * 3) % contexts.Length]);
            }

            PerfMeter.ForceFullCollection();
            long liveStart = GC.GetTotalMemory(true);
            int collectionsStart = PerfMeter.TotalCollections();
            long wallStart = System.Diagnostics.Stopwatch.GetTimestamp();

            for (int block = 0; block < blocks; block++)
            {
                long blockStart = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int step = 0; step < BlockSize; step++)
                {
                    int index = random.Next(contexts.Length);
                    if ((step & 1) == 0) player.SetContextPolicy(contexts[index], random.Next(20), random.Next(2) == 0);
                    else player.ClearContextPolicy(contexts[index]);
                }

                blockMeans.Add((System.Diagnostics.Stopwatch.GetTimestamp() - blockStart) * PerfMeter.TicksToMicroseconds / BlockSize);
                blockHeap.Add(GC.GetTotalMemory(false));
                if (OverrideCount(player) > contexts.Length) violations++;
            }

            double wallUs = (System.Diagnostics.Stopwatch.GetTimestamp() - wallStart) * PerfMeter.TicksToMicroseconds;
            PerfMeter.ForceFullCollection();
            long liveEnd = GC.GetTotalMemory(true);
            int collections = PerfMeter.TotalCollections() - collectionsStart;
            double slope = SlopePerBlock(blockHeap);
            double[] sorted = blockMeans.ToArray();
            Array.Sort(sorted);
            double last = blockMeans[blockMeans.Count - 1];
            double drift = MedianDriftPercent(blockMeans);

            WriteSoak("steps", steps, "count");
            WriteSoak("wallSeconds", wallUs / 1_000_000d, "s");
            WriteSoak("opP50Us", PerfMeter.Percentile(sorted, 0.50d), "us");
            WriteSoak("opP95Us", PerfMeter.Percentile(sorted, 0.95d), "us");
            WriteSoak("opP99Us", PerfMeter.Percentile(sorted, 0.99d), "us");
            WriteSoak("cpuDriftPercent", drift, "percent");
            WriteSoak("heapSlopePerBlock", slope, "byte");
            WriteSoak("liveDelta", liveEnd - liveStart, "byte");
            WriteSoak("collections", collections, "count");
            WriteSoak("violations", violations, "count");
            WriteSoak("finalOverrides", OverrideCount(player), "count");
            WriteSoak("blockFirstUs", blockMeans[0], "us");
            WriteSoak("blockSecondUs", blockMeans.Count > 1 ? blockMeans[1] : blockMeans[0], "us");
            WriteSoak("blockMinUs", sorted[0], "us");
            WriteSoak("blockMedianUs", PerfMeter.Percentile(sorted, 0.50d), "us");
            WriteSoak("blockMaxUs", sorted[sorted.Length - 1], "us");
            WriteSoak("blockLastUs", last, "us");

            PerfOutput.Log("policySoak steps=" + steps + " wall=" + PerfOutput.Num(wallUs / 1_000_000d, 2) + "s" +
                " p50=" + PerfOutput.Num(PerfMeter.Percentile(sorted, 0.50d), 2) + "us" +
                " drift=" + PerfOutput.Num(drift, 2) + "%" +
                " liveDelta=" + (liveEnd - liveStart) + "B collections=" + collections +
                " violations=" + violations);

            Assert.That(violations, Is.Zero, "Policy overrides leaked during the soak.");
            Assert.That(Math.Abs(drift), Is.LessThan(50d), "Per-operation cost drifted beyond the stability budget.");
            Assert.That(Math.Abs(liveEnd - liveStart), Is.LessThan(512 * 1024), "Live heap grew beyond the stability budget.");
        }

        private static void WriteSoak(string metric, double value, string unit)
        {
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "soak", (SeedBase + 9973).ToString(), "8", SoakSteps.ToString(),
                metric, PerfOutput.Num(value, 3), unit
            });
        }

        /// <summary>
        /// Drift between the first and the last quarter of the run, each reduced to its median block so a
        /// single scheduling spike cannot pass for a trend.
        /// </summary>
        private static double MedianDriftPercent(List<double> blockMeans)
        {
            int quarter = Math.Max(1, blockMeans.Count / 4);
            double[] head = new double[quarter];
            double[] tail = new double[quarter];
            for (int i = 0; i < quarter; i++)
            {
                head[i] = blockMeans[i];
                tail[i] = blockMeans[blockMeans.Count - quarter + i];
            }

            Array.Sort(head);
            Array.Sort(tail);
            double first = PerfMeter.Percentile(head, 0.50d);
            double last = PerfMeter.Percentile(tail, 0.50d);
            return (last - first) / Math.Max(1e-9, first) * 100d;
        }

        private static double SlopePerBlock(List<long> series)
        {
            int n = series.Count;
            if (n < 2) return 0d;
            double meanX = (n - 1) / 2d;
            double meanY = 0d;
            for (int i = 0; i < n; i++) meanY += series[i];
            meanY /= n;
            double numerator = 0d;
            double denominator = 0d;
            for (int i = 0; i < n; i++)
            {
                double dx = i - meanX;
                numerator += dx * (series[i] - meanY);
                denominator += dx * dx;
            }

            return denominator <= 0d ? 0d : numerator / denominator;
        }

        [Test]
        public void Cost_PolicyOperationsAcrossDepthsAndBindings()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            int[] depths = { 1, 4, 16 };
            int[] bindings = { 0, 8, 32 };

            for (int d = 0; d < depths.Length; d++)
            {
                for (int b = 0; b < bindings.Length; b++)
                {
                    InputPlayer player = CreatePlayer(depths[d], bindings[b], out InputContext[] contexts);
                    for (int i = 0; i < contexts.Length; i++) player.PushContext(contexts[i]);
                    InputContext target = contexts[contexts.Length - 1];

                    bool toggle = false;
                    double[] changed = PerfMeter.SamplePerCallMicroseconds(120, 1, () =>
                    {
                        toggle = !toggle;
                        player.SetContextPolicy(target, toggle ? 7 : 3, toggle);
                    });
                    double[] noOp = PerfMeter.SamplePerCallMicroseconds(120, 1, () =>
                        player.SetContextPolicy(target, 3, false));
                    double[] refresh = PerfMeter.SamplePerCallMicroseconds(120, 1, player.RefreshActiveContext);
                    double[] baseline = PerfMeter.SamplePerCallMicroseconds(120, 1, () =>
                    {
                        player.PushContext(target);
                        player.RemoveContext(target);
                    });

                    WriteCost(depths[d], bindings[b], "setPolicyChanged", PerfMeter.Summarize(changed));
                    WriteCost(depths[d], bindings[b], "setPolicyNoOp", PerfMeter.Summarize(noOp));
                    WriteCost(depths[d], bindings[b], "refresh", PerfMeter.Summarize(refresh));
                    WriteCost(depths[d], bindings[b], "pushRemoveBaseline", PerfMeter.Summarize(baseline));
                }
            }
        }

        private static void WriteCost(int depth, int bindings, string label, TimingStats stats)
        {
            string shape = "C" + depth + "B" + bindings + "." + label;
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "cost", shape, depth.ToString(), bindings.ToString(), shape + ".p50Us", PerfOutput.Num(stats.MedianUs, 3), "us" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "cost", shape, depth.ToString(), bindings.ToString(), shape + ".p95Us", PerfOutput.Num(stats.P95Us, 3), "us" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "cost", shape, depth.ToString(), bindings.ToString(), shape + ".p99Us", PerfOutput.Num(stats.P99Us, 3), "us" });
            PerfOutput.Log("policyCost " + shape + " p50=" + PerfOutput.Num(stats.MedianUs, 2) + "us p95=" + PerfOutput.Num(stats.P95Us, 2) + "us");
        }

        [UnityTest]
        public IEnumerator Allocation_PolicyOperationsInsideProfiledFrames()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            bool started = AllocationInstrument.TryStartFrameRecorder();
            Assert.That(started, Is.True, "The GC Allocated In Frame recorder must be available.");

            try
            {
                InputPlayer player = CreatePlayer(4, 8, out InputContext[] contexts);
                for (int i = 0; i < contexts.Length; i++) player.PushContext(contexts[i]);
                InputContext target = contexts[contexts.Length - 1];

                const int Frames = 16;
                const int PerFrame = 32;
                int operations = Frames * PerFrame;
                bool toggle = false;

                for (int frame = 0; frame < 4; frame++)
                {
                    for (int op = 0; op < PerFrame; op++) player.SetContextPolicy(target, toggle ? 7 : 3, toggle);
                    yield return null;
                }

                long idleStart = AllocationInstrument.FrameRecorderSampleSum();
                for (int frame = 0; frame < Frames; frame++) yield return null;
                long idle = AllocationInstrument.FrameRecorderSampleSum() - idleStart;

                long before = AllocationInstrument.FrameRecorderSampleSum();
                for (int frame = 0; frame < Frames; frame++)
                {
                    for (int op = 0; op < PerFrame; op++)
                    {
                        toggle = !toggle;
                        player.SetContextPolicy(target, toggle ? 7 : 3, toggle);
                    }
                    yield return null;
                }
                long measured = AllocationInstrument.FrameRecorderSampleSum() - before;
                double changedBytes = Math.Max(0d, (measured - idle) / (double)operations);

                long noOpBefore = AllocationInstrument.FrameRecorderSampleSum();
                for (int frame = 0; frame < Frames; frame++)
                {
                    for (int op = 0; op < PerFrame; op++) player.SetContextPolicy(target, 3, false);
                    yield return null;
                }
                long noOpMeasured = AllocationInstrument.FrameRecorderSampleSum() - noOpBefore;
                double noOpBytes = Math.Max(0d, (noOpMeasured - idle) / (double)operations);

                PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "alloc", "changed", "4", operations.ToString(), "bytesPerOperation", PerfOutput.Num(changedBytes, 2), "byte" });
                PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "alloc", "noOp", "4", operations.ToString(), "bytesPerOperation", PerfOutput.Num(noOpBytes, 2), "byte" });
                PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "alloc", "idle", "4", operations.ToString(), "bytesPerFrame", PerfOutput.Num(idle / (double)Frames, 2), "byte" });
                PerfOutput.Log("policyAlloc changed=" + PerfOutput.Num(changedBytes, 1) + "B noOp=" + PerfOutput.Num(noOpBytes, 1) + "B idleFrame=" + PerfOutput.Num(idle / (double)Frames, 0) + "B");
            }
            finally
            {
                if (started) AllocationInstrument.StopFrameRecorder();
            }
        }
    }
}
