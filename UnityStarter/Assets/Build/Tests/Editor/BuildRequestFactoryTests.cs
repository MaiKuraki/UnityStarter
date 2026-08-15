using System;
using System.Collections.Generic;
using System.IO;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class BuildRequestFactoryTests
    {
        private BuildData buildData;

        [SetUp]
        public void SetUp()
        {
            buildData = ScriptableObject.CreateInstance<BuildData>();
            var serialized = new SerializedObject(buildData);
            serialized.FindProperty("companyName").stringValue = "TestCompany";
            serialized.FindProperty("productName").stringValue = "TestProduct";
            serialized.FindProperty("applicationIdentifier").stringValue = "com.example.test";
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            if (buildData != null)
            {
                UnityEngine.Object.DestroyImmediate(buildData);
            }
        }

        [Test]
        public void CreateForCommandLine_WithExplicitOutput_ResolvesRelativeToProjectRootOnce()
        {
            string relativeOutput = Path.Combine("Build", "Artifacts", "Game.exe");
            BuildRequest request = CreateCommandLineRequest(
                BuildCommandLineOptionNames.Output,
                relativeOutput);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string expected = Path.GetFullPath(Path.Combine(projectRoot, relativeOutput));

            Assert.That(request.OutputPath, Is.EqualTo(expected));
            Assert.That(request.OutputDirectory, Is.EqualTo(Path.GetDirectoryName(expected)));
            StringAssert.DoesNotContain(
                Path.Combine("Build", "Build") + Path.DirectorySeparatorChar,
                request.OutputPath);
        }

        [Test]
        public void CreateForCommandLine_WithoutOutput_UsesBuildRootPlatformDefault()
        {
            BuildRequest request = CreateCommandLineRequest();
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string expected = Path.GetFullPath(Path.Combine(
                projectRoot,
                buildData.OutputBasePath,
                "Windows",
                "Release",
                buildData.ProductName + ".exe"));

            Assert.That(request.OutputPath, Is.EqualTo(expected));
            Assert.That(request.OutputDirectory, Is.EqualTo(Path.GetDirectoryName(expected)));
        }

        [Test]
        public void CreateForCommandLine_WithExternalOutput_RequiresExplicitGate()
        {
            string externalOutput = Path.Combine(
                Path.GetTempPath(),
                "UnityStarter",
                "BuildPipelineTests",
                Guid.NewGuid().ToString("N"),
                "deep",
                "external",
                "Game.exe");
            BuildCommandLineOptions denied = ParseCommandLine(
                BuildCommandLineOptionNames.Output,
                externalOutput);

            Assert.Throws<InvalidOperationException>(
                () => BuildRequestFactory.CreateForCommandLine(buildData, denied));

            BuildRequest allowed = CreateCommandLineRequest(
                BuildCommandLineOptionNames.Output,
                externalOutput,
                BuildCommandLineOptionNames.AllowExternalOutput);
            Assert.That(allowed.OutputPath, Is.EqualTo(Path.GetFullPath(externalOutput)));
            Assert.That(allowed.AllowExternalOutput, Is.True);
        }

        [Test]
        public void CreateForCommandLine_AndroidExportRejectsRecipeWithoutPlayerInvocation()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.Android),
                BuildCommandLineOptionNames.ExportAndroidProject,
                BuildCommandLineOptionNames.Recipe,
                "base-content=" + BuildStepTypeIds.AssetContent
            });

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => BuildRequestFactory.CreateForCommandLine(buildData, options));
            StringAssert.Contains(BuildStepTypeIds.Player, exception.Message);
        }

        [Test]
        public void CreateForCommandLine_ProfileRecipe_PreservesInvocationPolicyAndDependencies()
        {
            SetRecipe(new[]
            {
                new BuildRecipeInvocation(
                    "base-content",
                    BuildStepTypeIds.AssetContent,
                    incrementality: BuildIncrementality.Incremental),
                new BuildRecipeInvocation(
                    "player-client",
                    BuildStepTypeIds.Player,
                    dependencies: new[]
                    {
                        new BuildInvocationDependency(
                            "base-content",
                            BuildDependencyMode.Required)
                    })
            });

            BuildRequest request = CreateCommandLineRequest();

            Assert.That(request.Steps.Count, Is.EqualTo(2));
            Assert.That(
                request.GetInvocation("base-content").Incrementality,
                Is.EqualTo(BuildIncrementality.Incremental));
            Assert.That(
                request.GetInvocation("player-client").Dependencies[0].InvocationId,
                Is.EqualTo("base-content"));
            Assert.That(
                request.GetInvocation("player-client").Dependencies[0].Mode,
                Is.EqualTo(BuildDependencyMode.Required));
        }

        [Test]
        public void CreateForCommandLine_ProfileSelection_ExpandsRequiredClosureAndRetainsAuthoredState()
        {
            var contentConfiguration = ScriptableObject.CreateInstance<AddressablesBuildConfig>();
            string configurationPath = CreateAsset(contentConfiguration);
            try
            {
                SetRecipe(new[]
                {
                    new BuildRecipeInvocation(
                        "hot-release",
                        BuildStepTypeIds.HotUpdate,
                        enabled: false,
                        incrementality: BuildIncrementality.Incremental),
                    new BuildRecipeInvocation(
                        "content-base",
                        BuildStepTypeIds.AssetContent,
                        enabled: false,
                        configuration: contentConfiguration,
                        dependencies: new[]
                        {
                            new BuildInvocationDependency(
                                "hot-release",
                                BuildDependencyMode.Required)
                        }),
                    new BuildRecipeInvocation(
                        "optional-content",
                        BuildStepTypeIds.AssetContent,
                        enabled: false),
                    new BuildRecipeInvocation(
                        "player-client",
                        BuildStepTypeIds.Player,
                        enabled: false,
                        dependencies: new[]
                        {
                            new BuildInvocationDependency(
                                "content-base",
                                BuildDependencyMode.Required),
                            new BuildInvocationDependency(
                                "optional-content",
                                BuildDependencyMode.IfSelected)
                        })
                });

                BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
                {
                    BuildCommandLineOptionNames.BuildTarget,
                    nameof(BuildTarget.StandaloneWindows64),
                    BuildCommandLineOptionNames.Profile,
                    "Assets/BuildProfiles/Release.asset",
                    BuildCommandLineOptionNames.Selection,
                    "player-client",
                    BuildCommandLineOptionNames.StepIncrementality,
                    "content-base=Incremental"
                });
                BuildRequest request = BuildRequestFactory.CreateForCommandLine(
                    buildData,
                    options);

                Assert.That(request.Steps.Count, Is.EqualTo(3));
                Assert.That(request.GetInvocation("player-client"), Is.Not.Null);
                Assert.That(request.GetInvocation("content-base"), Is.Not.Null);
                Assert.That(
                    request.GetInvocation("content-base").Configuration,
                    Is.SameAs(contentConfiguration));
                Assert.That(
                    request.GetInvocation("content-base").Incrementality,
                    Is.EqualTo(BuildIncrementality.Incremental));
                Assert.That(
                    request.GetInvocation("hot-release").Incrementality,
                    Is.EqualTo(BuildIncrementality.Incremental));
                Assert.That(request.GetInvocation("optional-content"), Is.Null);
            }
            finally
            {
                AssetDatabase.DeleteAsset(configurationPath);
            }
        }

        [Test]
        public void CreateForCommandLine_ProfileSelection_RejectsUnknownInvocation()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Profile,
                "Assets/BuildProfiles/Release.asset",
                BuildCommandLineOptionNames.Selection,
                "missing-content"
            });

            BuildFailedException exception = Assert.Throws<BuildFailedException>(() =>
                BuildRequestFactory.CreateForCommandLine(buildData, options));

            StringAssert.Contains("unknown invocation", exception.Message);
        }

        [Test]
        public void CreateForCommandLine_ExplicitRecipe_AllowsRepeatedStepTypesAndAppliesInvocationOverrides()
        {
            var firstConfiguration = ScriptableObject.CreateInstance<AddressablesBuildConfig>();
            var secondConfiguration = ScriptableObject.CreateInstance<AddressablesBuildConfig>();
            string firstPath = CreateAsset(firstConfiguration);
            string secondPath = CreateAsset(secondConfiguration);
            try
            {
                BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
                {
                    BuildCommandLineOptionNames.BuildTarget,
                    nameof(BuildTarget.StandaloneWindows64),
                    BuildCommandLineOptionNames.Recipe,
                    "base-content=" + BuildStepTypeIds.AssetContent,
                    BuildCommandLineOptionNames.Recipe,
                    "dlc-content=" + BuildStepTypeIds.AssetContent,
                    BuildCommandLineOptionNames.StepConfiguration,
                    "base-content=" + firstPath,
                    BuildCommandLineOptionNames.StepConfiguration,
                    "dlc-content=" + secondPath,
                    BuildCommandLineOptionNames.StepIncrementality,
                    "dlc-content=Incremental",
                    BuildCommandLineOptionNames.StepDependency,
                    "dlc-content=Required:base-content"
                });

                BuildRequest request = BuildRequestFactory.CreateForCommandLine(buildData, options);
                BuildStepInvocation first = request.GetInvocation("base-content");
                BuildStepInvocation second = request.GetInvocation("dlc-content");

                Assert.That(first.StepTypeId, Is.EqualTo(BuildStepTypeIds.AssetContent));
                Assert.That(second.StepTypeId, Is.EqualTo(BuildStepTypeIds.AssetContent));
                Assert.That(first.Configuration, Is.SameAs(firstConfiguration));
                Assert.That(second.Configuration, Is.SameAs(secondConfiguration));
                Assert.That(first.Incrementality, Is.EqualTo(BuildIncrementality.Clean));
                Assert.That(second.Incrementality, Is.EqualTo(BuildIncrementality.Incremental));
                Assert.That(second.Dependencies[0].InvocationId, Is.EqualTo("base-content"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(firstPath);
                AssetDatabase.DeleteAsset(secondPath);
            }
        }

        [Test]
        public void CreateForCommandLine_ExplicitRecipe_DoesNotImplicitlyInheritAuthoredState()
        {
            var configuration = ScriptableObject.CreateInstance<AddressablesBuildConfig>();
            string configurationPath = CreateAsset(configuration);
            try
            {
                SetRecipe(new[]
                {
                    new BuildRecipeInvocation(
                        "content",
                        BuildStepTypeIds.AssetContent,
                        configuration: configuration,
                        incrementality: BuildIncrementality.Incremental,
                        dependencies: new[]
                        {
                            new BuildInvocationDependency("foundation")
                        })
                });

                BuildRequest request = CreateCommandLineRequest(
                    BuildCommandLineOptionNames.Recipe,
                    "content=" + BuildStepTypeIds.AssetContent);
                BuildStepInvocation invocation = request.GetInvocation("content");

                Assert.That(invocation.Configuration, Is.Null);
                Assert.That(invocation.Incrementality, Is.EqualTo(BuildIncrementality.Clean));
                Assert.That(invocation.Dependencies, Is.Empty);
            }
            finally
            {
                AssetDatabase.DeleteAsset(configurationPath);
            }
        }

        [Test]
        public void CreateForCommandLine_OverrideForUnselectedInvocation_FailsClosed()
        {
            BuildCommandLineOptions options = ParseCommandLine(
                BuildCommandLineOptionNames.Recipe,
                "player-client=" + BuildStepTypeIds.Player,
                BuildCommandLineOptionNames.StepIncrementality,
                "content=Incremental");

            BuildFailedException exception = Assert.Throws<BuildFailedException>(() =>
                BuildRequestFactory.CreateForCommandLine(buildData, options));
            StringAssert.Contains("does not target a selected recipe invocation", exception.Message);
        }

        [Test]
        public void CreateForCommandLine_WhenSelectedConfigurationIsNotPersistent_FailsClosed()
        {
            var configuration = ScriptableObject.CreateInstance<AddressablesBuildConfig>();
            try
            {
                SetRecipe(new[]
                {
                    new BuildRecipeInvocation(
                        "content",
                        BuildStepTypeIds.AssetContent,
                        configuration: configuration)
                });

                BuildFailedException exception = Assert.Throws<BuildFailedException>(
                    () => CreateCommandLineRequest());
                StringAssert.Contains("persistent .asset below Assets", exception.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void CreateInteractive_FocusedSelection_IgnoresDirtyConfigurationOutsideSelection()
        {
            var configuration = ScriptableObject.CreateInstance<AddressablesBuildConfig>();
            string configurationPath = CreateAsset(configuration);
            AssetDatabase.SaveAssetIfDirty(configuration);
            try
            {
                SetRecipe(new[]
                {
                    new BuildRecipeInvocation(
                        "content",
                        BuildStepTypeIds.AssetContent,
                        configuration: configuration),
                    new BuildRecipeInvocation("player-client", BuildStepTypeIds.Player)
                });
                EditorUtility.SetDirty(configuration);

                BuildRequest request = BuildRequestFactory.CreateInteractive(
                    buildData,
                    BuildTarget.StandaloneWindows64,
                    debugBuild: false,
                    invocationIdsOverride: new[] { "player-client" });

                Assert.That(request.Steps.Count, Is.EqualTo(1));
                Assert.That(request.Steps[0].InvocationId, Is.EqualTo("player-client"));
                Assert.That(EditorUtility.IsDirty(configuration), Is.True);
            }
            finally
            {
                AssetDatabase.DeleteAsset(configurationPath);
            }
        }

        [Test]
        public void CreateInteractive_WhenRequiredConfigurationIsDirty_FailsWithoutSavingIt()
        {
            var configuration = ScriptableObject.CreateInstance<AddressablesBuildConfig>();
            string configurationPath = CreateAsset(configuration);
            AssetDatabase.SaveAssetIfDirty(configuration);
            try
            {
                SetRecipe(new[]
                {
                    new BuildRecipeInvocation(
                        "content",
                        BuildStepTypeIds.AssetContent,
                        configuration: configuration,
                        enabled: false),
                    new BuildRecipeInvocation(
                        "player-client",
                        BuildStepTypeIds.Player,
                        dependencies: new[]
                        {
                            new BuildInvocationDependency(
                                "content",
                                BuildDependencyMode.Required)
                        })
                });
                EditorUtility.SetDirty(configuration);

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                    BuildRequestFactory.CreateInteractive(
                        buildData,
                        BuildTarget.StandaloneWindows64,
                        debugBuild: false,
                        invocationIdsOverride: new[] { "player-client" }));

                StringAssert.Contains("unsaved changes", exception.Message);
                StringAssert.Contains(configurationPath, exception.Message);
                Assert.That(EditorUtility.IsDirty(configuration), Is.True);
            }
            finally
            {
                AssetDatabase.DeleteAsset(configurationPath);
            }
        }

        [Test]
        public void CreateForCommandLine_WithVersionInfoPath_NormalizesSeparators()
        {
            BuildRequest request = CreateCommandLineRequest(
                BuildCommandLineOptionNames.VersionInfo,
                "Assets\\Resources\\Build\\VersionInfoData.asset");

            Assert.That(
                request.VersionInfoAssetPath,
                Is.EqualTo("Assets/Resources/Build/VersionInfoData.asset"));
        }

        [Test]
        public void CreateForCommandLine_WithIdentityOverride_PreservesExplicitValues()
        {
            BuildRequest request = CreateCommandLineRequest(
                BuildCommandLineOptionNames.BuildNumber,
                "17",
                BuildCommandLineOptionNames.SourceProvider,
                "git",
                BuildCommandLineOptionNames.SourceRevision,
                "abc123",
                BuildCommandLineOptionNames.SourceBranch,
                "main",
                BuildCommandLineOptionNames.CiProvider,
                "jenkins",
                BuildCommandLineOptionNames.CiRunId,
                "job-17");

            Assert.That(request.IdentityOverride.BuildNumber, Is.EqualTo(17));
            Assert.That(request.IdentityOverride.SourceProvider, Is.EqualTo("git"));
            Assert.That(request.IdentityOverride.SourceRevision, Is.EqualTo("abc123"));
            Assert.That(request.IdentityOverride.SourceBranch, Is.EqualTo("main"));
            Assert.That(request.IdentityOverride.CiProvider, Is.EqualTo("jenkins"));
            Assert.That(request.IdentityOverride.CiRunId, Is.EqualTo("job-17"));
        }

        [TestCase(CheatBuildMode.Disabled, false, null, false)]
        [TestCase(CheatBuildMode.DevelopmentBuilds, true, null, true)]
        [TestCase(CheatBuildMode.Enabled, false, null, true)]
        [TestCase(CheatBuildMode.Disabled, false, BuildCommandLineOptionNames.EnableCheat, true)]
        [TestCase(CheatBuildMode.Enabled, true, BuildCommandLineOptionNames.DisableCheat, false)]
        public void BuildRequest_CheatEnabled_ResolvesModeDebugAndCommandLineOverride(
            CheatBuildMode mode,
            bool debugBuild,
            string overrideOption,
            bool expected)
        {
            var serialized = new SerializedObject(buildData);
            serialized.FindProperty("cheatBuildMode").enumValueIndex = (int)mode;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var extra = new List<string>();
            if (debugBuild)
            {
                extra.Add(BuildCommandLineOptionNames.Development);
            }

            if (!string.IsNullOrEmpty(overrideOption))
            {
                extra.Add(overrideOption);
            }

            BuildRequest request = BuildRequestFactory.CreateForCommandLine(
                buildData,
                ParseCommandLine(extra.ToArray()));

            Assert.That(request.CheatEnabled, Is.EqualTo(expected));
            Assert.That(request.CheatBuildMode, Is.EqualTo(mode));
            Assert.That(request.DebugBuild, Is.EqualTo(debugBuild));
        }

        [TestCase(BuildSourceCleanlinessPolicy.RequireClean, false, true)]
        [TestCase(BuildSourceCleanlinessPolicy.RequireClean, true, true)]
        [TestCase(BuildSourceCleanlinessPolicy.AllowDirtyDevelopment, false, true)]
        [TestCase(BuildSourceCleanlinessPolicy.AllowDirtyDevelopment, true, false)]
        [TestCase(BuildSourceCleanlinessPolicy.AllowDirtyLocalRelease, false, true)]
        [TestCase(BuildSourceCleanlinessPolicy.AllowDirtyLocalRelease, true, false)]
        public void BuildRequest_RequireCleanSource_ReleaseCannotBeRelaxedAndDevelopmentRequiresOptIn(
            BuildSourceCleanlinessPolicy policy,
            bool debugBuild,
            bool expected)
        {
            var serialized = new SerializedObject(buildData);
            serialized.FindProperty("sourceCleanlinessPolicy").enumValueIndex = (int)policy;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var extra = new List<string>();
            if (debugBuild)
            {
                extra.Add(BuildCommandLineOptionNames.Development);
            }

            BuildRequest request = BuildRequestFactory.CreateForCommandLine(
                buildData,
                ParseCommandLine(extra.ToArray()));

            Assert.That(request.SourceCleanlinessPolicy, Is.EqualTo(policy));
            Assert.That(request.RequireCleanSource, Is.EqualTo(expected));
        }

        [Test]
        public void BuildSourceCleanlinessPolicy_SerializedValuesRemainStable()
        {
            Assert.That((int)BuildSourceCleanlinessPolicy.RequireClean, Is.Zero);
            Assert.That(
                (int)BuildSourceCleanlinessPolicy.AllowDirtyDevelopment,
                Is.EqualTo(1));
            Assert.That(
                (int)BuildSourceCleanlinessPolicy.AllowDirtyLocalRelease,
                Is.EqualTo(2));
        }

        [Test]
        public void BuildPurpose_PublicValuesRemainStable()
        {
            Assert.That((int)BuildPurpose.Release, Is.Zero);
            Assert.That((int)BuildPurpose.Development, Is.EqualTo(1));
            Assert.That((int)BuildPurpose.LocalReleasePreview, Is.EqualTo(2));
            Assert.That((int)BuildIdentityOrigin.VersionControl, Is.Zero);
            Assert.That((int)BuildIdentityOrigin.ExplicitOverride, Is.EqualTo(1));
            Assert.That((int)BuildIdentityOrigin.LocalDevelopment, Is.EqualTo(2));
            Assert.That((int)BuildIdentityOrigin.LocalPreview, Is.EqualTo(3));
        }

        [Test]
        public void CreateLocalReleasePreview_PlayerOnly_UsesIsolatedOptimizedOutput()
        {
            SetRecipe(new[]
            {
                new BuildRecipeInvocation(
                    "player-client",
                    BuildStepTypeIds.Player,
                    incrementality: BuildIncrementality.Clean)
            });

            BuildRequest request = BuildRequestFactory.CreateLocalReleasePreview(
                buildData,
                BuildTarget.StandaloneWindows64,
                invocationIdsOverride: null);

            Assert.That(request.Purpose, Is.EqualTo(BuildPurpose.LocalReleasePreview));
            Assert.That(request.DebugBuild, Is.False);
            Assert.That(request.DeleteDebugFiles, Is.True);
            Assert.That(request.RequireCleanSource, Is.False);
            Assert.That(request.CanPublishReleaseBaseline, Is.False);
            Assert.DoesNotThrow(
                () => BuildRequestFactory.ValidateLocalReleasePreviewRequest(request));
            Assert.That(request.Steps.Count, Is.EqualTo(1));
            Assert.That(request.Steps[0].StepTypeId, Is.EqualTo(BuildStepTypeIds.Player));
            StringAssert.Contains(
                Path.Combine("Build", "LocalPreview", "Windows", "Release"),
                request.OutputPath);
        }

        [Test]
        public void CreateLocalReleasePreview_PlayerWithRequiredContent_FailsClosed()
        {
            SetRecipe(new[]
            {
                new BuildRecipeInvocation(
                    "content",
                    BuildStepTypeIds.AssetContent,
                    enabled: false),
                new BuildRecipeInvocation(
                    "player-client",
                    BuildStepTypeIds.Player,
                    dependencies: new[]
                    {
                        new BuildInvocationDependency(
                            "content",
                            BuildDependencyMode.Required)
                    })
            });

            BuildFailedException exception = Assert.Throws<BuildFailedException>(() =>
                BuildRequestFactory.CreateLocalReleasePreview(
                    buildData,
                    BuildTarget.StandaloneWindows64,
                    invocationIdsOverride: null));
            StringAssert.Contains("cannot include required content", exception.Message);
        }

        [Test]
        public void CreateLocalReleasePreview_DisabledPlayer_FailsClosed()
        {
            SetRecipe(new[]
            {
                new BuildRecipeInvocation(
                    "player-client",
                    BuildStepTypeIds.Player,
                    enabled: false,
                    incrementality: BuildIncrementality.Clean)
            });

            BuildFailedException exception = Assert.Throws<BuildFailedException>(() =>
                BuildRequestFactory.CreateLocalReleasePreview(
                    buildData,
                    BuildTarget.StandaloneWindows64,
                    invocationIdsOverride: null));

            StringAssert.Contains("requires one Player invocation", exception.Message);
        }

        private BuildRequest CreateCommandLineRequest(params string[] extraArguments)
        {
            return BuildRequestFactory.CreateForCommandLine(
                buildData,
                ParseCommandLine(extraArguments));
        }

        private static BuildCommandLineOptions ParseCommandLine(params string[] extraArguments)
        {
            var arguments = new List<string>
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64)
            };
            arguments.AddRange(extraArguments);
            return BuildCommandLine.Parse(arguments);
        }

        private static string CreateAsset(ScriptableObject configuration)
        {
            string path = $"Assets/Build/Tests/Editor/RequestFactory-{Guid.NewGuid():N}.asset";
            AssetDatabase.CreateAsset(configuration, path);
            return path;
        }

        private void SetRecipe(IReadOnlyList<BuildRecipeInvocation> entries)
        {
            var serialized = new SerializedObject(buildData);
            SerializedProperty recipe = serialized.FindProperty("recipeInvocations");
            recipe.arraySize = entries.Count;
            for (int index = 0; index < entries.Count; index++)
            {
                BuildRecipeInvocation entry = entries[index];
                SerializedProperty element = recipe.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("enabled").boolValue = entry.Enabled;
                element.FindPropertyRelative("invocationId").stringValue = entry.InvocationId;
                element.FindPropertyRelative("stepTypeId").stringValue = entry.StepTypeId;
                element.FindPropertyRelative("configuration").objectReferenceValue =
                    entry.Configuration;
                element.FindPropertyRelative("incrementality").enumValueIndex =
                    (int)entry.Incrementality;

                IReadOnlyList<BuildInvocationDependency> dependencies = entry.Dependencies;
                SerializedProperty serializedDependencies =
                    element.FindPropertyRelative("dependencies");
                serializedDependencies.arraySize = dependencies.Count;
                for (int dependencyIndex = 0;
                     dependencyIndex < dependencies.Count;
                     dependencyIndex++)
                {
                    BuildInvocationDependency dependency = dependencies[dependencyIndex];
                    SerializedProperty serializedDependency =
                        serializedDependencies.GetArrayElementAtIndex(dependencyIndex);
                    serializedDependency.FindPropertyRelative("invocationId").stringValue =
                        dependency.InvocationId;
                    serializedDependency.FindPropertyRelative("mode").enumValueIndex =
                        (int)dependency.Mode;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
