using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Loads the YAML examples of both README files through the real initialization path, so the
    /// documented examples cannot drift from the accepted schema.
    /// </summary>
    public sealed class ReadmeExamplesTests
    {
        private static readonly Regex YamlBlock = new Regex("```yaml\r?\n(.*?)```", RegexOptions.Singleline);

        private readonly List<InputDevice> _devices = new List<InputDevice>();

        [SetUp]
        public void AddDevices()
        {
            _devices.Add(UIS.AddDevice<Keyboard>("ReadmeKeyboard"));
            _devices.Add(UIS.AddDevice<Gamepad>("ReadmeGamepad"));
        }

        [TearDown]
        public void RemoveDevices()
        {
            for (int i = _devices.Count - 1; i >= 0; i--)
            {
                InputDevice device = _devices[i];
                if (device != null && device.added) UIS.RemoveDevice(device);
            }

            _devices.Clear();
        }

        private static string PackageRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "ThirdParty/CycloneGames/CycloneGames.InputSystem"));

        private static string[] ReadYamlBlocks(string fileName)
        {
            string text = File.ReadAllText(Path.Combine(PackageRoot, fileName));
            MatchCollection matches = YamlBlock.Matches(text);
            var blocks = new string[matches.Count];
            for (int i = 0; i < matches.Count; i++) blocks[i] = matches[i].Groups[1].Value.Replace("\r\n", "\n");
            return blocks;
        }

        [Test]
        public void EnglishAndChineseReadme_CarryIdenticalYamlExamples()
        {
            string[] english = ReadYamlBlocks("README.md");
            string[] chinese = ReadYamlBlocks("README.SCH.md");

            Assert.That(english.Length, Is.EqualTo(2), "Both README files document the full configuration and the composite fragment.");
            Assert.That(chinese, Is.EqualTo(english));
        }

        [Test]
        public void MinimalConfiguration_InitializesAndExposesTheDocumentedActions()
        {
            string yaml = ReadYamlBlocks("README.md")[0];
            using (var manager = new InputManager())
            {
                InputManagerInitializationResult result = manager.InitializeWithResult(yaml);

                Assert.That(result.IsSuccess, Is.True, result.Message);
                InputPlayer player = manager.JoinSinglePlayer(0) as InputPlayer;
                Assert.That(player, Is.Not.Null);

                player.PushContext(new InputContext("PlayerActions", "Gameplay"));
                Assert.That(player.ActiveContextName.CurrentValue, Is.EqualTo("Gameplay"));
                Assert.That(player.GetVector2Observable("Gameplay", "PlayerActions", "Move"), Is.Not.SameAs(EmptyObservables.Vector2));
                Assert.That(player.GetButtonObservable("Gameplay", "PlayerActions", "Confirm"), Is.Not.SameAs(EmptyObservables.Unit));
            }
        }

        [Test]
        public void CompositeFragment_IsAcceptedInsideAContext()
        {
            string fragment = ReadYamlBlocks("README.md")[1];
            string yaml = "schemaVersion: 1\nplayerSlots:\n- playerId: 0\n  contexts:\n  - name: Gameplay\n    actionMap: PlayerActions\n    bindings:\n"
                + Regex.Replace(fragment, "^", "    ", RegexOptions.Multiline);

            using (var manager = new InputManager())
            {
                InputManagerInitializationResult result = manager.InitializeWithResult(yaml);

                Assert.That(result.IsSuccess, Is.True, result.Message);
                Assert.That(manager.JoinSinglePlayer(0), Is.Not.Null);
            }
        }

        [Test]
        public void ActionAddressingExamples_ResolveToStreams()
        {
            using (var manager = new InputManager())
            {
                Assert.That(manager.InitializeWithResult(ReadYamlBlocks("README.md")[0]).IsSuccess, Is.True);
                IInputPlayer player = manager.JoinSinglePlayer(0);

                int moveId = InputHashUtility.GetActionId("Gameplay", "PlayerActions", "Move");
                Assert.That(player.GetVector2Observable(moveId), Is.Not.SameAs(EmptyObservables.Vector2));
                Assert.That(player.GetVector2Observable("Gameplay", "PlayerActions", "Move"), Is.Not.SameAs(EmptyObservables.Vector2));

                int unknown = InputHashUtility.GetActionId("Gameplay", "PlayerActions", "Missing");
                Assert.That(player.GetVector2Observable(unknown), Is.SameAs(EmptyObservables.Vector2));
            }
        }
    }
}
