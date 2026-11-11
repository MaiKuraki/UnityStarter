using System;
using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Practical capacity of the YAML configuration source. The declared absolute limits allow far more actions
    /// than the configuration format can express, so the reachable ceiling is measured per player count instead of
    /// being inferred from the limits alone.
    /// </summary>
    public sealed class InputPerfCapacityTests
    {
        private const string File = "capacity.csv";
        private const string Header = "runId,players,contexts,actionsPerContext,totalActions,yamlLines,yamlChars,result,detail";

        private static readonly int[] PlayerCounts = { 1, 2, 4, 8 };
        private static readonly int[] ActionCounts = { 16, 32, 48, 64, 96, 128, 160, 192, 256 };
        private const int Contexts = 3;

        [Test]
        public void Capacity_LargestAcceptedConfigurationPerPlayerCount()
        {
            PerfOutput.WriteEnvironmentHeader(File);

            for (int playerIndex = 0; playerIndex < PlayerCounts.Length; playerIndex++)
            {
                int players = PlayerCounts[playerIndex];
                int largestAccepted = 0;
                for (int i = 0; i < ActionCounts.Length; i++)
                {
                    int actionsPerContext = ActionCounts[i];
                    var scenario = new PerfScenario("cap", players, Contexts, actionsPerContext, 50, DeviceScenario.Gamepad);
                    var slots = new List<PlayerSlotConfig>(players);
                    for (int player = 0; player < players; player++) slots.Add(PerfScenarioBuilder.BuildPlayerSlot(scenario, player));
                    string yaml = PerfScenarioBuilder.SerializeConfiguration(slots);
                    int lines = CountLines(yaml);

                    var manager = new InputManager(PerfScenarioBuilder.BuildLimits());
                    string outcome;
                    try
                    {
                        InputManagerInitializationResult result = manager.InitializeWithResult(yaml);
                        outcome = result.IsSuccess ? "accepted" : "rejected:" + result.Status;
                        if (result.IsSuccess) largestAccepted = actionsPerContext;
                    }
                    catch (Exception exception)
                    {
                        outcome = "threw:" + exception.GetType().Name;
                    }
                    finally
                    {
                        manager.Dispose();
                    }

                    PerfOutput.Row(
                        File,
                        Header,
                        new[]
                        {
                            PerfOutput.RunId,
                            players.ToString(),
                            Contexts.ToString(),
                            actionsPerContext.ToString(),
                            (players * Contexts * actionsPerContext).ToString(),
                            lines.ToString(),
                            yaml.Length.ToString(),
                            outcome,
                            "absoluteActionLimit=" + InputConfigurationLimits.AbsoluteMaxTotalActionsPerPlayer
                        });
                }

                PerfOutput.Log(
                    "capacity players=" + players +
                    " largestAcceptedActionsPerContext=" + largestAccepted +
                    " largestAcceptedTotalActions=" + (players * Contexts * largestAccepted));
                Assert.That(largestAccepted, Is.GreaterThan(0), "At least a small configuration must be accepted for " + players + " players.");
            }
        }

        private static int CountLines(string text)
        {
            int lines = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n') lines++;
            }

            return lines;
        }
    }
}
