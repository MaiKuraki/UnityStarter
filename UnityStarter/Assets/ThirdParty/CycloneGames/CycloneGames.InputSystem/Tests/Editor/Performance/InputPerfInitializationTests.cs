using System;
using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Initialization and retention cost: YAML parse, validation, preflight, action-graph construction, and player
    /// construction. These are one-time costs, so they are reported separately from steady-state figures.
    /// </summary>
    public sealed class InputPerfInitializationTests
    {
        private const string File = "initialization.csv";
        private const string Header =
            "runId,scenario,players,contexts,actionsPerContext,totalActions,metric,value,unit,detail";

        private const int RepeatCount = 5;

        // Registry preflight dominates this one-time cost (see cost-breakdown.csv). The largest scenarios
        // need several full initializations for a stable median, so the default 180 s test-runner budget
        // is too tight on a loaded editor; keep the measurement and widen the budget explicitly.
        [Test]
        [Timeout(300000)]
        public void Initialization_ManagerInitializeCost()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            AllocationInstrument.Initialize();

            var scenarios = new[]
            {
                new PerfScenario("I-A10", 1, 3, 10, 50, DeviceScenario.Gamepad),
                new PerfScenario("I-A64", 2, 3, 64, 50, DeviceScenario.Gamepad),
                new PerfScenario("I-A128", 2, 3, 128, 50, DeviceScenario.Gamepad),
                new PerfScenario("I-C6", 2, 6, 64, 50, DeviceScenario.Gamepad)
            };

            for (int scenarioIndex = 0; scenarioIndex < scenarios.Length; scenarioIndex++)
            {
                PerfScenario scenario = scenarios[scenarioIndex];
                var slots = new List<PlayerSlotConfig>(scenario.Players);
                for (int playerIndex = 0; playerIndex < scenario.Players; playerIndex++)
                {
                    slots.Add(PerfScenarioBuilder.BuildPlayerSlot(scenario, playerIndex));
                }

                string yaml = PerfScenarioBuilder.SerializeConfiguration(slots);
                double[] samples = new double[RepeatCount];
                long[] retained = new long[RepeatCount];

                for (int repeat = 0; repeat < RepeatCount; repeat++)
                {
                    PerfMeter.ForceFullCollection();
                    long before = PerfMeter.LiveHeapBytes();
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();

                    var manager = new InputManager(PerfScenarioBuilder.BuildLimits());
                    InputManagerInitializationResult result = manager.InitializeWithResult(yaml);

                    long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                    long after = PerfMeter.LiveHeapBytes();

                    Assert.That(result.IsSuccess, Is.True, result.Message);

                    samples[repeat] = elapsed * PerfMeter.TicksToMicroseconds;
                    retained[repeat] = after - before;
                    manager.Dispose();
                }

                TimingStats stats = PerfMeter.Summarize(samples);
                long retainedMedian = Median(retained);

                AllocationSample.Result allocation = AllocationSample.Measure(
                    () =>
                    {
                        var probe = new InputManager(PerfScenarioBuilder.BuildLimits());
                        probe.InitializeWithResult(yaml);
                        probe.Dispose();
                    },
                    6,
                    warmupIterations: 2);

                Write(scenario, "yamlLength", yaml.Length, "char", "serialized");
                Write(scenario, "init.p50", stats.MedianUs, "us", "managerInitialize");
                Write(scenario, "init.p95", stats.P95Us, "us", "managerInitialize");
                Write(scenario, "init.max", stats.MaxUs, "us", "managerInitialize");
                Write(scenario, "init.retained", retainedMedian, "byte", "reservedHeapDeltaAfterInitialize");
                Write(scenario, "init.allocated", allocation.MedianBytesPerIteration, "byte", "allocationPerInitialize|" + allocation.Kind);

                PerfOutput.Log(
                    "init " + scenario.Describe() +
                    " yaml=" + yaml.Length + "chars" +
                    " p50=" + PerfOutput.Num(stats.MedianUs, 1) + "us" +
                    " p95=" + PerfOutput.Num(stats.P95Us, 1) + "us" +
                    " retained=" + retainedMedian + "B" +
                    " allocatedPerInit=" + PerfOutput.Num(allocation.MedianBytesPerIteration, 1) + "B");
            }
        }

        [Test]
        public void Initialization_PlayerConstructionCost()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            AllocationInstrument.Initialize();

            var scenarios = new[]
            {
                new PerfScenario("J-A10", 1, 3, 10, 50, DeviceScenario.Gamepad),
                new PerfScenario("J-A64", 1, 3, 64, 50, DeviceScenario.Gamepad),
                new PerfScenario("J-A256", 1, 3, 256, 50, DeviceScenario.Gamepad),
                new PerfScenario("J-C6", 1, 6, 64, 50, DeviceScenario.Gamepad)
            };

            for (int scenarioIndex = 0; scenarioIndex < scenarios.Length; scenarioIndex++)
            {
                PerfScenario scenario = scenarios[scenarioIndex];
                PlayerSlotConfig slot = PerfScenarioBuilder.BuildPlayerSlot(scenario, 0);
                InputConfigurationLimits limits = PerfScenarioBuilder.BuildLimits();
                double[] samples = new double[RepeatCount];
                long[] retained = new long[RepeatCount];

                for (int repeat = 0; repeat < RepeatCount; repeat++)
                {
                    var gamepad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("JoinPad" + repeat);
                    UnityEngine.InputSystem.Users.InputUser user =
                        UnityEngine.InputSystem.Users.InputUser.PerformPairingWithDevice(gamepad);

                    PerfMeter.ForceFullCollection();
                    long before = PerfMeter.LiveHeapBytes();
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
                    var player = new InputPlayer(0, user, slot, limits, gamepad);
                    long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                    long after = PerfMeter.LiveHeapBytes();

                    samples[repeat] = elapsed * PerfMeter.TicksToMicroseconds;
                    retained[repeat] = after - before;

                    InputPlayerMemoryStats stats = player.GetMemoryStats();
                    player.Dispose();
                    UnityEngine.InputSystem.InputSystem.RemoveDevice(gamepad);

                    if (repeat == 0)
                    {
                        Write(scenario, "subjectsPerPlayer", stats.SubjectCount, "count", "eagerSubjects");
                        Write(scenario, "retained.perAction", (double)retained[repeat] / Math.Max(1, stats.ActionCount), "byte", "liveHeapDeltaPerAction");
                    }
                }

                TimingStats timing = PerfMeter.Summarize(samples);
                Write(scenario, "player.p50", timing.MedianUs, "us", "constructAndWire");
                Write(scenario, "player.p95", timing.P95Us, "us", "constructAndWire");
                Write(scenario, "player.retained", Median(retained), "byte", "liveHeapDeltaPerPlayer");

                PerfOutput.Log(
                    "player " + scenario.Describe() +
                    " p50=" + PerfOutput.Num(timing.MedianUs, 1) + "us" +
                    " p95=" + PerfOutput.Num(timing.P95Us, 1) + "us" +
                    " retained=" + Median(retained) + "B");
            }
        }

        private static long Median(long[] values)
        {
            var sorted = (long[])values.Clone();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }

        private static void Write(PerfScenario scenario, string metric, double value, string unit, string detail)
        {
            PerfOutput.Row(
                File,
                Header,
                new[]
                {
                    PerfOutput.RunId,
                    scenario.Name,
                    scenario.Players.ToString(),
                    scenario.Contexts.ToString(),
                    scenario.ActionsPerContext.ToString(),
                    scenario.ActionsPerPlayer.ToString(),
                    metric,
                    PerfOutput.Num(value, 3),
                    unit,
                    detail
                });
        }
    }
}
