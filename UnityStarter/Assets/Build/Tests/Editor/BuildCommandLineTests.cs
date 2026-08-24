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
        [TestCase(BuildCommandLineOptionNames.BuildNumber)]
        [TestCase(BuildCommandLineOptionNames.SourceProvider)]
        [TestCase(BuildCommandLineOptionNames.SourceRevision)]
        [TestCase(BuildCommandLineOptionNames.SourceBranch)]
        [TestCase(BuildCommandLineOptionNames.CiProvider)]
        [TestCase(BuildCommandLineOptionNames.CiRunId)]
        [TestCase(BuildCommandLineOptionNames.Recipe)]
        [TestCase(BuildCommandLineOptionNames.Selection)]
        [TestCase(BuildCommandLineOptionNames.StepConfiguration)]
        [TestCase(BuildCommandLineOptionNames.StepIncrementality)]
        [TestCase(BuildCommandLineOptionNames.StepDependency)]
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
                BuildCommandLineOptionNames.Development
            };

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => BuildCommandLine.Parse(arguments));

            StringAssert.Contains(
                $"'{BuildCommandLineOptionNames.Output}' requires a value",
                exception.Message);
        }

        [Test]
        public void Parse_WhenNonRepeatableOptionUsesDifferentCasing_Throws()
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

        [Test]
        public void Parse_WhenCheatFlagsConflict_Throws()
        {
            Assert.Throws<ArgumentException>(() => BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.EnableCheat,
                BuildCommandLineOptionNames.DisableCheat
            }));
        }

        [TestCase("-pipelineUnknown")]
        [TestCase("-pipelineUnexpectedFlag")]
        public void Parse_WhenPipelineOptionIsUnknown_Throws(string unknownOption)
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                BuildCommandLine.Parse(new[]
                {
                    BuildCommandLineOptionNames.BuildTarget,
                    nameof(BuildTarget.StandaloneWindows64),
                    unknownOption
                }));

            StringAssert.Contains("Unknown build pipeline option", exception.Message);
        }

        [Test]
        public void Parse_WhenUnityArgumentsArePresent_IgnoresThem()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                "Unity",
                "-batchmode",
                "-projectPath",
                "SomeProject",
                "-debugCodeOptimization",
                "-buildWindows64Player",
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64)
            });

            Assert.That(options.BuildTarget, Is.EqualTo(BuildTarget.StandaloneWindows64));
            Assert.That(options.RecipeInvocations, Is.Empty);
        }

        [Test]
        public void Parse_RecoverOnly_DoesNotRequireBuildTargetOrProfile()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.RecoverOnly
            });

            Assert.That(options.RecoverOnly, Is.True);
            Assert.That(options.BuildTarget, Is.EqualTo(BuildTarget.NoTarget));
            Assert.That(options.BuildProfilePath, Is.Null);
        }

        [Test]
        public void Parse_RecoverOnly_AllowsNativeBuildTargetForUnityStartupSelection()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.RecoverOnly,
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.Android)
            });

            Assert.That(options.RecoverOnly, Is.True);
            Assert.That(options.BuildTarget, Is.EqualTo(BuildTarget.Android));
        }

        [Test]
        public void Parse_RecoverOnly_RejectsBuildOptions()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                BuildCommandLine.Parse(new[]
                {
                    BuildCommandLineOptionNames.RecoverOnly,
                    BuildCommandLineOptionNames.Profile,
                    "Assets/BuildProfiles/Release.asset"
                }));

            StringAssert.Contains("cannot be combined", exception.Message);
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
            Assert.That(BuildCommandLine.GetUnityBuildTargetArgument(expected), Is.Not.Empty);
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
        public void Parse_ReplaceExactVersion_IsParsedAsFlag()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.ReplaceExactVersion
            });

            Assert.That(options.ReplaceExactVersion, Is.True);
        }

        [Test]
        public void Parse_WithoutReplaceExactVersion_DefaultsToFalse()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64)
            });

            Assert.That(options.ReplaceExactVersion, Is.False);
        }

        [Test]
        public void Parse_Recipe_AllowsRepeatedStepTypeWithDistinctInvocationIds()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Recipe,
                "base-content=" + BuildStepTypeIds.AssetContent,
                BuildCommandLineOptionNames.Recipe,
                "dlc-content=" + BuildStepTypeIds.AssetContent
            });

            Assert.That(options.RecipeInvocations.Count, Is.EqualTo(2));
            Assert.That(options.RecipeInvocations[0].InvocationId, Is.EqualTo("base-content"));
            Assert.That(options.RecipeInvocations[1].InvocationId, Is.EqualTo("dlc-content"));
            Assert.That(
                options.RecipeInvocations[0].StepTypeId,
                Is.EqualTo(options.RecipeInvocations[1].StepTypeId));
        }

        [Test]
        public void Parse_Recipe_RejectsDuplicateInvocationIds()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                BuildCommandLine.Parse(new[]
                {
                    BuildCommandLineOptionNames.BuildTarget,
                    nameof(BuildTarget.StandaloneWindows64),
                    BuildCommandLineOptionNames.Recipe,
                    "content-a=" + BuildStepTypeIds.AssetContent,
                    BuildCommandLineOptionNames.Recipe,
                    "content-a=" + BuildStepTypeIds.Player
                }));

            StringAssert.Contains("specified more than once", exception.Message);
        }

        [Test]
        public void Parse_ProfileSelection_PreservesDistinctInvocationIds()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Profile,
                "Assets/BuildProfiles/Release.asset",
                BuildCommandLineOptionNames.Selection,
                "content-base",
                BuildCommandLineOptionNames.Selection,
                "content-dlc"
            });

            Assert.That(
                options.SelectedInvocationIds,
                Is.EqualTo(new[] { "content-base", "content-dlc" }));
        }

        [Test]
        public void Parse_ProfileSelection_RequiresExplicitProfile()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                BuildCommandLine.Parse(new[]
                {
                    BuildCommandLineOptionNames.BuildTarget,
                    nameof(BuildTarget.StandaloneWindows64),
                    BuildCommandLineOptionNames.Selection,
                    "content"
                }));

            StringAssert.Contains(BuildCommandLineOptionNames.Profile, exception.Message);
        }

        [Test]
        public void Parse_ProfileSelection_RejectsDuplicateIdsAndRecipeReplacement()
        {
            ArgumentException duplicate = Assert.Throws<ArgumentException>(() =>
                BuildCommandLine.Parse(new[]
                {
                    BuildCommandLineOptionNames.BuildTarget,
                    nameof(BuildTarget.StandaloneWindows64),
                    BuildCommandLineOptionNames.Profile,
                    "Assets/BuildProfiles/Release.asset",
                    BuildCommandLineOptionNames.Selection,
                    "content",
                    BuildCommandLineOptionNames.Selection,
                    "content"
                }));

            StringAssert.Contains("specified more than once", duplicate.Message);

            ArgumentException conflict = Assert.Throws<ArgumentException>(() =>
                BuildCommandLine.Parse(new[]
                {
                    BuildCommandLineOptionNames.BuildTarget,
                    nameof(BuildTarget.StandaloneWindows64),
                    BuildCommandLineOptionNames.Profile,
                    "Assets/BuildProfiles/Release.asset",
                    BuildCommandLineOptionNames.Selection,
                    "content",
                    BuildCommandLineOptionNames.Recipe,
                    "content=" + BuildStepTypeIds.AssetContent
                }));

            StringAssert.Contains("cannot be combined", conflict.Message);
        }

        [TestCase(BuildCommandLineOptionNames.Recipe, "missing-assignment")]
        [TestCase(BuildCommandLineOptionNames.StepConfiguration, "Assets/Build/Config.asset")]
        [TestCase(BuildCommandLineOptionNames.StepIncrementality, "content")]
        [TestCase(BuildCommandLineOptionNames.StepDependency, "content")]
        public void Parse_WhenInvocationAssignmentIsMalformed_Throws(string option, string value)
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                BuildCommandLine.Parse(new[]
                {
                    BuildCommandLineOptionNames.BuildTarget,
                    nameof(BuildTarget.StandaloneWindows64),
                    option,
                    value
                }));

            StringAssert.Contains("<key>=<value>", exception.Message);
        }

        [Test]
        public void Parse_StepConfiguration_IsKeyedByInvocationId()
        {
            const string BasePath = "Assets/Build/BaseContent.asset";
            const string DlcPath = "Assets/Build/DlcContent.asset";
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.StepConfiguration,
                "base-content=" + BasePath,
                BuildCommandLineOptionNames.StepConfiguration,
                "dlc-content=" + DlcPath
            });

            Assert.That(options.StepConfigurationPathOverrides["base-content"], Is.EqualTo(BasePath));
            Assert.That(options.StepConfigurationPathOverrides["dlc-content"], Is.EqualTo(DlcPath));
        }

        [Test]
        public void Parse_StepIncrementality_RejectsUnknownOrNumericValues()
        {
            foreach (string invalid in new[] { "Fast", "1" })
            {
                Assert.Throws<ArgumentException>(() => BuildCommandLine.Parse(new[]
                {
                    BuildCommandLineOptionNames.BuildTarget,
                    nameof(BuildTarget.StandaloneWindows64),
                    BuildCommandLineOptionNames.StepIncrementality,
                    "content=" + invalid
                }));
            }
        }

        [Test]
        public void Parse_RecipePolicyAndDependencies_PreserveInvocationOwnedValues()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Recipe,
                "content=" + BuildStepTypeIds.AssetContent,
                BuildCommandLineOptionNames.Recipe,
                "player=" + BuildStepTypeIds.Player,
                BuildCommandLineOptionNames.StepIncrementality,
                "content=Incremental",
                BuildCommandLineOptionNames.StepDependency,
                "player=Required:content",
                BuildCommandLineOptionNames.StepDependency,
                "player=IfSelected:hot-update"
            });

            Assert.That(
                options.StepIncrementalityOverrides["content"],
                Is.EqualTo(BuildIncrementality.Incremental));
            Assert.That(options.StepDependencyOverrides["player"].Count, Is.EqualTo(2));
            Assert.That(
                options.StepDependencyOverrides["player"][0].Mode,
                Is.EqualTo(BuildDependencyMode.Required));
            Assert.That(
                options.StepDependencyOverrides["player"][0].InvocationId,
                Is.EqualTo("content"));
            Assert.That(
                options.StepDependencyOverrides["player"][1].Mode,
                Is.EqualTo(BuildDependencyMode.IfSelected));
        }

        [TestCase("player=Required")]
        [TestCase("player=:content")]
        [TestCase("player=Optional:content")]
        [TestCase("player=1:content")]
        public void Parse_StepDependency_RejectsMalformedMode(string value)
        {
            Assert.Throws<ArgumentException>(() => BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.StepDependency,
                value
            }));
        }

        [Test]
        public void Parse_StepOverrides_RejectDuplicateOwnerOrDependency()
        {
            Assert.Throws<ArgumentException>(() => BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.StepIncrementality,
                "content=Clean",
                BuildCommandLineOptionNames.StepIncrementality,
                "CONTENT=Incremental"
            }));

            Assert.Throws<ArgumentException>(() => BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.StepDependency,
                "player=Required:content",
                BuildCommandLineOptionNames.StepDependency,
                "PLAYER=IfSelected:CONTENT"
            }));
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

        [Test]
        public void Parse_WithExplicitBuildIdentity_PreservesValidatedProvenance()
        {
            BuildCommandLineOptions options = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.BuildNumber,
                "42",
                BuildCommandLineOptionNames.SourceProvider,
                "git",
                BuildCommandLineOptionNames.SourceRevision,
                "0123456789abcdef",
                BuildCommandLineOptionNames.SourceBranch,
                "release/1.0",
                BuildCommandLineOptionNames.CiProvider,
                "teamcity",
                BuildCommandLineOptionNames.CiRunId,
                "build-42"
            });

            Assert.That(options.IdentityOverride.BuildNumber, Is.EqualTo(42));
            Assert.That(options.IdentityOverride.SourceProvider, Is.EqualTo("git"));
            Assert.That(options.IdentityOverride.SourceRevision, Is.EqualTo("0123456789abcdef"));
            Assert.That(options.IdentityOverride.SourceBranch, Is.EqualTo("release/1.0"));
            Assert.That(options.IdentityOverride.CiProvider, Is.EqualTo("teamcity"));
            Assert.That(options.IdentityOverride.CiRunId, Is.EqualTo("build-42"));
        }

        [TestCase("0")]
        [TestCase("2147483648")]
        [TestCase("+1")]
        [TestCase("1.0")]
        public void Parse_WithInvalidBuildNumber_Throws(string value)
        {
            Assert.Catch<ArgumentException>(() => BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.BuildNumber,
                value
            }));
        }

        [Test]
        public void Parse_WithPartialProvenanceGroup_Throws()
        {
            Assert.Throws<ArgumentException>(() => BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.SourceProvider,
                "git"
            }));

            Assert.Throws<ArgumentException>(() => BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.CiProvider,
                "jenkins"
            }));
        }
    }
}
