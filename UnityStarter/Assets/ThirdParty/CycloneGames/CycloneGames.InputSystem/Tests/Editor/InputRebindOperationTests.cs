using System;
using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// State machine of <see cref="InputRebindOperation"/>: acceptance rules, conflict detection, swap resolution,
    /// timeout and terminal-state monotonicity.
    /// </summary>
    public sealed class InputRebindOperationTests
    {
        private const string Context = "Gameplay";
        private const string Map = "Map0";
        private const string Action = "Confirm";
        private const string OldBinding = "<Gamepad>/buttonSouth";

        [Test]
        public void AcceptedPath_AppliesThroughThePlayer()
        {
            var player = new FakePlayer(OldBinding);
            using var operation = new InputRebindOperation(player, Context, Map, Action, OldBinding);
            operation.Start(0d);

            Assert.That(operation.Offer("<Gamepad>/buttonEast", "Button", 1d), Is.True);
            Assert.That(operation.Status, Is.EqualTo(RebindStatus.Accepted));
            Assert.That(operation.ControlPath, Is.EqualTo("<Gamepad>/buttonEast"));
            Assert.That(operation.Apply(), Is.True);
            Assert.That(player.Applied, Is.EqualTo(new[] { OldBinding + "->" + "<Gamepad>/buttonEast" }));
        }

        [Test]
        public void ReservedPath_IsRejectedAndListeningContinues()
        {
            var player = new FakePlayer(OldBinding);
            using var operation = new InputRebindOperation(player, Context, Map, Action, OldBinding);
            operation.Start(0d);

            Assert.That(operation.Offer("<Keyboard>/escape", "Key", 1d), Is.False);
            Assert.That(operation.LastRejection, Is.EqualTo(RebindRejection.ReservedPath));
            Assert.That(operation.IsListening, Is.True);

            Assert.That(operation.Offer("<Gamepad>/buttonEast", "Button", 2d), Is.True);
            Assert.That(operation.Status, Is.EqualTo(RebindStatus.Accepted));
        }

        [Test]
        public void InvalidCandidates_AreRejectedWithTheReason()
        {
            var player = new FakePlayer(OldBinding);
            var policy = new InputRebindPolicy(new[] { "<Keyboard>/escape" }, new[] { "Button" }, 16);
            using var operation = new InputRebindOperation(player, Context, Map, Action, OldBinding, policy);
            operation.Start(0d);

            Assert.That(operation.Offer(null, "Button", 1d), Is.False);
            Assert.That(operation.LastRejection, Is.EqualTo(RebindRejection.EmptyPath));

            Assert.That(operation.Offer("<Gamepad>/averyveryverylongpath", "Button", 1d), Is.False);
            Assert.That(operation.LastRejection, Is.EqualTo(RebindRejection.PathTooLong));

            Assert.That(operation.Offer("<Keyboard>/space", "Key", 1d), Is.False);
            Assert.That(operation.LastRejection, Is.EqualTo(RebindRejection.LayoutNotAllowed));
        }

        [Test]
        public void ExistingBinding_IsReportedAsConflictAndSwapResolvesIt()
        {
            var player = new FakePlayer(OldBinding, "<Gamepad>/buttonEast");
            using var operation = new InputRebindOperation(player, Context, Map, Action, OldBinding);
            operation.Start(0d);

            Assert.That(operation.Offer("<Gamepad>/buttonEast", "Button", 1d), Is.True);
            Assert.That(operation.Status, Is.EqualTo(RebindStatus.Conflict));
            Assert.That(operation.ConflictingBinding, Is.EqualTo("<Gamepad>/buttonEast"));
            Assert.That(operation.Apply(), Is.False, "a conflict must be resolved before it is applied");

            Assert.That(operation.ResolveConflictBySwap(), Is.True);
            Assert.That(operation.Status, Is.EqualTo(RebindStatus.Accepted));
            Assert.That(player.Applied, Is.EqualTo(new[]
            {
                "<Gamepad>/buttonEast->" + OldBinding,
                OldBinding + "->" + "<Gamepad>/buttonEast"
            }));
            Assert.That(player.Bindings, Is.EquivalentTo(new[] { "<Gamepad>/buttonEast", OldBinding }));
        }

        [Test]
        public void Timeout_MovesTheSessionToATerminalState()
        {
            var player = new FakePlayer(OldBinding);
            using var operation = new InputRebindOperation(player, Context, Map, Action, OldBinding, null, timeoutSeconds: 5d);
            operation.Start(0d);

            Assert.That(operation.Poll(4d), Is.False);
            Assert.That(operation.Poll(6d), Is.True);
            Assert.That(operation.Status, Is.EqualTo(RebindStatus.TimedOut));
            Assert.That(operation.Offer("<Gamepad>/buttonEast", "Button", 7d), Is.False);
            Assert.That(operation.LastRejection, Is.EqualTo(RebindRejection.NotListening));
        }

        [Test]
        public void Cancel_KeepsTheSessionTerminal()
        {
            var player = new FakePlayer(OldBinding);
            using var operation = new InputRebindOperation(player, Context, Map, Action, OldBinding);
            operation.Start(0d);
            operation.Cancel();

            Assert.That(operation.Status, Is.EqualTo(RebindStatus.Canceled));
            Assert.That(operation.Offer("<Gamepad>/buttonEast", "Button", 1d), Is.False);
            Assert.That(operation.Apply(), Is.False);
        }

        [Test]
        public void Dispose_DoesNotOverwriteAnAcceptedResult()
        {
            var player = new FakePlayer(OldBinding);
            var operation = new InputRebindOperation(player, Context, Map, Action, OldBinding);
            operation.Start(0d);
            operation.Offer("<Gamepad>/buttonEast", "Button", 1d);
            operation.Dispose();

            Assert.That(operation.Status, Is.EqualTo(RebindStatus.Accepted));
            Assert.That(operation.Apply(), Is.False, "a disposed session cannot apply");
        }

        [Test]
        public void Fuzz_TerminalStatesAreAbsorbing()
        {
            var random = new System.Random(910019);
            string[] candidates =
            {
                "<Gamepad>/buttonSouth", "<Gamepad>/buttonEast", "<Keyboard>/escape", "<Keyboard>/space",
                "<Mouse>/leftButton", string.Empty
            };

            for (int round = 0; round < 200; round++)
            {
                var player = new FakePlayer(OldBinding);
                var policy = new InputRebindPolicy(new[] { "<Keyboard>/escape" }, null, 128);
                using var operation = new InputRebindOperation(player, Context, Map, Action, OldBinding, policy, 10d);
                operation.Start(0d);
                RebindStatus previous = operation.Status;

                for (int step = 0; step < 64; step++)
                {
                    int op = random.Next(10);
                    if (op < 6)
                    {
                        operation.Offer(candidates[random.Next(candidates.Length)], "Button", step);
                    }
                    else if (op < 8)
                    {
                        operation.Poll(step);
                    }
                    else if (op < 9)
                    {
                        operation.Cancel();
                    }
                    else
                    {
                        operation.Apply();
                    }

                    bool wasTerminal = previous == RebindStatus.Accepted || previous == RebindStatus.Conflict ||
                                       previous == RebindStatus.Canceled || previous == RebindStatus.TimedOut;
                    if (wasTerminal)
                    {
                        Assert.That(operation.Status, Is.EqualTo(previous), "a terminal state must not change");
                    }

                    if (operation.Status == RebindStatus.Accepted || operation.Status == RebindStatus.Conflict)
                    {
                        Assert.That(operation.ControlPath, Is.Not.Null);
                        Assert.That(operation.ControlPath, Is.Not.Empty);
                    }

                    previous = operation.Status;
                }
            }
        }

        private sealed class FakePlayer : IInputRebindOperations
        {
            public FakePlayer(params string[] bindings)
            {
                Bindings = new List<string>(bindings);
            }

            public List<string> Bindings { get; }

            public List<string> Applied { get; } = new List<string>();

            public bool RebindAction(string contextName, string actionMapName, string actionName, string oldBinding, string newBinding)
            {
                int index = Bindings.IndexOf(oldBinding);
                if (index < 0) return false;
                Bindings[index] = newBinding;
                Applied.Add(oldBinding + "->" + newBinding);
                return true;
            }

            public bool ResetActionBinding(string contextName, string actionMapName, string actionName)
            {
                return true;
            }

            public string[] GetActionBindings(string contextName, string actionMapName, string actionName)
            {
                return Bindings.ToArray();
            }

            public void ResetAllActionBindings()
            {
            }

            public string ExportBindingOverridesJson()
            {
                return string.Empty;
            }

            public bool TryExportBindingOverridesJson(out string json)
            {
                json = string.Empty;
                return true;
            }

            public bool ImportBindingOverridesJson(string json, bool removeExisting = true)
            {
                return true;
            }
        }
    }
}
