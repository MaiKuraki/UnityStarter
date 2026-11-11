using System;
using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Cost of context projection changes: push, pop, capture scope, block scope and forced refresh. The module
    /// rebuilds the Unity action-map projection on every change, so this is the most expensive non-frame
    /// operation and the one most exposed to bursts.
    /// </summary>
    public sealed class InputPerfContextSwitchTests
    {
        private const string File = "context-switch.csv";
        private const string Header =
            "runId,scenario,contexts,actionsPerContext,metric,value,unit,detail";

        private const int TimingSamples = 300;
        private const int AllocationIterations = 300;

        [Test]
        public void ContextSwitch_PushPopCaptureBlock()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            AllocationInstrument.Initialize();

            var scenarios = new[]
            {
                new PerfScenario("S-C1", 1, 1, 64, 50, DeviceScenario.Gamepad),
                new PerfScenario("S-C3", 1, 3, 64, 50, DeviceScenario.Gamepad),
                new PerfScenario("S-C6", 1, 6, 64, 50, DeviceScenario.Gamepad),
                new PerfScenario("S-A256", 1, 3, 256, 50, DeviceScenario.Gamepad)
            };

            for (int i = 0; i < scenarios.Length; i++) MeasureScenario(scenarios[i]);
        }

        private static void MeasureScenario(PerfScenario scenario)
        {
            using PerfRig rig = PerfRig.Create(scenario, pushContexts: false, subscribe: true);
            InputPlayer player = rig.First;

            // Keep one context permanently on the stack so that every push transitions between two projections.
            var baseContext = new InputContext("Map0", "Ctx0", 0, false);
            player.PushContext(baseContext);

            int toggleIndex = scenario.Contexts > 1 ? 1 : 0;
            var toggled = new InputContext("Map" + toggleIndex, "Ctx" + toggleIndex, toggleIndex, false);

            for (int warm = 0; warm < 32; warm++)
            {
                player.PushContext(toggled);
                player.PopContext();
            }

            double[] pushPop = PerfMeter.SamplePerCallMicroseconds(
                TimingSamples,
                1,
                () =>
                {
                    player.PushContext(toggled);
                    player.PopContext();
                });
            WriteTiming(scenario, pushPop, "pushPop", "onePushPlusOnePop");

            AllocationSample.Result pushPopAlloc = AllocationSample.Measure(
                () =>
                {
                    player.PushContext(toggled);
                    player.PopContext();
                },
                AllocationIterations,
                warmupIterations: 64);
            WriteAllocation(scenario, pushPopAlloc, "pushPop", "onePushPlusOnePop");

            double[] refresh = PerfMeter.SamplePerCallMicroseconds(
                TimingSamples,
                1,
                player.RefreshActiveContext);
            WriteTiming(scenario, refresh, "refresh", "noStateChange");

            AllocationSample.Result refreshAlloc = AllocationSample.Measure(
                player.RefreshActiveContext,
                AllocationIterations,
                warmupIterations: 64);
            WriteAllocation(scenario, refreshAlloc, "refresh", "noStateChange");

            var captureTarget = new InputContext("Map0", "Ctx0", 0, false);
            for (int warm = 0; warm < 16; warm++)
            {
                using (player.CaptureContext(captureTarget))
                {
                }
            }

            double[] capture = PerfMeter.SamplePerCallMicroseconds(
                TimingSamples,
                1,
                () =>
                {
                    using (player.CaptureContext(captureTarget))
                    {
                    }
                });
            WriteTiming(scenario, capture, "capture", "captureScopeEnterExit");

            AllocationSample.Result captureAlloc = AllocationSample.Measure(
                () =>
                {
                    using (player.CaptureContext(captureTarget))
                    {
                    }
                },
                AllocationIterations,
                warmupIterations: 64);
            WriteAllocation(scenario, captureAlloc, "capture", "captureScopeEnterExit");

            for (int warm = 0; warm < 16; warm++)
            {
                player.BlockInput();
                player.UnblockInput();
            }

            double[] block = PerfMeter.SamplePerCallMicroseconds(
                TimingSamples,
                1,
                () =>
                {
                    player.BlockInput();
                    player.UnblockInput();
                });
            WriteTiming(scenario, block, "block", "oneBlockPlusOneUnblock");

            AllocationSample.Result blockAlloc = AllocationSample.Measure(
                () =>
                {
                    player.BlockInput();
                    player.UnblockInput();
                },
                AllocationIterations,
                warmupIterations: 64);
            WriteAllocation(scenario, blockAlloc, "block", "oneBlockPlusOneUnblock");

            player.RemoveContext(baseContext);
        }

        private static void WriteTiming(PerfScenario scenario, double[] samples, string label, string detail)
        {
            TimingStats stats = PerfMeter.Summarize(samples);
            Write(scenario, label + ".p50", stats.MedianUs, "us", detail);
            Write(scenario, label + ".p95", stats.P95Us, "us", detail);
            Write(scenario, label + ".p99", stats.P99Us, "us", detail);
            Write(scenario, label + ".max", stats.MaxUs, "us", detail);
            PerfOutput.Log(
                "context " + scenario.Describe() + " " + label +
                " p50=" + PerfOutput.Num(stats.MedianUs, 2) + "us" +
                " p95=" + PerfOutput.Num(stats.P95Us, 2) + "us" +
                " p99=" + PerfOutput.Num(stats.P99Us, 2) + "us");
        }

        private static void WriteAllocation(PerfScenario scenario, AllocationSample.Result result, string label, string detail)
        {
            Write(scenario, label + ".allocation", result.MedianBytesPerIteration, "byte", detail + "|" + result.Kind);
            Write(scenario, label + ".collections", result.CollectionDelta, "count", detail);
            PerfOutput.Log(
                "context " + scenario.Describe() + " " + label +
                " alloc=" + PerfOutput.Num(result.MedianBytesPerIteration, 1) + "B" +
                " exact=" + result.IsExact +
                " collections=" + result.CollectionDelta);
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
                    scenario.Contexts.ToString(),
                    scenario.ActionsPerContext.ToString(),
                    metric,
                    PerfOutput.Num(value, 3),
                    unit,
                    detail
                });
        }
    }
}
