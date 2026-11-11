using System;
using System.Collections;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using R3;
using UnityEngine.TestTools;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Allocation and object-size measurement driven by the "GC Allocated In Frame" profiler statistic, the only
    /// allocation instrument on this host that tracks a known garbage volume to within two percent. Every scenario
    /// is paired with a zero-player control measured through the same code path, so the module's own per-frame
    /// allocation is the difference between the two, not the raw frame figure.
    /// </summary>
    public sealed class InputPerfFrameAllocationTests
    {
        private const string File = "frame-allocation.csv";
        private const string Header = "runId,scenario,metric,value,unit,detail";

        private const int WarmupFrames = 24;
        private const int MeasuredFrames = 60;
        private const int PumpsPerFrame = 32;
        private const int ControlAllocationBytes = 64 * 1024;

        [UnityTest]
        public IEnumerator FrameAllocation_MatrixWithRecorder()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            bool started = AllocationInstrument.TryStartFrameRecorder();
            Assert.That(started, Is.True, "The GC Allocated In Frame recorder must be available for this measurement.");

            try
            {
                var baseline = new PerfScenario("FA-P0", 0, 3, 64, 50, DeviceScenario.Gamepad);
                using (PerfRig control = PerfRig.Create(baseline, maxPlayers: 0))
                {
                    yield return MeasureInner(baseline, control, "zeroPlayerControl");
                }

                var scenarios = new[]
                {
                    new PerfScenario("FA-P1", 1, 3, 64, 50, DeviceScenario.Gamepad),
                    new PerfScenario("FA-P4", 4, 3, 64, 50, DeviceScenario.Gamepad),
                    new PerfScenario("FA-P8", 8, 3, 64, 50, DeviceScenario.Gamepad),
                    new PerfScenario("FA-Q0", 4, 3, 64, 0, DeviceScenario.Gamepad),
                    new PerfScenario("FA-Q100", 4, 3, 64, 100, DeviceScenario.Gamepad),
                    new PerfScenario("FA-A256", 2, 3, 256, 50, DeviceScenario.Gamepad),
                    new PerfScenario("FA-D-kbm", 2, 3, 64, 50, DeviceScenario.KeyboardMouse),
                    new PerfScenario("FA-C6", 2, 6, 64, 50, DeviceScenario.Gamepad)
                };

                for (int i = 0; i < scenarios.Length; i++)
                {
                    using (PerfRig rig = PerfRig.Create(scenarios[i]))
                    {
                        yield return MeasureInner(scenarios[i], rig, "module");
                    }
                }

                yield return MeasureObjectSize("SubjectUnit", 2048, i => new Subject<Unit>());
                yield return MeasureObjectSize("BehaviorSubjectBool", 2048, i => new BehaviorSubject<bool>(false));
                yield return MeasureObjectSize("ReactivePropertyString", 512, i => new ReactiveProperty<string>(null));
                yield return MeasureObjectSize("InputContext", 512, i => new InputContext("Map" + (i % 4), "Ctx" + (i % 4)));
                yield return MeasureObjectSize("ByteArray1024", 1024, i => new byte[1024]);
                yield return MeasureOperationAllocation();
            }
            finally
            {
                AllocationInstrument.StopFrameRecorder();
            }
        }

        private static IEnumerator MeasureInner(PerfScenario scenario, PerfRig rig, string detail)
        {
            double timestamp = 0d;
            for (int frame = 0; frame < WarmupFrames; frame++)
            {
                for (int pump = 0; pump < PumpsPerFrame; pump++)
                {
                    timestamp += 1d / 60d;
                    rig.PumpOnly(timestamp);
                }

                yield return null;
            }

            long sumBefore = AllocationInstrument.FrameRecorderSampleSum();
            for (int frame = 0; frame < MeasuredFrames; frame++)
            {
                for (int pump = 0; pump < PumpsPerFrame; pump++)
                {
                    timestamp += 1d / 60d;
                    rig.PumpOnly(timestamp);
                }

                yield return null;
            }

            long total = AllocationInstrument.FrameRecorderSampleSum() - sumBefore;

            // Control frame: the same pump work plus a known 64 KiB allocation, proving the recorder window is
            // live and correctly attributed.
            long controlBefore = AllocationInstrument.FrameRecorderSampleSum();
            for (int pump = 0; pump < PumpsPerFrame; pump++)
            {
                timestamp += 1d / 60d;
                rig.PumpOnly(timestamp);
            }

            var control = new byte[ControlAllocationBytes];
            control[0] = 1;
            yield return null;
            yield return null;
            long controlFrameBytes = AllocationInstrument.FrameRecorderSampleSum() - controlBefore;
            GC.KeepAlive(control);

            double bytesPerFrame = total / (double)MeasuredFrames;
            double bytesPerPumpCall = total / (double)(MeasuredFrames * PumpsPerFrame);

            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, scenario.Name, "players", PerfOutput.Num(scenario.Players), "count", detail });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, scenario.Name, "frames", PerfOutput.Num(MeasuredFrames), "count", "pumpsPerFrame=" + PumpsPerFrame });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, scenario.Name, "totalBytes", PerfOutput.Num(total), "byte", detail });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, scenario.Name, "bytesPerFrame", PerfOutput.Num(bytesPerFrame, 2), "byte", detail });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, scenario.Name, "bytesPerPumpCall", PerfOutput.Num(bytesPerPumpCall, 3), "byte", detail });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, scenario.Name, "controlFrameBytes", PerfOutput.Num(controlFrameBytes), "byte", "expectedAtLeast=" + ControlAllocationBytes });

            PerfOutput.Log(
                "frameAlloc " + scenario.Describe() +
                " frames=" + MeasuredFrames +
                " total=" + total + "B" +
                " perFrame=" + PerfOutput.Num(bytesPerFrame, 2) + "B" +
                " perPumpCall=" + PerfOutput.Num(bytesPerPumpCall, 3) + "B" +
                " controlFrame=" + controlFrameBytes + "B");

            Assert.That(
                controlFrameBytes,
                Is.GreaterThanOrEqualTo(ControlAllocationBytes),
                "The recorder did not observe the control allocation, so this frame window is not trustworthy.");
        }

        /// <summary>
        /// Allocation per context-projection operation, measured inside profiled frames. These operations rebuild
        /// the Unity action-map projection, so they are the most allocation-sensitive part of the module.
        /// </summary>
        private static IEnumerator MeasureOperationAllocation()
        {
            var scenario = new PerfScenario("OA-C3", 1, 3, 64, 50, DeviceScenario.Gamepad);
            using PerfRig rig = PerfRig.Create(scenario, pushContexts: false, subscribe: true);
            InputPlayer player = rig.First;

            var baseContext = new InputContext("Map0", "Ctx0", 0, false);
            var toggled = new InputContext("Map1", "Ctx1", 1, false);
            var captureTarget = new InputContext("Map0", "Ctx0", 0, false);
            player.PushContext(baseContext);
            yield return null;

            yield return MeasureOperation("pushPop", 20, 20, () =>
            {
                player.PushContext(toggled);
                player.PopContext();
            });

            yield return MeasureOperation("refresh", 20, 20, player.RefreshActiveContext);

            yield return MeasureOperation("capture", 20, 20, () =>
            {
                using (player.CaptureContext(captureTarget))
                {
                }
            });

            yield return MeasureOperation("blockUnblock", 20, 20, () =>
            {
                player.BlockInput();
                player.UnblockInput();
            });

            player.RemoveContext(baseContext);
        }

        private static IEnumerator MeasureOperation(string label, int frames, int operationsPerFrame, Action operation)
        {
            for (int frame = 0; frame < 8; frame++)
            {
                for (int op = 0; op < operationsPerFrame; op++) operation();
                yield return null;
            }

            long before = AllocationInstrument.FrameRecorderSampleSum();
            for (int frame = 0; frame < frames; frame++)
            {
                for (int op = 0; op < operationsPerFrame; op++) operation();
                yield return null;
            }

            long after = AllocationInstrument.FrameRecorderSampleSum();
            long total = after - before;
            double perOperation = total / (double)(frames * operationsPerFrame);

            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "operation-" + label, "operations", PerfOutput.Num(frames * operationsPerFrame), "count", "contexts=3,actions=192" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "operation-" + label, "totalBytes", PerfOutput.Num(total), "byte", "contexts=3,actions=192" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "operation-" + label, "bytesPerOperation", PerfOutput.Num(perOperation, 2), "byte", "contexts=3,actions=192" });
            PerfOutput.Log("operationAlloc " + label + "=" + PerfOutput.Num(perOperation, 2) + "B (" + (frames * operationsPerFrame) + " operations)");
        }

        /// <summary>
        /// Object sizes measured as allocation inside one profiled frame, which avoids the reserved-heap ruler that
        /// cannot see sub-megabyte allocations once the process heap has grown.
        /// </summary>
        private static IEnumerator MeasureObjectSize(string label, int count, Func<int, object> factory)
        {
            var holder = new object[count];
            for (int warm = 0; warm < count; warm++) holder[warm] = factory(warm);
            GC.KeepAlive(holder);
            yield return null;
            yield return null;

            long before = AllocationInstrument.FrameRecorderSampleSum();
            for (int i = 0; i < count; i++) holder[i] = factory(i);
            GC.KeepAlive(holder);
            yield return null;
            yield return null;
            long after = AllocationInstrument.FrameRecorderSampleSum();

            double perInstance = (after - before) / (double)count;
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "size-" + label, "allocationPerInstance", PerfOutput.Num(perInstance, 2), "byte", "count=" + count });
            PerfOutput.Log("objectSize " + label + "=" + PerfOutput.Num(perInstance, 2) + "B (" + count + " instances)");

            Assert.That(perInstance, Is.GreaterThan(0d), "Object size probe for " + label + " did not observe allocation.");
        }
    }
}
