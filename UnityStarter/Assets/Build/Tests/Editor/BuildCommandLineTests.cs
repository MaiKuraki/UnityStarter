using System;
using System.Collections.Generic;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class BuildCommandLineTests
    {
        [TestCase(BuildCommandLineOptionNames.BuildTarget)]
        [TestCase(BuildCommandLineOptionNames.Profile)]
        [TestCase(BuildCommandLineOptionNames.ScriptingBackend)]
        [TestCase(BuildCommandLineOptionNames.Output)]
        [TestCase(BuildCommandLineOptionNames.Version)]
        [TestCase(BuildCommandLineOptionNames.OutputRoot)]
        [TestCase(BuildCommandLineOptionNames.VersionInfo)]
        [TestCase(BuildCommandLineOptionNames.Provider)]
        [TestCase(BuildCommandLineOptionNames.ProviderConfiguration)]
        [TestCase(BuildCommandLineOptionNames.Steps)]
        public void Parse_WhenValueOptionIsMissing_Throws(string option)
        {
            var arguments = new List<string>();
            if (!string.Equals(
                    option,
                    BuildCommandLineOptionNames.BuildTarget,
                    StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add(BuildCommandLineOptionNames.BuildTarget);
                arguments.Add(nameof(BuildTarget.StandaloneWindows64));
            }

            arguments.Add(option);

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => BuildCommandLine.Parse(arguments));

            StringAssert.Contains("requires a value", exception.Message);
        }

        [Test]
        public void Parse_WhenValueIsFollowedByAnotherOption_Throws()
        {
            string[] arguments =
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Output,
                BuildCommandLineOptionNames.Clean
            };

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => BuildCommandLine.Parse(arguments));

            StringAssert.Contains(
                $"'{BuildCommandLineOptionNames.Output}' requires a value",
                exception.Message);
        }

        [Test]
        public void Parse_WhenOptionIsRepeatedWithDifferentCasing_Throws()
        {
            string[] arguments =
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Output,
                "Build/Windows/Game.exe",
                "-PIPELINEOUTPUT",
                "Build/Windows/Other.exe"
            };

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => BuildCommandLine.Parse(arguments));

            StringAssert.Contains("specified more than once", exception.Message);
        }

        [TestCase(BuildCommandLineOptionNames.UseHybridCLR, BuildCommandLineOptionNames.SkipHybridCLR)]
        [TestCase(BuildCommandLineOptionNames.EnableCheat, BuildCommandLineOptionNames.DisableCheat)]
        [TestCase(BuildCommandLineOptionNames.Clean, BuildCommandLineOptionNames.Incremental)]
        public void Parse_WhenOptionsConflict_Throws(string first, string second)
        {
            string[] arguments =
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                first,
                second
            };

            Assert.Throws<ArgumentException>(() => BuildCommandLine.Parse(arguments));
        }

        [TestCase("-pipelineUnknownProvider")]
        [TestCase("-pipelineDevelopmentt")]
        [TestCase("-pipelineCleann")]
        [TestCase("-pipelineIncrementall")]
        public void Parse_WhenBuildPipelineOptionIsUnknown_Throws(string unknownOption)
        {
            string[] arguments =
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                unknownOption
            };

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => BuildCommandLine.Parse(arguments));

            StringAssert.Contains("Unknown build pipeline option", exception.Message);
        }

        [Test]
        public void Parse_WhenUnityArgumentsArePresent_IgnoresThem()
        {
            string[] arguments =
            {
                "Unity",
                "-batchmode",
                "-projectPath",
                "SomeProject",
                "-debugCodeOptimization",
                "-buildWindows64Player",
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Clean
            };

            BuildCommandLineOptions options = BuildCommandLine.Parse(arguments);

            Assert.That(options.BuildTarget, Is.EqualTo(BuildTarget.StandaloneWindows64));
            Assert.That(options.Incrementality, Is.EqualTo(BuildIncrementality.Clean));
        }

        [Test]
        public void Parse_WithoutIncrementalFlag_DefaultsToCleanMode()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64)
            });

            Assert.That(options.Incrementality, Is.EqualTo(BuildIncrementality.Clean));
        }

        [Test]
        public void Parse_WithFastFlag_UsesIncrementalBuild()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Incremental
            });

            Assert.That(options.Incrementality, Is.EqualTo(BuildIncrementality.Incremental));
        }

        [Test]
        public void Parse_WithExplicitProfileAndBackend_PreservesValues()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Profile,
                "Assets/BuildProfiles/Release.asset",
                BuildCommandLineOptionNames.ScriptingBackend,
                "Mono2x"
            });

            Assert.That(options.BuildProfilePath, Is.EqualTo("Assets/BuildProfiles/Release.asset"));
            Assert.That(options.ScriptingBackend, Is.EqualTo(ScriptingImplementation.Mono2x));
        }

        [TestCase("Win64", BuildTarget.StandaloneWindows64)]
        [TestCase("OSXUniversal", BuildTarget.StandaloneOSX)]
        [TestCase("Linux64", BuildTarget.StandaloneLinux64)]
        [TestCase("Android", BuildTarget.Android)]
        [TestCase("iOS", BuildTarget.iOS)]
        [TestCase("WebGL", BuildTarget.WebGL)]
        [TestCase("StandaloneWindows64", BuildTarget.StandaloneWindows64)]
        public void Parse_AcceptsNativeUnityTargetTokensAndSupportedEnumAliases(
            string token,
            BuildTarget expected)
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                token
            });

            Assert.That(options.BuildTarget, Is.EqualTo(expected));
            Assert.That(
                BuildCommandLine.GetUnityBuildTargetArgument(expected),
                Is.Not.Empty);
        }

        [TestCase("Standalone")]
        [TestCase("Win")]
        [TestCase("999")]
        [TestCase("NoTarget")]
        public void Parse_RejectsAmbiguousUnsupportedOrNumericTargetTokens(string token)
        {
            Assert.Throws<ArgumentException>(() => BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                token
            }));
        }

        [Test]
        public void Parse_AndroidExportForNonAndroidTarget_Throws()
        {
            Assert.Throws<ArgumentException>(() => BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.ExportAndroidProject
            }));
        }

        [Test]
        public void Parse_WhenStepsContainDuplicateIds_Throws()
        {
            string[] arguments =
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Steps,
                "player,PLAYER"
            };

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => BuildCommandLine.Parse(arguments));

            StringAssert.Contains("duplicate step", exception.Message);
        }

        [Test]
        public void Parse_WhenAssetConfigurationHasNoProvider_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                BuildCommandLine.Parse(new[]
                {
                    BuildCommandLineOptionNames.BuildTarget,
                    nameof(BuildTarget.StandaloneWindows64),
                    BuildCommandLineOptionNames.ProviderConfiguration,
                    "Assets/Build/Addressables.asset"
                }));

            StringAssert.Contains(
                $"requires {BuildCommandLineOptionNames.Provider}",
                exception.Message);
        }

        [Test]
        public void Parse_WhenAssetProviderHasNoConfiguration_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                BuildCommandLine.Parse(new[]
                {
                    BuildCommandLineOptionNames.BuildTarget,
                    nameof(BuildTarget.StandaloneWindows64),
                    BuildCommandLineOptionNames.Provider,
                    AssetContentProviderIds.Addressables
                }));

            StringAssert.Contains(
                $"requires {BuildCommandLineOptionNames.ProviderConfiguration}",
                exception.Message);
        }

        [Test]
        public void Parse_WhenAssetProviderAndConfigurationArePaired_PreservesBothValues()
        {
            const string ConfigurationPath = "Assets/Build/Addressables.asset";

            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Provider,
                AssetContentProviderIds.Addressables,
                BuildCommandLineOptionNames.ProviderConfiguration,
                ConfigurationPath
            });

            Assert.That(
                options.AssetContentProviderId,
                Is.EqualTo(AssetContentProviderIds.Addressables));
            Assert.That(options.AssetContentConfigurationPath, Is.EqualTo(ConfigurationPath));
        }

        [Test]
        public void Parse_WhenAssetProviderIsNone_AcceptsMissingConfiguration()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Provider,
                "none"
            });

            Assert.That(options.AssetContentProviderId, Is.EqualTo("none"));
            Assert.That(options.AssetContentConfigurationPath, Is.Null);
        }

        [Test]
        public void Parse_WhenAssetProviderIsNoneAndConfigurationIsSpecified_Throws()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                BuildCommandLine.Parse(new[]
                {
                    BuildCommandLineOptionNames.BuildTarget,
                    nameof(BuildTarget.StandaloneWindows64),
                    BuildCommandLineOptionNames.Provider,
                    "none",
                    BuildCommandLineOptionNames.ProviderConfiguration,
                    "Assets/Build/Addressables.asset"
                }));

            StringAssert.Contains("cannot be used", exception.Message);
        }

        [Test]
        public void Parse_WithVersionInfoPath_PreservesProjectRelativePath()
        {
            const string VersionInfoPath = "Assets/Resources/Build/VersionInfoData.asset";

            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.VersionInfo,
                VersionInfoPath
            });

            Assert.That(options.VersionInfoAssetPath, Is.EqualTo(VersionInfoPath));
        }
    }
}
