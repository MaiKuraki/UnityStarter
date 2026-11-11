using System;
using System.Collections.Generic;
using System.Text;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Device-set matrix for <c>pairAllMatchingDevices</c>. Each case asserts how many devices a join ends up
    /// pairing, which is the property that decides whether bindings on the other devices can fire at all.
    /// </summary>
    /// <remarks>
    /// The matrix deliberately includes a configuration that references fewer devices than are attached: pairing
    /// must follow the referenced layouts, not every device in the process.
    /// </remarks>
    public sealed class InputMixedDeviceMatrixTests
    {
        private enum Attachment
        {
            KeyboardOnly,
            KeyboardAndMouse,
            KeyboardMouseGamepad,
            GamepadOnly
        }

        [Test]
        public void Matrix_PairAllMatching_SelectsOnlyReferencedDevices()
        {
            AssertMatrix(
                pairAllMatching: true,
                attachment: Attachment.KeyboardOnly,
                bindings: new[] { "<Keyboard>/enter" },
                expectedPaired: 1);

            AssertMatrix(
                pairAllMatching: true,
                attachment: Attachment.KeyboardAndMouse,
                bindings: new[] { "<Keyboard>/enter", "<Mouse>/leftButton" },
                expectedPaired: 2);

            AssertMatrix(
                pairAllMatching: true,
                attachment: Attachment.KeyboardMouseGamepad,
                bindings: new[] { "<Keyboard>/enter", "<Mouse>/leftButton", "<Gamepad>/buttonSouth" },
                expectedPaired: 3);

            AssertMatrix(
                pairAllMatching: true,
                attachment: Attachment.GamepadOnly,
                bindings: new[] { "<Gamepad>/buttonSouth" },
                expectedPaired: 1);
        }

        [Test]
        public void Matrix_PairAllMatching_IgnoresUnreferencedAttachments()
        {
            AssertMatrix(
                pairAllMatching: true,
                attachment: Attachment.KeyboardMouseGamepad,
                bindings: new[] { "<Keyboard>/enter", "<Gamepad>/buttonSouth" },
                expectedPaired: 2);
        }

        [Test]
        public void Matrix_DefaultSelection_DoesNotPairEveryDevice()
        {
            AssertMatrix(
                pairAllMatching: false,
                attachment: Attachment.KeyboardMouseGamepad,
                bindings: new[] { "<Keyboard>/enter", "<Mouse>/leftButton", "<Gamepad>/buttonSouth" },
                expectedPaired: -1);
        }

        [Test]
        public void Soak_RepeatedJoinAndDispose_KeepsPairingStable()
        {
            const int Iterations = 40;
            string yaml = BuildConfiguration(
                pairAllMatching: true,
                new[] { "<Keyboard>/enter", "<Mouse>/leftButton", "<Gamepad>/buttonSouth" });

            var counts = new List<int>(Iterations);
            for (int i = 0; i < Iterations; i++)
            {
                counts.Add(JoinAndCount(yaml, Attachment.KeyboardMouseGamepad));
            }

            for (int i = 1; i < counts.Count; i++)
            {
                Assert.That(
                    counts[i],
                    Is.EqualTo(counts[0]),
                    "Pairing count drifted at iteration " + i + ": first=" + counts[0] + ", current=" + counts[i]);
            }

            Assert.That(counts[0], Is.EqualTo(3), "Every iteration must pair the three referenced devices.");
        }

        private static void AssertMatrix(
            bool pairAllMatching,
            Attachment attachment,
            string[] bindings,
            int expectedPaired)
        {
            string yaml = BuildConfiguration(pairAllMatching, bindings);
            int paired = JoinAndCount(yaml, attachment);

            if (expectedPaired < 0)
            {
                Assert.That(
                    paired,
                    Is.LessThan(3),
                    "The default path must not pair every device; paired=" + paired);
                return;
            }

            Assert.That(
                paired,
                Is.EqualTo(expectedPaired),
                "pairAllMatching=" + pairAllMatching +
                " attachment=" + attachment +
                " bindings=" + string.Join("|", bindings) +
                " produced " + paired + " paired device(s).");
        }

        private static int JoinAndCount(string yaml, Attachment attachment)
        {
            var devices = new List<InputDevice>(4);
            if (attachment == Attachment.KeyboardOnly || attachment == Attachment.KeyboardAndMouse ||
                attachment == Attachment.KeyboardMouseGamepad)
            {
                devices.Add(UIS.AddDevice<Keyboard>());
            }

            if (attachment == Attachment.KeyboardAndMouse || attachment == Attachment.KeyboardMouseGamepad)
            {
                devices.Add(UIS.AddDevice<Mouse>());
            }

            if (attachment == Attachment.KeyboardMouseGamepad || attachment == Attachment.GamepadOnly)
            {
                devices.Add(UIS.AddDevice<Gamepad>());
            }

            var manager = new InputManager();
            try
            {
                InputManagerInitializationResult initialization = manager.InitializeWithResult(yaml);
                Assert.That(initialization.IsSuccess, Is.True, initialization.Message);

                IInputPlayer player = manager.JoinSinglePlayer(0);
                Assert.That(player, Is.Not.Null, "Join failed for attachment " + attachment);

                var concrete = player as InputPlayer;
                Assert.That(concrete, Is.Not.Null);
                return concrete.User.pairedDevices.Count;
            }
            finally
            {
                manager.Dispose();
                for (int i = devices.Count - 1; i >= 0; i--) UIS.RemoveDevice(devices[i]);
            }
        }

        private static string BuildConfiguration(bool pairAllMatching, string[] bindings)
        {
            var builder = new StringBuilder(512);
            builder.AppendLine("schemaVersion: 1");
            builder.AppendLine("schemaFingerprint: \"\"");
            builder.AppendLine("playerSlots:");
            builder.AppendLine("  - playerId: 0");
            builder.AppendLine("    pairAllMatchingDevices: " + (pairAllMatching ? "true" : "false"));
            builder.AppendLine("    contexts:");
            builder.AppendLine("      - name: Gameplay");
            builder.AppendLine("        actionMap: PlayerActions");
            builder.AppendLine("        priority: 0");
            builder.AppendLine("        blocksLowerPriority: true");
            builder.AppendLine("        bindings:");
            builder.AppendLine("          - type: Button");
            builder.AppendLine("            action: Confirm");
            builder.AppendLine("            expectedControlType: Button");
            builder.AppendLine("            bindingGroups: \"\"");
            builder.AppendLine("            deviceBindings:");
            for (int i = 0; i < bindings.Length; i++)
            {
                builder.AppendLine("              - \"" + bindings[i] + "\"");
            }

            builder.AppendLine("            compositeBindings: []");
            builder.AppendLine("            updateMode: EventDriven");
            builder.AppendLine("            longPressMs: 0");
            builder.AppendLine("            longPressValueThreshold: 0.5");
            return builder.ToString();
        }
    }
}
