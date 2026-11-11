using System;
using System.Runtime.InteropServices;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using R3;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Data-structure evidence: measured struct and object sizes, plus the cost of each lookup shape the public API
    /// exposes. Sizes are reported in bytes for the current 64-bit Mono editor host.
    /// </summary>
    public sealed class InputPerfLayoutTests
    {
        private const string File = "layout.csv";
        private const string Header = "runId,type,metric,value,unit,detail";

        [Test]
        public void Layout_PublicStructSizes()
        {
            PerfOutput.WriteEnvironmentHeader(File);

            WriteSize("InputPlayerMemoryStats", Marshal.SizeOf<InputPlayerMemoryStats>(), "unmanaged");
            WriteSize("InputManagerMemoryStats", Marshal.SizeOf<InputManagerMemoryStats>(), "unmanaged");
            WriteSize("InputPlayerDeviceStatus", UnsafeUtility.SizeOf<InputPlayerDeviceStatus>(), "managedStringField");
            WriteSize("InputActionKey", UnsafeUtility.SizeOf<InputActionKey>(), "managedThreeReferencesAndCachedHash");
            WriteSize("Vector2", UnsafeUtility.SizeOf<Vector2>(), "unmanaged");

            PerfOutput.Log(
                "layout sizes: playerMemoryStats=" + Marshal.SizeOf<InputPlayerMemoryStats>() +
                " managerMemoryStats=" + Marshal.SizeOf<InputManagerMemoryStats>() +
                " deviceStatus=" + UnsafeUtility.SizeOf<InputPlayerDeviceStatus>() +
                " actionKey=" + UnsafeUtility.SizeOf<InputActionKey>());

            // Three references plus the cached hash word, padded to pointer alignment. The cached hash trades one
            // pointer-sized word per key for a single hash walk at construction instead of one per dictionary probe.
            Assert.That(UnsafeUtility.SizeOf<InputActionKey>(), Is.EqualTo(IntPtr.Size * 4));
        }

        /// <summary>
        /// Lookup cost by identity shape. The string-key path hashes three strings on every call; the integer path
        /// is a single dictionary probe. The ratio quantifies the cost of the convenience overloads.
        /// </summary>
        [Test]
        public void Lookup_CostByKeyShape()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            AllocationInstrument.Initialize();

            var scenarios = new[]
            {
                new PerfScenario("L-A10", 1, 1, 10, 0, DeviceScenario.Gamepad),
                new PerfScenario("L-A64", 1, 1, 64, 0, DeviceScenario.Gamepad),
                new PerfScenario("L-A256", 1, 1, 256, 0, DeviceScenario.Gamepad)
            };

            for (int i = 0; i < scenarios.Length; i++)
            {
                PerfScenario scenario = scenarios[i];
                using PerfRig rig = PerfRig.Create(scenario);
                InputPlayer player = rig.First;

                const string ContextName = "Ctx0";
                const string MapName = "Map0";
                const string ActionName = "A0";
                int actionId = InputHashUtility.GetActionId(ContextName, MapName, ActionName);

                float sink = 0f;
                double[] byName = PerfMeter.SamplePerCallMicroseconds(2000, 16, () =>
                {
                    if (player.TryReadValue<float>(ContextName, MapName, ActionName, out float value)) sink += value;
                });
                double[] byId = PerfMeter.SamplePerCallMicroseconds(2000, 16, () =>
                {
                    if (player.TryReadValue<float>(actionId, out float value)) sink += value;
                });
                double[] observableByName = PerfMeter.SamplePerCallMicroseconds(1000, 8, () =>
                {
                    GC.KeepAlive(player.GetScalarObservable(ActionName));
                });
                double[] observableById = PerfMeter.SamplePerCallMicroseconds(1000, 8, () =>
                {
                    GC.KeepAlive(player.GetScalarObservable(actionId));
                });

                TimingStats named = PerfMeter.Summarize(byName);
                TimingStats id = PerfMeter.Summarize(byId);
                TimingStats obsNamed = PerfMeter.Summarize(observableByName);
                TimingStats obsId = PerfMeter.Summarize(observableById);

                Write("lookup", scenario.ActionsPerContext + ".readByName.p50", named.MedianUs, "us", scenario.Name);
                Write("lookup", scenario.ActionsPerContext + ".readByName.p95", named.P95Us, "us", scenario.Name);
                Write("lookup", scenario.ActionsPerContext + ".readById.p50", id.MedianUs, "us", scenario.Name);
                Write("lookup", scenario.ActionsPerContext + ".readById.p95", id.P95Us, "us", scenario.Name);
                Write("lookup", scenario.ActionsPerContext + ".observableByName.p50", obsNamed.MedianUs, "us", scenario.Name);
                Write("lookup", scenario.ActionsPerContext + ".observableById.p50", obsId.MedianUs, "us", scenario.Name);
                Write("lookup", scenario.ActionsPerContext + ".readNameOverIdRatio", named.MedianUs / Math.Max(1e-9, id.MedianUs), "ratio", scenario.Name);

                PerfOutput.Log(
                    "lookup " + scenario.Name +
                    " readByName=" + PerfOutput.Num(named.MedianUs, 4) + "us" +
                    " readById=" + PerfOutput.Num(id.MedianUs, 4) + "us" +
                    " ratio=" + PerfOutput.Num(named.MedianUs / Math.Max(1e-9, id.MedianUs), 2) +
                    " observableByName=" + PerfOutput.Num(obsNamed.MedianUs, 4) + "us" +
                    " observableById=" + PerfOutput.Num(obsId.MedianUs, 4) + "us" +
                    " sink=" + sink);

                Assert.That(player.TryReadValue<float>(actionId, out _), Is.True);
            }
        }

        /// <summary>
        /// Sensitivity of the string-key lookup to identifier length, which is the direct consequence of hashing
        /// characters on every probe instead of caching the hash in the key.
        /// </summary>
        [Test]
        public void Lookup_CostByIdentifierLength()
        {
            PerfOutput.WriteEnvironmentHeader(File);

            int[] lengths = { 1, 8, 32, 128 };
            for (int i = 0; i < lengths.Length; i++)
            {
                string actionName = new string('n', lengths[i]);
                var scenario = new PerfScenario("Len" + lengths[i], 1, 1, 3, 0, DeviceScenario.Gamepad);
                PlayerSlotConfig slot = PerfScenarioBuilder.BuildPlayerSlot(scenario, 0);
                slot.Contexts[0].Bindings[0].ActionName = actionName;

                var gamepad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("LenPad" + i);
                UnityEngine.InputSystem.Users.InputUser user =
                    UnityEngine.InputSystem.Users.InputUser.PerformPairingWithDevice(gamepad);
                var player = new InputPlayer(0, user, slot, PerfScenarioBuilder.BuildLimits(), gamepad);
                try
                {
                    int actionId = InputHashUtility.GetActionId("Ctx0", "Map0", actionName);
                    double[] byName = PerfMeter.SamplePerCallMicroseconds(1500, 16, () =>
                    {
                        player.TryReadValue<float>("Ctx0", "Map0", actionName, out _);
                    });
                    double[] byId = PerfMeter.SamplePerCallMicroseconds(1500, 16, () =>
                    {
                        player.TryReadValue<float>(actionId, out _);
                    });

                    TimingStats named = PerfMeter.Summarize(byName);
                    TimingStats id = PerfMeter.Summarize(byId);
                    Write("identifierLength", lengths[i] + ".readByName.p50", named.MedianUs, "us", "actionNameChars");
                    Write("identifierLength", lengths[i] + ".readById.p50", id.MedianUs, "us", "actionNameChars");
                    PerfOutput.Log(
                        "identifier length " + lengths[i] +
                        ": byName=" + PerfOutput.Num(named.MedianUs, 4) + "us byId=" + PerfOutput.Num(id.MedianUs, 4) + "us");
                }
                finally
                {
                    player.Dispose();
                    UnityEngine.InputSystem.InputSystem.RemoveDevice(gamepad);
                }
            }

            Assert.Pass();
        }

        private static void WriteSize(string typeName, int bytes, string detail)
        {
            Write("size", typeName, bytes, "byte", detail);
        }

        private static void Write(string section, string metric, double value, string unit, string detail)
        {
            PerfOutput.Row(
                File,
                Header,
                new[] { PerfOutput.RunId, section, metric, PerfOutput.Num(value, 4), unit, detail });
        }
    }
}
