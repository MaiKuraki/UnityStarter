using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;

namespace CycloneGames.InputSystem.Tests.Editor
{
    public sealed class InputBindingValidatorTests
    {
        [Test]
        public void DetectConflicts_ReturnsCritical_ForSameBindingAndSameType()
        {
            var config = CreateConfig(
                new ActionBindingConfig
                {
                    Type = ActionValueType.Button,
                    ActionName = "Confirm",
                    DeviceBindings = new List<string> { "<Keyboard>/enter" }
                },
                new ActionBindingConfig
                {
                    Type = ActionValueType.Button,
                    ActionName = "Submit",
                    DeviceBindings = new List<string> { "<Keyboard>/enter" }
                });

            List<BindingConflict> conflicts = InputBindingValidator.DetectConflicts(config);

            Assert.AreEqual(1, conflicts.Count);
            Assert.AreEqual(BindingConflictSeverity.Critical, conflicts[0].Severity);
            Assert.AreEqual("Confirm", conflicts[0].ActionA);
            Assert.AreEqual("Submit", conflicts[0].ActionB);
        }

        [Test]
        public void DetectConflicts_ReturnsWarning_ForSameBindingAndDifferentType()
        {
            var config = CreateConfig(
                new ActionBindingConfig
                {
                    Type = ActionValueType.Button,
                    ActionName = "Confirm",
                    DeviceBindings = new List<string> { "<Gamepad>/rightTrigger" }
                },
                new ActionBindingConfig
                {
                    Type = ActionValueType.Float,
                    ActionName = "Charge",
                    DeviceBindings = new List<string> { "<Gamepad>/rightTrigger" }
                });

            List<BindingConflict> conflicts = InputBindingValidator.DetectConflicts(config);

            Assert.AreEqual(1, conflicts.Count);
            Assert.AreEqual(BindingConflictSeverity.Warning, conflicts[0].Severity);
        }

        [Test]
        public void DetectConflicts_ExpandsInline2DVectorComposite()
        {
            var config = CreateConfig(
                new ActionBindingConfig
                {
                    Type = ActionValueType.Vector2,
                    ActionName = "Move",
                    DeviceBindings = new List<string>
                    {
                        "2DVector(up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)"
                    }
                },
                new ActionBindingConfig
                {
                    Type = ActionValueType.Button,
                    ActionName = "Interact",
                    DeviceBindings = new List<string> { "<Keyboard>/w" }
                });

            List<BindingConflict> conflicts = InputBindingValidator.DetectConflicts(config);

            Assert.AreEqual(1, conflicts.Count);
            Assert.AreEqual("<Keyboard>/w", conflicts[0].BindingPath);
        }

        [Test]
        public void FormatConflictsReport_ReturnsNoConflictMessage_ForEmptyList()
        {
            string report = InputBindingValidator.FormatConflictsReport(new List<BindingConflict>());

            Assert.AreEqual("No binding conflicts detected.", report);
        }

        private static PlayerSlotConfig CreateConfig(params ActionBindingConfig[] bindings)
        {
            return new PlayerSlotConfig
            {
                PlayerId = 0,
                Contexts = new List<ContextDefinitionConfig>
                {
                    new ContextDefinitionConfig
                    {
                        Name = "Gameplay",
                        ActionMap = "PlayerActions",
                        Bindings = new List<ActionBindingConfig>(bindings)
                    }
                }
            };
        }
    }
}
