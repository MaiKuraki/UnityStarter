using System;
using CycloneGames.InputSystem.Runtime;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Steady-state cost of the running configuration matrix: managed allocation per frame and per-frame
    /// execution time, measured separately for the module's own pump and for a full frame that also drives the
    /// Unity Input System.
    /// </summary>
    public sealed class InputPerfSteadyStateTests
    {
        private const string File = "steady-state.csv";
        private const string Header =
            "runId,scenario,players,contexts,actionsPerContext,totalActions,pollingPct,device,metric,value,unit,detail";

        private const int AllocationIterations = 3000;
        private const int TimingSamples = 1500;
        private const int TimingCallsPerSample = 8;
        private const int WarmupFrames = 400;

        [Test]
        public void Smoke_RigProducesDispatchAndMemoryStats()
        {
            var scenario = new PerfScenario("smoke", 1, 2, 9, 50, DeviceScenario.Gamepad);
            using PerfRig rig = PerfRig.Create(scenario);

            for (int frame = 0; frame < 30; frame++) rig.Frame((frame % 10) / 10f, frame / 60d);

            InputPlayerMemoryStats stats = rig.First.GetMemoryStats();
            PerfOutput.WriteEnvironmentHeader(File);
            PerfOutput.Row(
                File,
                Header,
                new[]
                {
                    PerfOutput.RunId, scenario.Name, "1", "2", "9", "18", "50", "gamepad",
                    "smoke.actionCount", PerfOutput.Num((long)stats.ActionCount), "count", "rig"
                });
            PerfOutput.Row(
                File,
                Header,
                new[]
                {
                    PerfOutput.RunId, scenario.Name, "1", "2", "9", "18", "50", "gamepad",
                    "smoke.subjectCount", PerfOutput.Num((long)stats.SubjectCount), "count", "rig"
                });
            PerfOutput.Row(
                File,
                Header,
                new[]
                {
                    PerfOutput.RunId, scenario.Name, "1", "2", "9", "18", "50", "gamepad",
                    "smoke.activeContextCount", PerfOutput.Num((long)stats.ActiveContextCount), "count", "rig"
                });

            Gamepad pad = null;
            for (int i = 0; i < rig.Devices.Count; i++)
            {
                if (rig.Devices[i] is Gamepad candidate) pad = candidate;
            }

            PerfOutput.Log(
                "smoke: actions=" + stats.ActionCount +
                " subjects=" + stats.SubjectCount +
                " activeContexts=" + stats.ActiveContextCount +
                " polling=" + stats.PollingActionCount +
                " holds=" + stats.HoldStateCount +
                " unitCallbacks=" + rig.Sink.UnitCount +
                " vectorSum=" + rig.Sink.Vector2Sum +
                " scalarSum=" + rig.Sink.ScalarSum +
                " boolToggles=" + rig.Sink.BoolToggles);
            PerfOutput.Log(
                "smoke.device: stick=" + (pad != null ? pad.leftStick.ReadValue().ToString() : "null") +
                " trigger=" + (pad != null ? pad.rightTrigger.ReadValue().ToString() : "null") +
                " buttonSouth=" + (pad != null ? pad.buttonSouth.isPressed.ToString() : "null") +
                " added=" + (pad != null ? pad.added.ToString() : "null"));

            bool readOk = rig.First.TryReadValue<UnityEngine.Vector2>("Ctx0", "Map0", "A1", out UnityEngine.Vector2 readValue);
            bool buttonOk = rig.First.TryReadValue<float>("Ctx0", "Map0", "A2", out float scalarValue);
            PerfOutput.Log(
                "smoke.read: moveOk=" + readOk + " move=" + readValue +
                " floatOk=" + buttonOk + " float=" + scalarValue);

            Assert.That(stats.ActionCount, Is.EqualTo(18));
            Assert.That(stats.ActiveContextCount, Is.EqualTo(2));
            Assert.That(stats.SubjectCount, Is.GreaterThan(0));
        }

        [Test]
        public void SteadyState_PlayerScaling() => MeasureGroup(PerfMatrix.PlayerScaling);

        [Test]
        public void SteadyState_ActionScaling() => MeasureGroup(PerfMatrix.ActionScaling);

        [Test]
        public void SteadyState_ContextScaling() => MeasureGroup(PerfMatrix.ContextScaling);

        [Test]
        public void SteadyState_PollingScaling() => MeasureGroup(PerfMatrix.PollingScaling);

        [Test]
        public void SteadyState_DeviceScaling() => MeasureGroup(PerfMatrix.DeviceScaling);

        private static void MeasureGroup(IReadOnlyList<PerfScenario> scenarios)
        {
            PerfOutput.WriteEnvironmentHeader(File);
            AllocationInstrument.Initialize();

            for (int i = 0; i < scenarios.Count; i++)
            {
                MeasureOne(scenarios[i]);
                // Each configuration is torn down before the next one is built so that heap-delta measurements
                // do not inherit garbage from the previous configuration.
                PerfMeter.ForceFullCollection();
            }
        }

        private static void MeasureOne(PerfScenario scenario)
        {
            using PerfRig rig = PerfRig.Create(scenario);

            double timestamp = 0d;
            for (int frame = 0; frame < WarmupFrames; frame++)
            {
                timestamp += 1d / 60d;
                rig.Frame((frame % 20) / 20f, timestamp);
            }

            InputPlayerMemoryStats stats = rig.First.GetMemoryStats();
            WriteRow(scenario, "stats.actionCount", stats.ActionCount, "count", "perPlayer");
            WriteRow(scenario, "stats.subjectCount", stats.SubjectCount, "count", "perPlayer");
            WriteRow(scenario, "stats.pollingActionCount", stats.PollingActionCount, "count", "perPlayer");
            WriteRow(scenario, "stats.holdStateCount", stats.HoldStateCount, "count", "perPlayer");
            WriteRow(scenario, "stats.activeContextCount", stats.ActiveContextCount, "count", "perPlayer");

            // Module-only pump: polling reads plus hold-progress evaluation, no Input System update.
            double pumpTimestamp = timestamp;
            AllocationSample.Result pumpAlloc = AllocationSample.Measure(
                () =>
                {
                    pumpTimestamp += 1d / 60d;
                    rig.PumpOnly(pumpTimestamp);
                },
                AllocationIterations);
            WriteAllocation(scenario, "pump", pumpAlloc);

            double pumpTime = pumpTimestamp;
            double[] pumpSamples = PerfMeter.SamplePerCallMicroseconds(
                TimingSamples,
                TimingCallsPerSample,
                () =>
                {
                    pumpTime += 1d / 60d;
                    rig.PumpOnly(pumpTime);
                });
            WriteTiming(scenario, "pump", pumpSamples, "modulePumpOnly");

            // Full frame: Input System update plus the module pump, without product callbacks removed.
            float phase = 0f;
            double frameTimestamp = timestamp;
            AllocationSample.Result frameAlloc = AllocationSample.Measure(
                () =>
                {
                    phase = phase > 1f ? 0f : phase + 0.1f;
                    frameTimestamp += 1d / 60d;
                    rig.Frame(phase, frameTimestamp);
                },
                Math.Max(256, AllocationIterations / 4));
            WriteAllocation(scenario, "frameNoDispatch", frameAlloc);

            float timePhase = 0f;
            double timeTimestamp = timestamp;
            double[] frameSamples = PerfMeter.SamplePerCallMicroseconds(
                Math.Max(256, TimingSamples / 4),
                1,
                () =>
                {
                    timePhase = timePhase > 1f ? 0f : timePhase + 0.1f;
                    timeTimestamp += 1d / 60d;
                    rig.Frame(timePhase, timeTimestamp);
                });
            WriteTiming(scenario, "frameNoDispatch", frameSamples, "unityInputSystemEditorUpdatePlusPump");

            PerfOutput.Log(
                "steady " + scenario.Describe() +
                " pumpAlloc=" + PerfOutput.Num(pumpAlloc.MedianBytesPerIteration, 3) + "B" +
                " (exact=" + pumpAlloc.IsExact + ", clean=" + pumpAlloc.CleanWindows + ", dirty=" + pumpAlloc.DirtyWindows + ")" +
                " pumpP50=" + PerfOutput.Num(PerfMeter.Summarize(pumpSamples).MedianUs, 3) + "us" +
                " pumpP95=" + PerfOutput.Num(PerfMeter.Summarize(pumpSamples).P95Us, 3) + "us" +
                " pumpP99=" + PerfOutput.Num(PerfMeter.Summarize(pumpSamples).P99Us, 3) + "us" +
                " frameAlloc=" + PerfOutput.Num(frameAlloc.MedianBytesPerIteration, 1) + "B" +
                " frameP50=" + PerfOutput.Num(PerfMeter.Summarize(frameSamples).MedianUs, 3) + "us" +
                " frameP95=" + PerfOutput.Num(PerfMeter.Summarize(frameSamples).P95Us, 3) + "us");

            Assert.That(stats.ActionCount, Is.EqualTo(scenario.ActionsPerPlayer));
        }

        /// <summary>
        /// Reference measurement of the Unity Input System update in isolation, used to attribute the full frame
        /// figure between the module and its host.
        /// </summary>
        [Test]
        public void SteadyState_UnityInputSystemBaseline()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            var scenario = new PerfScenario("baseline", 2, 3, 64, 50, DeviceScenario.Gamepad);
            using PerfRig rig = PerfRig.Create(scenario, pushContexts: false, subscribe: false, maxPlayers: 0);

            float phase = 0f;
            for (int i = 0; i < 200; i++)
            {
                phase = phase > 1f ? 0f : phase + 0.1f;
                rig.UnityInputUpdateOnly(phase);
            }

            AllocationSample.Result alloc = AllocationSample.Measure(
                () =>
                {
                    phase = phase > 1f ? 0f : phase + 0.1f;
                    rig.UnityInputUpdateOnly(phase);
                },
                512);

            double[] samples = PerfMeter.SamplePerCallMicroseconds(
                512,
                1,
                () =>
                {
                    phase = phase > 1f ? 0f : phase + 0.1f;
                    rig.UnityInputUpdateOnly(phase);
                });

            TimingStats stats = PerfMeter.Summarize(samples);
            WriteRow(scenario, "unityBaseline.allocation", alloc.MedianBytesPerIteration, "byte", "noPlayersNoBindings");
            WriteRow(scenario, "unityBaseline.p50", stats.MedianUs, "us", "noPlayersNoBindings");
            WriteRow(scenario, "unityBaseline.p95", stats.P95Us, "us", "noPlayersNoBindings");
            WriteRow(scenario, "unityBaseline.p99", stats.P99Us, "us", "noPlayersNoBindings");

            PerfOutput.Log(
                "unity baseline (2 gamepads, no players): alloc=" + PerfOutput.Num(alloc.MedianBytesPerIteration, 1) +
                "B p50=" + PerfOutput.Num(stats.MedianUs, 3) + "us p95=" + PerfOutput.Num(stats.P95Us, 3) + "us");

            Assert.That(rig.Players.Count, Is.Zero);
        }

        private static void WriteAllocation(PerfScenario scenario, string label, AllocationSample.Result result)
        {
            WriteRow(scenario, label + ".allocationMedian", result.MedianBytesPerIteration, "byte", result.Kind.ToString());
            WriteRow(scenario, label + ".allocationMin", result.MinBytesPerIteration, "byte", result.Kind.ToString());
            WriteRow(scenario, label + ".allocationMax", result.MaxBytesPerIteration, "byte", result.Kind.ToString());
            WriteRow(scenario, label + ".cleanWindows", result.CleanWindows, "count", result.Kind.ToString());
            WriteRow(scenario, label + ".dirtyWindows", result.DirtyWindows, "count", result.Kind.ToString());
            WriteRow(scenario, label + ".collectionDelta", result.CollectionDelta, "count", result.Kind.ToString());
            WriteRow(scenario, label + ".nurseryBudget", result.NurseryBudgetBytes, "byte", result.Kind.ToString());
        }

        private static void WriteTiming(PerfScenario scenario, string label, double[] samples, string detail)
        {
            TimingStats stats = PerfMeter.Summarize(samples);
            WriteRow(scenario, label + ".p50", stats.MedianUs, "us", detail);
            WriteRow(scenario, label + ".p95", stats.P95Us, "us", detail);
            WriteRow(scenario, label + ".p99", stats.P99Us, "us", detail);
            WriteRow(scenario, label + ".mean", stats.MeanUs, "us", detail);
            WriteRow(scenario, label + ".min", stats.MinUs, "us", detail);
            WriteRow(scenario, label + ".max", stats.MaxUs, "us", detail);
            WriteRow(scenario, label + ".stdDev", stats.StdDevUs, "us", detail);
            WriteRow(scenario, label + ".samples", stats.Count, "count", detail);
        }

        private static void WriteRow(PerfScenario scenario, string metric, double value, string unit, string detail)
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
                    scenario.TotalActions.ToString(),
                    scenario.PollingPercent.ToString(),
                    scenario.Device.ToString(),
                    metric,
                    PerfOutput.Num(value, 4),
                    unit,
                    detail
                });
        }
    }
}
