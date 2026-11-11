using System;
using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Frame-split evidence for batch join. A batch join now yields between players, so the observable contract is
    /// "a multi-player batch does not complete inside the frame that started it, and a single-player batch still
    /// does". These cases measure how many frame boundaries the batch actually spanned instead of asserting on the
    /// shape of the code, and they pair the multi-player case with a single-player control.
    /// </summary>
    /// <remarks>
    /// The measured boundary count is a lower bound, not a per-player count: in EditMode the editor pumps the UniTask
    /// player loop on every <c>EditorApplication.update</c>, so several yield continuations can complete inside one
    /// test frame. In a real player loop the yield maps to one frame per player. That is why the assertion is
    /// "spanned more than zero frames" and the evidence is the contrast with the single-player control, which must
    /// span zero.
    /// </remarks>
    public sealed class InputPerfBatchJoinTests
    {
        private const string File = "batch-join.csv";
        private const string Header = "runId,case,metric,value,unit,detail";

        private const int Contexts = 3;
        private const int ActionsPerContext = 32;

        /// <summary>
        /// Eight players must not complete inside the starting frame: the batch has to span at least one boundary.
        /// Before the batch yielded between players this case spanned zero, identically to the control below.
        /// </summary>
        [UnityTest]
        [Timeout(180000)]
        public System.Collections.IEnumerator BatchJoin_EightPlayers_DoesNotCompleteInOneFrame()
        {
            yield return MeasureBatch("eightPlayers", 8, minimumFrames: 1);
        }

        /// <summary>
        /// Control case: a single-player batch has no trailing yield, so it must complete inside the calling frame.
        /// If this ever spans a frame, the last-player guard has regressed into an unconditional yield.
        /// </summary>
        [UnityTest]
        [Timeout(180000)]
        public System.Collections.IEnumerator BatchJoin_SinglePlayer_SpansNoExtraFrame()
        {
            yield return MeasureBatch("singlePlayer", 1, minimumFrames: 0);
        }

        private static System.Collections.IEnumerator MeasureBatch(string caseName, int playerCount, int minimumFrames)
        {
            PerfOutput.WriteEnvironmentHeader(File);

            var scenario = new PerfScenario("batch-" + caseName, playerCount, Contexts, ActionsPerContext, 50, DeviceScenario.Gamepad);
            var slots = new List<PlayerSlotConfig>(playerCount);
            for (int i = 0; i < playerCount; i++) slots.Add(PerfScenarioBuilder.BuildPlayerSlot(scenario, i));
            string yaml = PerfScenarioBuilder.SerializeConfiguration(slots);

            var devices = new List<Gamepad>(playerCount);
            var manager = new InputManager(PerfScenarioBuilder.BuildLimits());
            try
            {
                InputManagerInitializationResult init = manager.InitializeWithResult(yaml);
                Assert.That(init.IsSuccess, Is.True, init.Message);

                for (int i = 0; i < playerCount; i++) devices.Add(UIS.AddDevice<Gamepad>("BatchPad" + caseName + i));

                var ids = new List<int>(playerCount);
                for (int i = 0; i < playerCount; i++) ids.Add(i);

                // One frame of slack so device registration has settled before the measured batch starts. UniTask
                // player-loop runners are installed by the editor host, so the pump needs at least one turn.
                yield return null;

                UniTask<List<IInputPlayer>> task = manager.JoinPlayersBatchAsync(ids, 5);
                int startFrame = Time.frameCount;
                double startTime = Time.realtimeSinceStartupAsDouble;
                int framesPumped = 0;
                double worstFrameMs = 0d;

                while (task.Status == UniTaskStatus.Pending)
                {
                    double frameStart = Time.realtimeSinceStartupAsDouble;
                    yield return null;
                    framesPumped++;
                    double frameMs = (Time.realtimeSinceStartupAsDouble - frameStart) * 1000d;
                    if (frameMs > worstFrameMs) worstFrameMs = frameMs;
                }

                int unityFrameDelta = Time.frameCount - startFrame;
                double totalMs = (Time.realtimeSinceStartupAsDouble - startTime) * 1000d;

                Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Succeeded), "Batch join did not complete.");
                List<IInputPlayer> joined = task.GetAwaiter().GetResult();
                Assert.That(joined.Count, Is.EqualTo(playerCount), "Not every player joined.");
                Assert.That(manager.ActivePlayerCount, Is.EqualTo(playerCount));

                Write(caseName, "players", playerCount, "count", "requested");
                Write(caseName, "joined", joined.Count, "count", "returned");
                Write(caseName, "framesPumped", framesPumped, "frame", "yield return null while pending");
                Write(caseName, "unityFrameDelta", unityFrameDelta, "frame", "Time.frameCount delta");
                Write(caseName, "minimumFrames", minimumFrames, "frame", "players - 1");
                Write(caseName, "totalMilliseconds", totalMs, "ms", "wall clock across the batch");
                Write(caseName, "worstFrameMilliseconds", worstFrameMs, "ms", "largest single pumped frame");
                Write(caseName, "perPlayerMilliseconds", totalMs / Math.Max(1, playerCount), "ms", "total / players");

                PerfOutput.Log(
                    "batch join " + caseName +
                    ": players=" + playerCount +
                    " framesPumped=" + framesPumped +
                    " unityFrameDelta=" + unityFrameDelta +
                    " total=" + PerfOutput.Num(totalMs, 2) + "ms" +
                    " worstFrame=" + PerfOutput.Num(worstFrameMs, 2) + "ms");

                Assert.That(
                    framesPumped,
                    Is.GreaterThanOrEqualTo(minimumFrames),
                    "Batch join spanned " + framesPumped + " pumped frames, expected at least " + minimumFrames + ".");

                if (playerCount > 1)
                {
                    // No single boundary may carry the whole batch, otherwise the spread is not doing anything.
                    Assert.That(
                        worstFrameMs,
                        Is.LessThan(totalMs),
                        "One frame carried the entire batch cost, so the batch did not spread.");
                }
            }
            finally
            {
                manager.Dispose();
                for (int i = 0; i < devices.Count; i++)
                {
                    if (devices[i] != null && devices[i].added) UIS.RemoveDevice(devices[i]);
                }
            }
        }

        private static void Write(string caseName, string metric, double value, string unit, string detail)
        {
            PerfOutput.Row(
                File,
                Header,
                new[] { PerfOutput.RunId, caseName, metric, PerfOutput.Num(value, 3), unit, detail });
        }
    }
}
