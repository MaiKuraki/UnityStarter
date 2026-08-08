using System;
using System.Linq;
using System.Reflection;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class BuildRecipeAuthoringTests
    {
        private BuildData profile;
        private YooAssetBuildConfig contentConfiguration;
        private HybridCLRBuildConfig hybridClrConfiguration;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<BuildData>();
            contentConfiguration = ScriptableObject.CreateInstance<YooAssetBuildConfig>();
            hybridClrConfiguration = ScriptableObject.CreateInstance<HybridCLRBuildConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            if (profile != null)
            {
                Undo.ClearUndo(profile);
                UnityEngine.Object.DestroyImmediate(profile);
            }

            if (contentConfiguration != null)
            {
                UnityEngine.Object.DestroyImmediate(contentConfiguration);
            }

            if (hybridClrConfiguration != null)
            {
                UnityEngine.Object.DestroyImmediate(hybridClrConfiguration);
            }
        }

        [TestCase(
            (int)BuildRecipePreset.PlayerOnly,
            BuildStepTypeIds.Player)]
        [TestCase(
            (int)BuildRecipePreset.PlayerWithDependencies,
            BuildStepTypeIds.HotUpdate,
            BuildStepTypeIds.AssetContent,
            BuildStepTypeIds.Player)]
        [TestCase(
            (int)BuildRecipePreset.PlayerWithContent,
            BuildStepTypeIds.AssetContent,
            BuildStepTypeIds.Player)]
        [TestCase(
            (int)BuildRecipePreset.ContentOnly,
            BuildStepTypeIds.AssetContent)]
        [TestCase(
            (int)BuildRecipePreset.ContentWithHotUpdate,
            BuildStepTypeIds.HotUpdate,
            BuildStepTypeIds.AssetContent)]
        [TestCase(
            (int)BuildRecipePreset.HotUpdateOnly,
            BuildStepTypeIds.HotUpdate)]
        public void GetInvocationIds_ReturnsCanonicalSequence(
            int presetValue,
            params string[] expected)
        {
            var preset = (BuildRecipePreset)presetValue;
            string[] first = BuildRecipePresetCatalog.GetInvocationIds(preset);
            first[0] = "mutated-by-test";

            CollectionAssert.AreEqual(expected, BuildRecipePresetCatalog.GetInvocationIds(preset));
        }

        [Test]
        public void Analyze_IdentifiesCanonicalGraphRegardlessOfSerializedOrder()
        {
            BuildRecipeInvocation[] canonical = CreateAuthoredInvocations(
                BuildRecipePreset.ContentWithHotUpdate);
            BuildRecipeAnalysis identified = BuildRecipePresetCatalog.Analyze(
                canonical.Reverse().ToArray());
            Assert.That(
                identified.MatchedPreset,
                Is.EqualTo(BuildRecipePreset.ContentWithHotUpdate));
            CollectionAssert.AreEqual(
                new[] { BuildStepTypeIds.HotUpdate, BuildStepTypeIds.AssetContent },
                identified.ExecutionOrderInvocationIds);

            BuildRecipeInvocation[] missingDependency =
                CreateAuthoredInvocations(BuildRecipePreset.ContentWithHotUpdate);
            missingDependency[1] = new BuildRecipeInvocation(
                BuildStepTypeIds.AssetContent,
                BuildStepTypeIds.AssetContent);
            Assert.That(
                BuildRecipePresetCatalog.Analyze(
                    missingDependency)
                    .MatchedPreset,
                Is.Null);
        }

        [Test]
        public void Analyze_ReportsSelectedStepsAsEffectiveOutputs()
        {
            BuildRecipeAnalysis analysis = BuildRecipePresetCatalog.Analyze(
                CreateAuthoredInvocations(BuildRecipePreset.ContentWithHotUpdate));

            Assert.That(analysis.MatchedPreset, Is.EqualTo(BuildRecipePreset.ContentWithHotUpdate));
            Assert.That(analysis.ProducesPlayer, Is.False);
            Assert.That(analysis.ProducesAssetContent, Is.True);
            Assert.That(analysis.ProducesHotUpdate, Is.True);
            Assert.That(analysis.IsReady, Is.True);
        }

        [Test]
        public void Analyze_EmptyRecipe_IsBlocked()
        {
            BuildRecipeAnalysis analysis = BuildRecipePresetCatalog.Analyze(
                Array.Empty<BuildRecipeInvocation>());

            Assert.That(analysis.IsReady, Is.False);
            Assert.That(analysis.BlockingIssues, Has.Some.Contains("at least one build invocation"));
        }

        [Test]
        public void Apply_FullPlayer_PreservesTypedConfigurationsAndDisablesCustomEntry()
        {
            SetRecipe(
                new BuildRecipeInvocation("custom-step", "custom-step", enabled: true),
                new BuildRecipeInvocation(
                    BuildStepTypeIds.AssetContent,
                    BuildStepTypeIds.AssetContent,
                    enabled: false,
                    configuration: contentConfiguration),
                new BuildRecipeInvocation(
                    BuildStepTypeIds.HotUpdate,
                    BuildStepTypeIds.HotUpdate,
                    enabled: false,
                    configuration: hybridClrConfiguration),
                new BuildRecipeInvocation(
                    BuildStepTypeIds.Player,
                    BuildStepTypeIds.Player,
                    enabled: false));

            Assert.That(
                BuildRecipePresetAuthoring.Apply(
                    profile,
                    BuildRecipePreset.PlayerWithDependencies),
                Is.True);

            CollectionAssert.AreEqual(
                new[]
                {
                    BuildStepTypeIds.HotUpdate,
                    BuildStepTypeIds.AssetContent,
                    BuildStepTypeIds.Player
                },
                profile.EnabledInvocationIds);
            BuildRecipeInvocation[] entries = profile.RecipeInvocations.ToArray();
            Assert.That(entries[0].Configuration, Is.SameAs(hybridClrConfiguration));
            Assert.That(entries[1].Configuration, Is.SameAs(contentConfiguration));
            Assert.That(entries.Single(entry => entry.InvocationId == "custom-step").Enabled, Is.False);
        }

        [Test]
        public void Apply_ContentWithoutTypedConfiguration_ExpressesIntentForLaterCompletion()
        {
            SetRecipe(
                new BuildRecipeInvocation(
                    BuildStepTypeIds.Player,
                    BuildStepTypeIds.Player,
                    enabled: true),
                new BuildRecipeInvocation(
                    BuildStepTypeIds.AssetContent,
                    BuildStepTypeIds.AssetContent,
                    enabled: false));

            Assert.That(
                BuildRecipePresetAuthoring.Apply(
                    profile,
                    BuildRecipePreset.ContentOnly),
                Is.True);
            CollectionAssert.AreEqual(
                new[] { BuildStepTypeIds.AssetContent },
                profile.EnabledInvocationIds);
            Assert.That(
                profile.RecipeInvocations.Single(
                    entry => entry.InvocationId == BuildStepTypeIds.AssetContent)
                    .Configuration,
                Is.Null);
        }

        [Test]
        public void Apply_HotUpdatePreset_SupportsUndoAndRedoWithoutLosingConfig()
        {
            SetRecipe(
                new BuildRecipeInvocation("custom-step", "custom-step", enabled: true),
                new BuildRecipeInvocation(
                    BuildStepTypeIds.HotUpdate,
                    BuildStepTypeIds.HotUpdate,
                    enabled: false,
                    configuration: hybridClrConfiguration));
            Undo.ClearUndo(profile);

            Assert.That(
                BuildRecipePresetAuthoring.Apply(
                    profile,
                    BuildRecipePreset.HotUpdateOnly),
                Is.True);
            CollectionAssert.AreEqual(
                new[] { BuildStepTypeIds.HotUpdate },
                profile.EnabledInvocationIds);

            Undo.PerformUndo();
            CollectionAssert.AreEqual(new[] { "custom-step" }, profile.EnabledInvocationIds);

            Undo.PerformRedo();
            CollectionAssert.AreEqual(
                new[] { BuildStepTypeIds.HotUpdate },
                profile.EnabledInvocationIds);
            Assert.That(
                profile.RecipeInvocations.Single(
                        entry => entry.InvocationId == BuildStepTypeIds.HotUpdate)
                    .Configuration,
                Is.SameAs(hybridClrConfiguration));
        }

        [Test]
        public void Apply_WhenRecipeAlreadyMatches_IsNoOp()
        {
            SetRecipe(
                new BuildRecipeInvocation(
                    BuildStepTypeIds.Player,
                    BuildStepTypeIds.Player,
                    enabled: true),
                new BuildRecipeInvocation(
                    BuildStepTypeIds.HotUpdate,
                    BuildStepTypeIds.HotUpdate,
                    enabled: false),
                new BuildRecipeInvocation(
                    BuildStepTypeIds.AssetContent,
                    BuildStepTypeIds.AssetContent,
                    enabled: false));

            Assert.That(
                BuildRecipePresetAuthoring.Apply(
                    profile,
                    BuildRecipePreset.PlayerOnly),
                Is.False);
        }

        [Test]
        public void FocusedContentSelection_WithMultipleMatchingInvocations_FailsClosed()
        {
            SetRecipe(
                new BuildRecipeInvocation(
                    "content-base",
                    BuildStepTypeIds.AssetContent,
                    enabled: true,
                    configuration: contentConfiguration),
                new BuildRecipeInvocation(
                    "content-dlc",
                    BuildStepTypeIds.AssetContent,
                    enabled: true,
                    configuration: contentConfiguration),
                new BuildRecipeInvocation(
                    BuildStepTypeIds.HotUpdate,
                    BuildStepTypeIds.HotUpdate,
                    enabled: false,
                    configuration: hybridClrConfiguration));

            MethodInfo resolve = typeof(BuildDataEditor).GetMethod(
                "TryResolveFocusedInvocationIds",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(resolve, Is.Not.Null);
            object[] arguments =
            {
                profile.RecipeInvocations,
                BuildRecipePresetCatalog.GetTemplates(BuildRecipePreset.ContentOnly),
                null,
                null
            };
            bool resolved = (bool)resolve.Invoke(null, arguments);

            Assert.That(resolved, Is.False);
            Assert.That((string)arguments[3], Does.Contain("More than one"));
        }

        [Test]
        public void FocusedContentSelection_WhenCanonicalIdHasWrongType_FailsClosed()
        {
            SetRecipe(
                new BuildRecipeInvocation(
                    BuildStepTypeIds.AssetContent,
                    BuildStepTypeIds.HotUpdate,
                    enabled: true,
                    configuration: hybridClrConfiguration),
                new BuildRecipeInvocation(
                    "content-main",
                    BuildStepTypeIds.AssetContent,
                    enabled: true,
                    configuration: contentConfiguration));

            MethodInfo resolve = typeof(BuildDataEditor).GetMethod(
                "TryResolveFocusedInvocationIds",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(resolve, Is.Not.Null);
            object[] arguments =
            {
                profile.RecipeInvocations,
                BuildRecipePresetCatalog.GetTemplates(BuildRecipePreset.ContentOnly),
                null,
                null
            };

            bool resolved = (bool)resolve.Invoke(null, arguments);

            Assert.That(resolved, Is.False);
            Assert.That((string)arguments[3], Does.Contain("instead of"));
        }

        [Test]
        public void FocusedInvocation_ExpandsRequiredClosureButNotIfSelectedEdges()
        {
            var authored = new[]
            {
                new BuildRecipeInvocation("generator", "custom-generator", enabled: false),
                new BuildRecipeInvocation("hot", BuildStepTypeIds.HotUpdate, enabled: false),
                new BuildRecipeInvocation(
                    "content-main",
                    BuildStepTypeIds.AssetContent,
                    enabled: true,
                    configuration: contentConfiguration,
                    dependencies: new[]
                    {
                        new BuildInvocationDependency("generator", BuildDependencyMode.Required),
                        new BuildInvocationDependency("hot", BuildDependencyMode.IfSelected)
                    })
            };
            bool resolved = BuildRecipeSelection.TryExpandRequiredClosure(
                authored,
                new[] { "content-main" },
                out System.Collections.Generic.IReadOnlyList<string> selected,
                out string reason);

            Assert.That(resolved, Is.True, reason);
            CollectionAssert.AreEqual(
                new[] { "generator", "content-main" },
                selected);
        }

        [Test]
        public void FocusedInvocation_DuplicateRootsFailClosed()
        {
            var authored = new[]
            {
                new BuildRecipeInvocation(
                    "content-main",
                    BuildStepTypeIds.AssetContent,
                    enabled: true,
                    configuration: contentConfiguration)
            };

            bool resolved = BuildRecipeSelection.TryExpandRequiredClosure(
                authored,
                new[] { "content-main", "content-main" },
                out _,
                out string reason);

            Assert.That(resolved, Is.False);
            Assert.That(reason, Does.Contain("more than once"));
        }

        [Test]
        public void PresetAvailability_WithAmbiguousSameTypeEntries_FailsClosed()
        {
            SetRecipe(
                new BuildRecipeInvocation(
                    "content-empty",
                    BuildStepTypeIds.AssetContent,
                    enabled: false),
                new BuildRecipeInvocation(
                    "content-configured",
                    BuildStepTypeIds.AssetContent,
                    enabled: true,
                    configuration: contentConfiguration));

            Assert.That(
                BuildRecipePresetCatalog.CanApply(
                    profile,
                    BuildRecipePreset.ContentOnly,
                    out string reason),
                Is.False);
            Assert.That(reason, Does.Contain("More than one"));
        }

        [Test]
        public void PresetAvailability_AllowsIntentBeforeOptionalConfigsAreCreated()
        {
            SetRecipe(
                new BuildRecipeInvocation(
                    BuildStepTypeIds.Player,
                    BuildStepTypeIds.Player,
                    enabled: true),
                new BuildRecipeInvocation(
                    BuildStepTypeIds.AssetContent,
                    BuildStepTypeIds.AssetContent,
                    enabled: false),
                new BuildRecipeInvocation(
                    BuildStepTypeIds.HotUpdate,
                    BuildStepTypeIds.HotUpdate,
                    enabled: false));

            Assert.That(
                BuildRecipePresetCatalog.CanApply(
                    profile,
                    BuildRecipePreset.PlayerWithDependencies,
                    out string reason),
                Is.True,
                reason);
        }

        [Test]
        public void InspectorValidation_ContentOnly_IgnoresPlayerOnlyIdentityAndVersionInfo()
        {
            SetRecipe(new BuildRecipeInvocation(
                BuildStepTypeIds.AssetContent,
                BuildStepTypeIds.AssetContent,
                enabled: true));
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("productName").stringValue = string.Empty;
            serialized.FindProperty("applicationIdentifier").stringValue = "invalid";
            serialized.FindProperty("versionInfoAssetPath").stringValue =
                "Assets/NotResources/VersionInfoData.asset";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            BuildDataEditor editor = CreateBuildDataEditor();
            try
            {
                editor.serializedObject.Update();
                BuildRecipeAnalysis analysis = BuildRecipePresetCatalog.Analyze(
                    profile.RecipeInvocations);
                var errors = (System.Collections.Generic.IReadOnlyList<string>)
                    InvokeEditorMethod(
                        editor,
                        "ValidateSerializedProfile",
                        analysis,
                        null);

                Assert.That(errors.Any(error => error.Contains("Product Name")), Is.False);
                Assert.That(errors.Any(error => error.Contains("Application Identifier")), Is.False);
                Assert.That(errors.Any(error => error.Contains("VersionInfo")), Is.False);
                Assert.That(errors.Any(error => error.Contains("Version Info")), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void AndroidExportRecipe_RequiresPlayerStep()
        {
            Assert.Throws<ArgumentException>(() =>
                BuildRequestFactory.ValidateAndroidExportRecipe(
                    CreateInvocations(BuildRecipePreset.ContentOnly),
                    exportAndroidProject: true));

            Assert.DoesNotThrow(() =>
                BuildRequestFactory.ValidateAndroidExportRecipe(
                    CreateInvocations(BuildRecipePreset.PlayerOnly),
                    exportAndroidProject: true));
        }

        [Test]
        public void EditorMenus_ExposeCurrentRecipeCommands()
        {
            string[] menuPaths = typeof(BuildEntryPoints)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), inherit: false))
                .Cast<MenuItem>()
                .Select(attribute => attribute.menuItem)
                .ToArray();

            CollectionAssert.Contains(
                menuPaths,
                "Build/Pipeline/Run Selected Recipe/Release");
            CollectionAssert.Contains(
                menuPaths,
                "Build/Pipeline/Run Selected Recipe/Development");
            CollectionAssert.Contains(
                menuPaths,
                "Build/Pipeline/Android/Export Player Gradle Project");
        }

        [Test]
        public void InspectorRenameInvocation_UpdatesDependencyReferencesAtomically()
        {
            SetRecipe(
                new BuildRecipeInvocation("content-main", BuildStepTypeIds.AssetContent),
                new BuildRecipeInvocation(
                    "player",
                    BuildStepTypeIds.Player,
                    dependencies: new[]
                    {
                        new BuildInvocationDependency("content-main")
                    }));

            BuildDataEditor editor = CreateBuildDataEditor();
            try
            {
                SerializedProperty recipe = editor.serializedObject.FindProperty("recipeInvocations");
                SerializedProperty invocationId = recipe.GetArrayElementAtIndex(0)
                    .FindPropertyRelative("invocationId");
                InvokeEditorMethod(
                    editor,
                    "RenameInvocation",
                    0,
                    invocationId,
                    "content-release");
                editor.serializedObject.ApplyModifiedPropertiesWithoutUndo();

                BuildRecipeInvocation[] entries = profile.RecipeInvocations.ToArray();
                Assert.That(entries[0].InvocationId, Is.EqualTo("content-release"));
                Assert.That(
                    entries[1].Dependencies.Single().InvocationId,
                    Is.EqualTo("content-release"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void InspectorDependencyCandidate_RejectsAnEdgeThatWouldCreateCycle()
        {
            SetRecipe(
                new BuildRecipeInvocation("first", "custom-first"),
                new BuildRecipeInvocation(
                    "second",
                    "custom-second",
                    dependencies: new[]
                    {
                        new BuildInvocationDependency("first")
                    }));

            BuildDataEditor editor = CreateBuildDataEditor();
            try
            {
                bool wouldCreateCycle = (bool)InvokeEditorMethod(
                    editor,
                    "WouldCreateDependencyCycle",
                    0,
                    "second");

                Assert.That(wouldCreateCycle, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void InspectorRemoveDependencyReferences_RemovesEveryIncomingEdge()
        {
            SetRecipe(
                new BuildRecipeInvocation("shared", "custom-shared"),
                new BuildRecipeInvocation(
                    "first-consumer",
                    "custom-first",
                    dependencies: new[]
                    {
                        new BuildInvocationDependency("shared")
                    }),
                new BuildRecipeInvocation(
                    "second-consumer",
                    "custom-second",
                    dependencies: new[]
                    {
                        new BuildInvocationDependency("shared")
                    }));

            BuildDataEditor editor = CreateBuildDataEditor();
            try
            {
                InvokeEditorMethod(
                    editor,
                    "RemoveDependencyReferences",
                    "shared",
                    0);
                editor.serializedObject.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(
                    profile.RecipeInvocations.Skip(1).All(
                        invocation => invocation.Dependencies.Count == 0),
                    Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void InspectorRecipeBudget_RejectsInvocationCountBeforeMaterialization()
        {
            var serialized = new SerializedObject(profile);
            SerializedProperty recipe = serialized.FindProperty("recipeInvocations");
            recipe.arraySize = BuildPipelineBudgets.MaximumInvocationCount + 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            BuildDataEditor editor = CreateBuildDataEditor();
            try
            {
                editor.serializedObject.Update();
                Assert.That(
                    InvokeEditorMethod(
                        editor,
                        "TryGetRecipeBudgetViolation",
                        (object)null),
                    Is.EqualTo(true));
                TargetInvocationException exception =
                    Assert.Throws<TargetInvocationException>(() =>
                        InvokeEditorMethod(
                            editor,
                            "GetSerializedRecipeInvocations"));
                Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void InspectorRecipeBudget_RejectsDependencyEdgeAboveSafetyBudget()
        {
            var serialized = new SerializedObject(profile);
            SerializedProperty recipe = serialized.FindProperty("recipeInvocations");
            recipe.arraySize = 1;
            SerializedProperty dependencies = recipe.GetArrayElementAtIndex(0)
                .FindPropertyRelative("dependencies");
            dependencies.arraySize = BuildPipelineBudgets.MaximumDependencyEdgeCount + 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            BuildDataEditor editor = CreateBuildDataEditor();
            try
            {
                editor.serializedObject.Update();
                Assert.That(
                    InvokeEditorMethod(
                        editor,
                        "TryGetRecipeBudgetViolation",
                        (object)null),
                    Is.EqualTo(true));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
            }
        }

        private void SetRecipe(params BuildRecipeInvocation[] entries)
        {
            var serialized = new SerializedObject(profile);
            SerializedProperty recipe = serialized.FindProperty("recipeInvocations");
            recipe.arraySize = entries.Length;
            for (int index = 0; index < entries.Length; index++)
            {
                SerializedProperty element = recipe.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("enabled").boolValue = entries[index].Enabled;
                element.FindPropertyRelative("invocationId").stringValue =
                    entries[index].InvocationId;
                element.FindPropertyRelative("stepTypeId").stringValue =
                    entries[index].StepTypeId;
                element.FindPropertyRelative("configuration").objectReferenceValue =
                    entries[index].Configuration;
                element.FindPropertyRelative("incrementality").enumValueIndex =
                    (int)entries[index].Incrementality;
                SerializedProperty dependencies = element.FindPropertyRelative("dependencies");
                dependencies.arraySize = entries[index].Dependencies.Count;
                for (int dependencyIndex = 0;
                     dependencyIndex < entries[index].Dependencies.Count;
                     dependencyIndex++)
                {
                    BuildInvocationDependency value = entries[index].Dependencies[dependencyIndex];
                    SerializedProperty dependency =
                        dependencies.GetArrayElementAtIndex(dependencyIndex);
                    dependency.FindPropertyRelative("invocationId").stringValue =
                        value.InvocationId;
                    dependency.FindPropertyRelative("mode").enumValueIndex =
                        (int)value.Mode;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static BuildStepInvocation[] CreateInvocations(BuildRecipePreset preset)
        {
            return BuildRecipePresetCatalog.GetTemplates(preset)
                .Select(template => new BuildStepInvocation(
                    template.InvocationId,
                    template.StepTypeId,
                    dependencies: template.Dependencies))
                .ToArray();
        }

        private static BuildRecipeInvocation[] CreateAuthoredInvocations(
            BuildRecipePreset preset)
        {
            return BuildRecipePresetCatalog.GetTemplates(preset)
                .Select(template => new BuildRecipeInvocation(
                    template.InvocationId,
                    template.StepTypeId,
                    enabled: true,
                    dependencies: template.Dependencies))
                .ToArray();
        }

        private BuildDataEditor CreateBuildDataEditor()
        {
            var editor = UnityEditor.Editor.CreateEditor(
                profile,
                typeof(BuildDataEditor)) as BuildDataEditor;
            Assert.That(editor, Is.Not.Null);
            return editor;
        }

        private static object InvokeEditorMethod(
            BuildDataEditor editor,
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = typeof(BuildDataEditor).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            return method.Invoke(editor, arguments);
        }
    }
}
