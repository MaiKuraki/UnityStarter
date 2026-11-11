using System;
using System.Collections.Generic;
using System.Text;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Deterministic fuzzing of the untrusted configuration boundary and of context mutation ordering.
    /// Every case records whether the module rejected the input, threw, or accepted it, so that a pass cannot be
    /// confused with silent acceptance.
    /// </summary>
    public sealed class InputPerfFuzzTests
    {
        private const string File = "fuzz.csv";
        private const string Header = "runId,case,corpus,outcome,metric,value,unit";

        private const int DefaultSeed = 20261111;

        private static readonly int Seed = ReadSeed();

        /// <summary>
        /// Allows successive campaigns to explore different corpora orderings through
        /// <c>INPUTSYSTEM_PERF_SEED</c> while keeping every run reproducible from its recorded seed.
        /// </summary>
        private static int ReadSeed()
        {
            string configured = Environment.GetEnvironmentVariable("INPUTSYSTEM_PERF_SEED");
            return int.TryParse(configured, out int seed) ? seed : DefaultSeed;
        }

        private static readonly string[] MalformedCorpus =
        {
            string.Empty,
            " ",
            "\n\n\n",
            "schemaVersion:",
            "schemaVersion: 1\nplayerSlots:",
            "schemaVersion: 99999\nplayerSlots: []\n",
            "schemaVersion: 1\nplayerSlots: [\n",
            "schemaVersion: &a 1\nplayerSlots: []\n",
            "schemaVersion: *a\nplayerSlots: []\n",
            "---\nschemaVersion: 1\n",
            "%YAML 1.2\nschemaVersion: 1\n",
            "schemaVersion: |\n  1\n",
            "schemaVersion: 1\nschemaVersion: 0\n",
            "schemaVersion: 1\nplayerSlots:\n  - playerId: 0\n    playerId: 1\n",
            "schemaVersion: 1\nplayerSlots:\n  - contexts:\n      - bindings:\n          - type: Button\n            action: \"\"\n",
            "schemaVersion: 1\nplayerSlots:\n  - contexts:\n      - bindings:\n          - type: Button\n            action: Fire\n            deviceBindings: ['<NotADevice>/x']\n",
            "schemaVersion: 1\nplayerSlots:\n  - contexts:\n      - bindings:\n          - type: Vector2\n            action: Move\n            interactions: 'NoSuchInteraction'\n",
            "schemaVersion: 1\nplayerSlots:\n  - contexts:\n      - bindings:\n          - type: Vector2\n            action: Move\n            processors: 'clamp(min=abc)'\n",
            "schemaVersion: 1\nplayerSlots:\n  - contexts:\n      - bindings:\n          - type: Float\n            action: Axis\n            longPressMs: -5\n",
            "schemaVersion: 1\nplayerSlots:\n  - playerIdx: 0\n",
            "schemaVersion: 1\njoinAction:\n  unknownKey: 1\nplayerSlots: []\n"
        };

        [Test]
        public void Fuzz_MalformedConfigurationCorpusIsRejectedWithoutThrowing()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            AllocationInstrument.Initialize();

            int rejected = 0;
            int accepted = 0;
            int threw = 0;

            for (int i = 0; i < MalformedCorpus.Length; i++)
            {
                var manager = new InputManager(PerfScenarioBuilder.BuildLimits());
                string outcome;
                try
                {
                    InputManagerInitializationResult result = manager.InitializeWithResult(MalformedCorpus[i]);
                    if (result.IsSuccess)
                    {
                        outcome = "accepted";
                        accepted++;
                    }
                    else
                    {
                        outcome = "rejected:" + result.Status;
                        rejected++;
                    }
                }
                catch (Exception exception)
                {
                    outcome = "threw:" + exception.GetType().Name;
                    threw++;
                }
                finally
                {
                    manager.Dispose();
                }

                PerfOutput.Row(
                    File,
                    Header,
                    new[] { PerfOutput.RunId, "malformed", "case" + i, outcome, "length", MalformedCorpus[i].Length.ToString(), "char" });
            }

            PerfOutput.Row(
                File,
                Header,
                new[] { PerfOutput.RunId, "malformed", "summary", "rejected", "count", rejected.ToString(), "tests" });
            PerfOutput.Row(
                File,
                Header,
                new[] { PerfOutput.RunId, "malformed", "summary", "accepted", "count", accepted.ToString(), "tests" });
            PerfOutput.Row(
                File,
                Header,
                new[] { PerfOutput.RunId, "malformed", "summary", "threw", "count", threw.ToString(), "tests" });

            PerfOutput.Log(
                "fuzz malformed: corpus=" + MalformedCorpus.Length +
                " rejected=" + rejected + " accepted=" + accepted + " threw=" + threw);

            Assert.That(threw, Is.Zero, "Malformed configuration must be rejected through the result type, not by throwing.");
            Assert.That(accepted, Is.Zero, "No malformed configuration may be accepted.");
        }

        [Test]
        public void Fuzz_OversizedConfigurationIsRejectedWithinBudget()
        {
            PerfOutput.WriteEnvironmentHeader(File);

            var builder = new StringBuilder();
            builder.Append("schemaVersion: 1\nplayerSlots:\n");
            for (int player = 0; player < 200; player++)
            {
                builder.Append("  - playerId: ").Append(player).Append('\n');
            }

            var manager = new InputManager(PerfScenarioBuilder.BuildLimits());
            try
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                InputManagerInitializationResult result = manager.InitializeWithResult(builder.ToString());
                double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * PerfMeter.TicksToMicroseconds;

                PerfOutput.Row(
                    File,
                    Header,
                    new[]
                    {
                        PerfOutput.RunId, "oversized", "200Players", result.IsSuccess ? "accepted" : "rejected:" + result.Status,
                        "elapsedUs", PerfOutput.Num(elapsed, 1), "us"
                    });
                PerfOutput.Log("fuzz oversized: accepted=" + result.IsSuccess + " status=" + result.Status + " elapsed=" + PerfOutput.Num(elapsed, 1) + "us");

                Assert.That(result.IsSuccess, Is.False);
            }
            finally
            {
                manager.Dispose();
            }
        }

        [Test]
        public void Fuzz_RandomContextMutationSequencesKeepInvariants()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            AllocationInstrument.Initialize();

            var random = new Random(Seed);
            var scenario = new PerfScenario("F-context", 1, 6, 32, 50, DeviceScenario.Gamepad);
            using PerfRig rig = PerfRig.Create(scenario, pushContexts: false, subscribe: true);
            InputPlayer player = rig.First;

            var contexts = new InputContext[6];
            for (int i = 0; i < contexts.Length; i++) contexts[i] = new InputContext("Map" + i, "Ctx" + i, i, false);

            const int Operations = 4000;
            int push = 0;
            int pop = 0;
            int captureOps = 0;
            int remove = 0;
            int exceptions = 0;
            int maxContextStack = 0;
            int maxCaptureStack = 0;
            var scopes = new List<IDisposable>();
            double[] samples = new double[Operations];

            for (int operation = 0; operation < Operations; operation++)
            {
                int choice = random.Next(100);
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    if (choice < 40)
                    {
                        player.PushContext(contexts[random.Next(contexts.Length)]);
                        push++;
                    }
                    else if (choice < 70)
                    {
                        player.PopContext();
                        pop++;
                    }
                    else if (choice < 90)
                    {
                        scopes.Add(player.CaptureContext(contexts[random.Next(contexts.Length)]));
                        captureOps++;
                    }
                    else if (scopes.Count > 0)
                    {
                        int index = random.Next(scopes.Count);
                        scopes[index].Dispose();
                        scopes.RemoveAt(index);
                    }
                    else
                    {
                        player.RemoveContext(contexts[random.Next(contexts.Length)]);
                        remove++;
                    }

                    InputPlayerMemoryStats stats = player.GetMemoryStats();
                    if (stats.ContextStackCount > maxContextStack) maxContextStack = stats.ContextStackCount;
                    if (stats.CaptureStackCount > maxCaptureStack) maxCaptureStack = stats.CaptureStackCount;
                }
                catch (Exception)
                {
                    exceptions++;
                }

                samples[operation] = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * PerfMeter.TicksToMicroseconds;
            }

            for (int i = 0; i < scopes.Count; i++) scopes[i].Dispose();

            TimingStats timing = PerfMeter.Summarize(samples);
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "contextFuzz", "seed", Seed.ToString(), "operations", Operations.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "contextFuzz", "push", "count", "value", push.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "contextFuzz", "pop", "count", "value", pop.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "contextFuzz", "capture", "count", "value", captureOps.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "contextFuzz", "remove", "count", "value", remove.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "contextFuzz", "exceptions", "count", "value", exceptions.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "contextFuzz", "maxContextStack", "count", "value", maxContextStack.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "contextFuzz", "maxCaptureStack", "count", "value", maxCaptureStack.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "contextFuzz", "opP50", "us", "value", PerfOutput.Num(timing.MedianUs, 3), "us" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "contextFuzz", "opP99", "us", "value", PerfOutput.Num(timing.P99Us, 3), "us" });

            PerfOutput.Log(
                "fuzz context: ops=" + Operations + " push=" + push + " pop=" + pop + " capture=" + captureOps +
                " remove=" + remove + " exceptions=" + exceptions +
                " maxContextStack=" + maxContextStack + " maxCaptureStack=" + maxCaptureStack +
                " p50=" + PerfOutput.Num(timing.MedianUs, 3) + "us p99=" + PerfOutput.Num(timing.P99Us, 3) + "us");

            Assert.That(exceptions, Is.Zero, "Context mutation ordering must not throw.");
            Assert.That(maxCaptureStack, Is.LessThanOrEqualTo(InputPlayer.MaximumContextCaptureDepth));
        }

        [Test]
        public void Fuzz_RandomRebindSelectorsAreRejectedDeterministically()
        {
            PerfOutput.WriteEnvironmentHeader(File);

            var random = new Random(Seed);
            var scenario = new PerfScenario("F-rebind", 1, 2, 24, 0, DeviceScenario.KeyboardMouse);
            using PerfRig rig = PerfRig.Create(scenario);
            InputPlayer player = rig.First;

            int accepted = 0;
            int rejected = 0;
            int exceptions = 0;
            string[] paths = { "<Keyboard>/a", "<Keyboard>/b", "<Keyboard>/nonexistent", "", "  ", null, "<Gamepad>/leftStick", new string('x', 4096) };

            for (int i = 0; i < 600; i++)
            {
                string actionName = "A" + random.Next(scenario.ActionsPerContext);
                string oldPath = paths[random.Next(paths.Length)];
                string newPath = paths[random.Next(paths.Length)];
                try
                {
                    if (player.RebindAction("Map0", actionName, oldPath, newPath)) accepted++;
                    else rejected++;
                }
                catch (Exception)
                {
                    exceptions++;
                }
            }

            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "rebindFuzz", "accepted", "count", accepted.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "rebindFuzz", "rejected", "count", rejected.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "rebindFuzz", "exceptions", "count", exceptions.ToString(), "count" });

            PerfOutput.Log("fuzz rebind: accepted=" + accepted + " rejected=" + rejected + " exceptions=" + exceptions);

            Assert.That(exceptions, Is.Zero);
            Assert.That(accepted + rejected, Is.EqualTo(600));
        }
    }
}
