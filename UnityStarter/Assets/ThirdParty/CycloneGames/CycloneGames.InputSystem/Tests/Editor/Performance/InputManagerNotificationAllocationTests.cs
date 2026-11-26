using System;
using System.Collections;
using System.Text;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Allocation on the two manager notifications. Measured as the difference between the same join
    /// sequence with no subscriber and with four, so graph construction cost cancels out.
    /// </summary>
    public sealed class InputManagerNotificationAllocationTests
    {
        private const int Cycles = 64;
        private const int Subscribers = 4;

        private InputManager _manager;
        private Gamepad _pad;

        [TearDown]
        public void TearDown()
        {
            _manager?.Dispose();
            _manager = null;
            if (_pad != null && _pad.added) UIS.RemoveDevice(_pad);
            _pad = null;
            InputManager.ResetGlobalStateForDomainReload();
        }

        [UnityTest]
        public IEnumerator Allocation_PlayerReadyNotification()
        {
            bool started = AllocationInstrument.TryStartFrameRecorder();
            Assert.That(started, Is.True);

            try
            {
                _pad = UIS.AddDevice<Gamepad>("Notify_Pad");
                _manager = new InputManager();
                Assert.That(_manager.InitializeWithResult(MinimalYaml()).IsSuccess, Is.True);

                for (int i = 0; i < 8; i++) JoinAndRemove();

                long withoutSubscribers = 0;
                for (int i = 0; i < 8; i++) yield return null;
                long idleStart = AllocationInstrument.FrameRecorderSampleSum();
                for (int i = 0; i < Cycles; i++)
                {
                    JoinAndRemove();
                    yield return null;
                }
                withoutSubscribers = AllocationInstrument.FrameRecorderSampleSum() - idleStart;

                long controlStart = AllocationInstrument.FrameRecorderSampleSum();
                for (int i = 0; i < Cycles; i++)
                {
                    JoinAndRemove();
                    yield return null;
                }
                long controlRepeat = AllocationInstrument.FrameRecorderSampleSum() - controlStart;
                double noise = (controlRepeat - withoutSubscribers) / (double)(Cycles * Subscribers);

                Action<IInputPlayer> handler = _ => { };
                for (int i = 0; i < Subscribers; i++) _manager.OnPlayerInputReady += handler;

                long subscribedStart = AllocationInstrument.FrameRecorderSampleSum();
                for (int i = 0; i < Cycles; i++)
                {
                    JoinAndRemove();
                    yield return null;
                }
                long withSubscribers = AllocationInstrument.FrameRecorderSampleSum() - subscribedStart;
                double perNotification = (withSubscribers - withoutSubscribers) / (double)(Cycles * Subscribers);
                double budget = Math.Max(4d, Math.Abs(noise) * 3d);

                PerfOutput.WriteEnvironmentHeader("notification-verify.csv");
                PerfOutput.Row("notification-verify.csv", "runId,cycles,subscribers,metric,value,unit", new[]
                {
                    PerfOutput.RunId, Cycles.ToString(), Subscribers.ToString(),
                    "playerReadyBytesPerNotification", PerfOutput.Num(perNotification), "byte"
                });
                PerfOutput.Row("notification-verify.csv", "runId,cycles,subscribers,metric,value,unit", new[]
                {
                    PerfOutput.RunId, Cycles.ToString(), "0", "baselineBytes", PerfOutput.Num(withoutSubscribers), "byte"
                });

                PerfOutput.Row("notification-verify.csv", "runId,cycles,subscribers,metric,value,unit", new[]
                {
                    PerfOutput.RunId, Cycles.ToString(), Subscribers.ToString(),
                    "controlNoiseBytes", PerfOutput.Num(noise), "byte"
                });

                Assert.That(perNotification, Is.LessThan(budget),
                    "a notification must not allocate per subscriber beyond frame-recorder noise.");
            }
            finally
            {
                AllocationInstrument.StopFrameRecorder();
            }

            yield return null;
        }

        private void JoinAndRemove()
        {
            Assert.That(_manager.JoinSinglePlayer(0), Is.Not.Null);
            Assert.That(_manager.RemovePlayer(0), Is.True);
        }

        private static string MinimalYaml()
        {
            var builder = new StringBuilder(600);
            builder.AppendLine("schemaVersion: 1");
            builder.AppendLine("playerSlots:");
            builder.AppendLine("  - playerId: 0");
            builder.AppendLine("    controlSchemes:");
            builder.AppendLine("      - name: Gamepad");
            builder.AppendLine("        bindingGroup: Gamepad");
            builder.AppendLine("        deviceRequirements:");
            builder.AppendLine("          - controlPath: \"<Gamepad>\"");
            builder.AppendLine("    contexts:");
            builder.AppendLine("      - name: Gameplay");
            builder.AppendLine("        actionMap: Map0");
            builder.AppendLine("        bindings:");
            builder.AppendLine("          - type: Button");
            builder.AppendLine("            action: Confirm");
            builder.AppendLine("            bindingGroups: Gamepad");
            builder.AppendLine("            deviceBindings:");
            builder.AppendLine("              - \"<Gamepad>/buttonSouth\"");
            return builder.ToString();
        }
    }
}
