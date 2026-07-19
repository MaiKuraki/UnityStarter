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
        public void DetectConflicts_IncludesStructuredCompositeParts()
        {
            var config = CreateConfig(
                new ActionBindingConfig
                {
                    Type = ActionValueType.Vector2,
                    ActionName = "Move",
                    DeviceBindings = new List<string>(),
                    CompositeBindings = new List<CompositeBindingConfig>
                    {
                        new CompositeBindingConfig
                        {
                            Name = "2DVector",
                            Parts = new List<CompositePartBindingConfig>
                            {
                                new CompositePartBindingConfig { Name = "up", Path = "<Keyboard>/w" },
                                new CompositePartBindingConfig { Name = "down", Path = "<Keyboard>/s" },
                                new CompositePartBindingConfig { Name = "left", Path = "<Keyboard>/a" },
                                new CompositePartBindingConfig { Name = "right", Path = "<Keyboard>/d" }
                            }
                        }
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
        public void DetectConflicts_SkipsNullAndOversizedUnvalidatedEntries()
        {
            var config = CreateConfig(
                null,
                new ActionBindingConfig
                {
                    Type = ActionValueType.Button,
                    ActionName = "Confirm",
                    DeviceBindings = new List<string> { null, new string('x', 1024), "<Keyboard>/enter" }
                },
                new ActionBindingConfig
                {
                    Type = ActionValueType.Button,
                    ActionName = "Submit",
                    DeviceBindings = new List<string> { "<keyboard>/ENTER" }
                });

            List<BindingConflict> conflicts = InputBindingValidator.DetectConflicts(config);

            Assert.That(conflicts, Has.Count.EqualTo(1));
            Assert.That(conflicts[0].Severity, Is.EqualTo(BindingConflictSeverity.Critical));
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
