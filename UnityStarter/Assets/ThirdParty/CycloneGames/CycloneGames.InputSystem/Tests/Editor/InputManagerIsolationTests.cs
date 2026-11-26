using System;
using System.Collections.Generic;
using System.Text;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Independence between managers in one process. The oracle is per-manager state: initialization, listening,
    /// players and disposal must never be observable through another manager.
    /// </summary>
    public sealed class InputManagerIsolationTests
    {
        private const string File = "isolation-verify.csv";
        private const string Header = "runId,round,seed,managers,steps,metric,value,unit";

        private static readonly int SeedBase = ReadInt("CGIS_ISOLATION_SEED", 510011);
        private static readonly int Rounds = ReadInt("CGIS_ISOLATION_ROUNDS", 4);
        private static readonly int StepScale = ReadInt("CGIS_ISOLATION_STEPS", 1);
        private static readonly int SoakCycles = ReadInt("CGIS_ISOLATION_SOAK", 6000);

        private readonly List<InputManager> _managers = new List<InputManager>();
        private readonly List<InputDevice> _devices = new List<InputDevice>();

        private static int ReadInt(string name, int fallback)
        {
            string configured = Environment.GetEnvironmentVariable(name);
            return int.TryParse(configured, out int value) && value > 0 ? value : fallback;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _managers.Count; i++) _managers[i]?.Dispose();
            _managers.Clear();
            for (int i = _devices.Count - 1; i >= 0; i--)
            {
                InputDevice device = _devices[i];
                if (device != null && device.added) UIS.RemoveDevice(device);
            }

            _devices.Clear();
            InputManager.ResetGlobalStateForDomainReload();
        }

        [Test]
        public void TwoManagers_HoldSeparatePlayers()
        {
            InputManager first = CreateManager(out InputManager second);

            Gamepad pad = AddGamepad("Isolation_PadA");
            Assert.That(first.JoinSinglePlayer(0), Is.Not.Null);
            Assert.That(pad, Is.Not.Null);

            Assert.That(first.ActivePlayerCount, Is.EqualTo(1));
            Assert.That(second.ActivePlayerCount, Is.Zero);
            Assert.That(second.GetInputPlayer(0), Is.Null);
            Assert.That(InputManager.ActiveManagerCount, Is.EqualTo(2));
        }

        [Test]
        public void DisposingOneManager_LeavesTheOtherInitialized()
        {
            InputManager first = CreateManager(out InputManager second);
            first.Dispose();

            Assert.That(first.IsDisposed, Is.True);
            Assert.That(second.IsInitialized, Is.True);
            Assert.That(second.IsDisposed, Is.False);
            Assert.That(InputManager.ActiveManagerCount, Is.EqualTo(1));

            AddGamepad("Isolation_PadB");
            Assert.That(second.JoinSinglePlayer(0), Is.Not.Null, "the surviving manager must still join.");
        }

        [Test]
        public void ListeningFlag_IsPerManager()
        {
            InputManager first = CreateManager(out InputManager second);

            Assert.That(first.IsListeningForPlayers, Is.False);
            Assert.That(second.IsListeningForPlayers, Is.False);

            first.StartListeningForPlayers(false);

            Assert.That(first.IsListeningForPlayers, Is.True);
            Assert.That(second.IsListeningForPlayers, Is.False, "the flag must not be shared between managers.");
        }

        [Test]
        public void ConstructDisposeCycle_ReturnsTheRegistryToZero()
        {
            Assert.That(InputManager.ActiveManagerCount, Is.Zero);

            var manager = new InputManager();
            Assert.That(InputManager.ActiveManagerCount, Is.EqualTo(1));

            manager.Dispose();
            Assert.That(InputManager.ActiveManagerCount, Is.Zero);
            Assert.That(manager.IsDisposed, Is.True);
        }

        [Test]
        public void LifecycleFuzz_MatchesThePerManagerModel()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            long totalSteps = 0;
            long totalViolations = 0;

            for (int round = 0; round < Rounds; round++)
            {
                int seed = SeedBase + round;
                int managerCount = 2 + (round % 3);
                int steps = (400 << Math.Min(3, round)) * StepScale;
                var random = new System.Random(seed);

                var managers = new InputManager[managerCount];
                var model = new ManagerModel[managerCount];
                for (int i = 0; i < managerCount; i++)
                {
                    managers[i] = new InputManager();
                    _managers.Add(managers[i]);
                    model[i] = new ManagerModel();
                }

                int violations = 0;
                for (int step = 0; step < steps; step++)
                {
                    int index = random.Next(managerCount);
                    InputManager manager = managers[index];
                    ManagerModel expected = model[index];
                    int op = random.Next(100);

                    if (op < 45)
                    {
                        bool actual = manager.InitializeWithResult(MinimalYaml()).IsSuccess;
                        bool modelResult = expected.ApplyInitialize();
                        if (actual != modelResult) violations++;
                    }
                    else if (op < 70)
                    {
                        if (!expected.Disposed && expected.Initialized)
                        {
                            manager.StartListeningForPlayers(false);
                            expected.Listening = true;
                        }
                    }
                    else if (op < 90)
                    {
                        if (!expected.Disposed)
                        {
                            manager.StopListeningForPlayers();
                            expected.Listening = false;
                        }
                    }
                    else if (!expected.Disposed)
                    {
                        manager.Dispose();
                        expected.Disposed = true;
                        expected.Listening = false;
                        expected.Players = 0;
                    }

                    if (manager.IsDisposed != expected.Disposed ||
                        manager.IsListeningForPlayers != expected.Listening ||
                        manager.ActivePlayerCount != expected.Players)
                    {
                        violations++;
                    }

                    int alive = 0;
                    for (int i = 0; i < managerCount; i++) if (!model[i].Disposed) alive++;
                    if (InputManager.ActiveManagerCount != alive) violations++;
                }

                totalSteps += steps;
                totalViolations += violations;
                PerfOutput.Row(File, Header, new[]
                {
                    PerfOutput.RunId, round.ToString(), seed.ToString(), managerCount.ToString(), steps.ToString(),
                    "fuzzViolations", violations.ToString(), "count"
                });
                PerfOutput.Row(File, Header, new[]
                {
                    PerfOutput.RunId, round.ToString(), seed.ToString(), managerCount.ToString(), steps.ToString(),
                    "fuzzSteps", steps.ToString(), "count"
                });
            }

            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "all", SeedBase.ToString(), "-", totalSteps.ToString(),
                "totalViolations", totalViolations.ToString(), "count"
            });

            Assert.That(totalViolations, Is.Zero);
        }

        [Test]
        public void Soak_ListenerTogglesStayFlatAndCyclesReturnToBaseline()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            string yaml = MinimalYaml();
            var manager = new InputManager();
            _managers.Add(manager);
            Assert.That(manager.InitializeWithResult(yaml).IsSuccess, Is.True);

            for (int i = 0; i < 200; i++)
            {
                manager.StartListeningForPlayers(false);
                manager.StopListeningForPlayers();
            }

            PerfMeter.ForceFullCollection();
            long toggleBefore = GC.GetTotalMemory(true);
            int collectionsBefore = PerfMeter.TotalCollections();
            for (int i = 0; i < SoakCycles; i++)
            {
                manager.StartListeningForPlayers(false);
                manager.StopListeningForPlayers();
            }

            PerfMeter.ForceFullCollection();
            long toggleDelta = GC.GetTotalMemory(true) - toggleBefore;
            int collections = PerfMeter.TotalCollections() - collectionsBefore;

            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "soak", "-", "1", SoakCycles.ToString(),
                "toggleLiveDelta", PerfOutput.Num(toggleDelta), "byte"
            });
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "soak", "-", "1", SoakCycles.ToString(),
                "toggleCollections", PerfOutput.Num(collections), "count"
            });

            Assert.That(manager.IsListeningForPlayers, Is.False);
            Assert.That(toggleDelta, Is.LessThan(64 * 1024), "toggling the listener must not retain memory.");

            const int Cycles = 200;
            int baseline = InputManager.ActiveManagerCount;
            PerfMeter.ForceFullCollection();
            long cycleBefore = GC.GetTotalMemory(true);
            for (int cycle = 0; cycle < Cycles; cycle++)
            {
                var temporary = new InputManager();
                temporary.InitializeWithResult(yaml);
                temporary.StartListeningForPlayers(false);
                temporary.StopListeningForPlayers();
                temporary.Dispose();
            }

            PerfMeter.ForceFullCollection();
            long cycleDelta = GC.GetTotalMemory(true) - cycleBefore;
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "soak", "-", Cycles.ToString(), SoakCycles.ToString(),
                "cycleResidueBytes", PerfOutput.Num(cycleDelta / (double)Cycles), "byte"
            });

            Assert.That(InputManager.ActiveManagerCount, Is.EqualTo(baseline), "the registry must return to its baseline.");
            Assert.That(manager.IsInitialized, Is.True);
        }

        [Test]
        public void Cost_ConstructionAndInitialization()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            string yaml = MinimalYaml();

            double[] construct = PerfMeter.SamplePerCallMicroseconds(32, 64, () =>
            {
                var manager = new InputManager();
                manager.Dispose();
            });

            var candidates = new List<InputManager>(64);
            double[] initialize = PerfMeter.SamplePerCallMicroseconds(32, 4, () =>
            {
                var manager = new InputManager();
                manager.InitializeWithResult(yaml);
                candidates.Add(manager);
            });

            for (int i = 0; i < candidates.Count; i++) candidates[i].Dispose();

            TimingStats constructStats = PerfMeter.Summarize(construct);
            TimingStats initializeStats = PerfMeter.Summarize(initialize);
            WriteCost("construct", constructStats);
            WriteCost("initialize", initializeStats);
        }

        private static void WriteCost(string metric, TimingStats stats)
        {
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "cost", "-", "-", stats.Count.ToString(),
                metric + ".p50Us", PerfOutput.Num(stats.MedianUs), "us"
            });
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "cost", "-", "-", stats.Count.ToString(),
                metric + ".p95Us", PerfOutput.Num(stats.P95Us), "us"
            });
            PerfOutput.Row(File, Header, new[]
            {
                PerfOutput.RunId, "cost", "-", "-", stats.Count.ToString(),
                metric + ".p99Us", PerfOutput.Num(stats.P99Us), "us"
            });
        }

        private InputManager CreateManager(out InputManager second)
        {
            var first = new InputManager();
            var other = new InputManager();
            _managers.Add(first);
            _managers.Add(other);
            string yaml = MinimalYaml();
            Assert.That(first.InitializeWithResult(yaml).IsSuccess, Is.True);
            Assert.That(other.InitializeWithResult(yaml).IsSuccess, Is.True);
            second = other;
            return first;
        }

        private Gamepad AddGamepad(string name)
        {
            Gamepad pad = UIS.AddDevice<Gamepad>(name);
            _devices.Add(pad);
            return pad;
        }

        private static string MinimalYaml()
        {
            var builder = new StringBuilder(700);
            builder.AppendLine("schemaVersion: 1");
            builder.AppendLine("playerSlots:");
            builder.AppendLine("  - playerId: 0");
            builder.AppendLine("    joinAction:");
            builder.AppendLine("      type: Button");
            builder.AppendLine("      action: Join");
            builder.AppendLine("      deviceBindings:");
            builder.AppendLine("        - \"<Gamepad>/start\"");
            builder.AppendLine("      updateMode: EventDriven");
            builder.AppendLine("      longPressMs: 0");
            builder.AppendLine("      longPressValueThreshold: 0.5");
            builder.AppendLine("    controlSchemes:");
            builder.AppendLine("      - name: Gamepad");
            builder.AppendLine("        bindingGroup: Gamepad");
            builder.AppendLine("        deviceRequirements:");
            builder.AppendLine("          - controlPath: \"<Gamepad>\"");
            builder.AppendLine("    contexts:");
            builder.AppendLine("      - name: Gameplay");
            builder.AppendLine("        actionMap: Map0");
            builder.AppendLine("        priority: 0");
            builder.AppendLine("        blocksLowerPriority: true");
            builder.AppendLine("        bindings:");
            builder.AppendLine("          - type: Button");
            builder.AppendLine("            action: Confirm");
            builder.AppendLine("            bindingGroups: Gamepad");
            builder.AppendLine("            deviceBindings:");
            builder.AppendLine("              - \"<Gamepad>/buttonSouth\"");
            builder.AppendLine("            compositeBindings: []");
            builder.AppendLine("            updateMode: EventDriven");
            builder.AppendLine("            longPressMs: 0");
            builder.AppendLine("            longPressValueThreshold: 0.5");
            return builder.ToString();
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

        private sealed class ManagerModel
        {
            public bool Initialized;
            public bool Listening;
            public bool Disposed;
            public int Players;

            public bool ApplyInitialize()
            {
                if (Disposed) return false;
                Initialized = true;
                return true;
            }
        }
    }
}
