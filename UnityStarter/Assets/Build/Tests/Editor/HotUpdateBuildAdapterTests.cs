using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class HotUpdateBuildAdapterTests
    {
        private readonly List<ScriptableObject> configurations =
            new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < configurations.Count; index++)
            {
                UnityEngine.Object.DestroyImmediate(configurations[index]);
            }

            configurations.Clear();
            RecordingHotUpdateBuildAdapter.Reset();
        }

        [Test]
        public void GenericStep_DelegatesToOneInvocationScopedAdapterInstance()
        {
            RecordingHotUpdateBuildAdapter.Reset();
            RecordingHotUpdateBuildConfiguration configuration =
                CreateConfiguration<RecordingHotUpdateBuildConfiguration>();
            BuildStepInvocation invocation = CreateInvocation(
                "tests-hot-recording",
                configuration);
            BuildExecutionContext context = CreateContext(
                new[] { invocation },
                cheatEnabled: false);
            var step = new HotUpdateBuildStep();

            IReadOnlyList<string> errors = step.Validate(context, invocation);
            BuildStepRequirements requirements =
                step.GetRequirements(context, invocation);
            step.Execute(context, invocation);

            Assert.That(errors, Is.Empty);
            Assert.That(
                requirements,
                Is.EqualTo(BuildStepRequirements.VersionInfoAsset));
            Assert.That(RecordingHotUpdateBuildAdapter.InstanceCount, Is.EqualTo(1));
            Assert.That(RecordingHotUpdateBuildAdapter.ValidateCount, Is.EqualTo(1));
            Assert.That(RecordingHotUpdateBuildAdapter.RequirementsCount, Is.EqualTo(1));
            Assert.That(RecordingHotUpdateBuildAdapter.ExecuteCount, Is.EqualTo(1));
            Assert.That(
                context.ResolveHotUpdateAdapter(invocation),
                Is.SameAs(context.ResolveHotUpdateAdapter(invocation)));
        }

        [Test]
        public void GenericStep_WhenProviderIsMissing_FailsClosedDuringPreflight()
        {
            MissingHotUpdateBuildConfiguration configuration =
                CreateConfiguration<MissingHotUpdateBuildConfiguration>();
            BuildStepInvocation invocation = CreateInvocation(
                "tests-hot-missing",
                configuration);
            BuildExecutionContext context = CreateContext(
                new[] { invocation },
                cheatEnabled: false);

            IReadOnlyList<string> errors =
                new HotUpdateBuildStep().Validate(context, invocation);

            Assert.That(
                errors.Any(error => error.Contains(
                    "No compatible 'tests-hot-missing' hot-update adapter")),
                Is.True);
        }

        [Test]
        public void GenericStep_WhenProviderConfigurationTypeDoesNotMatch_FailsClosed()
        {
            MismatchedHotUpdateBuildConfiguration configuration =
                CreateConfiguration<MismatchedHotUpdateBuildConfiguration>();
            BuildStepInvocation invocation = CreateInvocation(
                "tests-hot-mismatch",
                configuration);
            BuildExecutionContext context = CreateContext(
                new[] { invocation },
                cheatEnabled: false);

            IReadOnlyList<string> errors =
                new HotUpdateBuildStep().Validate(context, invocation);

            Assert.That(
                errors.Any(error =>
                    error.Contains("expects ExpectedHotUpdateBuildConfiguration")
                    && error.Contains("MismatchedHotUpdateBuildConfiguration")),
                Is.True);
        }

        [Test]
        public void GenericStep_AllowsMultipleProviderInvocations()
        {
            RecordingHotUpdateBuildConfiguration firstConfiguration =
                CreateConfiguration<RecordingHotUpdateBuildConfiguration>();
            RecordingHotUpdateBuildConfiguration secondConfiguration =
                CreateConfiguration<RecordingHotUpdateBuildConfiguration>();
            BuildStepInvocation first = CreateInvocation(
                "tests-hot-first",
                firstConfiguration);
            BuildStepInvocation second = CreateInvocation(
                "tests-hot-second",
                secondConfiguration);
            BuildExecutionContext context = CreateContext(
                new[] { first, second },
                cheatEnabled: false);

            IReadOnlyList<CompiledBuildStep> plan =
                BuildPlanCompiler.Compile(context);

            Assert.That(plan.Count, Is.EqualTo(2));
            Assert.That(
                plan.All(compiled => compiled.Step is HotUpdateBuildStep),
                Is.True);
            Assert.That(RecordingHotUpdateBuildAdapter.InstanceCount, Is.EqualTo(2));
        }

        [Test]
        public void HybridCLRAdapter_RejectsMultipleGlobalGenerationInvocations()
        {
            HybridCLRBuildConfig firstConfiguration =
                CreateConfiguration<HybridCLRBuildConfig>();
            HybridCLRBuildConfig secondConfiguration =
                CreateConfiguration<HybridCLRBuildConfig>();
            BuildStepInvocation first = CreateInvocation(
                "hybridclr-first",
                firstConfiguration);
            BuildStepInvocation second = CreateInvocation(
                "hybridclr-second",
                secondConfiguration);
            BuildExecutionContext context = CreateContext(
                new[] { first, second },
                cheatEnabled: false);

            IReadOnlyList<string> errors = new HybridCLRBuildAdapter().Validate(
                new HotUpdateBuildRequest(context, first, firstConfiguration));

            Assert.That(
                errors.Any(error => error.Contains(
                    "one invocation per run")),
                Is.True);
        }

        [Test]
        public void ProviderCatalog_MapsExplicitProductionConfigurationsToAdapters()
        {
            IReadOnlyList<HotUpdateProviderDescriptor> descriptors =
                HotUpdateBuildAdapterRegistry.GetProviderDescriptors();

            HotUpdateProviderDescriptor hybridClr = descriptors.Single(
                descriptor => descriptor.ProviderId ==
                              HybridCLRHotUpdateProviderIds.Standard);
            HotUpdateProviderDescriptor hybridClrObfuz = descriptors.Single(
                descriptor => descriptor.ProviderId ==
                              HybridCLRHotUpdateProviderIds.Obfuz);

            Assert.That(
                hybridClr.ConfigurationType,
                Is.EqualTo(typeof(HybridCLRBuildConfig)));
            Assert.That(
                hybridClr.AdapterType,
                Is.EqualTo(typeof(HybridCLRBuildAdapter)));
            Assert.That(
                hybridClrObfuz.ConfigurationType,
                Is.EqualTo(typeof(HybridCLRObfuzBuildConfig)));
            Assert.That(
                hybridClrObfuz.AdapterType,
                Is.EqualTo(typeof(HybridCLRObfuzBuildAdapter)));

            HotUpdateProviderAuthoringAttribute combinedRegistration =
                (HotUpdateProviderAuthoringAttribute)Attribute.GetCustomAttribute(
                    typeof(HybridCLRObfuzBuildConfig),
                    typeof(HotUpdateProviderAuthoringAttribute),
                    inherit: false);
            CollectionAssert.AreEqual(
                new[]
                {
                    "HybridCLR.Editor.Commands.PrebuildCommand",
                    "Obfuz.Settings.ObfuzSettings",
                    "Obfuz4HybridCLR.PrebuildCommandExt"
                },
                combinedRegistration.RequiredEditorTypeNames);
            Assert.That(
                hybridClrObfuz.DependencyAvailable,
                Is.EqualTo(combinedRegistration.RequiredEditorTypeNames.All(
                    typeName => ReflectionCache.GetType(typeName) != null)));
        }

        [Test]
        public void PlayerValidation_DoesNotApplyHybridCLRPolicyToAnotherProvider()
        {
            RecordingHotUpdateBuildConfiguration configuration =
                CreateConfiguration<RecordingHotUpdateBuildConfiguration>();
            BuildStepInvocation hotUpdateInvocation = CreateInvocation(
                "tests-hot-recording",
                configuration);
            var playerInvocation = new BuildStepInvocation(
                "tests-player",
                BuildStepTypeIds.Player,
                dependencies: new[]
                {
                    new BuildInvocationDependency(hotUpdateInvocation.InvocationId)
                });
            BuildExecutionContext context = CreateContext(
                new[] { hotUpdateInvocation, playerInvocation },
                cheatEnabled: true);
            context.SetPlan(new[]
            {
                new CompiledBuildStep(
                    hotUpdateInvocation,
                    new HotUpdateBuildStep(),
                    isApplicable: true),
                new CompiledBuildStep(
                    playerInvocation,
                    new PlayerBuildStep(),
                    isApplicable: true)
            });

            IReadOnlyList<string> errors =
                new PlayerBuildStep().Validate(context, playerInvocation);

            Assert.That(
                errors.Any(error => error.Contains("HybridCLR")),
                Is.False);
            Assert.That(
                errors.Any(error => error.Contains("per-build ENABLE_CHEAT")),
                Is.False);
        }

        private T CreateConfiguration<T>() where T : ScriptableObject
        {
            T configuration = ScriptableObject.CreateInstance<T>();
            configurations.Add(configuration);
            return configuration;
        }

        private static BuildStepInvocation CreateInvocation(
            string invocationId,
            HotUpdateBuildConfiguration configuration,
            IReadOnlyList<BuildInvocationDependency> dependencies = null)
        {
            return new BuildStepInvocation(
                invocationId,
                BuildStepTypeIds.HotUpdate,
                configuration,
                BuildIncrementality.Clean,
                dependencies);
        }

        internal static BuildExecutionContext CreateContext(
            IReadOnlyList<BuildStepInvocation> invocations,
            bool cheatEnabled)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string buildRoot = Path.Combine(projectRoot, "Build", "Tests");
            string outputDirectory = Path.Combine(buildRoot, "Player");
            var request = new BuildRequest(
                "TestCompany",
                "TestProduct",
                "com.example.tests",
                "Assets/Build/Runtime/Resources/VersionInfoData.asset",
                Array.Empty<string>(),
                cheatEnabled ? CheatBuildMode.Enabled : CheatBuildMode.Disabled,
                BuildTarget.StandaloneWindows64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP,
                projectRoot,
                buildRoot,
                Path.Combine(outputDirectory, "TestProduct.exe"),
                outputDirectory,
                outputIsFolder: false,
                deleteDebugFiles: true,
                debugBuild: false,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: false,
                applicationVersion: "1.0.0",
                identityOverride: BuildIdentityOverride.Empty,
                steps: invocations,
                sourceCleanlinessPolicy: BuildSourceCleanlinessPolicy.RequireClean,
                purpose: BuildPurpose.Release);
            return new BuildExecutionContext(
                request,
                "tests-hot-update-run",
                new TestEventSink());
        }

        private sealed class TestEventSink : IBuildEventSink
        {
            public void RunStarted(
                BuildExecutionContext context,
                IReadOnlyList<CompiledBuildStep> plan)
            {
            }

            public void StepStarted(
                BuildExecutionContext context,
                CompiledBuildStep step)
            {
            }

            public void StepFinished(
                BuildExecutionContext context,
                BuildStepResult result)
            {
            }

            public void RunFinished(
                BuildExecutionContext context,
                BuildRunResult result)
            {
            }
        }
    }

    public sealed class RecordingHotUpdateBuildConfiguration :
        HotUpdateBuildConfiguration
    {
        public override string ProviderId => "tests-hot-recording";
    }

    public sealed class MissingHotUpdateBuildConfiguration :
        HotUpdateBuildConfiguration
    {
        public override string ProviderId => "tests-hot-missing";
    }

    public sealed class MismatchedHotUpdateBuildConfiguration :
        HotUpdateBuildConfiguration
    {
        public override string ProviderId => "tests-hot-mismatch";
    }

    public sealed class ExpectedHotUpdateBuildConfiguration :
        HotUpdateBuildConfiguration
    {
        public override string ProviderId => "tests-hot-mismatch";
    }

    [HotUpdateAdapterRegistration(
        "tests-hot-recording",
        typeof(RecordingHotUpdateBuildConfiguration))]
    public sealed class RecordingHotUpdateBuildAdapter : IHotUpdateBuildAdapter
    {
        public RecordingHotUpdateBuildAdapter()
        {
            InstanceCount++;
        }

        public static int InstanceCount { get; private set; }
        public static int RequirementsCount { get; private set; }
        public static int ValidateCount { get; private set; }
        public static int ExecuteCount { get; private set; }

        public string ProviderId => "tests-hot-recording";
        public Type ConfigurationType =>
            typeof(RecordingHotUpdateBuildConfiguration);

        public BuildStepRequirements GetRequirements(HotUpdateBuildRequest request)
        {
            RequirementsCount++;
            return BuildStepRequirements.VersionInfoAsset;
        }

        public IReadOnlyList<string> Validate(HotUpdateBuildRequest request)
        {
            ValidateCount++;
            return Array.Empty<string>();
        }

        public void Execute(HotUpdateBuildRequest request)
        {
            ExecuteCount++;
        }

        public static void Reset()
        {
            InstanceCount = 0;
            RequirementsCount = 0;
            ValidateCount = 0;
            ExecuteCount = 0;
        }
    }

    [HotUpdateAdapterRegistration(
        "tests-hot-mismatch",
        typeof(ExpectedHotUpdateBuildConfiguration))]
    public sealed class MismatchedHotUpdateBuildAdapter : IHotUpdateBuildAdapter
    {
        public string ProviderId => "tests-hot-mismatch";
        public Type ConfigurationType =>
            typeof(ExpectedHotUpdateBuildConfiguration);

        public BuildStepRequirements GetRequirements(HotUpdateBuildRequest request)
        {
            return BuildStepRequirements.None;
        }

        public IReadOnlyList<string> Validate(HotUpdateBuildRequest request)
        {
            return Array.Empty<string>();
        }

        public void Execute(HotUpdateBuildRequest request)
        {
        }
    }
}
