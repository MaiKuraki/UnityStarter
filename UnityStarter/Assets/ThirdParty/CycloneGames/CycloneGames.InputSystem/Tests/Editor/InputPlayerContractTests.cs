using System.Collections.Generic;
using System.Text;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// The player contract is the union of three narrow interfaces, and the configuration version gate reports
    /// newer, older and missing values differently.
    /// </summary>
    public sealed class InputPlayerContractTests
    {
        private InputManager _manager;
        private Gamepad _pad;

        [TearDown]
        public void TearDown()
        {
            _manager?.Dispose();
            _manager = null;
            if (_pad != null && _pad.added) UIS.RemoveDevice(_pad);
            _pad = null;
            InputManager.ResetGlobalStateForDomainReload();
        }

        [Test]
        public void Player_SatisfiesEveryNarrowInterface()
        {
            _pad = UIS.AddDevice<Gamepad>("Contract_Pad");
            _manager = new InputManager();
            Assert.That(_manager.InitializeWithResult(MinimalYaml(1)).IsSuccess, Is.True);
            IInputPlayer player = _manager.JoinSinglePlayer(0);
            Assert.That(player, Is.Not.Null);

            IInputActionReader reader = player;
            IInputContextController contexts = player;
            IInputRebindOperations rebind = player;

            int actionId = InputHashUtility.GetActionId("Gameplay", "Map0", "Confirm");
            Assert.That(reader.TryReadValue<float>(actionId, out _), Is.True);
            Assert.That(reader.ActionBuffer, Is.Not.Null);
            Assert.That(contexts.ActiveContextName.CurrentValue, Is.Null);
            Assert.That(rebind.GetActionBindings("Gameplay", "Map0", "Confirm"), Is.Not.Empty);
        }

        [Test]
        public void SchemaVersion_MissingIsReportedAsMissing()
        {
            _manager = new InputManager();
            InputManagerInitializationResult result = _manager.InitializeWithResult(MinimalYaml(0));

            Assert.That(result.IsSuccess, Is.False);
            StringAssert.Contains("schemaVersion is missing", MessageOf(result));
        }

        [Test]
        public void SchemaVersion_NewerThanThePackageIsReportedAsNewer()
        {
            _manager = new InputManager();
            InputManagerInitializationResult result = _manager.InitializeWithResult(MinimalYaml(2));

            Assert.That(result.IsSuccess, Is.False);
            StringAssert.Contains("newer than this package supports", MessageOf(result));
        }

        [Test]
        public void SchemaVersion_OlderThanThePackageIsReportedAsOlder()
        {
            if (InputConfiguration.CurrentSchemaVersion <= 1) Assert.Pass("No older version exists yet.");

            _manager = new InputManager();
            InputManagerInitializationResult result = _manager.InitializeWithResult(MinimalYaml(1));

            Assert.That(result.IsSuccess, Is.False);
            StringAssert.Contains("older than this package requires", MessageOf(result));
        }

        private static string MessageOf(InputManagerInitializationResult result)
        {
            if (result.Validation == null) return result.Message ?? string.Empty;
            var builder = new StringBuilder();
            IReadOnlyList<InputConfigurationIssue> issues = result.Validation.Issues;
            for (int i = 0; i < issues.Count; i++) builder.Append(issues[i].Message).Append(' ');
            return builder.Length == 0 ? result.Message ?? string.Empty : builder.ToString();
        }

        private static string MinimalYaml(int schemaVersion)
        {
            var builder = new StringBuilder(700);
            if (schemaVersion > 0) builder.AppendLine("schemaVersion: " + schemaVersion);
            builder.AppendLine("playerSlots:");
            builder.AppendLine("  - playerId: 0");
            builder.AppendLine("    controlSchemes:");
            builder.AppendLine("      - name: Gamepad");
            builder.AppendLine("        bindingGroup: Gamepad");
            builder.AppendLine("        deviceRequirements:");
            builder.AppendLine("          - controlPath: \"<Gamepad>\"");
            builder.AppendLine("    contexts:");
            builder.AppendLine("      - name: Gameplay");
            builder.AppendLine("        actionMap: Map0");
            builder.AppendLine("        bindings:");
            builder.AppendLine("          - type: Button");
            builder.AppendLine("            action: Confirm");
            builder.AppendLine("            bindingGroups: Gamepad");
            builder.AppendLine("            deviceBindings:");
            builder.AppendLine("              - \"<Gamepad>/buttonSouth\"");
            return builder.ToString();
        }
    }
}
