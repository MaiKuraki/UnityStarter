using System;
using System.Collections.Generic;
using System.IO;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEditor;
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
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Output,
                relativeOutput
            });

            BuildRequest request = BuildRequestFactory.CreateForCommandLine(buildData, options);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string expected = Path.GetFullPath(Path.Combine(projectRoot, relativeOutput));

            Assert.That(request.OutputPath, Is.EqualTo(expected));
            Assert.That(
                request.OutputDirectory,
                Is.EqualTo(Path.GetDirectoryName(expected)));
            StringAssert.DoesNotContain(
                Path.Combine("Build", "Build") + Path.DirectorySeparatorChar,
                request.OutputPath);
        }

        [Test]
        public void CreateForCommandLine_WithoutOutput_UsesBuildRootPlatformDefault()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64)
            });

            BuildRequest request = BuildRequestFactory.CreateForCommandLine(buildData, options);
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

            BuildCommandLineOptions deniedOptions = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Output,
                externalOutput
            });

            Assert.Throws<InvalidOperationException>(
                () => BuildRequestFactory.CreateForCommandLine(buildData, deniedOptions));

            BuildCommandLineOptions allowedOptions = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Output,
                externalOutput,
                BuildCommandLineOptionNames.AllowExternalOutput
            });

            BuildRequest request = BuildRequestFactory.CreateForCommandLine(buildData, allowedOptions);

            Assert.That(request.OutputPath, Is.EqualTo(Path.GetFullPath(externalOutput)));
            Assert.That(request.OutputDirectory, Is.EqualTo(Path.GetDirectoryName(Path.GetFullPath(externalOutput))));
            Assert.That(request.AllowExternalOutput, Is.True);
        }

        [Test]
        public void CreateForCommandLine_OutputFileDirectlyUnderBuildRoot_RejectsSharedCleanDirectory()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Output,
                Path.Combine("Build", "Game.exe")
            });

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BuildRequestFactory.CreateForCommandLine(buildData, options));
            StringAssert.Contains("dedicated directory", exception.Message);
        }

        [Test]
        public void CreateForCommandLine_AndroidDefault_ProducesApkInReleaseDirectory()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.Android)
            });

            BuildRequest request = BuildRequestFactory.CreateForCommandLine(buildData, options);

            StringAssert.EndsWith(
                Path.Combine("Android", "Release", buildData.ProductName + ".apk"),
                request.OutputPath);
            Assert.That(request.ExportAndroidProject, Is.False);
            Assert.That(request.OutputIsFolder, Is.False);
        }

        [Test]
        public void CreateForCommandLine_AndroidDirectoryOutput_RequiresExportFlag()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.Android),
                BuildCommandLineOptionNames.Output,
                "Build/Android/Export"
            });

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => BuildRequestFactory.CreateForCommandLine(buildData, options));
            StringAssert.Contains(BuildCommandLineOptionNames.ExportAndroidProject, exception.Message);
        }

        [Test]
        public void CreateForCommandLine_AndroidExportRejectsContentOnlyRecipe()
        {
            var serialized = new SerializedObject(buildData);
            SerializedProperty steps = serialized.FindProperty("pipelineSteps");
            steps.arraySize = 2;
            steps.GetArrayElementAtIndex(0).stringValue = BuildStepIds.HotUpdate;
            steps.GetArrayElementAtIndex(1).stringValue = BuildStepIds.AssetContent;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.Android),
                BuildCommandLineOptionNames.ExportAndroidProject
            });

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => BuildRequestFactory.CreateForCommandLine(buildData, options));
            StringAssert.Contains(BuildStepIds.Player, exception.Message);
        }

        [Test]
        public void CreateForCommandLine_WithAssetProviderNone_ClearsProfileBinding()
        {
            var configuration = ScriptableObject.CreateInstance<AddressablesBuildConfig>();
            try
            {
                var serialized = new SerializedObject(buildData);
                serialized.FindProperty("assetContentProviderId").stringValue =
                    AssetContentProviderIds.Addressables;
                serialized.FindProperty("assetContentConfiguration").objectReferenceValue =
                    configuration;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
                {
                    BuildCommandLineOptionNames.BuildTarget,
                    nameof(BuildTarget.StandaloneWindows64),
                    BuildCommandLineOptionNames.Provider,
                    "none"
                });

                BuildRequest request = BuildRequestFactory.CreateForCommandLine(buildData, options);

                Assert.That(request.AssetContentProviderId, Is.Empty);
                Assert.That(request.AssetContentConfiguration, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void CreateForCommandLine_ContentOnlyRecipeRejectsProviderNone()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Provider,
                "none",
                BuildCommandLineOptionNames.Steps,
                $"{BuildStepIds.HotUpdate},{BuildStepIds.AssetContent}"
            });

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => BuildRequestFactory.CreateForCommandLine(buildData, options));

            StringAssert.Contains(BuildStepIds.AssetContent, exception.Message);
            StringAssert.Contains("Asset Content Provider", exception.Message);
        }

        [Test]
        public void CreateForCommandLine_WithVersionInfoPath_NormalizesSeparators()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.VersionInfo,
                "Assets\\Resources\\Build\\VersionInfoData.asset"
            });

            BuildRequest request = BuildRequestFactory.CreateForCommandLine(buildData, options);

            Assert.That(
                request.VersionInfoAssetPath,
                Is.EqualTo("Assets/Resources/Build/VersionInfoData.asset"));
        }

        [TestCase(CheatBuildMode.Disabled, false, null, false)]
        [TestCase(CheatBuildMode.DevelopmentBuilds, false, null, false)]
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

            var arguments = new List<string>
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64)
            };
            if (debugBuild)
            {
                arguments.Add(BuildCommandLineOptionNames.Development);
            }

            if (!string.IsNullOrEmpty(overrideOption))
            {
                arguments.Add(overrideOption);
            }

            BuildRequest request = BuildRequestFactory.CreateForCommandLine(
                buildData,
                BuildCommandLine.Parse(arguments));

            Assert.That(request.CheatEnabled, Is.EqualTo(expected));
            Assert.That(request.CheatBuildMode, Is.EqualTo(mode));
            Assert.That(request.DebugBuild, Is.EqualTo(debugBuild));
        }

        [Test]
        public void ContainsCheatDefine_RequiresAnExactEffectiveCompilerSymbol()
        {
            Assert.That(
                CheatBuildDefineUtility.ContainsCheatDefine(
                    new[] { "OTHER", " ENABLE_CHEAT " }),
                Is.True);
            Assert.That(
                CheatBuildDefineUtility.ContainsCheatDefine(
                    new[] { "enable_cheat", "ENABLE_CHEAT_EXTRA" }),
                Is.False);
            Assert.That(
                CheatBuildDefineUtility.ContainsCheatDefine(null),
                Is.False);
            Assert.That(
                CheatBuildDefineUtility.IsCheatRuntimeAssemblyWithDefine(
                    "Unrelated.Runtime",
                    new[] { CheatBuildDefineUtility.DefineSymbol }),
                Is.False);
            Assert.That(
                CheatBuildDefineUtility.IsCheatRuntimeAssemblyWithDefine(
                    "CycloneGames.Cheat.Runtime",
                    new[] { CheatBuildDefineUtility.DefineSymbol }),
                Is.True);
        }
    }
}
