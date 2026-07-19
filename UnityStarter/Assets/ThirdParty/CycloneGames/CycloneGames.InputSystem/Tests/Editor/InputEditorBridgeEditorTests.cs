using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CycloneGames.InputSystem.Editor;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.InputSystem.Tests.Editor
{
    public sealed class InputEditorBridgeEditorTests
    {
        private const string LegacyAssetPath =
            "Assets/ThirdParty/CycloneGames/CycloneGames.InputSystem/Tests/Editor/Fixtures/LegacyInputConfiguration.asset";

        [Test]
        public void YamlWorkingCopyRoundTrip_PreservesVersionNullsAndExtendedBindings()
        {
            InputConfiguration source = CreateExtendedConfiguration();
            EditorWindow window = CreateEditorWindow();
            string temporaryPath = Path.Combine(
                Path.GetTempPath(),
                "CycloneGames.InputSystem.EditorTests",
                Guid.NewGuid().ToString("N"),
                "input.yaml");

            try
            {
                byte[] yaml = InvokeStatic<byte[]>(
                    window.GetType(),
                    "SerializeConfiguration",
                    source);
                Directory.CreateDirectory(Path.GetDirectoryName(temporaryPath));
                File.WriteAllBytes(temporaryPath, yaml);

                bool loaded = InvokeInstance<bool>(
                    window,
                    "LoadConfigFromPath",
                    temporaryPath,
                    "Loaded test configuration.");

                Assert.That(loaded, Is.True);
                InputConfiguration roundTripped = ReadWorkingConfiguration(window);

                Assert.That(roundTripped.SchemaVersion, Is.EqualTo(InputConfiguration.CurrentSchemaVersion));
                Assert.That(roundTripped.SchemaFingerprint, Is.EqualTo("editor-round-trip"));
                Assert.That(roundTripped.JoinAction, Is.Null);
                Assert.That(roundTripped.PlayerSlots[0].JoinAction, Is.Null);
                Assert.That(roundTripped.PlayerSlots[0].DefaultControlScheme, Is.Null);

                ControlSchemeConfig scheme = roundTripped.PlayerSlots[0].ControlSchemes[0];
                Assert.That(scheme.Name, Is.EqualTo("KeyboardMouse"));
                Assert.That(scheme.BindingGroup, Is.EqualTo("KeyboardMouse"));
                Assert.That(scheme.DeviceRequirements[1].IsOptional, Is.True);
                Assert.That(scheme.DeviceRequirements[1].IsOr, Is.True);

                ContextDefinitionConfig context = roundTripped.PlayerSlots[0].Contexts[0];
                Assert.That(context.Priority, Is.EqualTo(17));
                Assert.That(context.BlocksLowerPriority, Is.False);

                ActionBindingConfig action = context.Bindings[0];
                Assert.That(action.UpdateMode, Is.EqualTo(InputUpdateMode.Polling));
                Assert.That(action.ExpectedControlType, Is.EqualTo("Vector2"));
                Assert.That(action.Interactions, Is.EqualTo("Hold(duration=0.2)"));
                Assert.That(action.Processors, Is.EqualTo("NormalizeVector2"));
                Assert.That(action.BindingGroups, Is.Null);
                Assert.That(action.DeviceBindings, Is.Null);
                Assert.That(action.LongPressMs, Is.EqualTo(123));
                Assert.That(action.LongPressValueThreshold, Is.EqualTo(0.75f));

                CompositeBindingConfig composite = action.CompositeBindings[0];
                Assert.That(composite.Name, Is.EqualTo("2DVector"));
                Assert.That(composite.Parameters, Is.Null);
                Assert.That(composite.BindingGroups, Is.EqualTo("KeyboardMouse"));
                Assert.That(composite.Parts[0].Processors, Is.Null);
                Assert.That(composite.Parts[0].Interactions, Is.Null);
            }
            finally
            {
                DestroyWindow(window);
                string directory = Path.GetDirectoryName(temporaryPath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void DefaultTemplate_IsRuntimeValidAndAllowsCrossPlayerReuse()
        {
            EditorWindow window = CreateEditorWindow();
            try
            {
                InputConfiguration template = InvokeInstance<InputConfiguration>(
                    window,
                    "CreateDefaultConfigTemplate");
                InputConfigurationValidationResult validation =
                    InputConfigurationValidator.ValidateAndPrepare(template);

                Assert.That(validation.IsValid, Is.True, FirstIssue(validation));
                Assert.That(template.SchemaVersion, Is.EqualTo(InputConfiguration.CurrentSchemaVersion));
                Assert.That(template.JoinAction, Is.Null);
                Assert.That(template.PlayerSlots, Has.Count.EqualTo(2));
                Assert.That(template.PlayerSlots[0].Contexts[0].Name, Is.EqualTo("Gameplay"));
                Assert.That(template.PlayerSlots[1].Contexts[0].Name, Is.EqualTo("Gameplay"));
                Assert.That(template.PlayerSlots[0].Contexts[0].ActionMap, Is.EqualTo("PlayerActions"));
                Assert.That(template.PlayerSlots[1].Contexts[0].ActionMap, Is.EqualTo("PlayerActions"));
                Assert.That(
                    template.PlayerSlots[0].Contexts[0].Bindings[0].UpdateMode,
                    Is.EqualTo(InputUpdateMode.Polling));
                Assert.That(
                    template.PlayerSlots[0].Contexts[0].Bindings[0].CompositeBindings,
                    Has.Count.EqualTo(1));
                Assert.That(template.PlayerSlots[0].ControlSchemes, Has.Count.EqualTo(2));
            }
            finally
            {
                DestroyWindow(window);
            }
        }

        [Test]
        public void DeviceBindingSelector_AllowsCustomControlPaths()
        {
            FieldInfo field = typeof(ActionBindingDrawerData).GetField("DeviceBindings");
            Assert.That(field, Is.Not.Null);

            object selector = null;
            object[] attributes = field.GetCustomAttributes(false);
            for (int index = 0; index < attributes.Length; index++)
            {
                if (attributes[index].GetType().Name == "StringAsConstSelectorAttribute")
                {
                    selector = attributes[index];
                    break;
                }
            }

            Assert.That(selector, Is.Not.Null);
            PropertyInfo allowCustom = selector.GetType().GetProperty("AllowCustom");
            Assert.That(allowCustom, Is.Not.Null);
            Assert.That((bool)allowCustom.GetValue(selector), Is.True);
        }

        [Test]
        public void AppliedSerializedModification_MarksValidationDirty()
        {
            string fixturePath = Path.Combine(
                Application.dataPath,
                "ThirdParty",
                "CycloneGames",
                "CycloneGames.InputSystem",
                "Samples",
                "Fixtures",
                "input_config.yaml");
            EditorWindow window = CreateEditorWindow();
            try
            {
                Assert.That(
                    InvokeInstance<bool>(window, "LoadConfigFromPath", fixturePath, "Loaded fixture."),
                    Is.True);
                FieldInfo configField = window.GetType().GetField(
                    "_configSO",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo dirtyField = window.GetType().GetField(
                    "_validationCacheDirty",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(configField, Is.Not.Null);
                Assert.That(dirtyField, Is.Not.Null);
                dirtyField.SetValue(window, false);

                var modification = new UndoPropertyModification
                {
                    currentValue = new PropertyModification
                    {
                        target = (UnityEngine.Object)configField.GetValue(window),
                        propertyPath = "_playerSlots.Array.data[0].PlayerId",
                        value = "4"
                    }
                };
                InvokeInstance<UndoPropertyModification[]>(
                    window,
                    "HandlePostprocessModifications",
                    (object)new[] { modification });

                Assert.That((bool)dirtyField.GetValue(window), Is.True);
            }
            finally
            {
                DestroyWindow(window);
            }
        }

        [Test]
        public void SampleFixture_LoadsThroughEditorValidation()
        {
            string fixturePath = Path.Combine(
                Application.dataPath,
                "ThirdParty",
                "CycloneGames",
                "CycloneGames.InputSystem",
                "Samples",
                "Fixtures",
                "input_config.yaml");
            Assert.That(File.Exists(fixturePath), Is.True, "The sample fixture must remain discoverable.");

            EditorWindow window = CreateEditorWindow();
            try
            {
                bool loaded = InvokeInstance<bool>(
                    window,
                    "LoadConfigFromPath",
                    fixturePath,
                    "Loaded sample fixture.");

                Assert.That(loaded, Is.True);
                InputConfiguration configuration = ReadWorkingConfiguration(window);
                Assert.That(configuration.PlayerSlots[0].ControlSchemes, Has.Count.EqualTo(2));
                Assert.That(
                    configuration.PlayerSlots[0].Contexts[0].Bindings[0].CompositeBindings,
                    Has.Count.EqualTo(1));

                FieldInfo configField = window.GetType().GetField(
                    "_configSO",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(configField, Is.Not.Null);
                var workingCopy = (UnityEngine.Object)configField.GetValue(window);
                Assert.That(
                    workingCopy.hideFlags & HideFlags.NotEditable,
                    Is.EqualTo(HideFlags.None));
                Assert.That(
                    workingCopy.hideFlags & HideFlags.DontSaveInEditor,
                    Is.Not.EqualTo(HideFlags.None));
            }
            finally
            {
                DestroyWindow(window);
            }
        }

        [Test]
        public void LegacySerializedAsset_MigratesImplicitJoinActions()
        {
            AssetDatabase.ImportAsset(
                LegacyAssetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            InputConfigurationSO asset =
                AssetDatabase.LoadAssetAtPath<InputConfigurationSO>(LegacyAssetPath);

            Assert.That(asset, Is.Not.Null);
            InputConfiguration configuration = asset.ToData();
            Assert.That(configuration.JoinAction, Is.Not.Null);
            Assert.That(configuration.JoinAction.ActionName, Is.EqualTo("JoinGame"));
            Assert.That(configuration.PlayerSlots, Has.Count.EqualTo(1));
            Assert.That(configuration.PlayerSlots[0].JoinAction, Is.Not.Null);
            Assert.That(configuration.PlayerSlots[0].JoinAction.ActionName, Is.EqualTo("JoinGame"));
            Assert.That(configuration.PlayerSlots[0].Contexts[0].Bindings[0].ActionName, Is.EqualTo("Confirm"));
        }

        [Test]
        public void ObsoleteEditorShims_RoundTripCurrentSchemaFieldsWithoutLoss()
        {
#pragma warning disable 0618
            InputConfiguration source = CreateExtendedConfiguration();
            source.PlayerSlots[0].JoinAction = new ActionBindingConfig
            {
                Type = ActionValueType.Button,
                ActionName = "Join",
                DeviceBindings = new List<string> { "<Keyboard>/enter" },
                CompositeBindings = new List<CompositeBindingConfig>(),
                UpdateMode = InputUpdateMode.EventDriven,
                LongPressValueThreshold = 0.5f
            };
            PlayerSlotSO shim = ScriptableObject.CreateInstance<PlayerSlotSO>();
            try
            {
                shim.FromData(source.PlayerSlots[0]);
                PlayerSlotConfig roundTripped = shim.ToData();

                Assert.That(roundTripped.JoinAction.ActionName, Is.EqualTo("Join"));
                Assert.That(roundTripped.DefaultControlScheme, Is.Null);
                Assert.That(roundTripped.ControlSchemes, Has.Count.EqualTo(1));
                Assert.That(roundTripped.ControlSchemes[0].Name, Is.EqualTo("KeyboardMouse"));
                Assert.That(roundTripped.ControlSchemes[0].DeviceRequirements, Has.Count.EqualTo(2));
                Assert.That(roundTripped.Contexts, Has.Count.EqualTo(1));
                Assert.That(roundTripped.Contexts[0].Priority, Is.EqualTo(17));
                Assert.That(roundTripped.Contexts[0].BlocksLowerPriority, Is.False);
                Assert.That(roundTripped.Contexts[0].Bindings[0].CompositeBindings, Has.Count.EqualTo(1));
            }
            finally
            {
                if (shim.Contexts != null)
                {
                    for (int index = 0; index < shim.Contexts.Count; index++)
                    {
                        if (shim.Contexts[index] != null) UnityEngine.Object.DestroyImmediate(shim.Contexts[index]);
                    }
                }
                UnityEngine.Object.DestroyImmediate(shim);
            }
#pragma warning restore 0618
        }

        [Test]
        public void ObsoletePlayerSlotShim_FreshLegacyDefaultsRemainAbsent()
        {
#pragma warning disable 0618
            PlayerSlotSO shim = ScriptableObject.CreateInstance<PlayerSlotSO>();
            try
            {
                PlayerSlotConfig data = shim.ToData();
                Assert.That(data.JoinAction, Is.Null);
                Assert.That(data.DefaultControlScheme, Is.Null);
                Assert.That(data.ControlSchemes, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(shim);
            }
#pragma warning restore 0618
        }

        [Test]
        public void SchemaZeroInlineComposite_LoadsIntoMigratedWorkingCopyAndReloads()
        {
            InputConfiguration source = CreateExtendedConfiguration();
            source.SchemaVersion = 0;
            ActionBindingConfig action = source.PlayerSlots[0].Contexts[0].Bindings[0];
            action.DeviceBindings = new List<string>
            {
                "2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)"
            };
            action.CompositeBindings.Clear();

            EditorWindow window = CreateEditorWindow();
            string directory = Path.Combine(
                Path.GetTempPath(),
                "CycloneGames.InputSystem.EditorTests",
                Guid.NewGuid().ToString("N"));
            string firstPath = Path.Combine(directory, "schema0.yaml");
            string migratedPath = Path.Combine(directory, "schema1.yaml");
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(
                    firstPath,
                    InvokeStatic<byte[]>(window.GetType(), "SerializeConfiguration", source));
                Assert.That(
                    InvokeInstance<bool>(window, "LoadConfigFromPath", firstPath, "Loaded schema zero."),
                    Is.True);

                InputConfiguration migrated = ReadWorkingConfiguration(window);
                Assert.That(migrated.SchemaVersion, Is.EqualTo(InputConfiguration.CurrentSchemaVersion));
                Assert.That(migrated.PlayerSlots[0].Contexts[0].Bindings[0].DeviceBindings, Is.Empty);
                Assert.That(migrated.PlayerSlots[0].Contexts[0].Bindings[0].CompositeBindings, Has.Count.EqualTo(1));

                File.WriteAllBytes(
                    migratedPath,
                    InvokeStatic<byte[]>(window.GetType(), "SerializeConfiguration", migrated));
                Assert.That(
                    InvokeInstance<bool>(window, "LoadConfigFromPath", migratedPath, "Reloaded schema one."),
                    Is.True);
                InputConfiguration reloaded = ReadWorkingConfiguration(window);
                Assert.That(reloaded.SchemaVersion, Is.EqualTo(InputConfiguration.CurrentSchemaVersion));
                Assert.That(reloaded.PlayerSlots[0].Contexts[0].Bindings[0].CompositeBindings, Has.Count.EqualTo(1));
            }
            finally
            {
                DestroyWindow(window);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void EditorLoad_RejectsUtf8AndUtf16ByteOrderMarks(bool utf16)
        {
            EditorWindow window = CreateEditorWindow();
            string directory = Path.Combine(
                Path.GetTempPath(),
                "CycloneGames.InputSystem.EditorTests",
                Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "bom.yaml");
            try
            {
                byte[] canonical = InvokeStatic<byte[]>(
                    window.GetType(),
                    "SerializeConfiguration",
                    CreateExtendedConfiguration());
                string text = Encoding.UTF8.GetString(canonical);
                Encoding encoding = utf16 ? Encoding.Unicode : new UTF8Encoding(true);
                byte[] preamble = encoding.GetPreamble();
                byte[] body = encoding.GetBytes(text);
                var content = new byte[preamble.Length + body.Length];
                Buffer.BlockCopy(preamble, 0, content, 0, preamble.Length);
                Buffer.BlockCopy(body, 0, content, preamble.Length, body.Length);
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(path, content);

                Assert.That(
                    InvokeInstance<bool>(window, "LoadConfigFromPath", path, "Unexpected success."),
                    Is.False);
            }
            finally
            {
                DestroyWindow(window);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static InputConfiguration CreateExtendedConfiguration()
        {
            return new InputConfiguration
            {
                SchemaVersion = InputConfiguration.CurrentSchemaVersion,
                SchemaFingerprint = "editor-round-trip",
                JoinAction = null,
                PlayerSlots = new List<PlayerSlotConfig>
                {
                    new PlayerSlotConfig
                    {
                        PlayerId = 4,
                        JoinAction = null,
                        DefaultControlScheme = null,
                        ControlSchemes = new List<ControlSchemeConfig>
                        {
                            new ControlSchemeConfig
                            {
                                Name = "KeyboardMouse",
                                BindingGroup = "KeyboardMouse",
                                DeviceRequirements = new List<ControlSchemeDeviceRequirementConfig>
                                {
                                    new ControlSchemeDeviceRequirementConfig
                                    {
                                        ControlPath = "<Keyboard>"
                                    },
                                    new ControlSchemeDeviceRequirementConfig
                                    {
                                        ControlPath = "<Mouse>",
                                        IsOptional = true,
                                        IsOr = true
                                    }
                                }
                            }
                        },
                        Contexts = new List<ContextDefinitionConfig>
                        {
                            new ContextDefinitionConfig
                            {
                                Name = "Gameplay",
                                ActionMap = "PlayerActions",
                                Priority = 17,
                                BlocksLowerPriority = false,
                                Bindings = new List<ActionBindingConfig>
                                {
                                    new ActionBindingConfig
                                    {
                                        Type = ActionValueType.Vector2,
                                        ActionName = "Move",
                                        ExpectedControlType = "Vector2",
                                        Interactions = "Hold(duration=0.2)",
                                        Processors = "NormalizeVector2",
                                        BindingGroups = null,
                                        UpdateMode = InputUpdateMode.Polling,
                                        DeviceBindings = null,
                                        CompositeBindings = new List<CompositeBindingConfig>
                                        {
                                            new CompositeBindingConfig
                                            {
                                                Name = "2DVector",
                                                Parameters = null,
                                                BindingGroups = "KeyboardMouse",
                                                Parts = new List<CompositePartBindingConfig>
                                                {
                                                    new CompositePartBindingConfig
                                                    {
                                                        Name = "Up",
                                                        Path = "<Keyboard>/w",
                                                        Processors = null,
                                                        Interactions = null
                                                    }
                                                }
                                            }
                                        },
                                        LongPressMs = 123,
                                        LongPressValueThreshold = 0.75f
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        private static EditorWindow CreateEditorWindow()
        {
            return ScriptableObject.CreateInstance<InputEditorWindow>();
        }

        private static InputConfiguration ReadWorkingConfiguration(EditorWindow window)
        {
            FieldInfo field = window.GetType().GetField(
                "_configSO",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            object workingCopy = field.GetValue(window);
            Assert.That(workingCopy, Is.Not.Null);
            return (InputConfiguration)workingCopy.GetType()
                .GetMethod("ToData", BindingFlags.Instance | BindingFlags.Public)
                .Invoke(workingCopy, null);
        }

        private static T InvokeStatic<T>(Type type, string methodName, params object[] arguments)
        {
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            return (T)method.Invoke(null, arguments);
        }

        private static T InvokeInstance<T>(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            return (T)method.Invoke(target, arguments);
        }

        private static string FirstIssue(InputConfigurationValidationResult validation)
        {
            return validation.Issues.Count == 0 ? string.Empty : validation.Issues[0].ToString();
        }

        private static void DestroyWindow(EditorWindow window)
        {
            if (window != null)
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }
    }
}
