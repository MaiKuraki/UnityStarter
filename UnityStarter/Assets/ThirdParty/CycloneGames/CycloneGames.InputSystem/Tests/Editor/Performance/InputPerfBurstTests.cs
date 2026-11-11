using System;
using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using R3;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Pulse behaviour: device hot-plug storms, join storms, context churn, rebind churn, capture depth and
    /// subscriber failure storms. Each case records peak per-operation cost and the failure-mode decision that
    /// the module makes under overload.
    /// </summary>
    public sealed class InputPerfBurstTests
    {
        private const string File = "burst.csv";
        private const string Header = "runId,case,metric,value,unit,detail";

        [Test]
        public void Burst_DeviceHotPlugStorm()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            AllocationInstrument.Initialize();

            var scenario = new PerfScenario("B-hotplug", 4, 3, 64, 50, DeviceScenario.Gamepad);
            using PerfRig rig = PerfRig.Create(scenario);

            const int Cycles = 60;
            double[] samples = new double[Cycles];

            for (int cycle = 0; cycle < Cycles; cycle++)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                var pad = UIS.AddDevice<Gamepad>("HotPad" + cycle);
                UIS.RemoveDevice(pad);
                samples[cycle] = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * PerfMeter.TicksToMicroseconds;
            }

            TimingStats stats = PerfMeter.Summarize(samples);
            Write("hotplug", "cycles", Cycles, "count", "addAndRemoveGamepad");
            Write("hotplug", "p50", stats.MedianUs, "us", "oneAddPlusOneRemove");
            Write("hotplug", "p95", stats.P95Us, "us", "oneAddPlusOneRemove");
            Write("hotplug", "max", stats.MaxUs, "us", "oneAddPlusOneRemove");
            Write("hotplug", "playersAfter", rig.Players.Count, "count", "endState");
            Write("hotplug", "activeContextsAfter", rig.First.GetMemoryStats().ActiveContextCount, "count", "endState");

            PerfOutput.Log(
                "burst hotplug: p50=" + PerfOutput.Num(stats.MedianUs, 1) + "us p95=" + PerfOutput.Num(stats.P95Us, 1) +
                "us max=" + PerfOutput.Num(stats.MaxUs, 1) + "us players=" + rig.Players.Count);

            Assert.That(rig.Players.Count, Is.EqualTo(4));
        }

        [Test]
        public void Burst_JoinStorm()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            AllocationInstrument.Initialize();

            const int PlayerCount = 8;
            var scenario = new PerfScenario("B-join", PlayerCount, 3, 32, 50, DeviceScenario.Gamepad);
            var slots = new List<PlayerSlotConfig>(PlayerCount);
            for (int i = 0; i < PlayerCount; i++) slots.Add(PerfScenarioBuilder.BuildPlayerSlot(scenario, i));
            string yaml = PerfScenarioBuilder.SerializeConfiguration(slots);

            var devices = new List<Gamepad>(PlayerCount);
            var manager = new InputManager(PerfScenarioBuilder.BuildLimits());
            try
            {
                InputManagerInitializationResult init = manager.InitializeWithResult(yaml);
                Assert.That(init.IsSuccess, Is.True, init.Message);

                for (int i = 0; i < PlayerCount; i++) devices.Add(UIS.AddDevice<Gamepad>("JoinPad" + i));

                PerfMeter.ForceFullCollection();
                long before = PerfMeter.LiveHeapBytes();
                double[] samples = new double[PlayerCount];
                for (int i = 0; i < PlayerCount; i++)
                {
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
                    IInputPlayer player = manager.JoinPlayerAndLockDevice(i, devices[i]);
                    samples[i] = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * PerfMeter.TicksToMicroseconds;
                    Assert.That(player, Is.Not.Null, "Join failed for player " + i);
                }

                long after = PerfMeter.LiveHeapBytes();
                TimingStats stats = PerfMeter.Summarize(samples);

                Write("join", "players", PlayerCount, "count", "joinPlayerAndLockDevice");
                Write("join", "p50", stats.MedianUs, "us", "perPlayer");
                Write("join", "p95", stats.P95Us, "us", "perPlayer");
                Write("join", "max", stats.MaxUs, "us", "perPlayer");
                Write("join", "totalUspan", Sum(samples), "us", "allPlayers");
                Write("join", "retainedTotal", after - before, "byte", "allPlayers");
                Write("join", "activePlayerCount", manager.ActivePlayerCount, "count", "endState");

                PerfOutput.Log(
                    "burst join: p50=" + PerfOutput.Num(stats.MedianUs, 1) + "us p95=" + PerfOutput.Num(stats.P95Us, 1) +
                    "us max=" + PerfOutput.Num(stats.MaxUs, 1) + "us total=" + PerfOutput.Num(Sum(samples), 1) +
                    "us retained=" + (after - before) + "B");
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

        [Test]
        public void Burst_ContextChurnAndRefreshPassCap()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            AllocationInstrument.Initialize();

            var scenario = new PerfScenario("B-context", 1, 6, 64, 50, DeviceScenario.Gamepad);
            using PerfRig rig = PerfRig.Create(scenario, pushContexts: false, subscribe: true);
            InputPlayer player = rig.First;

            var contexts = new InputContext[6];
            for (int i = 0; i < contexts.Length; i++) contexts[i] = new InputContext("Map" + i, "Ctx" + i, i, false);

            const int Cycles = 200;
            double[] samples = new double[Cycles];
            for (int cycle = 0; cycle < Cycles; cycle++)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < contexts.Length; i++) player.PushContext(contexts[i]);
                for (int i = 0; i < contexts.Length; i++) player.PopContext();
                samples[cycle] = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * PerfMeter.TicksToMicroseconds;
            }

            TimingStats stats = PerfMeter.Summarize(samples);
            Write("contextChurn", "p50", stats.MedianUs, "us", "6pushPlus6pop");
            Write("contextChurn", "p95", stats.P95Us, "us", "6pushPlus6pop");
            Write("contextChurn", "max", stats.MaxUs, "us", "6pushPlus6pop");

            // A subscriber that requests another refresh from inside the change notification exercises the
            // synchronous refresh pass cap.
            int reentrantCalls = 0;
            Action<string> handler = _ =>
            {
                reentrantCalls++;
                if (reentrantCalls > 4) return;
                player.RefreshActiveContext();
            };

            player.OnContextChanged += handler;
            try
            {
                player.PushContext(contexts[0]);
                Write("contextChurn", "reentrantRefreshCalls", reentrantCalls, "count", "subscriberRequestedRefresh");
                Write("contextChurn", "activeContexts", player.GetMemoryStats().ActiveContextCount, "count", "afterReentrantRefresh");
            }
            finally
            {
                player.OnContextChanged -= handler;
            }

            // Capture depth cap: leaking 1025 scopes must fail closed rather than grow the stack.
            var captureContext = new InputContext("Map0", "Ctx0", 0, false);
            int captured = 0;
            Exception capturedException = null;
            var scopes = new List<IDisposable>(InputPlayer.MaximumContextCaptureDepth + 1);
            try
            {
                for (int i = 0; i < InputPlayer.MaximumContextCaptureDepth + 1; i++)
                {
                    scopes.Add(player.CaptureContext(captureContext));
                    captured++;
                }
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
            finally
            {
                for (int i = 0; i < scopes.Count; i++) scopes[i].Dispose();
            }

            Write("captureDepth", "captured", captured, "count", "beforeFailure");
            Write("captureDepth", "cap", InputPlayer.MaximumContextCaptureDepth, "count", "MaximumContextCaptureDepth");
            Write("captureDepth", "exception", capturedException == null ? "none" : capturedException.GetType().Name, "type", "failClosed");

            PerfOutput.Log(
                "burst context churn: p50=" + PerfOutput.Num(stats.MedianUs, 1) + "us p95=" + PerfOutput.Num(stats.P95Us, 1) +
                "us max=" + PerfOutput.Num(stats.MaxUs, 1) + "us reentrant=" + reentrantCalls +
                " captured=" + captured + " exception=" + (capturedException == null ? "none" : capturedException.GetType().Name));

            Assert.That(captured, Is.EqualTo(InputPlayer.MaximumContextCaptureDepth));
            Assert.That(capturedException, Is.Not.Null);
        }

        [Test]
        public void Burst_RebindStormAndOverrideBudget()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            AllocationInstrument.Initialize();

            var scenario = new PerfScenario("B-rebind", 1, 1, 128, 0, DeviceScenario.KeyboardMouse);
            using PerfRig rig = PerfRig.Create(scenario);
            InputPlayer player = rig.First;

            const int Attempts = 160;
            int applied = 0;
            int rejected = 0;
            double[] samples = new double[Attempts];
            for (int i = 0; i < Attempts; i++)
            {
                string actionName = "A" + (i % scenario.ActionsPerContext);
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                bool result = player.RebindAction("Map0", actionName, "<Keyboard>/a", "<Keyboard>/z");
                samples[i] = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * PerfMeter.TicksToMicroseconds;
                if (result) applied++;
                else rejected++;
            }

            TimingStats stats = PerfMeter.Summarize(samples);
            Write("rebindStorm", "applied", applied, "count", "attempts=" + Attempts);
            Write("rebindStorm", "rejected", rejected, "count", "budgetOrSelector");
            Write("rebindStorm", "p50", stats.MedianUs, "us", "rebindAction");
            Write("rebindStorm", "p95", stats.P95Us, "us", "rebindAction");
            Write("rebindStorm", "max", stats.MaxUs, "us", "rebindAction");

            // Export is documented as a settings-flow operation; its cost is recorded so that callers do not put
            // it on a hot path by accident.
            double[] exportSamples = PerfMeter.SamplePerCallMicroseconds(20, 1, () =>
            {
                player.TryExportBindingOverridesJson(out _);
            });
            TimingStats exportStats = PerfMeter.Summarize(exportSamples);
            Write("rebindStorm", "export.p50", exportStats.MedianUs, "us", "exportOverridesJson");

            PerfOutput.Log(
                "burst rebind: applied=" + applied + " rejected=" + rejected +
                " p50=" + PerfOutput.Num(stats.MedianUs, 2) + "us p95=" + PerfOutput.Num(stats.P95Us, 2) +
                "us exportP50=" + PerfOutput.Num(exportStats.MedianUs, 1) + "us");

            Assert.That(applied, Is.GreaterThan(0));
            Assert.That(applied + rejected, Is.EqualTo(Attempts));
        }

        [Test]
        public void Burst_FailingSubscriberStormFailsClosed()
        {
            PerfOutput.WriteEnvironmentHeader(File);

            var scenario = new PerfScenario("B-throwing", 1, 3, 16, 50, DeviceScenario.Gamepad);
            using PerfRig rig = PerfRig.Create(scenario, pushContexts: false, subscribe: false);
            InputPlayer player = rig.First;

            var context = new InputContext("Map0", "Ctx0", 0, false);
            context.AddBinding(new ThrowingObservable(), new ThrowingCommand());

            int failures = 0;
            Exception last = null;
            try
            {
                player.PushContext(context);
            }
            catch (Exception exception)
            {
                failures++;
                last = exception;
            }

            for (int i = 0; i < 49; i++)
            {
                try
                {
                    player.RefreshActiveContext();
                }
                catch (Exception exception)
                {
                    failures++;
                    last = exception;
                }
            }

            Write("failingSubscriber", "refreshAttempts", 50, "count", "pushPlus49RefreshesWithThrowingSubscriber");
            Write("failingSubscriber", "failures", failures, "count", "refreshAttemptsWithThrow");
            Write("failingSubscriber", "exception", last == null ? "none" : last.GetType().Name, "type", "propagated");
            Write("failingSubscriber", "activeContexts", player.GetMemoryStats().ActiveContextCount, "count", "afterFailures");

            PerfOutput.Log(
                "burst failing subscriber: failures=" + failures +
                " exception=" + (last == null ? "none" : last.GetType().Name) +
                " activeContexts=" + player.GetMemoryStats().ActiveContextCount);

            Assert.That(failures, Is.EqualTo(50), "Every projection attempt must fail closed and report the failure to the caller.");
        }

        /// <summary>
        /// Command bound to a throwing observable. The command body is never reached because the module fails
        /// while creating the subscription.
        /// </summary>
        private sealed class ThrowingCommand : IActionCommand
        {
            public void Execute()
            {
            }
        }

        /// <summary>
        /// Observable whose subscription always fails, used to provoke the module's fail-closed projection path.
        /// </summary>
        private sealed class ThrowingObservable : Observable<Unit>
        {
            protected override IDisposable SubscribeCore(Observer<Unit> observer)
            {
                throw new InvalidOperationException("Synthetic subscriber failure.");
            }
        }

        private static double Sum(double[] values)
        {
            double sum = 0d;
            for (int i = 0; i < values.Length; i++) sum += values[i];
            return sum;
        }

        private static void Write(string testCase, string metric, double value, string unit, string detail)
        {
            PerfOutput.Row(
                File,
                Header,
                new[] { PerfOutput.RunId, testCase, metric, PerfOutput.Num(value, 3), unit, detail });
        }

        private static void Write(string testCase, string metric, string value, string unit, string detail)
        {
            PerfOutput.Row(
                File,
                Header,
                new[] { PerfOutput.RunId, testCase, metric, value, unit, detail });
        }
    }
}
