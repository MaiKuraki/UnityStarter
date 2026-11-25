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
    /// Incremental action-map projection. The oracle is Unity's map.enabled state: a map is enabled
    /// exactly when an active context resolves to it.
    /// </summary>
    public sealed class InputProjectionOptimizationTests
    {
        private const string File = "projection-verify.csv";
        private const string Header = "runId,round,seed,depth,steps,metric,value,unit";

        private static readonly int SeedBase = ReadInt("CGIS_PROJECTION_SEED", 77001);
        private static readonly int Rounds = ReadInt("CGIS_PROJECTION_ROUNDS", 4);
        private static readonly int StepScale = ReadInt("CGIS_PROJECTION_STEPS", 1);
        private static readonly int SoakSteps = ReadInt("CGIS_PROJECTION_SOAK", 60000);
        private static readonly int[] Depths = { 2, 4, 8, 16 };

        private static int _deviceSerial;
        private readonly List<InputPlayer> _players = new List<InputPlayer>();
        private readonly List<InputDevice> _devices = new List<InputDevice>();

        private static readonly FieldInfo AssetField =
            typeof(InputPlayer).GetField("_inputActionAsset", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo ActiveField =
            typeof(InputPlayer).GetField("_activeContexts", BindingFlags.Instance | BindingFlags.NonPublic);

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

        private InputPlayer CreatePlayer(int contextCount, int bindingsPerContext, bool allBlocking, out InputContext[] contexts)
        {
            var slot = new PlayerSlotConfig { PlayerId = 0, Contexts = new List<ContextDefinitionConfig>(contextCount) };
            for (int i = 0; i < contextCount; i++)
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

            Gamepad device = UIS.AddDevice<Gamepad>("ProjectionPad" + (++_deviceSerial));
            _devices.Add(device);
            InputUser user = InputUser.PerformPairingWithDevice(device);
            var player = new InputPlayer(0, user, slot, device);
            _players.Add(player);

            contexts = new InputContext[contextCount];
            for (int i = 0; i < contextCount; i++)
            {
                contexts[i] = new InputContext("M" + i, "C" + i, 0, allBlocking || i % 2 == 0);
                for (int b = 0; b < bindingsPerContext; b++)
                {
                    contexts[i].AddBinding(new Subject<Unit>(), NullCommand.Instance);
                }
            }

            return player;
        }

        private static InputActionAsset Asset(InputPlayer player)
        {
            return (InputActionAsset)AssetField.GetValue(player);
        }

        private static List<InputContext> Active(InputPlayer player)
        {
            return (List<InputContext>)ActiveField.GetValue(player);
        }

        private static bool MapDesired(InputActionMap map, List<InputContext> active)
        {
            string mapName = map.name;
            for (int i = 0; i < active.Count; i++)
            {
                string prefix = active[i].ActionMapName + "::" + active[i].Name + "#";
                if (mapName.Length >= prefix.Length && string.CompareOrdinal(mapName, 0, prefix, 0, prefix.Length) == 0)
                    return true;
            }

            return false;
        }

        private static int VerifyMapState(InputPlayer player, bool blocked)
        {
            InputActionAsset asset = Asset(player);
            List<InputContext> active = Active(player);
            int violations = 0;
            foreach (InputActionMap map in asset.actionMaps)
            {
                bool desired = !blocked && MapDesired(map, active);
                if (map.enabled != desired) violations++;
            }

            return violations;
        }

        [Test]
        public void NoOpRefresh_PerformsZeroMapTransitions()
        {
            InputPlayer player = CreatePlayer(4, 8, true, out InputContext[] contexts);
            for (int i = 0; i < contexts.Length; i++) player.PushContext(contexts[i]);

            InputPlayer.ProjectionDiagnostics before = player.GetProjectionDiagnostics();
            const int Refreshes = 32;
            for (int i = 0; i < Refreshes; i++) player.RefreshActiveContext();
            InputPlayer.ProjectionDiagnostics after = player.GetProjectionDiagnostics();

            int passes = after.Passes - before.Passes;
            int enables = after.MapEnables - before.MapEnables;
            int disables = after.MapDisables - before.MapDisables;
            int reuses = after.MapReuses - before.MapReuses;

            Write(0, SeedBase, 4, Refreshes, "noOpPasses", passes, "count");
            Write(0, SeedBase, 4, Refreshes, "noOpMapTransitions", enables + disables, "count");
            Write(0, SeedBase, 4, Refreshes, "noOpMapReuses", reuses, "count");
            PerfOutput.Log("projectionNoOp passes=" + passes + " transitions=" + (enables + disables) + " reuses=" + reuses);

            Assert.That(passes, Is.EqualTo(Refreshes));
            Assert.That(enables, Is.Zero, "An unchanged projection must not enable any map.");
            Assert.That(disables, Is.Zero, "An unchanged projection must not disable any map.");
            Assert.That(reuses, Is.EqualTo(Refreshes * 4), "Every configured map is reused on every pass.");
            Assert.That(after.SubscriptionRebuilds - before.SubscriptionRebuilds, Is.Zero,
                "An unchanged projection must not rebuild any binding subscription.");
            Assert.That(after.SubscriptionReuses - before.SubscriptionReuses, Is.EqualTo(Refreshes),
                "The single active context reuses its subscriptions on every pass.");
            Assert.That(after.PolicyCacheMisses - before.PolicyCacheMisses, Is.Zero,
                "An unchanged projection must not re-resolve any context policy.");
            Assert.That(after.PolicyCacheHits - before.PolicyCacheHits, Is.GreaterThan(0));
            Write(0, SeedBase, 4, Refreshes, "noOpPolicyCacheHits", after.PolicyCacheHits - before.PolicyCacheHits, "count");
            Assert.That(VerifyMapState(player, false), Is.Zero);
        }

        [Test]
        public void PolicyCache_ResolvesEachContextOnceAndFollowsOverrides()
        {
            InputPlayer player = CreatePlayer(4, 0, true, out InputContext[] contexts);
            InputPlayer.ProjectionDiagnostics start = player.GetProjectionDiagnostics();
            for (int i = 0; i < contexts.Length; i++) player.PushContext(contexts[i]);
            InputPlayer.ProjectionDiagnostics pushed = player.GetProjectionDiagnostics();

            Assert.That(pushed.PolicyCacheMisses - start.PolicyCacheMisses, Is.EqualTo(contexts.Length),
                "Every context is resolved exactly once no matter how many refreshes read it.");

            Assert.That(player.SetContextPolicy(contexts[3], 100, false), Is.True);
            Assert.That(Active(player).ToArray(), Is.EqualTo(new[] { contexts[3], contexts[2] }),
                "The override is applied without touching the memoized base policy.");
            InputPlayer.ProjectionDiagnostics overridden = player.GetProjectionDiagnostics();
            Assert.That(overridden.PolicyCacheMisses - pushed.PolicyCacheMisses, Is.Zero);

            player.RemoveContext(contexts[3]);
            player.PushContext(contexts[3]);
            InputPlayer.ProjectionDiagnostics reAdded = player.GetProjectionDiagnostics();
            Assert.That(reAdded.PolicyCacheMisses - overridden.PolicyCacheMisses, Is.EqualTo(1),
                "Leaving the player releases the memo; re-entering resolves exactly once.");
            Assert.That(Active(player).ToArray(), Is.EqualTo(new[] { contexts[3] }),
                "The memo is released together with the override, so the base policy is back.");
            Assert.That(VerifyMapState(player, false), Is.Zero);
        }

        [Test]
        public void BindingChange_RebuildsOnlyTheChangedContext()
        {
            InputPlayer player = CreatePlayer(3, 0, true, out InputContext[] contexts);
            for (int i = 0; i < contexts.Length; i++) player.PushContext(contexts[i]);
            InputPlayer.ProjectionDiagnostics before = player.GetProjectionDiagnostics();

            contexts[2].AddBinding(new Subject<Unit>(), NullCommand.Instance);
            player.RefreshActiveContext();
            InputPlayer.ProjectionDiagnostics after = player.GetProjectionDiagnostics();

            Assert.That(after.SubscriptionRebuilds - before.SubscriptionRebuilds, Is.EqualTo(1),
                "A changed binding set must rebuild exactly the changed context.");
            Assert.That(after.MapEnables - before.MapEnables + after.MapDisables - before.MapDisables, Is.Zero);
            Write(0, SeedBase, 3, 1, "bindingChangeRebuilds", after.SubscriptionRebuilds - before.SubscriptionRebuilds, "count");
        }

        [Test]
        public void SameMapPolicyChange_PerformsZeroMapTransitions()
        {
            InputPlayer player = CreatePlayer(2, 8, true, out InputContext[] contexts);
            var sharedA = new InputContext("M0", "C0", 0, true);
            var sharedB = new InputContext("M0", "C0", 0, true);
            player.PushContext(sharedA);
            player.PushContext(sharedB);

            InputPlayer.ProjectionDiagnostics before = player.GetProjectionDiagnostics();
            Assert.That(player.SetContextPolicy(sharedB, 7, false), Is.True);
            Assert.That(player.SetContextPolicy(sharedB, 3, true), Is.True);
            Assert.That(player.ClearContextPolicy(sharedB), Is.True);
            InputPlayer.ProjectionDiagnostics after = player.GetProjectionDiagnostics();
            int transitions = after.MapEnables - before.MapEnables + after.MapDisables - before.MapDisables;

            Write(0, SeedBase, 2, 3, "sameMapTransitions", transitions, "count");
            PerfOutput.Log("projectionSameMap policyChanges=3 transitions=" + transitions);
            Assert.That(transitions, Is.Zero, "Changing policy inside one action map must not toggle any map.");
            Assert.That(VerifyMapState(player, false), Is.Zero);
        }

        [Test]
        public void CrossMapSwitch_TogglesOnlyTheMapsThatChanged()
        {
            InputPlayer player = CreatePlayer(3, 8, true, out InputContext[] contexts);
            player.PushContext(contexts[0]);
            InputPlayer.ProjectionDiagnostics before = player.GetProjectionDiagnostics();
            player.PushContext(contexts[1]);
            InputPlayer.ProjectionDiagnostics after = player.GetProjectionDiagnostics();

            int enables = after.MapEnables - before.MapEnables;
            int disables = after.MapDisables - before.MapDisables;
            Write(0, SeedBase, 3, 2, "crossMapEnables", enables, "count");
            Write(0, SeedBase, 3, 2, "crossMapDisables", disables, "count");
            PerfOutput.Log("projectionCrossMap enables=" + enables + " disables=" + disables);
            Assert.That(enables, Is.EqualTo(1));
            Assert.That(disables, Is.EqualTo(1));
            Assert.That(VerifyMapState(player, false), Is.Zero);
        }

        [Test]
        public void FailClosedAndRecovery_KeepMapStateConsistent()
        {
            InputPlayer player = CreatePlayer(2, 0, true, out InputContext[] contexts);
            player.PushContext(contexts[0]);
            Observable<Unit> failing = Observable.Create<Unit>(_ =>
                throw new InvalidOperationException("Expected synchronous subscription failure."));
            contexts[1].AddBinding(failing, NullCommand.Instance);

            Assert.Throws<InvalidOperationException>(() => player.PushContext(contexts[1]));
            Assert.That(Asset(player).enabled, Is.False, "Fail-closed must disable the whole asset.");
            foreach (InputActionMap map in Asset(player).actionMaps) Assert.That(map.enabled, Is.False);

            Assert.That(contexts[1].RemoveBinding(failing), Is.True);
            Assert.DoesNotThrow(player.RefreshActiveContext);
            Assert.That(Asset(player).enabled, Is.True);
            Assert.That(VerifyMapState(player, false), Is.Zero, "Recovery must rebuild the exact desired map set.");
        }

        [Test]
        public void Cost_IncrementalProjectionAcrossDepthsAndBindings()
        {
            int[] depths = { 1, 4, 16 };
            int[] bindings = { 0, 8, 32 };
            for (int d = 0; d < depths.Length; d++)
            {
                for (int b = 0; b < bindings.Length; b++)
                {
                    InputPlayer player = CreatePlayer(depths[d], bindings[b], true, out InputContext[] contexts);
                    for (int i = 0; i < contexts.Length; i++) player.PushContext(contexts[i]);

                    double[] noOp = PerfMeter.SamplePerCallMicroseconds(160, 1, player.RefreshActiveContext);
                    bool toggle = false;
                    double[] policy = PerfMeter.SamplePerCallMicroseconds(160, 1, () =>
                    {
                        toggle = !toggle;
                        player.SetContextPolicy(contexts[contexts.Length - 1], toggle ? 7 : 3, toggle);
                    });
                    double[] baseline = PerfMeter.SamplePerCallMicroseconds(160, 1, () =>
                    {
                        player.PushContext(contexts[contexts.Length - 1]);
                        player.RemoveContext(contexts[contexts.Length - 1]);
                    });

                    string shape = "C" + depths[d] + "B" + bindings[b];
                    WriteCost(shape, "noOpRefresh", PerfMeter.Summarize(noOp));
                    WriteCost(shape, "policyChange", PerfMeter.Summarize(policy));
                    WriteCost(shape, "pushRemove", PerfMeter.Summarize(baseline));
                }
            }
        }

        private static void WriteCost(string shape, string label, TimingStats stats)
        {
            string key = shape + "." + label;
            Write(0, SeedBase, 0, 0, key + ".p50Us", stats.MedianUs, "us");
            Write(0, SeedBase, 0, 0, key + ".p95Us", stats.P95Us, "us");
            Write(0, SeedBase, 0, 0, key + ".p99Us", stats.P99Us, "us");
            PerfOutput.Log("projectionCost " + key + " p50=" + PerfOutput.Num(stats.MedianUs, 2) +
                " p95=" + PerfOutput.Num(stats.P95Us, 2) + " p99=" + PerfOutput.Num(stats.P99Us, 2));
        }

        [Test]
        public void Fuzz_MapStateMatchesActiveContextsAcrossSeedsAndDepths()
        {
            int totalSteps = 0;
            int totalViolations = 0;
            int totalTransitions = 0;
            int totalReuses = 0;
            for (int round = 0; round < Rounds; round++)
            {
                int depth = Depths[round % Depths.Length];
                int steps = 2500 * (round + 1) * StepScale;
                FuzzResult result = RunFuzz(SeedBase + round, round, depth, steps);
                totalSteps += result.Steps;
                totalViolations += result.Violations;
                totalTransitions += result.Transitions;
                totalReuses += result.Reuses;

                Write(round, result.Seed, depth, steps, "fuzzSteps", result.Steps, "count");
                Write(round, result.Seed, depth, steps, "fuzzViolations", result.Violations, "count");
                Write(round, result.Seed, depth, steps, "fuzzExceptions", result.Exceptions, "count");
                Write(round, result.Seed, depth, steps, "fuzzMapTransitions", result.Transitions, "count");
                Write(round, result.Seed, depth, steps, "fuzzMapReuses", result.Reuses, "count");
                Write(round, result.Seed, depth, steps, "fuzzCacheHitPercent", result.CacheHitPercent, "percent");
                Write(round, result.Seed, depth, steps, "fuzzOpP95Us", result.P95Us, "us");
                Write(round, result.Seed, depth, steps, "fuzzLiveHeapDelta", result.LiveHeapDelta, "byte");
            }

            PerfOutput.Log("projectionFuzz seeds=" + SeedBase + ".." + (SeedBase + Rounds - 1) +
                " steps=" + totalSteps + " violations=" + totalViolations +
                " transitions=" + totalTransitions + " reuses=" + totalReuses);
            Assert.That(totalViolations, Is.Zero, "Map state diverged from the desired projection.");
        }

        private struct FuzzResult
        {
            internal int Seed;
            internal int Steps;
            internal int Violations;
            internal int Exceptions;
            internal int Transitions;
            internal int Reuses;
            internal double CacheHitPercent;
            internal double P95Us;
            internal long LiveHeapDelta;
        }

        private FuzzResult RunFuzz(int seed, int round, int depth, int steps)
        {
            var result = new FuzzResult { Seed = seed, Steps = steps };
            var random = new System.Random(seed);
            PerfMeter.ForceFullCollection();
            long liveBefore = GC.GetTotalMemory(true);
            InputPlayer player = CreatePlayer(depth, 4, false, out InputContext[] contexts);
            var stackModel = new List<InputContext>(depth);
            var captureModel = new List<InputContext>(depth);
            var scopes = new List<IDisposable>(depth);
            var timing = new double[steps];
            int blockDepth = 0;
            InputPlayer.ProjectionDiagnostics start = player.GetProjectionDiagnostics();

            for (int step = 0; step < steps; step++)
            {
                long startTick = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    int choice = random.Next(100);
                    if (choice < 14)
                    {
                        InputContext context = contexts[random.Next(depth)];
                        stackModel.Remove(context);
                        stackModel.Add(context);
                        player.PushContext(context);
                    }
                    else if (choice < 28 && stackModel.Count > 0)
                    {
                        stackModel.RemoveAt(stackModel.Count - 1);
                        player.PopContext();
                    }
                    else if (choice < 36)
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
                        }
                    }
                    else if (choice < 44)
                    {
                        InputContext context = contexts[random.Next(depth)];
                        captureModel.Add(context);
                        scopes.Add(player.CaptureContext(context));
                    }
                    else if (choice < 52 && scopes.Count > 0)
                    {
                        int s = random.Next(scopes.Count);
                        captureModel.RemoveAt(s);
                        scopes[s].Dispose();
                        scopes.RemoveAt(s);
                    }
                    else if (choice < 58)
                    {
                        blockDepth++;
                        player.BlockInput();
                    }
                    else if (choice < 64 && blockDepth > 0)
                    {
                        blockDepth--;
                        player.UnblockInput();
                    }
                    else if (choice < 80)
                    {
                        int index = random.Next(depth);
                        player.SetContextPolicy(contexts[index], random.Next(-5, 20), random.Next(2) == 0);
                    }
                    else if (choice < 90)
                    {
                        player.ClearContextPolicy(contexts[random.Next(depth)]);
                    }
                    else
                    {
                        player.RefreshActiveContext();
                    }

                    result.Violations += VerifyMapState(player, blockDepth > 0);
                    result.Violations += stackModel.Count == player.GetMemoryStats().ContextStackCount ? 0 : 1;
                }
                catch (Exception)
                {
                    result.Exceptions++;
                }
                timing[step] = (System.Diagnostics.Stopwatch.GetTimestamp() - startTick) * PerfMeter.TicksToMicroseconds;
            }

            for (int s = scopes.Count - 1; s >= 0; s--) scopes[s].Dispose();
            InputPlayer.ProjectionDiagnostics end = player.GetProjectionDiagnostics();
            result.Transitions = end.MapEnables - start.MapEnables + end.MapDisables - start.MapDisables;
            result.Reuses = end.MapReuses - start.MapReuses;
            double total = result.Transitions + result.Reuses;
            result.CacheHitPercent = total <= 0d ? 100d : result.Reuses / total * 100d;
            result.P95Us = PerfMeter.Summarize(timing).P95Us;
            PerfMeter.ForceFullCollection();
            result.LiveHeapDelta = GC.GetTotalMemory(true) - liveBefore;
            return result;
        }

        [Test]
        public void Soak_LongProjectionChurnRemainsStable()
        {
            const int BlockSize = 2000;
            int blocks = Math.Max(4, SoakSteps / BlockSize);
            int steps = blocks * BlockSize;
            InputPlayer player = CreatePlayer(8, 8, false, out InputContext[] contexts);
            for (int i = 0; i < contexts.Length; i++) player.PushContext(contexts[i]);
            var random = new System.Random(SeedBase + 9973);
            InputPlayer.ProjectionDiagnostics start = player.GetProjectionDiagnostics();

            for (int i = 0; i < 4000; i++)
            {
                player.SetContextPolicy(contexts[i % contexts.Length], i % 11, i % 2 == 0);
                player.RefreshActiveContext();
            }

            PerfMeter.ForceFullCollection();
            long liveStart = GC.GetTotalMemory(true);
            int collectionsStart = PerfMeter.TotalCollections();
            var blockMeans = new List<double>(blocks);
            var blockHeap = new List<long>(blocks);
            int violations = 0;
            long wallStart = System.Diagnostics.Stopwatch.GetTimestamp();

            for (int block = 0; block < blocks; block++)
            {
                long blockStart = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int step = 0; step < BlockSize; step++)
                {
                    int index = random.Next(contexts.Length);
                    if ((step & 3) == 0) player.SetContextPolicy(contexts[index], random.Next(20), random.Next(2) == 0);
                    else if ((step & 3) == 1) player.ClearContextPolicy(contexts[index]);
                    else player.RefreshActiveContext();
                }
                blockMeans.Add((System.Diagnostics.Stopwatch.GetTimestamp() - blockStart) * PerfMeter.TicksToMicroseconds / BlockSize);
                blockHeap.Add(GC.GetTotalMemory(false));
                violations += VerifyMapState(player, false);
            }

            double wallUs = (System.Diagnostics.Stopwatch.GetTimestamp() - wallStart) * PerfMeter.TicksToMicroseconds;
            PerfMeter.ForceFullCollection();
            long liveEnd = GC.GetTotalMemory(true);
            int collections = PerfMeter.TotalCollections() - collectionsStart;
            double[] sorted = blockMeans.ToArray();
            Array.Sort(sorted);
            double last = blockMeans[blockMeans.Count - 1];
            double drift = MedianDriftPercent(blockMeans);
            InputPlayer.ProjectionDiagnostics end = player.GetProjectionDiagnostics();
            int transitions = end.MapEnables - start.MapEnables + end.MapDisables - start.MapDisables;
            int reuses = end.MapReuses - start.MapReuses;
            double hit = reuses / (double)Math.Max(1, reuses + transitions) * 100d;

            WriteSoak(steps, "steps", steps, "count");
            WriteSoak(steps, "wallSeconds", wallUs / 1_000_000d, "s");
            WriteSoak(steps, "opP50Us", PerfMeter.Percentile(sorted, 0.50d), "us");
            WriteSoak(steps, "opP95Us", PerfMeter.Percentile(sorted, 0.95d), "us");
            WriteSoak(steps, "cacheHitPercent", hit, "percent");
            WriteSoak(steps, "mapTransitions", transitions, "count");
            WriteSoak(steps, "cpuDriftPercent", drift, "percent");
            WriteSoak(steps, "blockSecondUs", blockMeans.Count > 1 ? blockMeans[1] : blockMeans[0], "us");
            WriteSoak(steps, "blockMaxUs", sorted[sorted.Length - 1], "us");
            WriteSoak(steps, "blockLastUs", last, "us");
            WriteSoak(steps, "liveDelta", liveEnd - liveStart, "byte");
            WriteSoak(steps, "collections", collections, "count");
            WriteSoak(steps, "violations", violations, "count");

            PerfOutput.Log("projectionSoak steps=" + steps + " wall=" + PerfOutput.Num(wallUs / 1_000_000d, 2) +
                "s p50=" + PerfOutput.Num(PerfMeter.Percentile(sorted, 0.50d), 2) + "us hit=" + PerfOutput.Num(hit, 2) +
                "% transitions=" + transitions + " drift=" + PerfOutput.Num(drift, 2) +
                "% liveDelta=" + (liveEnd - liveStart) + "B collections=" + collections + " violations=" + violations);

            Assert.That(violations, Is.Zero);
            Assert.That(Math.Abs(drift), Is.LessThan(50d));
            Assert.That(Math.Abs(liveEnd - liveStart), Is.LessThan(512 * 1024));
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

        private static void WriteSoak(int steps, string metric, double value, string unit)
        {
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "soak", (SeedBase + 9973).ToString(), "8", steps.ToString(), metric, PerfOutput.Num(value, 3), unit });
        }

        private static void Write(int round, int seed, int depth, int steps, string metric, double value, string unit)
        {
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, round.ToString(), seed.ToString(), depth.ToString(), steps.ToString(), metric, PerfOutput.Num(value, 3), unit });
        }

        [UnityTest]
        public IEnumerator Allocation_IncrementalProjectionInsideProfiledFrames()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            bool started = AllocationInstrument.TryStartFrameRecorder();
            Assert.That(started, Is.True);

            try
            {
                InputPlayer player = CreatePlayer(4, 8, true, out InputContext[] contexts);
                for (int i = 0; i < contexts.Length; i++) player.PushContext(contexts[i]);
                InputContext target = contexts[contexts.Length - 1];
                const int Frames = 16;
                const int PerFrame = 32;
                int operations = Frames * PerFrame;

                for (int frame = 0; frame < 4; frame++)
                {
                    for (int op = 0; op < PerFrame; op++) player.RefreshActiveContext();
                    yield return null;
                }

                long idleStart = AllocationInstrument.FrameRecorderSampleSum();
                for (int frame = 0; frame < Frames; frame++) yield return null;
                long idle = AllocationInstrument.FrameRecorderSampleSum() - idleStart;

                long noOpStart = AllocationInstrument.FrameRecorderSampleSum();
                for (int frame = 0; frame < Frames; frame++)
                {
                    for (int op = 0; op < PerFrame; op++) player.RefreshActiveContext();
                    yield return null;
                }
                long noOp = AllocationInstrument.FrameRecorderSampleSum() - noOpStart;

                int notifications = 0;
                Action<string> onContextChanged = _ => notifications++;
                player.OnContextChanged += onContextChanged;
                long notifiedStart = AllocationInstrument.FrameRecorderSampleSum();
                for (int frame = 0; frame < Frames; frame++)
                {
                    for (int op = 0; op < PerFrame; op++) player.RefreshActiveContext();
                    yield return null;
                }
                long notified = AllocationInstrument.FrameRecorderSampleSum() - notifiedStart;
                player.OnContextChanged -= onContextChanged;

                long policyStart = AllocationInstrument.FrameRecorderSampleSum();
                bool toggle = false;
                for (int frame = 0; frame < Frames; frame++)
                {
                    for (int op = 0; op < PerFrame; op++)
                    {
                        toggle = !toggle;
                        player.SetContextPolicy(target, toggle ? 7 : 3, toggle);
                    }
                    yield return null;
                }
                long policy = AllocationInstrument.FrameRecorderSampleSum() - policyStart;

                double noOpBytes = Math.Max(0d, (noOp - idle) / (double)operations);
                double notifiedBytes = Math.Max(0d, (notified - idle) / (double)operations);
                double policyBytes = Math.Max(0d, (policy - idle) / (double)operations);
                Write(0, SeedBase, 4, operations, "allocIdleBytesPerFrame", idle / (double)Frames, "byte");
                Write(0, SeedBase, 4, operations, "allocNoOpRefreshBytesPerOperation", noOpBytes, "byte");
                Write(0, SeedBase, 4, operations, "allocNotifiedRefreshBytesPerOperation", notifiedBytes, "byte");
                Write(0, SeedBase, 4, operations, "contextChangedNotifications", notifications, "count");
                Write(0, SeedBase, 4, operations, "allocPolicyChangeBytesPerOperation", policyBytes, "byte");
                PerfOutput.Log("projectionAlloc idleFrame=" + PerfOutput.Num(idle / (double)Frames, 0) +
                    "B noOp=" + PerfOutput.Num(noOpBytes, 1) + "B notified=" + PerfOutput.Num(notifiedBytes, 1) +
                    "B policy=" + PerfOutput.Num(policyBytes, 1) + "B");
            }
            finally
            {
                if (started) AllocationInstrument.StopFrameRecorder();
            }
        }
    }
}
