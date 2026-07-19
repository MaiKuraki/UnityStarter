using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using VYaml.Serialization;

namespace CycloneGames.InputSystem.Runtime.Tests
{
    public sealed class InputRuntimeCoreTests
    {
        private sealed class DisposableFrameWorkItem : IFrameRunnerWorkItem, IDisposable
        {
            public bool IsDisposed { get; private set; }

            public bool MoveNext(long frameCount)
            {
                return !IsDisposed;
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        private sealed class ThrowingDisposable : IDisposable
        {
            public void Dispose()
            {
                throw new InvalidOperationException("Expected teardown failure.");
            }
        }

        private sealed class MemoryConfigurationSource : IInputConfigurationSource
        {
            private readonly string _content;

            internal MemoryConfigurationSource(string content)
            {
                _content = content;
            }

            internal int LoadCount { get; private set; }

            public UniTask<InputConfigurationReadResult> LoadAsync(
                string key,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LoadCount++;
                return UniTask.FromResult(InputConfigurationReadResult.Success(_content));
            }
        }

        private sealed class MissingConfigurationSource : IInputConfigurationSource
        {
            internal int LoadCount { get; private set; }

            public UniTask<InputConfigurationReadResult> LoadAsync(
                string key,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LoadCount++;
                return UniTask.FromResult(
                    InputConfigurationReadResult.Failure(InputConfigurationStorageStatus.NotFound));
            }
        }

        private sealed class MemoryConfigurationStore : IInputConfigurationStore
        {
            private string _content;

            internal MemoryConfigurationStore(string content)
            {
                _content = content;
            }

            internal int SaveCount { get; private set; }
            internal string Content => _content;

            public UniTask<InputConfigurationReadResult> LoadAsync(
                string key,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return UniTask.FromResult(InputConfigurationReadResult.Success(_content));
            }

            public UniTask<InputConfigurationStoreResult> SaveAsync(
                string key,
                string content,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _content = content;
                SaveCount++;
                return UniTask.FromResult(InputConfigurationStoreResult.Success());
            }

            public UniTask<InputConfigurationStoreResult> DeleteAsync(
                string key,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _content = null;
                return UniTask.FromResult(InputConfigurationStoreResult.Success());
            }
        }

        private sealed class MissingConfigurationStore : IInputConfigurationStore
        {
            internal string Content { get; private set; }
            internal int SaveCount { get; private set; }

            public UniTask<InputConfigurationReadResult> LoadAsync(
                string key,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return UniTask.FromResult(
                    InputConfigurationReadResult.Failure(InputConfigurationStorageStatus.NotFound));
            }

            public UniTask<InputConfigurationStoreResult> SaveAsync(
                string key,
                string content,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Content = content;
                SaveCount++;
                return UniTask.FromResult(InputConfigurationStoreResult.Success());
            }

            public UniTask<InputConfigurationStoreResult> DeleteAsync(
                string key,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Content = null;
                return UniTask.FromResult(InputConfigurationStoreResult.Success());
            }
        }

        private sealed class CancelOnSaveMissingConfigurationStore : IInputConfigurationStore
        {
            private readonly CancellationTokenSource _callerCancellation;

            internal CancelOnSaveMissingConfigurationStore(CancellationTokenSource callerCancellation)
            {
                _callerCancellation = callerCancellation;
            }

            internal bool SaveTokenCanBeCanceled { get; private set; }
            internal int SaveCount { get; private set; }

            public UniTask<InputConfigurationReadResult> LoadAsync(
                string key,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return UniTask.FromResult(
                    InputConfigurationReadResult.Failure(InputConfigurationStorageStatus.NotFound));
            }

            public UniTask<InputConfigurationStoreResult> SaveAsync(
                string key,
                string content,
                CancellationToken cancellationToken = default)
            {
                SaveTokenCanBeCanceled = cancellationToken.CanBeCanceled;
                _callerCancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                SaveCount++;
                return UniTask.FromResult(InputConfigurationStoreResult.Success());
            }

            public UniTask<InputConfigurationStoreResult> DeleteAsync(
                string key,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return UniTask.FromResult(InputConfigurationStoreResult.Success());
            }
        }

        [Test]
        public void ValidateAndPrepare_MigratesSchemaZeroAndDeepClones()
        {
            InputConfiguration source = CreateValidConfiguration();
            source.SchemaVersion = 0;

            InputConfigurationValidationResult result = InputConfigurationValidator.ValidateAndPrepare(source);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.WasMigrated, Is.True);
            Assert.That(result.Configuration.SchemaVersion, Is.EqualTo(InputConfiguration.CurrentSchemaVersion));
            source.PlayerSlots[0].Contexts[0].Name = "Mutated";
            Assert.That(result.Configuration.PlayerSlots[0].Contexts[0].Name, Is.EqualTo("Gameplay"));
        }

        [TestCase("2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)")]
        [TestCase("2dvector( MODE = 2, UP = <Keyboard>/upArrow, DOWN = <Keyboard>/downArrow, LEFT = <Keyboard>/leftArrow, RIGHT = <Keyboard>/rightArrow )")]
        public void ValidateAndPrepare_MigratesBoundedLegacyInline2DVectorWithoutMutatingSource(
            string inlineBinding)
        {
            InputConfiguration source = CreateLegacyVectorConfiguration(inlineBinding);
            ActionBindingConfig sourceAction = source.PlayerSlots[0].Contexts[0].Bindings[0];

            InputConfigurationValidationResult result =
                InputConfigurationValidator.ValidateAndPrepare(source);

            Assert.That(result.IsValid, Is.True, result.Issues.FirstOrDefault().ToString());
            Assert.That(result.WasMigrated, Is.True);
            ActionBindingConfig migrated = result.Configuration.PlayerSlots[0].Contexts[0].Bindings[0];
            Assert.That(migrated.DeviceBindings, Is.Empty);
            Assert.That(migrated.CompositeBindings, Has.Count.EqualTo(1));
            Assert.That(migrated.CompositeBindings[0].Name, Is.EqualTo("2DVector"));
            Assert.That(migrated.CompositeBindings[0].Parameters, Is.EqualTo("mode=2"));
            Assert.That(
                migrated.CompositeBindings[0].Parts.Select(part => part.Name),
                Is.EqualTo(new[] { "up", "down", "left", "right" }));

            Assert.That(source.SchemaVersion, Is.Zero);
            Assert.That(sourceAction.DeviceBindings, Is.EqualTo(new[] { inlineBinding }));
            Assert.That(sourceAction.CompositeBindings, Is.Null);
        }

        [TestCase("2DVector(mode=2,up=<Keyboard>/w,up=<Keyboard>/upArrow,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)")]
        [TestCase("2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,forward=<Keyboard>/d)")]
        [TestCase("2DVector(mode=2)")]
        [TestCase("2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d),")]
        public void ValidateAndPrepare_RejectsMalformedLegacyInline2DVectorWithoutMutatingSource(
            string inlineBinding)
        {
            InputConfiguration source = CreateLegacyVectorConfiguration(inlineBinding);

            InputConfigurationValidationResult result =
                InputConfigurationValidator.ValidateAndPrepare(source);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.WasMigrated, Is.True);
            Assert.That(
                result.Issues.Any(issue => issue.Code == InputConfigurationIssueCode.InvalidValue),
                Is.True);
            Assert.That(source.SchemaVersion, Is.Zero);
            Assert.That(
                source.PlayerSlots[0].Contexts[0].Bindings[0].DeviceBindings,
                Is.EqualTo(new[] { inlineBinding }));
            Assert.That(source.PlayerSlots[0].Contexts[0].Bindings[0].CompositeBindings, Is.Null);
        }

        [Test]
        public void ValidateAndPrepare_RejectsOversizedLegacyInline2DVectorBeforeMigration()
        {
            string inlineBinding = "2DVector(up=<Keyboard>/" + new string('x', 300) + ")";
            InputConfiguration source = CreateLegacyVectorConfiguration(inlineBinding);

            InputConfigurationValidationResult result =
                InputConfigurationValidator.ValidateAndPrepare(source);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.WasMigrated, Is.True);
            Assert.That(
                result.Issues.Any(issue => issue.Code == InputConfigurationIssueCode.LimitExceeded),
                Is.True);
            Assert.That(
                source.PlayerSlots[0].Contexts[0].Bindings[0].DeviceBindings,
                Is.EqualTo(new[] { inlineBinding }));
            Assert.That(source.PlayerSlots[0].Contexts[0].Bindings[0].CompositeBindings, Is.Null);
        }

        [Test]
        public void ValidateAndPrepare_RejectsLegacyMigrationThatExhaustsBindingCapacity()
        {
            const string InlineBinding =
                "2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)";
            InputConfiguration source = CreateLegacyVectorConfiguration(InlineBinding);
            ActionBindingConfig action = source.PlayerSlots[0].Contexts[0].Bindings[0];
            action.CompositeBindings = new List<CompositeBindingConfig>
            {
                new CompositeBindingConfig
                {
                    Name = "LegacyAxis",
                    Parts = new List<CompositePartBindingConfig>
                    {
                        new CompositePartBindingConfig { Name = "first", Path = "<Keyboard>/q" },
                        new CompositePartBindingConfig { Name = "second", Path = "<Keyboard>/e" },
                        new CompositePartBindingConfig { Name = "third", Path = "<Keyboard>/r" }
                    }
                }
            };
            var limits = new InputConfigurationLimits(
                maxBindingsPerAction: 4,
                maxCompositesPerAction: 2);

            InputConfigurationValidationResult result =
                InputConfigurationValidator.ValidateAndPrepare(source, limits);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.WasMigrated, Is.True);
            Assert.That(result.Configuration, Is.Not.Null);
            Assert.That(
                result.Issues.Any(issue =>
                    issue.Code == InputConfigurationIssueCode.LimitExceeded &&
                    issue.Message.Contains("Total direct and composite-part binding count", StringComparison.Ordinal)),
                Is.True);
            Assert.That(action.DeviceBindings, Is.EqualTo(new[] { InlineBinding }));
            Assert.That(action.CompositeBindings, Has.Count.EqualTo(1));
        }

        [Test]
        public void ValidateAndPrepare_RejectsNullListsAndConfiguredLimits()
        {
            InputConfiguration source = CreateValidConfiguration();
            source.PlayerSlots[0].Contexts = null;
            Assert.That(InputConfigurationValidator.ValidateAndPrepare(source).IsValid, Is.False);

            source = CreateValidConfiguration();
            var limits = new InputConfigurationLimits(maxPlayers: 1, maxContextsPerPlayer: 1, maxActionsPerContext: 1);
            source.PlayerSlots.Add(CreateValidConfiguration().PlayerSlots[0]);
            Assert.That(InputConfigurationValidator.ValidateAndPrepare(source, limits).IsValid, Is.False);
        }

        [Test]
        public void ValidateAndPrepare_RejectsDuplicateIdentityAndKnownHashCollision()
        {
            InputConfiguration duplicate = CreateValidConfiguration();
            ActionBindingConfig action = duplicate.PlayerSlots[0].Contexts[0].Bindings[0];
            duplicate.PlayerSlots[0].Contexts[0].Bindings.Add(CloneAction(action));
            InputConfigurationValidationResult duplicateResult = InputConfigurationValidator.ValidateAndPrepare(duplicate);
            Assert.That(duplicateResult.Issues.Any(issue => issue.Code == InputConfigurationIssueCode.DuplicateActionIdentity), Is.True);

            InputConfiguration collision = CreateValidConfiguration();
            collision.PlayerSlots[0].Contexts[0].Name = "Ctx";
            collision.PlayerSlots[0].Contexts[0].ActionMap = "Map";
            collision.PlayerSlots[0].Contexts[0].Bindings = new List<ActionBindingConfig>
            {
                CreateButton("A1suh7sxrmbp5y", "<Keyboard>/a"),
                CreateButton("Aejdwytloo2v9", "<Keyboard>/b")
            };
            InputConfigurationValidationResult collisionResult = InputConfigurationValidator.ValidateAndPrepare(collision);
            Assert.That(collisionResult.Issues.Any(issue => issue.Code == InputConfigurationIssueCode.ActionIdCollision), Is.True);
        }

        [Test]
        public void ValidateAndPrepare_RejectsPseudoCompositeDuplicateBindingAndUnknownGroup()
        {
            InputConfiguration source = CreateValidConfiguration();
            ActionBindingConfig action = source.PlayerSlots[0].Contexts[0].Bindings[0];
            action.DeviceBindings = new List<string>
            {
                "2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)",
                "2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)"
            };
            action.BindingGroups = "Typo";
            source.PlayerSlots[0].ControlSchemes = new List<ControlSchemeConfig>
            {
                new ControlSchemeConfig
                {
                    Name = "KeyboardMouse",
                    BindingGroup = "KeyboardMouse",
                    DeviceRequirements = new List<ControlSchemeDeviceRequirementConfig>
                    {
                        new ControlSchemeDeviceRequirementConfig { ControlPath = "<Keyboard>" }
                    }
                }
            };

            InputConfigurationValidationResult result = InputConfigurationValidator.ValidateAndPrepare(source);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues.Count, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void ValidateAndPrepare_RejectsInvisiblePrivateAndUnpairedSurrogatesButAllowsPair()
        {
            string[] invalidNames =
                { "Bad\u200BName", "Bad\uE000Name", "Bad\u2028Name", "Bad\U000E0001Name", "Bad\uD800Name", "Bad\uDC00Name" };
            for (int i = 0; i < invalidNames.Length; i++)
            {
                InputConfiguration invalid = CreateValidConfiguration();
                invalid.PlayerSlots[0].Contexts[0].Name = invalidNames[i];
                Assert.That(InputConfigurationValidator.ValidateAndPrepare(invalid).IsValid, Is.False);
            }

            InputConfiguration valid = CreateValidConfiguration();
            valid.PlayerSlots[0].Contexts[0].Name = "Gameplay\U0001F3AE";
            Assert.That(InputConfigurationValidator.ValidateAndPrepare(valid).IsValid, Is.True);
        }

        [Test]
        public void ValidateAndPrepare_RejectsNegativePlayerAndUnsupportedJoinSemantics()
        {
            InputConfiguration source = CreateValidConfiguration();
            source.PlayerSlots[0].PlayerId = -1;
            source.PlayerSlots[0].JoinAction = new ActionBindingConfig
            {
                Type = ActionValueType.Vector2,
                ActionName = "Join",
                DeviceBindings = new List<string> { "<Gamepad>/start" },
                UpdateMode = InputUpdateMode.Polling,
                LongPressMs = 250
            };

            InputConfigurationValidationResult result = InputConfigurationValidator.ValidateAndPrepare(source);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues.Count, Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void ValidateAndPrepare_RejectsOversizedShapeBeforeCloneAndCapsDiagnostics()
        {
            InputConfiguration oversized = CreateValidConfiguration();
            while (oversized.PlayerSlots.Count <= InputConfigurationLimits.Default.MaxPlayers)
            {
                oversized.PlayerSlots.Add(null);
            }

            InputConfigurationValidationResult shapeResult =
                InputConfigurationValidator.ValidateAndPrepare(oversized);
            Assert.That(shapeResult.IsValid, Is.False);
            Assert.That(shapeResult.Configuration, Is.Null);
            Assert.That(shapeResult.Issues, Has.Count.EqualTo(1));
            Assert.That(shapeResult.Issues[0].Code, Is.EqualTo(InputConfigurationIssueCode.LimitExceeded));

            InputConfiguration noisy = CreateValidConfiguration();
            noisy.PlayerSlots[0].Contexts.Clear();
            for (int contextIndex = 0; contextIndex < 2; contextIndex++)
            {
                var context = new ContextDefinitionConfig
                {
                    Name = string.Empty,
                    ActionMap = string.Empty,
                    Bindings = new List<ActionBindingConfig>()
                };
                for (int actionIndex = 0; actionIndex < 128; actionIndex++)
                {
                    context.Bindings.Add(new ActionBindingConfig
                    {
                        Type = (ActionValueType)99,
                        ActionName = string.Empty,
                        DeviceBindings = new List<string>(),
                        CompositeBindings = new List<CompositeBindingConfig>(),
                        UpdateMode = (InputUpdateMode)99,
                        LongPressMs = -1,
                        LongPressValueThreshold = float.NaN
                    });
                }

                noisy.PlayerSlots[0].Contexts.Add(context);
            }

            InputConfigurationValidationResult noisyResult =
                InputConfigurationValidator.ValidateAndPrepare(noisy);
            Assert.That(noisyResult.IsValid, Is.False);
            Assert.That(noisyResult.Issues, Has.Count.EqualTo(256));
        }

        [Test]
        public void ValidateAndPrepare_DoesNotTokenizeOrComposeOversizedOptionalStrings()
        {
            InputConfiguration oversizedGroups = CreateValidConfiguration();
            oversizedGroups.PlayerSlots[0].Contexts[0].Bindings[0].BindingGroups =
                new string(';', 100_000);
            InputConfigurationValidationResult groupsResult =
                InputConfigurationValidator.ValidateAndPrepare(oversizedGroups);
            Assert.That(groupsResult.IsValid, Is.False);
            Assert.That(
                groupsResult.Issues.Any(issue =>
                    issue.Code == InputConfigurationIssueCode.LimitExceeded &&
                    issue.Path.EndsWith(".bindingGroups", StringComparison.Ordinal)),
                Is.True);

            InputConfiguration oversizedParameters = CreateValidConfiguration();
            ActionBindingConfig action = oversizedParameters.PlayerSlots[0].Contexts[0].Bindings[0];
            action.DeviceBindings.Clear();
            action.CompositeBindings = new List<CompositeBindingConfig>();
            action.CompositeBindings.Add(new CompositeBindingConfig
            {
                Name = "2DVector",
                Parameters = new string('x', 100_000),
                Parts = new List<CompositePartBindingConfig>
                {
                    new CompositePartBindingConfig { Name = "up", Path = "<Keyboard>/w" }
                }
            });
            InputConfigurationValidationResult parametersResult =
                InputConfigurationValidator.ValidateAndPrepare(oversizedParameters);
            Assert.That(parametersResult.IsValid, Is.False);
            Assert.That(
                parametersResult.Issues.Any(issue =>
                    issue.Code == InputConfigurationIssueCode.LimitExceeded &&
                    issue.Path.EndsWith(".parameters", StringComparison.Ordinal)),
                Is.True);
        }

        [Test]
        public void InputContext_DefaultAndExplicitPoliciesAreStable()
        {
            using var defaultPolicy = new InputContext("Player");
            using var layered = new InputContext("UI", "Overlay", 100, false);
            Assert.That(defaultPolicy.Priority, Is.Zero);
            Assert.That(defaultPolicy.BlocksLowerPriority, Is.True);
            Assert.That(layered.Priority, Is.EqualTo(100));
            Assert.That(layered.BlocksLowerPriority, Is.False);
        }

        [Test]
        public void BindingOverrideProfile_RoundTripsAcrossBindingReorder()
        {
            Keyboard keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            InputPlayer first = null;
            InputPlayer second = null;
            try
            {
                PlayerSlotConfig firstConfig = CreateValidConfiguration().PlayerSlots[0];
                firstConfig.Contexts[0].Bindings[0].DeviceBindings = new List<string>
                {
                    "<Keyboard>/space",
                    "<Keyboard>/enter"
                };
                InputUser firstUser = InputUser.PerformPairingWithDevice(keyboard);
                first = new InputPlayer(0, firstUser, firstConfig, keyboard);
                Assert.That(first.RebindAction("Gameplay", "Player", "Fire", "<Keyboard>/space", "<Keyboard>/a"), Is.True);
                string profile = first.ExportBindingOverridesJson();
                first.Dispose();
                first = null;

                PlayerSlotConfig reordered = CreateValidConfiguration().PlayerSlots[0];
                reordered.Contexts[0].Bindings[0].DeviceBindings = new List<string>
                {
                    "<Keyboard>/enter",
                    "<Keyboard>/space"
                };
                InputUser secondUser = InputUser.PerformPairingWithDevice(keyboard);
                second = new InputPlayer(0, secondUser, reordered, keyboard);
                Assert.That(second.ImportBindingOverridesJson(profile), Is.True);
                Assert.That(second.GetActionBindings("Gameplay", "Player", "Fire")[1], Is.EqualTo("<Keyboard>/a"));
            }
            finally
            {
                second?.Dispose();
                first?.Dispose();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void ManagerBindingProfile_RejectsInvalidUnjoinedEntriesWithoutPoisoningLaterJoin()
        {
            const string EmptyOverrides = "{\"schemaVersion\":1,\"bindings\":[]}";
            int assetsBefore = Resources.FindObjectsOfTypeAll<InputActionAsset>().Length;
            Keyboard keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            InputManager manager = null;
            try
            {
                keyboard.MakeCurrent();
                manager = new InputManager();
                Assert.That(manager.InitializeWithResult(Serialize(CreateValidConfiguration())).IsSuccess, Is.True);

                Assert.That(
                    manager.ImportBindingOverrideProfile(CreateProfile("{not-json")),
                    Is.False);
                Assert.That(
                    manager.ImportBindingOverrideProfile(
                        CreateProfile("{\"schemaVersion\":2,\"bindings\":[]}")),
                    Is.False);
                Assert.That(
                    manager.ImportBindingOverrideProfile(
                        CreateProfile("{\"schemaVersion\":1,\"bindings\":[{\"contextName\":\"Gameplay\"}]}")),
                    Is.False);
                Assert.That(
                    manager.ImportBindingOverrideProfile(
                        CreateProfile(
                            "{\"schemaVersion\":1,\"bindings\":[{" +
                            "\"contextName\":\"Gameplay\",\"actionMapName\":\"Player\"," +
                            "\"actionName\":\"Missing\",\"bindingIndex\":0," +
                            "\"originalPath\":\"<Keyboard>/space\",\"overridePath\":\"<Keyboard>/a\"}]}")),
                    Is.False);
                Assert.That(
                    manager.ImportBindingOverrideProfile(
                        CreateProfile(CreateBindingOverrideJson(
                            overridePath: "<DefinitelyMissing>/button"))),
                    Is.False);
                Assert.That(
                    manager.ImportBindingOverrideProfile(
                        CreateProfile(CreateBindingOverrideJson(
                            overridePath: "<Mouse>/position"))),
                    Is.False);
                Assert.That(
                    manager.ImportBindingOverrideProfile(
                        CreateProfile(CreateBindingOverrideJson(
                            overrideInteractions: "DefinitelyMissingInteraction"))),
                    Is.False);
                Assert.That(
                    manager.ImportBindingOverrideProfile(
                        CreateProfile(CreateBindingOverrideJson(
                            overrideProcessors: "DefinitelyMissingProcessor"))),
                    Is.False);

                var duplicatePlayers = new InputBindingOverrideProfile();
                duplicatePlayers.Players.Add(
                    new InputBindingOverrideEntry { PlayerId = 0, OverridesJson = EmptyOverrides });
                duplicatePlayers.Players.Add(
                    new InputBindingOverrideEntry { PlayerId = 0, OverridesJson = EmptyOverrides });
                Assert.That(manager.ImportBindingOverrideProfile(duplicatePlayers), Is.False);
                Assert.That(
                    Resources.FindObjectsOfTypeAll<InputActionAsset>().Length,
                    Is.EqualTo(assetsBefore));

                Assert.That(manager.JoinPlayerOnSharedDevice(0), Is.Not.Null);
                Assert.That(manager.ActivePlayerCount, Is.EqualTo(1));
            }
            finally
            {
                manager?.Dispose();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }

            Assert.That(
                Resources.FindObjectsOfTypeAll<InputActionAsset>().Length,
                Is.EqualTo(assetsBefore));
        }

        [Test]
        public void InactivePlayerBindingProfile_RejectsUnknownOverrideRegistrationsBeforeContextEnable()
        {
            Keyboard keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            InputPlayer player = null;
            InputContext context = null;
            try
            {
                InputUser user = InputUser.PerformPairingWithDevice(keyboard);
                player = new InputPlayer(
                    0,
                    user,
                    CreateValidConfiguration().PlayerSlots[0],
                    keyboard);
                context = new InputContext("Player", "Gameplay");

                Assert.That(
                    player.ImportBindingOverridesJson(
                        CreateBindingOverrideJson(
                            overrideProcessors: "DefinitelyMissingProcessor")),
                    Is.False);
                Assert.DoesNotThrow(() => player.PushContext(context));
                Assert.That(player.ActiveContextName.CurrentValue, Is.EqualTo("Gameplay"));
                Assert.That(GetPlayerActionAsset(player).enabled, Is.True);
            }
            finally
            {
                context?.Dispose();
                player?.Dispose();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void CaptureContext_ReentrantLeaseDoesNotReleaseNewerCapture()
        {
            Keyboard keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            InputPlayer player = null;
            InputContext context = null;
            IDisposable first = null;
            IDisposable second = null;
            try
            {
                PlayerSlotConfig config = CreateValidConfiguration().PlayerSlots[0];
                InputUser user = InputUser.PerformPairingWithDevice(keyboard);
                player = new InputPlayer(0, user, config, keyboard);
                context = new InputContext("Player", "Gameplay");

                first = player.CaptureContext(context);
                second = player.CaptureContext(context);
                first.Dispose();
                first = null;

                Assert.That(player.ActiveContextName.CurrentValue, Is.EqualTo("Gameplay"));
                second.Dispose();
                second = null;
                Assert.That(player.ActiveContextName.CurrentValue, Is.Null);
            }
            finally
            {
                second?.Dispose();
                first?.Dispose();
                context?.Dispose();
                player?.Dispose();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void ActivateTopContext_AllowsSynchronousSubscriberToPopContext()
        {
            Keyboard keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            InputPlayer player = null;
            InputContext context = null;
            BehaviorSubject<Unit> trigger = null;
            try
            {
                PlayerSlotConfig config = CreateValidConfiguration().PlayerSlots[0];
                InputUser user = InputUser.PerformPairingWithDevice(keyboard);
                player = new InputPlayer(0, user, config, keyboard);
                context = new InputContext("Player", "Gameplay");
                trigger = new BehaviorSubject<Unit>(Unit.Default);
                context.AddBinding(trigger, new ActionCommand(player.PopContext));

                Assert.DoesNotThrow(() => player.PushContext(context));
                Assert.That(player.ActiveContextName.CurrentValue, Is.Null);
                Assert.That(player.RemoveContext(context), Is.False);
            }
            finally
            {
                trigger?.Dispose();
                context?.Dispose();
                player?.Dispose();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void ContextChanged_SynchronousRefreshLoopStopsAfterBoundedPassesAndFailsClosed()
        {
            Keyboard keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            InputPlayer player = null;
            InputContext context = null;
            try
            {
                InputUser user = InputUser.PerformPairingWithDevice(keyboard);
                player = new InputPlayer(0, user, CreateValidConfiguration().PlayerSlots[0], keyboard);
                context = new InputContext("Player", "Gameplay");
                int notificationCount = 0;
                player.OnContextChanged += _ =>
                {
                    notificationCount++;
                    player.RefreshActiveContext();
                };
                Assert.DoesNotThrow(() => player.PushContext(context));

                Assert.That(notificationCount, Is.EqualTo(16));
                Assert.That(player.ActiveContextName.CurrentValue, Is.Null);
                Assert.That(GetPlayerActionAsset(player).enabled, Is.False);
            }
            finally
            {
                player?.Dispose();
                context?.Dispose();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void ContextSubscriptionFailure_CommitsModelFailsClosedAndRecoversAfterExplicitRefresh()
        {
            Keyboard keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            InputPlayer player = null;
            InputContext context = null;
            Observable<Unit> failingSource = null;
            try
            {
                InputUser user = InputUser.PerformPairingWithDevice(keyboard);
                player = new InputPlayer(0, user, CreateValidConfiguration().PlayerSlots[0], keyboard);
                context = new InputContext("Player", "Gameplay");
                failingSource = Observable.Create<Unit>(_ =>
                    throw new InvalidOperationException("Expected synchronous subscription failure."));
                context.AddBinding(failingSource, NullCommand.Instance);
                Assert.Throws<InvalidOperationException>(() => player.PushContext(context));
                Assert.That(player.ActiveContextName.CurrentValue, Is.Null);
                Assert.That(GetPlayerActionAsset(player).enabled, Is.False);

                Assert.That(context.RemoveBinding(failingSource), Is.True);
                Assert.DoesNotThrow(player.RefreshActiveContext);
                Assert.That(player.ActiveContextName.CurrentValue, Is.EqualTo("Gameplay"));
                Assert.That(GetPlayerActionAsset(player).enabled, Is.True);
            }
            finally
            {
                context?.Dispose();
                player?.Dispose();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void ContextSubscription_SynchronousPlayerDisposeDoesNotThrowOrLeakOwnedResources()
        {
            int assetsBefore = Resources.FindObjectsOfTypeAll<InputActionAsset>().Length;
            int usersBefore = InputUser.all.Count;
            Keyboard keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            InputPlayer player = null;
            InputContext context = null;
            BehaviorSubject<Unit> trigger = null;
            try
            {
                InputUser user = InputUser.PerformPairingWithDevice(keyboard);
                player = new InputPlayer(0, user, CreateValidConfiguration().PlayerSlots[0], keyboard);
                context = new InputContext("Player", "Gameplay");
                trigger = new BehaviorSubject<Unit>(Unit.Default);
                context.AddBinding(trigger, new ActionCommand(player.Dispose));

                Assert.DoesNotThrow(() => player.PushContext(context));

                Assert.That(player.IsDisposed, Is.True);
                Assert.That(user.valid, Is.False);
                Assert.That(InputUser.all.Count, Is.EqualTo(usersBefore));
                Assert.That(Resources.FindObjectsOfTypeAll<InputActionAsset>().Length, Is.EqualTo(assetsBefore));
            }
            finally
            {
                trigger?.Dispose();
                context?.Dispose();
                player?.Dispose();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void Dispose_ReleasesInputUserAndActionAssetWhenContextSubscriptionThrows()
        {
            int assetsBefore = Resources.FindObjectsOfTypeAll<InputActionAsset>().Length;
            Keyboard keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            InputPlayer player = null;
            InputContext context = null;
            InputUser user = default;
            try
            {
                PlayerSlotConfig config = CreateValidConfiguration().PlayerSlots[0];
                user = InputUser.PerformPairingWithDevice(keyboard);
                player = new InputPlayer(0, user, config, keyboard);
                context = new InputContext("Player", "Gameplay");
                Observable<Unit> source = Observable.Create<Unit>(_ => new ThrowingDisposable());
                context.AddBinding(source, NullCommand.Instance);
                player.PushContext(context);

                Assert.DoesNotThrow(player.Dispose);
                player = null;

                Assert.That(user.valid, Is.False);
                Assert.That(
                    Resources.FindObjectsOfTypeAll<InputActionAsset>().Length,
                    Is.EqualTo(assetsBefore));
            }
            finally
            {
                context?.Dispose();
                player?.Dispose();
                if (user.valid) user.UnpairDevicesAndRemoveUser();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void RebindAction_RejectsOversizedAndForbiddenPathsBeforeApplyingOverride()
        {
            Keyboard keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            InputPlayer player = null;
            try
            {
                PlayerSlotConfig config = CreateValidConfiguration().PlayerSlots[0];
                InputUser user = InputUser.PerformPairingWithDevice(keyboard);
                player = new InputPlayer(0, user, config, keyboard);

                Assert.That(
                    player.RebindAction("Gameplay", "Player", "Fire", "<Keyboard>/space", new string('x', 1025)),
                    Is.False);
                Assert.That(
                    player.RebindAction("Gameplay", "Player", "Fire", "<Keyboard>/space", "<Keyboard>/a\n"),
                    Is.False);
                Assert.That(
                    player.GetActionBindings("Gameplay", "Player", "Fire")[0],
                    Is.EqualTo("<Keyboard>/space"));
            }
            finally
            {
                player?.Dispose();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void DeviceFallback_TreatsMixedLayoutsAsAlternatives()
        {
            Keyboard keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            Gamepad gamepad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
            using var manager = new InputManager();
            try
            {
                InputConfiguration source = CreateValidConfiguration();
                source.PlayerSlots[0].Contexts[0].Bindings[0].DeviceBindings.Add("<Gamepad>/buttonSouth");
                InputConfigurationValidationResult validation = InputConfigurationValidator.ValidateAndPrepare(source);
                Assert.That(validation.IsValid, Is.True);

                bool selected = manager.TrySelectDevices(
                    validation.RuntimeConfiguration.PlayerSlots[0],
                    gamepad,
                    out List<InputDevice> devices,
                    out string schemeName);

                Assert.That(selected, Is.True);
                Assert.That(schemeName, Is.Null);
                Assert.That(devices, Has.Count.EqualTo(1));
                Assert.That(devices[0], Is.SameAs(gamepad));
            }
            finally
            {
                UnityEngine.InputSystem.InputSystem.RemoveDevice(gamepad);
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void ReinitializeWhileListening_RebuildsJoinBindings()
        {
            using var manager = new InputManager();
            InputConfiguration first = CreateValidConfiguration();
            first.JoinAction = CreateButton("Join", "<Keyboard>/enter");
            first.PlayerSlots[0].JoinAction = CreateButton("Join", "<Keyboard>/enter");
            InputConfiguration second = CreateValidConfiguration();
            second.JoinAction = CreateButton("Join", "<Gamepad>/start");
            second.PlayerSlots[0].JoinAction = CreateButton("Join", "<Gamepad>/start");

            Assert.That(manager.InitializeWithResult(Serialize(first)).IsSuccess, Is.True);
            manager.StartListeningForPlayers(true);
            Assert.That(manager.ReinitializeWithResult(Serialize(second)).IsSuccess, Is.True);

            FieldInfo field = typeof(InputManager).GetField(
                "_joinAction",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var joinAction = (InputAction)field.GetValue(manager);
            Assert.That(joinAction, Is.Not.Null);
            Assert.That(joinAction.bindings.Select(binding => binding.path), Is.EquivalentTo(new[] { "<Gamepad>/start" }));
        }

        [Test]
        public void PlayerReadyHandler_RemovingPlayerMakesJoinReturnNullAndClearsRegistry()
        {
            int usersBefore = InputUser.all.Count;
            Keyboard keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            InputManager manager = null;
            try
            {
                keyboard.MakeCurrent();
                manager = new InputManager();
                Assert.That(manager.InitializeWithResult(Serialize(CreateValidConfiguration())).IsSuccess, Is.True);
                manager.OnPlayerInputReady += _ => manager.RemovePlayer(0);

                IInputPlayer joined = manager.JoinPlayerOnSharedDevice(0);

                Assert.That(joined, Is.Null);
                Assert.That(manager.ActivePlayerCount, Is.Zero);
                Assert.That(manager.GetInputPlayer(0), Is.Null);
                Assert.That(InputUser.all.Count, Is.EqualTo(usersBefore));
            }
            finally
            {
                manager?.Dispose();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PairingCallback_ReinitializeObservesJoinLeaseForSharedAndLockedJoin(bool sharedDevice)
        {
            Keyboard keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            InputManager manager = null;
            InputManagerInitializationResult callbackResult = null;
            bool callbackEntered = false;
            Action<InputUser, InputUserChange, InputDevice> onUserChange = null;
            try
            {
                keyboard.MakeCurrent();
                manager = new InputManager();
                string yaml = Serialize(CreateValidConfiguration());
                Assert.That(manager.InitializeWithResult(yaml).IsSuccess, Is.True);
                onUserChange = (_, change, device) =>
                {
                    if (callbackEntered || change != InputUserChange.DevicePaired ||
                        !ReferenceEquals(device, keyboard)) return;
                    callbackEntered = true;
                    callbackResult = manager.ReinitializeWithResult(yaml);
                };
                InputUser.onChange += onUserChange;

                IInputPlayer joined = sharedDevice
                    ? manager.JoinPlayerOnSharedDevice(0)
                    : manager.JoinPlayerAndLockDevice(0, keyboard);

                Assert.That(callbackEntered, Is.True);
                Assert.That(callbackResult, Is.Not.Null);
                Assert.That(callbackResult.Status, Is.EqualTo(InputManagerInitializationStatus.JoinInProgress));
                Assert.That(joined, Is.Not.Null);
                Assert.That(manager.ActivePlayerCount, Is.EqualTo(1));
            }
            finally
            {
                if (onUserChange != null) InputUser.onChange -= onUserChange;
                manager?.Dispose();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [Test]
        public async Task BatchJoin_RejectsOversizedAndDuplicateIdsBeforeJoiningAnyPrefix()
        {
            Keyboard keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            InputManager manager = null;
            try
            {
                var limits = new InputConfigurationLimits(maxPlayers: 2);
                manager = new InputManager(limits);
                Assert.That(manager.InitializeWithResult(Serialize(CreateValidConfiguration())).IsSuccess, Is.True);

                List<IInputPlayer> oversized = manager.JoinPlayersBatch(new List<int> { 0, 1, 2 });
                List<IInputPlayer> duplicate = manager.JoinPlayersBatch(new List<int> { 0, 0 });
                List<IInputPlayer> oversizedAsync =
                    await manager.JoinPlayersBatchAsync(new List<int> { 0, 1, 2 }, 1);
                List<IInputPlayer> duplicateAsync =
                    await manager.JoinPlayersBatchAsync(new List<int> { 0, 0 }, 1);

                Assert.That(oversized, Is.Empty);
                Assert.That(duplicate, Is.Empty);
                Assert.That(oversizedAsync, Is.Empty);
                Assert.That(duplicateAsync, Is.Empty);
                Assert.That(manager.ActivePlayerCount, Is.Zero);
            }
            finally
            {
                manager?.Dispose();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void BatchJoin_ExternalCancellationRollsBackPlayersCreatedByTheBatch()
        {
            int usersBefore = InputUser.all.Count;
            Keyboard keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            InputManager manager = null;
            var cancellation = new CancellationTokenSource();
            try
            {
                keyboard.MakeCurrent();
                InputConfiguration configuration = CreateValidConfiguration();
                PlayerSlotConfig second = CreateValidConfiguration().PlayerSlots[0];
                second.PlayerId = 1;
                configuration.PlayerSlots.Add(second);
                manager = new InputManager();
                Assert.That(manager.InitializeWithResult(Serialize(configuration)).IsSuccess, Is.True);
                manager.OnPlayerInputReady += player =>
                {
                    if (player.PlayerId == 0) cancellation.Cancel();
                };

                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await manager.JoinPlayersBatchAsync(
                        new List<int> { 0, 1 },
                        5,
                        cancellation.Token).AsTask());

                Assert.That(manager.ActivePlayerCount, Is.Zero);
                Assert.That(manager.GetInputPlayer(0), Is.Null);
                Assert.That(manager.GetInputPlayer(1), Is.Null);
                Assert.That(InputUser.all.Count, Is.EqualTo(usersBefore));
            }
            finally
            {
                cancellation.Dispose();
                manager?.Dispose();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void JoinSinglePlayerAsync_AlreadyCanceledOnMainThreadThrowsBeforeJoining()
        {
            Keyboard keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            InputManager manager = null;
            var cancellation = new CancellationTokenSource();
            try
            {
                keyboard.MakeCurrent();
                manager = new InputManager();
                Assert.That(manager.InitializeWithResult(Serialize(CreateValidConfiguration())).IsSuccess, Is.True);
                cancellation.Cancel();

                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await manager.JoinSinglePlayerAsync(0, 5, cancellation.Token).AsTask());
                Assert.That(manager.ActivePlayerCount, Is.Zero);
            }
            finally
            {
                cancellation.Dispose();
                manager?.Dispose();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void StopListening_DisposeCallbackCanRestartWithoutCorruptingStaticCount()
        {
            InputManager.ResetGlobalStateForDomainReload();
            InputManager manager = null;
            Action<object, InputActionChange> onActionChange = null;
            try
            {
                manager = new InputManager();
                InputConfiguration configuration = CreateValidConfiguration();
                configuration.JoinAction = CreateButton("Join", "<Keyboard>/enter");
                Assert.That(manager.InitializeWithResult(Serialize(configuration)).IsSuccess, Is.True);
                manager.StartListeningForPlayers(true);
                InputAction firstListener = GetJoinAction(manager);
                bool reentered = false;
                onActionChange = (changed, change) =>
                {
                    if (reentered || change != InputActionChange.ActionDisabled ||
                        !ReferenceEquals(changed, firstListener)) return;
                    reentered = true;
                    manager.StartListeningForPlayers(false);
                };
                UnityEngine.InputSystem.InputSystem.onActionChange += onActionChange;

                manager.StopListeningForPlayers();

                Assert.That(reentered, Is.True);
                Assert.That(GetJoinAction(manager), Is.Not.Null);
                Assert.That(GetJoinAction(manager), Is.Not.SameAs(firstListener));
                Assert.That(InputManager.IsListeningForPlayers, Is.True);
            }
            finally
            {
                if (onActionChange != null)
                    UnityEngine.InputSystem.InputSystem.onActionChange -= onActionChange;
                manager?.Dispose();
                InputManager.ResetGlobalStateForDomainReload();
            }

            Assert.That(InputManager.IsListeningForPlayers, Is.False);
        }

        [Test]
        public void StartListening_ActionEnabledCallbackReinitializeDiscardsStalePreparedListener()
        {
            InputManager.ResetGlobalStateForDomainReload();
            InputManager manager = null;
            Action<object, InputActionChange> onActionChange = null;
            try
            {
                manager = new InputManager();
                InputConfiguration initial = CreateValidConfiguration();
                initial.JoinAction = CreateButton("Join", "<Keyboard>/enter");
                InputConfiguration replacement = CreateValidConfiguration();
                replacement.JoinAction = CreateButton("Join", "<Gamepad>/start");
                Assert.That(manager.InitializeWithResult(Serialize(initial)).IsSuccess, Is.True);
                string replacementYaml = Serialize(replacement);
                bool reentered = false;
                InputManagerInitializationResult reentrantResult = null;
                onActionChange = (changed, change) =>
                {
                    if (reentered || change != InputActionChange.ActionEnabled ||
                        !(changed is InputAction action) ||
                        !string.Equals(action.name, "CombinedJoin", StringComparison.Ordinal)) return;
                    reentered = true;
                    reentrantResult = manager.ReinitializeWithResult(replacementYaml);
                };
                UnityEngine.InputSystem.InputSystem.onActionChange += onActionChange;

                manager.StartListeningForPlayers(true);

                Assert.That(reentered, Is.True);
                Assert.That(reentrantResult, Is.Not.Null);
                Assert.That(reentrantResult.Status, Is.EqualTo(InputManagerInitializationStatus.Success));
                Assert.That(GetJoinAction(manager), Is.Null);
                Assert.That(InputManager.IsListeningForPlayers, Is.False);
            }
            finally
            {
                if (onActionChange != null)
                    UnityEngine.InputSystem.InputSystem.onActionChange -= onActionChange;
                manager?.Dispose();
                InputManager.ResetGlobalStateForDomainReload();
            }
        }

        [TestCase("schemaVersion: &version 1\nplayerSlots: []\n")]
        [TestCase("schemaVersion: *version\nplayerSlots: []\n")]
        public void InitializeWithResult_RejectsYamlAnchorsAndAliasesBeforeParsing(string yaml)
        {
            using var manager = new InputManager();

            InputManagerInitializationResult result = manager.InitializeWithResult(yaml);

            Assert.That(result.Status, Is.EqualTo(InputManagerInitializationStatus.ValidationFailed));
            Assert.That(result.Message, Does.Contain("anchors, aliases"));
            Assert.That(manager.IsInitialized, Is.False);
        }

        [Test]
        public void InitializeWithResult_RejectsStructuralTokenBudgetBeforeParsing()
        {
            const int LineCount = 13_108;
            var yaml = new StringBuilder(LineCount * 17);
            for (int i = 0; i < LineCount; i++) yaml.Append("a: b: c: d: e:\n");
            using var manager = new InputManager();

            InputManagerInitializationResult result = manager.InitializeWithResult(yaml.ToString());

            Assert.That(result.Status, Is.EqualTo(InputManagerInitializationStatus.ValidationFailed));
            Assert.That(result.Message, Does.Contain("structural token limit"));
            Assert.That(manager.IsInitialized, Is.False);
        }

        [TestCase(null, "empty")]
        [TestCase("schemaVersion: |\n  1\n", "block scalars")]
        [TestCase("---\nschemaVersion: 1\n", "document markers")]
        [TestCase("%YAML 1.2\nschemaVersion: 1\n", "directives")]
        [TestCase("schemaVersion: \u2028 1\n", "line separator")]
        public void YamlPreflight_RejectsUnsupportedLexicalFeatures(string yaml, string expectedMessage)
        {
            Assert.That(InputConfigurationYamlPreflight.TryValidate(yaml, out string error), Is.False);
            Assert.That(error, Does.Contain(expectedMessage));
        }

        [TestCase('-')]
        [TestCase('?')]
        public void YamlPreflight_RejectsCompactBlockNestingBeyondLimit(char indicator)
        {
            var yaml = new StringBuilder("value:\n  ");
            for (int i = 0; i < 65; i++) yaml.Append(indicator).Append(' ');
            yaml.Append("null\n");

            Assert.That(InputConfigurationYamlPreflight.TryValidate(yaml.ToString(), out string error), Is.False);
            Assert.That(error, Does.Contain("block nesting limit"));
        }

        [Test]
        public void YamlPreflight_ApostropheInsidePlainScalarDoesNotDisableLaterScanning()
        {
            var yaml = new StringBuilder("name: player's\nvalue:\n  ");
            for (int i = 0; i < 65; i++) yaml.Append("- ");
            yaml.Append("null\n");

            Assert.That(InputConfigurationYamlPreflight.TryValidate(yaml.ToString(), out string error), Is.False);
            Assert.That(error, Does.Contain("block nesting limit"));
        }

        [Test]
        public void YamlPreflight_AllowsValidSupplementaryUnicodeInQuotedScalar()
        {
            Assert.That(
                InputConfigurationYamlPreflight.TryValidate("schemaFingerprint: '\U0001F3AE'\n", out string error),
                Is.True,
                error);
        }

        [TestCase(
            "schemaVersion: 1\nschemaVersion: 0\nplayerSlots: []\n",
            "schemaVersion",
            "$")]
        [TestCase(
            "schemaVersion: 1\nplayerSlots:\n - playerId: 0\n   playerId: 1\n",
            "playerId",
            "$.playerSlots[]")]
        [TestCase(
            "schemaVersion: 1\njoinAction:\n  deviceBindings: []\n  deviceBindings: []\nplayerSlots: []\n",
            "deviceBindings",
            "$.joinAction")]
        public void YamlPreflight_RejectsDuplicateKeysWithinMappingScope(
            string yaml,
            string key,
            string path)
        {
            Assert.That(InputConfigurationYamlPreflight.TryValidate(yaml, out string error), Is.False);
            Assert.That(error, Does.Contain("duplicate key"));
            Assert.That(error, Does.Contain(key));
            Assert.That(error, Does.Contain(path));
        }

        [TestCase("schemaVerzion: 1\n", "schemaVerzion", "$")]
        [TestCase(
            "schemaVersion: 1\nplayerSlots:\n  - playerIdx: 0\n",
            "playerIdx",
            "$.playerSlots[]")]
        [TestCase(
            "schemaVersion: 1\nplayerSlots:\n  - contexts:\n      - actionMapp: Gameplay\n",
            "actionMapp",
            "$.playerSlots[].contexts[]")]
        [TestCase(
            "schemaVersion: 1\njoinAction:\n  deviceBinding: []\n",
            "deviceBinding",
            "$.joinAction")]
        [TestCase(
            "schemaVersion: 1\nplayerSlots:\n  - controlSchemes:\n      - bindingGroups: KeyboardMouse\n",
            "bindingGroups",
            "$.playerSlots[].controlSchemes[]")]
        [TestCase(
            "schemaVersion: 1\nplayerSlots:\n  - controlSchemes:\n      - deviceRequirements:\n          - controlPaths: '<Gamepad>'\n",
            "controlPaths",
            "$.playerSlots[].controlSchemes[].deviceRequirements[]")]
        [TestCase(
            "schemaVersion: 1\njoinAction:\n  compositeBindings:\n    - parameterz: mode=2\n",
            "parameterz",
            "$.joinAction.compositeBindings[]")]
        [TestCase(
            "schemaVersion: 1\njoinAction:\n  compositeBindings:\n    - parts:\n        - paths: '<Keyboard>/w'\n",
            "paths",
            "$.joinAction.compositeBindings[].parts[]")]
        public void YamlPreflight_RejectsUnknownKeysAtEverySchemaMappingType(
            string yaml,
            string key,
            string path)
        {
            Assert.That(InputConfigurationYamlPreflight.TryValidate(yaml, out string error), Is.False);
            Assert.That(error, Does.Contain("unknown key"));
            Assert.That(error, Does.Contain(key));
            Assert.That(error, Does.Contain(path));
        }

        [Test]
        public void YamlPreflight_AllowsSameKeysAcrossScopesAndSequenceItems()
        {
            const string Yaml =
                "schemaVersion: 1\n" +
                "joinAction:\n" +
                "  action: Join\n" +
                "  deviceBindings: []\n" +
                "playerSlots:\n" +
                "  - playerId: 0\n" +
                "    joinAction:\n" +
                "      action: Join\n" +
                "      deviceBindings: []\n" +
                "    contexts: []\n" +
                "  - playerId: 1\n" +
                "    contexts: []\n";

            Assert.That(InputConfigurationYamlPreflight.TryValidate(Yaml, out string error), Is.True, error);
        }

        [TestCase("\"schemaVersion\": 1\n", "quoted mapping keys")]
        [TestCase("\"schema\\u0056ersion\": 1\n", "quoted mapping keys")]
        [TestCase("schemaVersion: 1\n'schemaVersion': 0\n", "quoted mapping keys")]
        [TestCase("? schemaVersion\n: 1\n", "explicit mapping keys")]
        [TestCase("{ schemaVersion: 1 }\n", "flow-style")]
        [TestCase("schemaVersion: 1\njoinAction: {}\n", "flow mappings")]
        [TestCase("schemaVersion: 1\nplayerSlots: [{ playerId: 0 }]\n", "non-empty flow sequences")]
        public void YamlPreflight_RejectsAmbiguousMappingSyntax(string yaml, string expectedMessage)
        {
            Assert.That(InputConfigurationYamlPreflight.TryValidate(yaml, out string error), Is.False);
            Assert.That(error, Does.Contain(expectedMessage));
        }

        [Test]
        public void YamlPreflight_DoesNotTreatCommentsOrQuotedValueColonsAsKeys()
        {
            const string Yaml =
                "schemaVersion: 1 # duplicate-looking: schemaVersion\n" +
                "schemaFingerprint: \"key: value\"\n" +
                "joinAction:\n" +
                "  deviceBindings:\n" +
                "    - \"urn: button\"\n" +
                "playerSlots: []\n";

            Assert.That(InputConfigurationYamlPreflight.TryValidate(Yaml, out string error), Is.True, error);
        }

        [Test]
        public void InitializeWithResult_RejectsUnknownInputSystemRegistrationBeforeCommit()
        {
            using var manager = new InputManager();
            InputConfiguration invalid = CreateValidConfiguration();
            invalid.PlayerSlots[0].Contexts[0].Bindings[0].Processors = "DefinitelyMissingProcessor";

            InputManagerInitializationResult result = manager.InitializeWithResult(Serialize(invalid));

            Assert.That(result.Status, Is.EqualTo(InputManagerInitializationStatus.InputSystemPreflightFailed));
            Assert.That(result.Preflight, Is.Not.Null);
            Assert.That(result.Preflight.Issues[0].Code, Is.EqualTo(InputConfigurationPreflightIssueCode.UnknownProcessor));
            Assert.That(manager.IsInitialized, Is.False);
        }

        [Test]
        public async Task Loader_FallsBackToDefaultAfterUserInputSystemPreflightFailure()
        {
            InputConfiguration invalidUser = CreateValidConfiguration();
            invalidUser.PlayerSlots[0].Contexts[0].Bindings[0].Processors =
                "DefinitelyMissingProcessor";
            string invalidUserYaml = Serialize(invalidUser);
            var userStore = new MemoryConfigurationStore(invalidUserYaml);
            var defaultSource = new MemoryConfigurationSource(Serialize(CreateValidConfiguration()));
            using var manager = new InputManager();

            InputSystemLoadResult result = await InputSystemLoader.LoadAndInitializeAsync(
                defaultSource,
                "default",
                userStore,
                "user",
                manager);

            Assert.That(result.Status, Is.EqualTo(InputSystemLoadStatus.SuccessFromDefaultConfiguration));
            Assert.That(manager.IsInitialized, Is.True);
            Assert.That(userStore.Content, Is.EqualTo(invalidUserYaml));
            Assert.That(userStore.SaveCount, Is.Zero);
        }

        [Test]
        public async Task Bootstrap_DisabledPerformsNoReadsAndLeavesManagerUninitialized()
        {
            var source = new MemoryConfigurationSource(Serialize(CreateValidConfiguration()));
            var options = new InputSystemBootstrapOptions(
                InputSystemBootstrapMode.Disabled,
                source,
                "ignored");
            using var manager = new InputManager();

            InputSystemLoadResult result = await InputSystemLoader.LoadAndInitializeAsync(
                options,
                manager);

            Assert.That(result.Status, Is.EqualTo(InputSystemLoadStatus.NotConfigured));
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.IsBootstrapComplete, Is.True);
            Assert.That(source.LoadCount, Is.Zero);
            Assert.That(manager.IsInitialized, Is.False);
        }

        [Test]
        public async Task Bootstrap_OptionalTreatsAbsentSourcesAsNotConfigured()
        {
            var source = new MissingConfigurationSource();
            var userStore = new MissingConfigurationStore();
            var options = new InputSystemBootstrapOptions(
                InputSystemBootstrapMode.Optional,
                source,
                "default",
                userStore,
                "user");
            using var manager = new InputManager();

            InputSystemLoadResult result = await InputSystemLoader.LoadAndInitializeAsync(
                options,
                manager);

            Assert.That(result.Status, Is.EqualTo(InputSystemLoadStatus.NotConfigured));
            Assert.That(result.IsBootstrapComplete, Is.True);
            Assert.That(source.LoadCount, Is.EqualTo(1));
            Assert.That(userStore.SaveCount, Is.Zero);
            Assert.That(manager.IsInitialized, Is.False);
        }

        [Test]
        public async Task Bootstrap_RequiredReportsAbsentConfiguration()
        {
            var source = new MissingConfigurationSource();
            var options = new InputSystemBootstrapOptions(
                InputSystemBootstrapMode.Required,
                source,
                "default");
            using var manager = new InputManager();

            InputSystemLoadResult result = await InputSystemLoader.LoadAndInitializeAsync(
                options,
                manager);

            Assert.That(
                result.Status,
                Is.EqualTo(InputSystemLoadStatus.DefaultConfigurationUnavailable));
            Assert.That(result.IsBootstrapComplete, Is.False);
            Assert.That(manager.IsInitialized, Is.False);
        }

        [Test]
        public async Task Bootstrap_OptionalDoesNotHideInvalidUserContent()
        {
            var source = new MissingConfigurationSource();
            var userStore = new MemoryConfigurationStore("schemaVersion: 1\nunknown: true\n");
            var options = new InputSystemBootstrapOptions(
                InputSystemBootstrapMode.Optional,
                source,
                "default",
                userStore,
                "user");
            using var manager = new InputManager();

            InputSystemLoadResult result = await InputSystemLoader.LoadAndInitializeAsync(
                options,
                manager);

            Assert.That(
                result.Status,
                Is.EqualTo(InputSystemLoadStatus.DefaultConfigurationUnavailable));
            Assert.That(result.IsBootstrapComplete, Is.False);
            Assert.That(manager.IsInitialized, Is.False);
        }

        [Test]
        public async Task Bootstrap_DefaultPersistenceIsOptIn()
        {
            var source = new MemoryConfigurationSource(Serialize(CreateValidConfiguration()));
            var userStore = new MissingConfigurationStore();
            var options = new InputSystemBootstrapOptions(
                InputSystemBootstrapMode.Required,
                source,
                "default",
                userStore,
                "user",
                persistDefaultToUser: false);
            using var manager = new InputManager();

            InputSystemLoadResult result = await InputSystemLoader.LoadAndInitializeAsync(
                options,
                manager);

            Assert.That(
                result.Status,
                Is.EqualTo(InputSystemLoadStatus.SuccessFromDefaultConfiguration));
            Assert.That(userStore.SaveCount, Is.Zero);
            Assert.That(manager.IsInitialized, Is.True);
        }

        [Test]
        public async Task Bootstrap_OptionalCanInitializeFromUserOnlySource()
        {
            var userStore = new MemoryConfigurationStore(Serialize(CreateValidConfiguration()));
            var options = new InputSystemBootstrapOptions(
                InputSystemBootstrapMode.Optional,
                userStore: userStore,
                userKey: "user");
            using var manager = new InputManager();

            InputSystemLoadResult result = await InputSystemLoader.LoadAndInitializeAsync(
                options,
                manager);

            Assert.That(
                result.Status,
                Is.EqualTo(InputSystemLoadStatus.SuccessFromUserConfiguration));
            Assert.That(manager.IsInitialized, Is.True);
        }

        [Test]
        public void BootstrapOptions_RejectAmbiguousSourceAndKeyPairs()
        {
            Assert.Throws<ArgumentException>(() =>
                new InputSystemBootstrapOptions(InputSystemBootstrapMode.Optional));
            Assert.Throws<ArgumentException>(() =>
                new InputSystemBootstrapOptions(
                    InputSystemBootstrapMode.Optional,
                    new MissingConfigurationSource(),
                    null));
            Assert.Throws<ArgumentException>(() =>
                new InputSystemBootstrapOptions(
                    InputSystemBootstrapMode.Required,
                    userKey: "user"));
        }

        [Test]
        public async Task Loader_CompatibilityCompositionPreservesLegacyManagerUserUri()
        {
            const string ManagerUserUri = "compatibility-user-config-uri";
            var userStore = new MemoryConfigurationStore(Serialize(CreateValidConfiguration()));
            var defaultSource = new MemoryConfigurationSource(Serialize(CreateValidConfiguration()));
            using var manager = new InputManager();

            InputSystemLoadResult result =
                await InputSystemLoader.LoadAndInitializeCompatibilityAsync(
                    defaultSource,
                    "default",
                    userStore,
                    "user",
                    manager,
                    ManagerUserUri);

            FieldInfo field = typeof(InputManager).GetField(
                "_userConfigUri",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(field, Is.Not.Null);
            Assert.That(field.GetValue(manager), Is.EqualTo(ManagerUserUri));
        }

        [Test]
        public async Task Loader_PersistsPreparedSchemaOneWhenCreatingUserConfigFromLegacyDefault()
        {
            const string InlineBinding =
                "2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)";
            InputConfiguration legacyDefault = CreateLegacyVectorConfiguration(InlineBinding);
            var defaultSource = new MemoryConfigurationSource(Serialize(legacyDefault));
            var userStore = new MissingConfigurationStore();
            using var manager = new InputManager();

            InputSystemLoadResult result = await InputSystemLoader.LoadAndInitializeAsync(
                defaultSource,
                "default",
                userStore,
                "user",
                manager);

            Assert.That(result.Status, Is.EqualTo(InputSystemLoadStatus.SuccessFromDefaultConfiguration));
            Assert.That(userStore.SaveCount, Is.EqualTo(1));
            Assert.That(userStore.Content, Is.Not.Null.And.Not.Empty);
            Assert.That(userStore.Content, Does.Not.Contain("2DVector("));
            InputConfiguration persisted =
                YamlSerializer.Deserialize<InputConfiguration>(Encoding.UTF8.GetBytes(userStore.Content));
            Assert.That(persisted.SchemaVersion, Is.EqualTo(InputConfiguration.CurrentSchemaVersion));
            Assert.That(
                persisted.PlayerSlots[0].Contexts[0].Bindings[0].CompositeBindings,
                Has.Count.EqualTo(1));
        }

        [Test]
        public async Task Loader_CancellationAfterRuntimeCommitDoesNotReportCanceledState()
        {
            var cancellation = new CancellationTokenSource();
            var userStore = new CancelOnSaveMissingConfigurationStore(cancellation);
            var defaultSource = new MemoryConfigurationSource(Serialize(CreateValidConfiguration()));
            using var manager = new InputManager();

            InputSystemLoadResult result = await InputSystemLoader.LoadAndInitializeAsync(
                defaultSource,
                "default",
                userStore,
                "user",
                manager,
                cancellationToken: cancellation.Token);

            Assert.That(result.Status, Is.EqualTo(InputSystemLoadStatus.SuccessFromDefaultConfiguration));
            Assert.That(manager.IsInitialized, Is.True);
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(result.PersistenceStatus, Is.EqualTo(InputSystemPersistenceStatus.Canceled));
            Assert.That(result.IsPersistenceComplete, Is.False);
            Assert.That(userStore.SaveTokenCanBeCanceled, Is.True);
            Assert.That(userStore.SaveCount, Is.Zero);
        }

        [Test]
        public void InitializeWithResult_RefreshesLastResultWhenAlreadyInitialized()
        {
            using var manager = new InputManager();
            Assert.That(manager.InitializeWithResult(Serialize(CreateValidConfiguration())).IsSuccess, Is.True);

            InputConfiguration invalid = CreateValidConfiguration();
            invalid.PlayerSlots[0].Contexts[0].Bindings[0].Processors = "DefinitelyMissingProcessor";
            Assert.That(
                manager.ReinitializeWithResult(Serialize(invalid)).Status,
                Is.EqualTo(InputManagerInitializationStatus.InputSystemPreflightFailed));

            InputManagerInitializationResult result =
                manager.InitializeWithResult(Serialize(CreateValidConfiguration()));

            Assert.That(result.Status, Is.EqualTo(InputManagerInitializationStatus.Success));
            Assert.That(manager.LastInitializationResult, Is.SameAs(result));
        }

        [Test]
        public void InputSystemPreflight_ReleasesTemporaryActionAssets()
        {
            int before = Resources.FindObjectsOfTypeAll<InputActionAsset>().Length;
            for (int iteration = 0; iteration < 10; iteration++)
            {
                InputConfigurationPreflightResult result =
                    InputSystemConfigurationPreflight.Validate(CreateValidConfiguration());
                Assert.That(result.IsSuccess, Is.True, result.Issues.FirstOrDefault()?.ToString());
            }

            int after = Resources.FindObjectsOfTypeAll<InputActionAsset>().Length;
            Assert.That(after, Is.EqualTo(before));
        }

        [Test]
        public void ReinitializeWithInvalidInputSystemGraph_PreservesCurrentListener()
        {
            using var manager = new InputManager();
            InputConfiguration current = CreateValidConfiguration();
            current.JoinAction = CreateButton("Join", "<Keyboard>/enter");
            current.PlayerSlots[0].JoinAction = CreateButton("Join", "<Keyboard>/enter");
            Assert.That(manager.InitializeWithResult(Serialize(current)).IsSuccess, Is.True);
            manager.StartListeningForPlayers(true);

            InputConfiguration invalid = CreateValidConfiguration();
            invalid.JoinAction = CreateButton("Join", "<Gamepad>/start");
            invalid.PlayerSlots[0].Contexts[0].Bindings[0].Interactions = "MissingInteraction";
            InputManagerInitializationResult result = manager.ReinitializeWithResult(Serialize(invalid));

            Assert.That(result.Status, Is.EqualTo(InputManagerInitializationStatus.InputSystemPreflightFailed));
            FieldInfo field = typeof(InputManager).GetField(
                "_joinAction",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var joinAction = (InputAction)field.GetValue(manager);
            Assert.That(joinAction.bindings.Select(binding => binding.path), Is.EquivalentTo(new[] { "<Keyboard>/enter" }));
        }

        [Test]
        public async Task ReinitializeWithResult_RejectsWhileAsyncJoinIsWaiting()
        {
            Keyboard addedKeyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            InputUser blockingUser = default;
            var cancellation = new CancellationTokenSource();
            using var manager = new InputManager();
            try
            {
                var devices = UnityEngine.InputSystem.InputSystem.devices;
                for (int i = 0; i < devices.Count; i++)
                {
                    if (!(devices[i] is Keyboard keyboard)) continue;
                    blockingUser = blockingUser.valid
                        ? InputUser.PerformPairingWithDevice(keyboard, blockingUser)
                        : InputUser.PerformPairingWithDevice(keyboard);
                }

                Assert.That(manager.InitializeWithResult(Serialize(CreateValidConfiguration())).IsSuccess, Is.True);
                UniTask<IInputPlayer> pendingJoin =
                    manager.JoinSinglePlayerAsync(0, 30, cancellation.Token);

                InputManagerInitializationResult result =
                    manager.ReinitializeWithResult(Serialize(CreateValidConfiguration()));

                Assert.That(result.Status, Is.EqualTo(InputManagerInitializationStatus.JoinInProgress));
                cancellation.Cancel();
                try
                {
                    await pendingJoin;
                    Assert.Fail("The pending join did not propagate caller cancellation.");
                }
                catch (OperationCanceledException)
                {
                    // Expected. Await directly so the EditMode player loop can advance cancellation.
                }
            }
            finally
            {
                cancellation.Cancel();
                cancellation.Dispose();
                if (blockingUser.valid) blockingUser.UnpairDevicesAndRemoveUser();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(addedKeyboard);
            }
        }

        [TestCase("<Mouse>/delta", true)]
        [TestCase("<Mouse>/delta/x", true)]
        [TestCase("<Mouse>/deltaFoo", false)]
        public void DeltaPathDetection_MatchesPathComponentExactly(string path, bool expected)
        {
            Assert.That(InputPlayer.IsDeltaPath(path), Is.EqualTo(expected));
        }

        [Test]
        public void ResetGlobalStateForDomainReload_ReplacesCompatibilityInstance()
        {
            InputManager before = InputManager.Instance;
            InputManager.ResetGlobalStateForDomainReload();
            InputManager after = InputManager.Instance;
            Assert.That(after, Is.Not.SameAs(before));
            Assert.That(after.ActivePlayerCount, Is.Zero);
            InputManager.ResetGlobalStateForDomainReload();
        }

        [Test]
        public void ResetGlobalStateForDomainReload_DisposesExplicitManagersAndListeners()
        {
            InputManager.ResetGlobalStateForDomainReload();
            var manager = new InputManager();
            InputConfiguration configuration = CreateValidConfiguration();
            configuration.JoinAction = CreateButton("Join", "<Keyboard>/enter");
            Assert.That(manager.InitializeWithResult(Serialize(configuration)).IsSuccess, Is.True);
            manager.StartListeningForPlayers(false);
            Assert.That(InputManager.IsListeningForPlayers, Is.True);

            InputManager.ResetGlobalStateForDomainReload();

            Assert.That(manager.IsDisposed, Is.True);
            Assert.That(InputManager.IsListeningForPlayers, Is.False);
        }

        [Test]
        public void FrameProviderReset_DisposesPendingDisposableWorkItems()
        {
            var provider = (InputSystemFrameProvider)InputSystemFrameProvider.BeforeUpdate;
            provider.Reset();
            var item = new DisposableFrameWorkItem();
            provider.Register(item);

            provider.Reset();

            Assert.That(item.IsDisposed, Is.True);
            Assert.That(provider.GetFrameCount(), Is.Zero);
        }

        [Test]
        public void UpdateRuntimeState_AfterWarmup_StaysInsideManagedAllocationBudget()
        {
            const int IterationCount = 10_000;
            const long AllocationBudgetBytes = 64;
            Gamepad gamepad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
            InputPlayer player = null;
            InputContext context = null;
            IDisposable subscription = null;
            try
            {
                PlayerSlotConfig config = CreateValidConfiguration().PlayerSlots[0];
                config.Contexts[0].Bindings[0] = new ActionBindingConfig
                {
                    Type = ActionValueType.Vector2,
                    ActionName = "Move",
                    ExpectedControlType = "Vector2",
                    DeviceBindings = new List<string> { "<Gamepad>/leftStick" },
                    UpdateMode = InputUpdateMode.Polling
                };

                InputUser user = InputUser.PerformPairingWithDevice(gamepad);
                player = new InputPlayer(0, user, config, gamepad);
                context = new InputContext("Player", "Gameplay");
                player.PushContext(context);
                float sink = 0f;
                subscription = player
                    .GetVector2Observable("Gameplay", "Player", "Move")
                    .Subscribe(value => sink += value.x);

                for (int i = 0; i < 64; i++) player.UpdateRuntimeState(i / 60d);
                GC.Collect();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < IterationCount; i++) player.UpdateRuntimeState(i / 60d);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(sink, Is.Zero);
                Assert.That(
                    allocated,
                    Is.LessThanOrEqualTo(AllocationBudgetBytes),
                    $"Polling allocated {allocated} bytes across {IterationCount} warmed calls.");
            }
            finally
            {
                subscription?.Dispose();
                context?.Dispose();
                player?.Dispose();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(gamepad);
            }
        }

        private static InputConfiguration CreateValidConfiguration()
        {
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
                                Bindings = new List<ActionBindingConfig>
                                {
                                    CreateButton("Fire", "<Keyboard>/space")
                                }
                            }
                        }
                    }
                }
            };
        }

        private static InputConfiguration CreateLegacyVectorConfiguration(string inlineBinding)
        {
            InputConfiguration configuration = CreateValidConfiguration();
            configuration.SchemaVersion = 0;
            configuration.PlayerSlots[0].Contexts[0].Bindings[0] = new ActionBindingConfig
            {
                Type = ActionValueType.Vector2,
                ActionName = "Move",
                ExpectedControlType = "Vector2",
                DeviceBindings = new List<string> { inlineBinding }
            };
            return configuration;
        }

        private static InputActionAsset GetPlayerActionAsset(InputPlayer player)
        {
            FieldInfo field = typeof(InputPlayer).GetField(
                "_inputActionAsset",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (InputActionAsset)field.GetValue(player);
        }

        private static InputAction GetJoinAction(InputManager manager)
        {
            FieldInfo field = typeof(InputManager).GetField(
                "_joinAction",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (InputAction)field.GetValue(manager);
        }

        private static ActionBindingConfig CreateButton(string name, string path)
        {
            return new ActionBindingConfig
            {
                Type = ActionValueType.Button,
                ActionName = name,
                DeviceBindings = new List<string> { path }
            };
        }

        private static string Serialize(InputConfiguration configuration)
        {
            return Encoding.UTF8.GetString(YamlSerializer.Serialize(configuration).ToArray());
        }

        private static InputBindingOverrideProfile CreateProfile(string overridesJson)
        {
            var profile = new InputBindingOverrideProfile();
            profile.Players.Add(new InputBindingOverrideEntry
            {
                PlayerId = 0,
                OverridesJson = overridesJson
            });
            return profile;
        }

        private static string CreateBindingOverrideJson(
            string overridePath = "<Keyboard>/a",
            string overrideInteractions = null,
            string overrideProcessors = null)
        {
            return
                "{\"schemaVersion\":1,\"bindings\":[{" +
                "\"contextName\":\"Gameplay\",\"actionMapName\":\"Player\"," +
                "\"actionName\":\"Fire\",\"bindingIndex\":0," +
                "\"originalPath\":\"<Keyboard>/space\"," +
                "\"overridePath\":" + JsonStringOrNull(overridePath) + "," +
                "\"overrideInteractions\":" + JsonStringOrNull(overrideInteractions) + "," +
                "\"overrideProcessors\":" + JsonStringOrNull(overrideProcessors) + "}]}";
        }

        private static string JsonStringOrNull(string value)
        {
            return value == null
                ? "null"
                : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static ActionBindingConfig CloneAction(ActionBindingConfig source)
        {
            return new ActionBindingConfig
            {
                Type = source.Type,
                ActionName = source.ActionName,
                DeviceBindings = new List<string>(source.DeviceBindings)
            };
        }
    }
}
