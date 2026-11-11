using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using VYaml.Serialization;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Splits a full manager initialization into its phases so that any caching work can be justified by its
    /// measured share instead of by assumption. Every phase runs through the same public entry points the runtime
    /// uses, in the same order, over identical input bytes.
    /// </summary>
    /// <remarks>
    /// The scenario matrix keeps every configuration inside the serialized line budget, because a rejected
    /// configuration would time the failure path rather than the work. Warmup runs are discarded so that first-call
    /// JIT and static initialization do not land in the reported medians.
    /// </remarks>
    public sealed class InputPerfCostBreakdownTests
    {
        private const string File = "cost-breakdown.csv";
        private const string Header = "runId,scenario,players,contexts,actionsPerContext,totalActions,metric,value,unit";

        private const int WarmupRuns = 1;
        private const int MeasuredRuns = 7;

        [Test]
        public void CostBreakdown_InitializePhases()
        {
            PerfOutput.WriteEnvironmentHeader(File);

            PerfScenario[] scenarios =
            {
                new PerfScenario("B-p1-c3-a16", 1, 3, 16, 50, DeviceScenario.Gamepad),
                new PerfScenario("B-p1-c3-a64", 1, 3, 64, 50, DeviceScenario.Gamepad),
                new PerfScenario("B-p1-c3-a256", 1, 3, 256, 50, DeviceScenario.Gamepad),
                new PerfScenario("B-p2-c3-a32", 2, 3, 32, 50, DeviceScenario.Gamepad),
                new PerfScenario("B-p2-c3-a128", 2, 3, 128, 50, DeviceScenario.Gamepad),
                new PerfScenario("B-p4-c3-a32", 4, 3, 32, 50, DeviceScenario.Gamepad),
                new PerfScenario("B-p4-c3-a96", 4, 3, 96, 50, DeviceScenario.Gamepad),
                new PerfScenario("B-p8-c3-a32", 8, 3, 32, 50, DeviceScenario.Gamepad),
                new PerfScenario("B-p8-c3-a48", 8, 3, 48, 50, DeviceScenario.Gamepad),
                new PerfScenario("B-p2-c1-a64", 2, 1, 64, 50, DeviceScenario.Gamepad),
                new PerfScenario("B-p2-c6-a32", 2, 6, 32, 50, DeviceScenario.Gamepad)
            };

            for (int i = 0; i < scenarios.Length; i++)
            {
                MeasureScenario(scenarios[i]);
            }
        }

        private static void MeasureScenario(PerfScenario scenario)
        {
            var slots = new List<PlayerSlotConfig>(scenario.Players);
            for (int i = 0; i < scenario.Players; i++)
            {
                slots.Add(PerfScenarioBuilder.BuildPlayerSlot(scenario, i));
            }

            string yaml = PerfScenarioBuilder.SerializeConfiguration(slots);
            byte[] utf8 = Encoding.UTF8.GetBytes(yaml);
            InputConfigurationLimits limits = PerfScenarioBuilder.BuildLimits();

            var parse = new List<double>(MeasuredRuns);
            var validate = new List<double>(MeasuredRuns);
            var preflight = new List<double>(MeasuredRuns);
            var total = new List<double>(MeasuredRuns);

            for (int run = 0; run < WarmupRuns + MeasuredRuns; run++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                InputConfiguration parsed = YamlSerializer.Deserialize<InputConfiguration>(utf8);
                watch.Stop();
                double parseUs = watch.Elapsed.TotalMilliseconds * 1000d;

                watch.Restart();
                InputConfigurationValidationResult validation =
                    InputConfigurationValidator.ValidateAndPrepare(parsed, limits);
                watch.Stop();
                double validateUs = watch.Elapsed.TotalMilliseconds * 1000d;
                Assert.That(validation.IsValid, Is.True);

                watch.Restart();
                InputConfigurationPreflightResult preflightResult =
                    InputSystemConfigurationPreflight.Run(validation.RuntimeConfiguration);
                watch.Stop();
                double preflightUs = watch.Elapsed.TotalMilliseconds * 1000d;
                Assert.That(preflightResult.IsSuccess, Is.True, "Preflight must accept the generated configuration.");

                var manager = new InputManager(limits);
                try
                {
                    watch.Restart();
                    InputManagerInitializationResult initialization = manager.InitializeWithResult(yaml);
                    watch.Stop();
                    double totalUs = watch.Elapsed.TotalMilliseconds * 1000d;
                    Assert.That(initialization.IsSuccess, Is.True, initialization.Message);

                    if (run >= WarmupRuns)
                    {
                        parse.Add(parseUs);
                        validate.Add(validateUs);
                        preflight.Add(preflightUs);
                        total.Add(totalUs);
                    }
                }
                finally
                {
                    manager.Dispose();
                }
            }

            int totalActions = scenario.Players * scenario.Contexts * scenario.ActionsPerContext;
            double parseMedian = Median(parse);
            double validateMedian = Median(validate);
            double preflightMedian = Median(preflight);
            double totalMedian = Median(total);

            Write(scenario, totalActions, "init.parse", parseMedian);
            Write(scenario, totalActions, "init.validate", validateMedian);
            Write(scenario, totalActions, "init.preflight", preflightMedian);
            Write(scenario, totalActions, "init.total", totalMedian);
            Write(scenario, totalActions, "init.other", totalMedian - parseMedian - validateMedian - preflightMedian);
            Write(scenario, totalActions, "preflightSharePercent", preflightMedian / Math.Max(1e-9d, totalMedian) * 100d);

            PerfOutput.Log(
                "costBreakdown " + scenario.Name +
                " actions=" + totalActions +
                " total=" + PerfOutput.Num(totalMedian, 1) + "us" +
                " preflight=" + PerfOutput.Num(preflightMedian, 1) + "us" +
                " share=" + PerfOutput.Num(preflightMedian / Math.Max(1e-9d, totalMedian) * 100d, 2) + "%");
        }

        private static void Write(PerfScenario scenario, int totalActions, string metric, double value)
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
                    totalActions.ToString(),
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
