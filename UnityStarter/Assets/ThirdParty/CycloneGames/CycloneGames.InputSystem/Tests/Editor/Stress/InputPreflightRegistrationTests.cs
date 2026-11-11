using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Covers the registration-resolution branch of the configuration preflight. That branch resolves
    /// interaction, processor and composite instances from the Input System registry by name and instantiates
    /// them through reflection, so it is the place where a stripped or missing implementation surfaces.
    /// <para>
    /// The contract being verified is fail-closed with a typed issue code: an unusable registration must be
    /// reported, never skipped and never thrown out of the public entry point. A positive control runs first so
    /// that a preflight which rejected everything could not make the negative cases pass while proving nothing.
    /// </para>
    /// <para>
    /// Code assertions are split into two groups on purpose. Unknown registrations are deterministic and are
    /// asserted with an exact code. Invalid parameter values are converted by package-owned code, whose choice
    /// between "throw" and "reject with a related code" is not part of this module's contract, so those cases
    /// assert rejection plus membership in the invalid-parameter code set instead of one pinned code.
    /// </para>
    /// </summary>
    public sealed class InputPreflightRegistrationTests
    {
        private static readonly InputConfigurationPreflightIssueCode[] InvalidInteractionParameterCodes =
        {
            InputConfigurationPreflightIssueCode.InvalidInteractionParameters,
            InputConfigurationPreflightIssueCode.UnsupportedPlacement
        };

        private static readonly InputConfigurationPreflightIssueCode[] InvalidProcessorParameterCodes =
        {
            InputConfigurationPreflightIssueCode.InvalidProcessorParameters,
            InputConfigurationPreflightIssueCode.UnsupportedPlacement
        };

        [SetUp]
        public void SetUp()
        {
            Assert.That(
                PlayerLoopHelper.IsMainThread,
                Is.True,
                "Preflight is main-thread confined; these cases must run on the main thread.");
        }

        /// <summary>
        /// Positive control. Without this case a blanket rejection would satisfy every negative case below.
        /// </summary>
        [Test]
        public void Preflight_ValidHoldInteraction_Succeeds()
        {
            InputConfiguration configuration = CreateConfiguration(interactions: "Hold(duration=0.4)");

            InputConfigurationPreflightResult result = InputSystemConfigurationPreflight.Validate(configuration);

            Assert.That(result.Status, Is.EqualTo(InputConfigurationPreflightStatus.Success), Describe(result));
            Assert.That(result.Issues, Is.Empty, Describe(result));
        }

        [Test]
        public void Preflight_UnknownInteraction_ReturnsUnknownInteraction()
        {
            InputConfiguration configuration =
                CreateConfiguration(interactions: "DefinitelyMissingInteractionForTest");

            InputConfigurationPreflightResult result = InputSystemConfigurationPreflight.Validate(configuration);

            Assert.That(result.Status, Is.EqualTo(InputConfigurationPreflightStatus.Failed), Describe(result));
            AssertContainsCode(result, InputConfigurationPreflightIssueCode.UnknownInteraction);
        }

        [Test]
        public void Preflight_UnknownProcessor_ReturnsUnknownProcessor()
        {
            InputConfiguration configuration =
                CreateConfiguration(processors: "DefinitelyMissingProcessorForTest");

            InputConfigurationPreflightResult result = InputSystemConfigurationPreflight.Validate(configuration);

            Assert.That(result.Status, Is.EqualTo(InputConfigurationPreflightStatus.Failed), Describe(result));
            AssertContainsCode(result, InputConfigurationPreflightIssueCode.UnknownProcessor);
        }

        [Test]
        public void Preflight_UnknownComposite_ReturnsUnknownComposite()
        {
            var composite = new CompositeBindingConfig
            {
                Name = "DefinitelyMissingCompositeForTest",
                Parameters = "mode=2",
                Parts = new List<CompositePartBindingConfig>
                {
                    new CompositePartBindingConfig { Name = "up", Path = "<Keyboard>/w" },
                    new CompositePartBindingConfig { Name = "down", Path = "<Keyboard>/s" }
                }
            };

            InputConfiguration configuration = CreateConfiguration(composite: composite);

            InputConfigurationPreflightResult result = InputSystemConfigurationPreflight.Validate(configuration);

            Assert.That(result.Status, Is.EqualTo(InputConfigurationPreflightStatus.Failed), Describe(result));
            AssertContainsCode(result, InputConfigurationPreflightIssueCode.UnknownComposite);
        }

        /// <summary>
        /// A value that cannot be converted must not be silently left at the parameter default. Whether the
        /// conversion throws or reports is package-owned; being accepted is the outcome that must not happen.
        /// </summary>
        [Test]
        public void Preflight_InvalidInteractionParameterValue_IsRejected()
        {
            InputConfiguration configuration = CreateConfiguration(interactions: "Hold(duration=notanumber)");

            InputConfigurationPreflightResult result = InputSystemConfigurationPreflight.Validate(configuration);

            Assert.That(result.Status, Is.EqualTo(InputConfigurationPreflightStatus.Failed), Describe(result));
            AssertContainsAnyCode(result, InvalidInteractionParameterCodes);
        }

        [Test]
        public void Preflight_InvalidProcessorParameterValue_IsRejected()
        {
            InputConfiguration configuration = CreateConfiguration(processors: "Scale(factor=notanumber)");

            InputConfigurationPreflightResult result = InputSystemConfigurationPreflight.Validate(configuration);

            Assert.That(result.Status, Is.EqualTo(InputConfigurationPreflightStatus.Failed), Describe(result));
            AssertContainsAnyCode(result, InvalidProcessorParameterCodes);
        }

        /// <summary>
        /// A rejected registration must leave the caller's configuration object untouched, so a caller can fix
        /// the offending field and retry instead of rebuilding the whole document.
        /// </summary>
        [Test]
        public void Preflight_RejectedRegistration_DoesNotMutateInputConfiguration()
        {
            InputConfiguration configuration =
                CreateConfiguration(interactions: "DefinitelyMissingInteractionForTest");

            InputConfigurationPreflightResult result = InputSystemConfigurationPreflight.Validate(configuration);

            Assert.That(result.Status, Is.EqualTo(InputConfigurationPreflightStatus.Failed), Describe(result));
            Assert.That(configuration.PlayerSlots, Is.Not.Null);
            Assert.That(configuration.PlayerSlots.Count, Is.EqualTo(1));
            Assert.That(configuration.PlayerSlots[0].Contexts, Is.Not.Null);
            Assert.That(configuration.PlayerSlots[0].Contexts.Count, Is.EqualTo(1));
            Assert.That(
                configuration.PlayerSlots[0].Contexts[0].Bindings[0].Interactions,
                Is.EqualTo("DefinitelyMissingInteractionForTest"));
        }

        private static InputConfiguration CreateConfiguration(
            string interactions = null,
            string processors = null,
            CompositeBindingConfig composite = null)
        {
            var binding = new ActionBindingConfig
            {
                Type = ActionValueType.Button,
                ActionName = "Fire",
                DeviceBindings = composite == null ? new List<string> { "<Keyboard>/space" } : null,
                CompositeBindings = composite == null ? null : new List<CompositeBindingConfig> { composite },
                Interactions = interactions,
                Processors = processors
            };

            return new InputConfiguration
            {
                SchemaVersion = InputConfiguration.CurrentSchemaVersion,
                PlayerSlots = new List<PlayerSlotConfig>
                {
                    new PlayerSlotConfig
                    {
                        PlayerId = 0,
                        Contexts = new List<ContextDefinitionConfig>
                        {
                            new ContextDefinitionConfig
                            {
                                Name = "Gameplay",
                                ActionMap = "Player",
                                Bindings = new List<ActionBindingConfig> { binding }
                            }
                        }
                    }
                }
            };
        }

        private static void AssertContainsCode(
            InputConfigurationPreflightResult result,
            InputConfigurationPreflightIssueCode expected)
        {
            for (int i = 0; i < result.Issues.Count; i++)
            {
                if (result.Issues[i].Code == expected) return;
            }

            Assert.Fail("Expected issue code " + expected + ". " + Describe(result));
        }

        private static void AssertContainsAnyCode(
            InputConfigurationPreflightResult result,
            InputConfigurationPreflightIssueCode[] accepted)
        {
            for (int i = 0; i < result.Issues.Count; i++)
            {
                for (int codeIndex = 0; codeIndex < accepted.Length; codeIndex++)
                {
                    if (result.Issues[i].Code == accepted[codeIndex]) return;
                }
            }

            Assert.Fail(
                "Expected one of " + string.Join(", ", accepted) +
                " but the preflight reported a different rejection code. " + Describe(result));
        }

        private static string Describe(InputConfigurationPreflightResult result)
        {
            if (result.Issues == null || result.Issues.Count == 0)
            {
                return "Status=" + result.Status + "; no issues were reported.";
            }

            var builder = new System.Text.StringBuilder();
            builder.Append("Status=").Append(result.Status).Append("; issues=");
            for (int i = 0; i < result.Issues.Count; i++)
            {
                if (i > 0) builder.Append(" | ");
                builder.Append(result.Issues[i].Code);
                builder.Append(" @ ").Append(result.Issues[i].Path);
                builder.Append(" : ").Append(result.Issues[i].Message);
            }

            return builder.ToString();
        }
    }
}
