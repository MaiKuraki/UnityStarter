using System;
using System.Collections.Generic;
using System.IO;
using Build.Pipeline.Editor;
using Build.VersionControl.Editor;
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
            SourceRevalidationBuildStep.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            MutateFollowingConfigurationBuildStep.Reset();
            MutateBeforePublicationBuildStep.Reset();
            SourceRevalidationBuildStep.Reset();

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

        [Test]
        public void Runner_ReleaseSourceBecomesDirtyBeforePublication_DoesNotPublish()
        {
            BuildRequest request = CreateRequest(
                new BuildStepInvocation(
                    "source-revalidation",
                    SourceRevalidationBuildStep.StepTypeIdValue));
            BuildVersionContext initial = CreateVersion("source-a", CreateCleanWorkspace());
            BuildVersionContext terminal = CreateVersion("source-a", CreateDirtyWorkspace());
            int resolverCalls = 0;

            BuildRunResult result = Run(
                request,
                _ => resolverCalls++ == 0 ? initial : terminal);
            TrackingSourceQualificationPublication publication =
                SourceRevalidationBuildStep.Publication;

            Assert.That(result.Succeeded, Is.False);
            Assert.That(resolverCalls, Is.EqualTo(2));
            Assert.That(publication.PublishCount, Is.Zero);
            Assert.That(publication.CompleteCount, Is.Zero);
            Assert.That(publication.DisposeCount, Is.EqualTo(1));
            Assert.That(publication.SuspendCount, Is.EqualTo(1));
            Assert.That(publication.ResumeCount, Is.EqualTo(1));
            StringAssert.Contains(
                "Source workspace qualification changed before terminal publication",
                result.Failure?.ToString());
            SourceManifestDocument manifest = ReadSourceManifest(result);
            Assert.That(manifest.sourceWorkspace.overallStatus, Is.EqualTo("Dirty"));
            Assert.That(manifest.effectiveIdentity.sourceRevision, Is.EqualTo("source-a"));
        }

        [Test]
        public void Runner_ReleaseRevisionChangesBeforePublication_DoesNotPublish()
        {
            BuildRequest request = CreateRequest(
                new BuildStepInvocation(
                    "source-revalidation",
                    SourceRevalidationBuildStep.StepTypeIdValue));
            BuildVersionContext initial = CreateVersion("source-a", CreateCleanWorkspace());
            BuildVersionContext terminal = CreateVersion("source-b", CreateCleanWorkspace());
            int resolverCalls = 0;

            BuildRunResult result = Run(
                request,
                _ => resolverCalls++ == 0 ? initial : terminal);
            TrackingSourceQualificationPublication publication =
                SourceRevalidationBuildStep.Publication;

            Assert.That(result.Succeeded, Is.False);
            Assert.That(resolverCalls, Is.EqualTo(2));
            Assert.That(publication.PublishCount, Is.Zero);
            Assert.That(publication.CompleteCount, Is.Zero);
            Assert.That(publication.DisposeCount, Is.EqualTo(1));
            Assert.That(publication.SuspendCount, Is.EqualTo(1));
            Assert.That(publication.ResumeCount, Is.EqualTo(1));
            StringAssert.Contains(
                "detected source revision changed",
                result.Failure?.ToString());
            SourceManifestDocument manifest = ReadSourceManifest(result);
            Assert.That(manifest.effectiveIdentity.sourceRevision, Is.EqualTo("source-a"));
        }

        [Test]
        public void Runner_ReleaseDetectedBranchChangesBeforePublication_DoesNotPublish()
        {
            BuildRequest request = CreateRequest(
                new BuildStepInvocation(
                    "source-revalidation",
                    SourceRevalidationBuildStep.StepTypeIdValue));
            BuildVersionContext initial = CreateVersion(
                "source-a",
                CreateCleanWorkspace(),
                detectedBranch: "main");
            BuildVersionContext terminal = CreateVersion(
                "source-a",
                CreateCleanWorkspace(),
                detectedBranch: "release");
            int resolverCalls = 0;

            BuildRunResult result = Run(
                request,
                _ => resolverCalls++ == 0 ? initial : terminal);
            TrackingSourceQualificationPublication publication =
                SourceRevalidationBuildStep.Publication;

            Assert.That(result.Succeeded, Is.False);
            Assert.That(resolverCalls, Is.EqualTo(2));
            Assert.That(publication.PublishCount, Is.Zero);
            Assert.That(publication.CompleteCount, Is.Zero);
            Assert.That(publication.DisposeCount, Is.EqualTo(1));
            Assert.That(publication.SuspendCount, Is.EqualTo(1));
            Assert.That(publication.ResumeCount, Is.EqualTo(1));
            StringAssert.Contains(
                "detected source branch changed",
                result.Failure?.ToString());
        }

        [Test]
        public void SourceQualificationSuspensionScope_AcquiresReverseAndResumesOriginalOrder()
        {
            var events = new List<string>();
            var first = new OrderedSourceQualificationPublication("first", events);
            var second = new OrderedSourceQualificationPublication("second", events);
            var third = new OrderedSourceQualificationPublication("third", events);

            using (BuildSourceQualificationSuspensionScope.Begin(
                       new IBuildDeferredPublication[] { first, second, third }))
            {
                CollectionAssert.AreEqual(
                    new[]
                    {
                        "suspend:third",
                        "suspend:second",
                        "suspend:first"
                    },
                    events);
            }

            CollectionAssert.AreEqual(
                new[]
                {
                    "suspend:third",
                    "suspend:second",
                    "suspend:first",
                    "resume:first",
                    "resume:second",
                    "resume:third"
                },
                events);
        }

        [Test]
        public void Runner_ReleaseTerminalSourceCaptureFails_RecordsUnknownAndDoesNotPublish()
        {
            BuildRequest request = CreateRequest(
                new BuildStepInvocation(
                    "source-revalidation",
                    SourceRevalidationBuildStep.StepTypeIdValue));
            BuildVersionContext initial = CreateVersion("source-a", CreateCleanWorkspace());
            int resolverCalls = 0;

            BuildRunResult result = Run(
                request,
                _ => resolverCalls++ == 0
                    ? initial
                    : throw new InvalidOperationException("terminal capture failed"));
            TrackingSourceQualificationPublication publication =
                SourceRevalidationBuildStep.Publication;

            Assert.That(result.Succeeded, Is.False);
            Assert.That(resolverCalls, Is.EqualTo(2));
            Assert.That(publication.PublishCount, Is.Zero);
            Assert.That(publication.CompleteCount, Is.Zero);
            Assert.That(publication.DisposeCount, Is.EqualTo(1));
            Assert.That(publication.SuspendCount, Is.EqualTo(1));
            Assert.That(publication.ResumeCount, Is.EqualTo(1));
            SourceManifestDocument manifest = ReadSourceManifest(result);
            Assert.That(manifest.sourceWorkspace.overallStatus, Is.EqualTo("Unknown"));
            Assert.That(
                manifest.sourceWorkspace.failureCode,
                Is.EqualTo(VersionControlWorkspaceEvidence.CommandFailed));
            Assert.That(manifest.effectiveIdentity.sourceRevision, Is.EqualTo("source-a"));
        }

        [Test]
        public void Runner_ReleaseSourceRemainsStable_PublishesAfterSecondSnapshot()
        {
            BuildRequest request = CreateRequest(
                new BuildStepInvocation(
                    "source-revalidation",
                    SourceRevalidationBuildStep.StepTypeIdValue));
            BuildVersionContext version = CreateVersion(
                "source-a",
                CreateCleanWorkspace());
            int resolverCalls = 0;

            BuildRunResult result = Run(
                request,
                _ =>
                {
                    resolverCalls++;
                    if (resolverCalls == 2)
                    {
                        Assert.That(
                            SourceRevalidationBuildStep.Publication.IsSuspended,
                            Is.True,
                            "Terminal source capture must run while transaction-owned downstream inputs are suspended.");
                    }

                    return version;
                });
            TrackingSourceQualificationPublication publication =
                SourceRevalidationBuildStep.Publication;

            Assert.That(result.Succeeded, Is.True);
            Assert.That(resolverCalls, Is.EqualTo(2));
            Assert.That(publication.PublishCount, Is.EqualTo(1));
            Assert.That(publication.CompleteCount, Is.EqualTo(1));
            Assert.That(publication.DisposeCount, Is.EqualTo(1));
            Assert.That(publication.SuspendCount, Is.EqualTo(1));
            Assert.That(publication.ResumeCount, Is.EqualTo(1));
        }

        [Test]
        public void Runner_LocalDirtyDevelopment_DoesNotCaptureTerminalSourceSnapshot()
        {
            BuildRequest request = CreateRequest(
                debugBuild: true,
                batchMode: false,
                BuildSourceCleanlinessPolicy.AllowDirtyDevelopment,
                new BuildStepInvocation(
                    "source-revalidation",
                    SourceRevalidationBuildStep.StepTypeIdValue));
            BuildVersionContext version = CreateVersion(
                "source-a",
                VersionControlWorkspaceEvidence.Unknown(
                    VersionControlWorkspaceEvidence.CommandTimedOut));
            int resolverCalls = 0;

            BuildRunResult result = Run(
                request,
                _ =>
                {
                    resolverCalls++;
                    if (resolverCalls > 1)
                    {
                        throw new InvalidOperationException(
                            "Local dirty Development must not request terminal source qualification.");
                    }

                    return version;
                });
            TrackingSourceQualificationPublication publication =
                SourceRevalidationBuildStep.Publication;

            Assert.That(result.Succeeded, Is.True);
            Assert.That(resolverCalls, Is.EqualTo(1));
            Assert.That(publication.PublishCount, Is.EqualTo(1));
            Assert.That(publication.CompleteCount, Is.EqualTo(1));
            Assert.That(publication.DisposeCount, Is.EqualTo(1));
            Assert.That(publication.SuspendCount, Is.Zero);
            Assert.That(publication.ResumeCount, Is.Zero);
        }

        private BuildRunResult Run(BuildRequest request)
        {
            return Run(request, BuildTestVersionResolver.ResolveClean);
        }

        private BuildRunResult Run(
            BuildRequest request,
            Func<BuildRequest, BuildVersionContext> versionResolver)
        {
            BuildRunResult result = new BuildPipelineRunner(
                    new NoOpEventSink(),
                    GetProjectRoot(),
                    () => false,
                    versionResolver)
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
            return CreateRequest(
                debugBuild: false,
                batchMode: false,
                BuildSourceCleanlinessPolicy.RequireClean,
                steps);
        }

        private static BuildRequest CreateRequest(
            bool debugBuild,
            bool batchMode,
            BuildSourceCleanlinessPolicy policy,
            params BuildStepInvocation[] steps)
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
                debugBuild: debugBuild,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: batchMode,
                applicationVersion: "1.0.0",
                identityOverride: BuildIdentityOverride.Empty,
                steps: steps,
                sourceCleanlinessPolicy: policy,
                purpose: debugBuild ? BuildPurpose.Development : BuildPurpose.Release);
        }

        private static BuildVersionContext CreateVersion(
            string sourceRevision,
            VersionControlWorkspaceEvidence workspace,
            string detectedBranch = null)
        {
            return new BuildVersionContext(
                "1.0.0",
                "1.0.0.42",
                42,
                sourceRevision,
                "42",
                "main",
                "2026-08-12T00:00:00Z",
                "Git",
                sourceWorkspace: workspace,
                detectedBranch: detectedBranch);
        }

        private static VersionControlWorkspaceEvidence CreateCleanWorkspace()
        {
            return CreateWorkspace(VersionControlWorkspaceComponentStatus.Clean, 0);
        }

        private static VersionControlWorkspaceEvidence CreateDirtyWorkspace()
        {
            return CreateWorkspace(VersionControlWorkspaceComponentStatus.Dirty, 1);
        }

        private static VersionControlWorkspaceEvidence CreateWorkspace(
            VersionControlWorkspaceComponentStatus trackedStatus,
            int trackedCount)
        {
            var tracked = new VersionControlWorkspaceComponentEvidence(
                trackedStatus,
                trackedCount);
            var clean = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.Clean,
                0);
            var notApplicable = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.NotApplicable,
                0);
            return new VersionControlWorkspaceEvidence(
                tracked,
                clean,
                notApplicable,
                notApplicable);
        }

        private static SourceManifestDocument ReadSourceManifest(BuildRunResult result)
        {
            return JsonUtility.FromJson<SourceManifestDocument>(
                File.ReadAllText(result.ResultManifestPath));
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

        [Serializable]
        private sealed class SourceManifestDocument
        {
            public SourceIdentityDocument effectiveIdentity = null;
            public SourceWorkspaceDocument sourceWorkspace = null;
        }

        [Serializable]
        private sealed class SourceIdentityDocument
        {
            public string sourceRevision = null;
        }

        [Serializable]
        private sealed class SourceWorkspaceDocument
        {
            public string overallStatus = null;
            public string failureCode = null;
        }

        private sealed class OrderedSourceQualificationPublication
            : IBuildSourceQualificationPublication
        {
            private readonly string name;
            private readonly List<string> events;

            internal OrderedSourceQualificationPublication(
                string name,
                List<string> events)
            {
                this.name = name;
                this.events = events;
            }

            public string Id => "test-source-qualification:" + name;
            public string RecoveryStateRelativePath =>
                ".buildpipeline/transactions/test-source-qualification-" + name;

            public void ActivateForDownstream()
            {
            }

            public IDisposable SuspendForSourceQualification()
            {
                events.Add("suspend:" + name);
                return new OrderedSuspension(name, events);
            }

            public void Publish()
            {
            }

            public void Complete()
            {
            }

            public void Dispose()
            {
            }

            private sealed class OrderedSuspension : IDisposable
            {
                private readonly string name;
                private readonly List<string> events;
                private bool disposed;

                internal OrderedSuspension(
                    string name,
                    List<string> events)
                {
                    this.name = name;
                    this.events = events;
                }

                public void Dispose()
                {
                    if (disposed)
                    {
                        return;
                    }

                    disposed = true;
                    events.Add("resume:" + name);
                }
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

    public sealed class TrackingSourceQualificationPublication
        : IBuildSourceQualificationPublication
    {
        private bool activated;
        private bool suspended;
        private bool disposed;

        public string Id =>
            "build-pipeline-tests.source-qualification-publication";
        public string RecoveryStateRelativePath =>
            ".buildpipeline/transactions/test-source-qualification-publication";
        public int PublishCount { get; private set; }
        public int CompleteCount { get; private set; }
        public int DisposeCount { get; private set; }
        public int SuspendCount { get; private set; }
        public int ResumeCount { get; private set; }
        public bool IsSuspended => suspended;

        public void ActivateForDownstream()
        {
            ThrowIfDisposed();
            if (activated)
            {
                throw new InvalidOperationException(
                    "Test publication is already active.");
            }

            activated = true;
        }

        public IDisposable SuspendForSourceQualification()
        {
            ThrowIfDisposed();
            if (!activated || suspended)
            {
                throw new InvalidOperationException(
                    "Test publication is not available for source qualification.");
            }

            suspended = true;
            SuspendCount++;
            return new Suspension(this);
        }

        public void Publish()
        {
            ThrowIfDisposed();
            if (!activated || suspended)
            {
                throw new InvalidOperationException(
                    "Test publication is not publication-ready.");
            }

            PublishCount++;
        }

        public void Complete()
        {
            ThrowIfDisposed();
            if (suspended)
            {
                throw new InvalidOperationException(
                    "Test publication cannot complete while suspended.");
            }

            CompleteCount++;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            suspended = false;
            DisposeCount++;
        }

        private void Resume()
        {
            ThrowIfDisposed();
            if (!suspended)
            {
                throw new InvalidOperationException(
                    "Test publication is not suspended.");
            }

            suspended = false;
            ResumeCount++;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(TrackingSourceQualificationPublication));
            }
        }

        private sealed class Suspension : IDisposable
        {
            private TrackingSourceQualificationPublication owner;

            internal Suspension(
                TrackingSourceQualificationPublication owner)
            {
                this.owner = owner;
            }

            public void Dispose()
            {
                TrackingSourceQualificationPublication current = owner;
                owner = null;
                current?.Resume();
            }
        }
    }

    [BuildStepRegistration(
        SourceRevalidationBuildStep.StepTypeIdValue,
        HiddenFromAuthoring = true)]
    public sealed class SourceRevalidationBuildStep : IBuildStep
    {
        public const string StepTypeIdValue =
            "build-pipeline-tests.source-revalidation";

        public static TrackingSourceQualificationPublication Publication
        {
            get;
            private set;
        }

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
            Publication = new TrackingSourceQualificationPublication();
            context.RegisterDeferredPublication(Publication);
            Publication.ActivateForDownstream();
        }

        public static void Reset()
        {
            Publication = null;
        }
    }
}
