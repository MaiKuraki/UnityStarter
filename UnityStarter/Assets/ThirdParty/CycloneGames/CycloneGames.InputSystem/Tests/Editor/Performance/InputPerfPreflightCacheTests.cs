using System;
using System.Collections.Generic;
using System.Diagnostics;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Measures the effect of reusing a successful preflight verdict across reinitializations, and proves that the
    /// reuse cannot mask a rejected configuration. The first initialization of every sample runs the full preflight,
    /// the following two reuse it, and the last one changes the content so the cache must miss.
    /// </summary>
    /// <remarks>
    /// Only successful verdicts are cached, so an invalid configuration is always re-validated. The fuzz case
    /// alternates a valid and an invalid configuration to verify exactly that property instead of trusting it.
    /// </remarks>
    public sealed class InputPerfPreflightCacheTests
    {
        private const string File = "preflight-cache.csv";
        private const string Header = "runId,scenario,players,contexts,actionsPerContext,totalActions,metric,value,unit";

        private const int WarmupRuns = 1;
        private const int MeasuredRuns = 5;

        [Test]
        public void PreflightCache_RepeatReinitialize()
        {
            PerfOutput.WriteEnvironmentHeader(File);

            PerfScenario[] scenarios =
            {
                new PerfScenario("C-p1-c3-a16", 1, 3, 16, 50, DeviceScenario.Gamepad),
                new PerfScenario("C-p2-c3-a32", 2, 3, 32, 50, DeviceScenario.Gamepad),
                new PerfScenario("C-p4-c3-a32", 4, 3, 32, 50, DeviceScenario.Gamepad),
                new PerfScenario("C-p2-c6-a32", 2, 6, 32, 50, DeviceScenario.Gamepad),
                new PerfScenario("C-p8-c3-a32", 8, 3, 32, 50, DeviceScenario.Gamepad)
            };

            for (int i = 0; i < scenarios.Length; i++)
            {
                MeasureScenario(scenarios[i]);
            }
        }

        [Test]
        public void PreflightCache_SoakRepeatedReinitialize()
        {
            const int Iterations = 60;
            PerfOutput.WriteEnvironmentHeader("preflight-cache-soak.csv");

            var scenario = new PerfScenario("S-p2-c3-a32", 2, 3, 32, 50, DeviceScenario.Gamepad);
            string yaml = BuildYaml(scenario);
            InputConfigurationLimits limits = PerfScenarioBuilder.BuildLimits();

            var samples = new List<double>(Iterations);
            var manager = new InputManager(limits);
            try
            {
                InputManagerInitializationResult initialization = manager.InitializeWithResult(yaml);
                Assert.That(initialization.IsSuccess, Is.True, initialization.Message);

                for (int i = 0; i < Iterations; i++)
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    InputManagerInitializationResult result = manager.ReinitializeWithResult(yaml);
                    watch.Stop();
                    Assert.That(result.IsSuccess, Is.True, result.Message);
                    samples.Add(watch.Elapsed.TotalMilliseconds * 1000d);
                }
            }
            finally
            {
                manager.Dispose();
            }

            double first = samples[0];
            double last = samples[samples.Count - 1];
            double min = double.MaxValue;
            double max = 0d;
            double sum = 0d;
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i] < min) min = samples[i];
                if (samples[i] > max) max = samples[i];
                sum += samples[i];
            }

            double median = Median(samples);
            double drift = (last - first) / Math.Max(1e-9d, first) * 100d;

            Write(scenario, scenario.Contexts * scenario.ActionsPerContext, "soak.iterations", Iterations);
            Write(scenario, scenario.Contexts * scenario.ActionsPerContext, "soak.first", first);
            Write(scenario, scenario.Contexts * scenario.ActionsPerContext, "soak.last", last);
            Write(scenario, scenario.Contexts * scenario.ActionsPerContext, "soak.median", median);
            Write(scenario, scenario.Contexts * scenario.ActionsPerContext, "soak.min", min);
            Write(scenario, scenario.Contexts * scenario.ActionsPerContext, "soak.max", max);
            Write(scenario, scenario.Contexts * scenario.ActionsPerContext, "soak.driftPercent", drift);

            PerfOutput.Log(
                "preflightCacheSoak iterations=" + Iterations +
                " first=" + PerfOutput.Num(first, 1) + "us" +
                " median=" + PerfOutput.Num(median, 1) + "us" +
                " last=" + PerfOutput.Num(last, 1) + "us" +
                " drift=" + PerfOutput.Num(drift, 2) + "%");
        }

        [Test]
        public void PreflightCache_FuzzAlternatingValidity()
        {
            const int Iterations = 40;
            PerfOutput.WriteEnvironmentHeader("preflight-cache-fuzz.csv");

            var valid = new PerfScenario("F-valid", 2, 3, 16, 50, DeviceScenario.Gamepad);
            string validYaml = BuildYaml(valid);
            string invalidYaml = BuildInvalidYaml(valid);
            InputConfigurationLimits limits = PerfScenarioBuilder.BuildLimits();

            var manager = new InputManager(limits);
            int validAccepted = 0;
            int invalidRejected = 0;
            try
            {
                InputManagerInitializationResult seed = manager.InitializeWithResult(validYaml);
                Assert.That(seed.IsSuccess, Is.True, seed.Message);

                var random = new Random(20261111);
                for (int i = 0; i < Iterations; i++)
                {
                    bool useInvalid = random.Next(2) == 0;
                    string yaml = useInvalid ? invalidYaml : validYaml;
                    InputManagerInitializationResult result = manager.ReinitializeWithResult(yaml);

                    if (useInvalid)
                    {
                        Assert.That(
                            result.IsSuccess,
                            Is.False,
                            "A rejected configuration must never be served from the successful-verdict cache.");
                        invalidRejected++;
                    }
                    else
                    {
                        Assert.That(result.IsSuccess, Is.True, result.Message);
                        validAccepted++;
                    }
                }
            }
            finally
            {
                manager.Dispose();
            }

            Write(valid, 48, "fuzz.iterations", Iterations);
            Write(valid, 48, "fuzz.validAccepted", validAccepted);
            Write(valid, 48, "fuzz.invalidRejected", invalidRejected);

            PerfOutput.Log(
                "preflightCacheFuzz iterations=" + Iterations +
                " validAccepted=" + validAccepted +
                " invalidRejected=" + invalidRejected);

            Assert.That(validAccepted + invalidRejected, Is.EqualTo(Iterations));
        }

        private static void MeasureScenario(PerfScenario scenario)
        {
            string yaml = BuildYaml(scenario);
            string changedYaml = BuildYaml(
                new PerfScenario(
                    scenario.Name + "-changed",
                    scenario.Players,
                    scenario.Contexts,
                    scenario.ActionsPerContext,
                    scenario.PollingPercent == 100 ? 0 : 100,
                    scenario.Device));
            InputConfigurationLimits limits = PerfScenarioBuilder.BuildLimits();

            var first = new List<double>(MeasuredRuns);
            var hitOne = new List<double>(MeasuredRuns);
            var hitTwo = new List<double>(MeasuredRuns);
            var miss = new List<double>(MeasuredRuns);

            for (int run = 0; run < WarmupRuns + MeasuredRuns; run++)
            {
                var manager = new InputManager(limits);
                try
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    InputManagerInitializationResult initialization = manager.InitializeWithResult(yaml);
                    watch.Stop();
                    Assert.That(initialization.IsSuccess, Is.True, initialization.Message);
                    double firstUs = watch.Elapsed.TotalMilliseconds * 1000d;

                    watch.Restart();
                    InputManagerInitializationResult reuseOne = manager.ReinitializeWithResult(yaml);
                    watch.Stop();
                    Assert.That(reuseOne.IsSuccess, Is.True, reuseOne.Message);
                    double hitOneUs = watch.Elapsed.TotalMilliseconds * 1000d;

                    watch.Restart();
                    InputManagerInitializationResult reuseTwo = manager.ReinitializeWithResult(yaml);
                    watch.Stop();
                    Assert.That(reuseTwo.IsSuccess, Is.True, reuseTwo.Message);
                    double hitTwoUs = watch.Elapsed.TotalMilliseconds * 1000d;

                    watch.Restart();
                    InputManagerInitializationResult changed = manager.ReinitializeWithResult(changedYaml);
                    watch.Stop();
                    Assert.That(changed.IsSuccess, Is.True, changed.Message);
                    double missUs = watch.Elapsed.TotalMilliseconds * 1000d;

                    if (run < WarmupRuns) continue;

                    first.Add(firstUs);
                    hitOne.Add(hitOneUs);
                    hitTwo.Add(hitTwoUs);
                    miss.Add(missUs);
                }
                finally
                {
                    manager.Dispose();
                }
            }

            int totalActions = scenario.Players * scenario.Contexts * scenario.ActionsPerContext;
            double firstMedian = Median(first);
            double hitMedian = Median(hitOne);
            double hitTwoMedian = Median(hitTwo);
            double missMedian = Median(miss);

            Write(scenario, totalActions, "init.first", firstMedian);
            Write(scenario, totalActions, "init.reuseFirst", hitMedian);
            Write(scenario, totalActions, "init.reuseSecond", hitTwoMedian);
            Write(scenario, totalActions, "init.changedContent", missMedian);
            Write(scenario, totalActions, "reuseSpeedupRatio", firstMedian / Math.Max(1e-9d, hitMedian));
            Write(scenario, totalActions, "reuseSharePercent", hitMedian / Math.Max(1e-9d, firstMedian) * 100d);

            PerfOutput.Log(
                "preflightCache " + scenario.Name +
                " actions=" + totalActions +
                " first=" + PerfOutput.Num(firstMedian, 1) + "us" +
                " reuse=" + PerfOutput.Num(hitMedian, 1) + "us" +
                " speedup=" + PerfOutput.Num(firstMedian / Math.Max(1e-9d, hitMedian), 2) + "x");
        }

        private static string BuildYaml(PerfScenario scenario)
        {
            var slots = new List<PlayerSlotConfig>(scenario.Players);
            for (int i = 0; i < scenario.Players; i++)
            {
                slots.Add(PerfScenarioBuilder.BuildPlayerSlot(scenario, i));
            }

            return PerfScenarioBuilder.SerializeConfiguration(slots);
        }

        /// <summary>
        /// Produces a configuration that parses and validates but fails preflight, by referencing an interaction
        /// type that is not registered.
        /// </summary>
        private static string BuildInvalidYaml(PerfScenario scenario)
        {
            var slots = new List<PlayerSlotConfig>(scenario.Players);
            for (int i = 0; i < scenario.Players; i++)
            {
                slots.Add(PerfScenarioBuilder.BuildPlayerSlot(scenario, i));
            }

            PlayerSlotConfig slot = slots[0];
            ContextDefinitionConfig context = slot.Contexts[0];
            ActionBindingConfig binding = context.Bindings[0];
            binding.Interactions = "DefinitelyMissingInteraction";
            context.Bindings[0] = binding;

            return PerfScenarioBuilder.SerializeConfiguration(slots);
        }

        private static void Write(PerfScenario scenario, int actions, string metric, double value)
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
                    actions.ToString(),
                    metric,
                    PerfOutput.Num(value, 2),
                    "microsecond"
                });
        }

        private static double Median(List<double> samples)
        {
            if (samples.Count == 0) return 0d;
            samples.Sort();
            int middle = samples.Count / 2;
            return samples.Count % 2 == 0
                ? (samples[middle - 1] + samples[middle]) * 0.5d
                : samples[middle];
        }
    }
}
