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
    public sealed class BuildRecipeProvenanceInvariantTests
    {
        private const string AssetPathPrefix =
            "Assets/Build/Tests/Editor/BuildRecipeProvenanceInvariant-";

        private readonly List<string> createdAssetPaths = new List<string>();
        private readonly List<string> resultManifestPaths = new List<string>();

        [SetUp]
        public void SetUp()
        {
            MutateFollowingConfigurationBuildStep.Reset();
            MutateBeforePublicationBuildStep.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            MutateFollowingConfigurationBuildStep.Reset();
            MutateBeforePublicationBuildStep.Reset();

            for (int index = 0; index < createdAssetPaths.Count; index++)
            {
                AssetDatabase.DeleteAsset(createdAssetPaths[index]);
            }
            createdAssetPaths.Clear();

            for (int index = 0; index < resultManifestPaths.Count; index++)
            {
                string path = resultManifestPaths[index];
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            resultManifestPaths.Clear();
        }

        [Test]
        public void Runner_WhenEarlierStepChangesLaterConfiguration_FailsBeforeConsumerExecutes()
        {
            MutableProvenanceBuildConfiguration configuration =
                CreatePersistentConfiguration("initial");
            MutateFollowingConfigurationBuildStep.Target = configuration;
            var request = CreateRequest(
                new BuildStepInvocation(
                    "mutator",
                    MutateFollowingConfigurationBuildStep.StepTypeIdValue),
                new BuildStepInvocation(
                    "consumer",
                    ObserveConfigurationBuildStep.StepTypeIdValue,
                    configuration,
                    dependencies: new[]
                    {
                        new BuildInvocationDependency(
                            "mutator",
                            BuildDependencyMode.Required)
                    }));

            BuildRunResult result = Run(request);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(MutateFollowingConfigurationBuildStep.Executed, Is.True);
            Assert.That(ObserveConfigurationBuildStep.Executed, Is.False);
            StringAssert.Contains(
                "provenance changed after preflight",
                result.Failure?.ToString());
            StringAssert.Contains("consumer", result.Failure?.ToString());
        }

        [Test]
        public void Runner_WhenStepChangesConfigurationBeforeTerminalDecision_DoesNotPublish()
        {
            MutableProvenanceBuildConfiguration configuration =
                CreatePersistentConfiguration("initial");
            var request = CreateRequest(
                new BuildStepInvocation(
                    "terminal-mutator",
                    MutateBeforePublicationBuildStep.StepTypeIdValue,
                    configuration));

            BuildRunResult result = Run(request);
            TrackingDeferredPublication publication =
                MutateBeforePublicationBuildStep.Publication;

            Assert.That(result.Succeeded, Is.False);
            Assert.That(publication, Is.Not.Null);
            Assert.That(publication.PublishCount, Is.Zero);
            Assert.That(publication.CompleteCount, Is.Zero);
            Assert.That(publication.DisposeCount, Is.EqualTo(1));
            StringAssert.Contains(
                "provenance changed after preflight",
                result.Failure?.ToString());
            StringAssert.Contains("terminal publication", result.Failure?.ToString());
        }

        private BuildRunResult Run(BuildRequest request)
        {
            BuildRunResult result = new BuildPipelineRunner(
                    new NoOpEventSink(),
                    GetProjectRoot(),
                    () => false)
                .Run(request);
            resultManifestPaths.Add(result.ResultManifestPath);
            return result;
        }

        private MutableProvenanceBuildConfiguration CreatePersistentConfiguration(
            string value)
        {
            string assetPath = AssetPathPrefix + Guid.NewGuid().ToString("N") + ".asset";
            var configuration =
                ScriptableObject.CreateInstance<MutableProvenanceBuildConfiguration>();
            configuration.SetValue(value);
            AssetDatabase.CreateAsset(configuration, assetPath);
            AssetDatabase.SaveAssetIfDirty(configuration);
            createdAssetPaths.Add(assetPath);
            Assert.That(EditorUtility.IsDirty(configuration), Is.False);
            return configuration;
        }

        private static BuildRequest CreateRequest(params BuildStepInvocation[] steps)
        {
            string projectRoot = GetProjectRoot();
            string buildRoot = Path.Combine(
                projectRoot,
                "Build",
                ".buildpipeline-tests",
                "provenance-invariant",
                Guid.NewGuid().ToString("N"));
            string outputDirectory = Path.Combine(buildRoot, "Windows", "Release");
            return new BuildRequest(
                "TestCompany",
                "TestProduct",
                "com.example.provenance",
                "Assets/Build/Runtime/Resources/VersionInfoData.asset",
                Array.Empty<string>(),
                CheatBuildMode.Disabled,
                BuildTarget.StandaloneWindows64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.Mono2x,
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
                steps: steps);
        }

        private static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private sealed class NoOpEventSink : IBuildEventSink
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

    [BuildStepRegistration(
        MutateFollowingConfigurationBuildStep.StepTypeIdValue,
        HiddenFromAuthoring = true)]
    public sealed class MutateFollowingConfigurationBuildStep : IBuildStep
    {
        public const string StepTypeIdValue =
            "build-pipeline-tests.mutate-following-configuration";

        public static MutableProvenanceBuildConfiguration Target { get; set; }
        public static bool Executed { get; private set; }

        public string StepTypeId => StepTypeIdValue;

        public bool IsApplicable(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            return true;
        }

        public IReadOnlyList<string> Validate(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            return Array.Empty<string>();
        }

        public void Execute(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            Executed = true;
            PersistChange(Target, "changed-before-consumer");
        }

        public static void Reset()
        {
            Target = null;
            Executed = false;
            ObserveConfigurationBuildStep.Reset();
        }

        internal static void PersistChange(
            MutableProvenanceBuildConfiguration configuration,
            string value)
        {
            if (configuration == null)
            {
                throw new InvalidOperationException(
                    "A mutable provenance test configuration is required.");
            }

            configuration.SetValue(value);
            EditorUtility.SetDirty(configuration);
            AssetDatabase.SaveAssetIfDirty(configuration);
        }
    }

    [BuildStepRegistration(
        ObserveConfigurationBuildStep.StepTypeIdValue,
        HiddenFromAuthoring = true,
        ConfigurationType = typeof(MutableProvenanceBuildConfiguration),
        ConfigurationRequired = true)]
    public sealed class ObserveConfigurationBuildStep : IBuildStep
    {
        public const string StepTypeIdValue =
            "build-pipeline-tests.observe-configuration";

        public static bool Executed { get; private set; }

        public string StepTypeId => StepTypeIdValue;

        public bool IsApplicable(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            return true;
        }

        public IReadOnlyList<string> Validate(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            return Array.Empty<string>();
        }

        public void Execute(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            Executed = true;
        }

        internal static void Reset()
        {
            Executed = false;
        }
    }

    [BuildStepRegistration(
        MutateBeforePublicationBuildStep.StepTypeIdValue,
        HiddenFromAuthoring = true,
        ConfigurationType = typeof(MutableProvenanceBuildConfiguration),
        ConfigurationRequired = true)]
    public sealed class MutateBeforePublicationBuildStep : IBuildStep
    {
        public const string StepTypeIdValue =
            "build-pipeline-tests.mutate-before-publication";

        public static TrackingDeferredPublication Publication { get; private set; }

        public string StepTypeId => StepTypeIdValue;

        public bool IsApplicable(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            return true;
        }

        public IReadOnlyList<string> Validate(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            return Array.Empty<string>();
        }

        public void Execute(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            Publication = new TrackingDeferredPublication();
            context.RegisterDeferredPublication(Publication);
            MutateFollowingConfigurationBuildStep.PersistChange(
                invocation.GetRequiredConfiguration<MutableProvenanceBuildConfiguration>(),
                "changed-before-terminal-publication");
        }

        public static void Reset()
        {
            Publication = null;
        }
    }

    public sealed class TrackingDeferredPublication : IBuildDeferredPublication
    {
        public string Id => "build-pipeline-tests.provenance-publication";
        public string RecoveryStateRelativePath =>
            ".buildpipeline/transactions/test-provenance-publication";
        public int PublishCount { get; private set; }
        public int CompleteCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void Publish()
        {
            PublishCount++;
        }

        public void Complete()
        {
            CompleteCount++;
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
