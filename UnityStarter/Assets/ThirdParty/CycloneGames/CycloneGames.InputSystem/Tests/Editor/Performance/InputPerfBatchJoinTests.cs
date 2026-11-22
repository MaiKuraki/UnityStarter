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
    /// Measures how many frame boundaries a batch join spans. A multi-player batch yields between
    /// players and must not complete inside the starting frame; a single-player batch has no trailing
    /// yield and completes inside the calling frame.
    /// </summary>
    /// <remarks>
    /// The measured boundary count is a lower bound: in EditMode the editor pumps the UniTask player
    /// loop on every <c>EditorApplication.update</c>, so several continuations can complete inside one
    /// test frame.
    /// </remarks>
    public sealed class InputPerfBatchJoinTests
    {
        private const string File = "batch-join.csv";
        private const string Header = "runId,case,metric,value,unit,detail";

        private const int Contexts = 3;
        private const int ActionsPerContext = 32;

        /// <summary>
        /// Eight players must not complete inside the starting frame: the batch spans at least one boundary.
        /// </summary>
        [UnityTest]
        [Timeout(180000)]
        public System.Collections.IEnumerator BatchJoin_EightPlayers_DoesNotCompleteInOneFrame()
        {
            yield return MeasureBatch("eightPlayers", 8, minimumFrames: 1);
        }

        /// <summary>
        /// Control case: a single-player batch has no trailing yield and completes inside the calling frame.
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
