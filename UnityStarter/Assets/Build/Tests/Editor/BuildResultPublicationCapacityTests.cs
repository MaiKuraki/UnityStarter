using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.TestTools;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class BuildResultPublicationCapacityTests
    {
        private string projectRoot;

        [SetUp]
        public void SetUp()
        {
            projectRoot = Path.Combine(
                Path.GetTempPath(),
                "BuildResultPublicationCapacityTests-" +
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(
                Path.Combine(projectRoot, "Assets", "Resources"));
            Directory.CreateDirectory(
                Path.Combine(projectRoot, "ProjectSettings"));
            ThrowLongCompletionBuildStep.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            ThrowLongCompletionBuildStep.Reset();
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void CapacityGate_WhenEnvelopeExceedsLimit_RollsBackBeforePublishAndWritesNothing()
        {
            BuildExecutionContext context = CreateContext(Array.Empty<BuildStepInvocation>());
            var order = new List<string>();
            var publication = new TrackingPublication("capacity", order);
            context.RegisterDeferredPublication(publication);
            var provisional = CreateResult(context, succeeded: true, failure: null);
            context.SealForPublication();
            BuildResultManifestSnapshot snapshot =
                BuildResultManifestWriter.FreezeForPublication(
                    context,
                    provisional);

            IOException capacityFailure = Assert.Throws<IOException>(() =>
                BuildResultManifestWriter.ValidatePublicationCapacity(
                    snapshot,
                    snapshot.WorstCaseByteCount - 1));
            Exception terminalFailure = InvokeFinalize(context, capacityFailure);

            Assert.That(terminalFailure, Is.Not.Null);
            Assert.That(order, Is.EqualTo(new[] { "dispose:capacity" }));
            Assert.That(publication.PublishCount, Is.Zero);
            Assert.That(File.Exists(provisional.ResultManifestPath), Is.False);
            Assert.That(File.Exists(provisional.ResultManifestPath + ".tmp"), Is.False);
        }

        [Test]
        public void CapacityGate_AtExactWorstCaseBoundary_SucceedsWithoutWriting()
        {
            BuildExecutionContext context = CreateContext(Array.Empty<BuildStepInvocation>());
            BuildRunResult provisional = CreateResult(
                context,
                succeeded: true,
                failure: null);
            context.SealForPublication();
            BuildResultManifestSnapshot snapshot =
                BuildResultManifestWriter.FreezeForPublication(
                    context,
                    provisional);

            Assert.DoesNotThrow(() =>
                BuildResultManifestWriter.ValidatePublicationCapacity(
                    snapshot,
                    snapshot.WorstCaseByteCount));
            Assert.That(snapshot.CapacityValidated, Is.True);
            Assert.That(File.Exists(provisional.ResultManifestPath), Is.False);
            Assert.That(File.Exists(provisional.ResultManifestPath + ".tmp"), Is.False);
        }

        [Test]
        public void Context_AfterPublicationSeal_RejectsEvidenceAndPublicationMutation()
        {
            BuildExecutionContext context = CreateContext(Array.Empty<BuildStepInvocation>());
            context.SealForPublication();
            AssetContentBuildResult content = AssetContentBuildResult.Success(
                "test-provider",
                "package",
                "1.0.0");

            Assert.Throws<InvalidOperationException>(() =>
                context.AddContentResult("content", content));
            Assert.Throws<InvalidOperationException>(() =>
                context.RegisterDeferredPublication(
                    new TrackingPublication("sealed", new List<string>())));
            Assert.Throws<InvalidOperationException>(() =>
                context.RegisterExclusiveOutputPaths(
                    "sealed",
                    new[] { Path.Combine(projectRoot, "Build", "sealed") }));
            Assert.Throws<InvalidOperationException>(() => context.Version = null);
            Assert.Throws<InvalidOperationException>(() =>
                context.PlayerBuildReport = null);
            Assert.Throws<InvalidOperationException>(() =>
                context.SetPlan(Array.Empty<CompiledBuildStep>()));
            Assert.Throws<InvalidOperationException>(() =>
                context.SetRecipeProvenance(
                    Array.Empty<BuildRecipeProvenanceEntry>()));
            Assert.Throws<InvalidOperationException>(() =>
                context.SetPlayerExtensionFingerprint(new string('a', 64)));
        }

        [Test]
        public void EvidencePolicy_LongControlText_IsDeterministicallySummarized()
        {
            const string shortText = "short\u0001text";
            Assert.That(
                BuildResultEvidencePolicy.NormalizeDiagnosticText(shortText),
                Is.EqualTo(shortText));
            string longText = new string(
                '\u0001',
                BuildResultEvidencePolicy.MaximumDiagnosticCharacters + 128);

            string first =
                BuildResultEvidencePolicy.NormalizeDiagnosticText(longText);
            string second =
                BuildResultEvidencePolicy.NormalizeDiagnosticText(longText);

            Assert.That(first, Is.EqualTo(second));
            Assert.That(
                first.Length,
                Is.LessThanOrEqualTo(
                    BuildResultEvidencePolicy.MaximumDiagnosticCharacters));
            Assert.That(first, Does.Contain("[truncated"));
            Assert.That(first, Does.Contain("sha256="));
        }

        [Test]
        public void ContentResult_WhenProviderEvidenceExceedsBudgets_FailsBeforeContextMutation()
        {
            var warnings = new string[
                BuildResultEvidencePolicy.MaximumContentWarningCount + 1];
            for (int index = 0; index < warnings.Length; index++)
            {
                warnings[index] = "warning";
            }

            Assert.Throws<InvalidOperationException>(() =>
                AssetContentBuildResult.Success(
                    "test-provider",
                    "package",
                    "1.0.0",
                    warnings: warnings));

            BuildExecutionContext context = CreateContext(Array.Empty<BuildStepInvocation>());
            string evidence = new string('e', 250 * 1024);
            AssetContentBuildResult bounded = AssetContentBuildResult.Failure(
                "test-provider",
                "package",
                "1.0.0",
                "task",
                evidence);
            int accepted = (int)(
                BuildResultEvidencePolicy.MaximumContentRunUtf8Bytes /
                bounded.EvidenceUtf8Bytes);
            for (int index = 0; index < accepted; index++)
            {
                context.AddContentResult("content", bounded);
            }

            Assert.Throws<InvalidOperationException>(() =>
                context.AddContentResult("content", bounded));
            Assert.That(context.ContentResults, Has.Count.EqualTo(accepted));
        }

        [Test]
        public void Runner_PostCommitLongControlFailure_WritesAndConfirmsBoundedEvidence()
        {
            var invocation = new BuildStepInvocation(
                ThrowLongCompletionBuildStep.StepTypeIdValue,
                ThrowLongCompletionBuildStep.StepTypeIdValue);
            BuildRequest request = CreateRequest(new[] { invocation });
            BuildResultEvidenceSession evidence =
                BuildResultEvidenceSession.Begin(projectRoot, "build");
            using (evidence)
            {
                LogAssert.Expect(
                    LogType.Error,
                    new Regex(
                        @"\[BuildPipeline\] Run .+ failed\.[\s\S]*\[truncated chars="));
                BuildRunResult result = new BuildPipelineRunner(
                        evidence.CreateEventSink(),
                        projectRoot,
                        () => false)
                    .Run(request, evidence.RunId, evidence.ManifestPath);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Failure, Is.Not.Null);
                Assert.That(
                    ThrowLongCompletionBuildStep.Publication.PublishCount,
                    Is.EqualTo(1));
                Assert.That(File.Exists(result.ResultManifestPath), Is.True);
                TerminalManifest manifest = JsonUtility.FromJson<TerminalManifest>(
                    File.ReadAllText(result.ResultManifestPath));
                Assert.That(
                    manifest.failure.Length,
                    Is.LessThanOrEqualTo(
                        BuildResultEvidencePolicy.MaximumDiagnosticCharacters));
                Assert.That(manifest.failure, Does.Contain("[truncated"));
                Assert.DoesNotThrow(() => evidence.ConfirmTerminalManifest(result));
                Assert.That(evidence.HasValidatedTerminalManifest, Is.True);
            }

            Assert.That(evidence.TerminalEvidenceConfirmed, Is.True);
        }

        private BuildExecutionContext CreateContext(
            IReadOnlyList<BuildStepInvocation> steps)
        {
            return new BuildExecutionContext(
                CreateRequest(steps),
                "capacity-test-run",
                new NoOpEventSink());
        }

        private BuildRequest CreateRequest(
            IReadOnlyList<BuildStepInvocation> steps)
        {
            string buildRoot = Path.Combine(projectRoot, "Build");
            string outputDirectory = Path.Combine(
                buildRoot,
                "Windows",
                "Release");
            return new BuildRequest(
                "TestCompany",
                "TestProduct",
                "com.example.test",
                "Assets/Resources/VersionInfoData.asset",
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
                debugBuild: true,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: false,
                applicationVersion: "0.1.0",
                identityOverride: BuildIdentityOverride.Empty,
                steps: steps);
        }

        private static BuildRunResult CreateResult(
            BuildExecutionContext context,
            bool succeeded,
            Exception failure)
        {
            return new BuildRunResult(
                context.RunId,
                succeeded,
                context.Request.OutputPath,
                BuildResultManifestWriter.GetManifestPath(
                    context.Request,
                    context.RunId),
                Array.Empty<BuildStepResult>(),
                failure);
        }

        private static Exception InvokeFinalize(
            BuildExecutionContext context,
            Exception failure)
        {
            MethodInfo method = typeof(BuildPipelineRunner).GetMethod(
                "FinalizeDeferredPublications",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (Exception)method.Invoke(
                null,
                new object[] { context, failure });
        }

        [Serializable]
        private sealed class TerminalManifest
        {
            public string failure = string.Empty;
        }

        private sealed class TrackingPublication : IBuildDeferredPublication
        {
            private readonly IList<string> order;

            internal TrackingPublication(string id, IList<string> order)
            {
                Id = id;
                this.order = order;
            }

            public string Id { get; }
            public string RecoveryStateRelativePath =>
                ".buildpipeline/transactions/capacity-" + Id;
            internal int PublishCount { get; private set; }

            public void Publish()
            {
                PublishCount++;
                order.Add("publish:" + Id);
            }

            public void Complete()
            {
                order.Add("complete:" + Id);
            }

            public void Dispose()
            {
                order.Add("dispose:" + Id);
            }
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
        ThrowLongCompletionBuildStep.StepTypeIdValue,
        HiddenFromAuthoring = true)]
    public sealed class ThrowLongCompletionBuildStep : IBuildStep
    {
        public const string StepTypeIdValue =
            "build-pipeline-tests.long-completion-failure";

        public static LongCompletionFailurePublication Publication { get; private set; }

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
            Publication = new LongCompletionFailurePublication();
            context.RegisterDeferredPublication(Publication);
        }

        internal static void Reset()
        {
            Publication = null;
        }
    }

    public sealed class LongCompletionFailurePublication : IBuildDeferredPublication
    {
        public string Id => "build-pipeline-tests.long-completion-publication";
        public string RecoveryStateRelativePath =>
            ".buildpipeline/transactions/test-long-completion-publication";
        public int PublishCount { get; private set; }

        public void Publish()
        {
            PublishCount++;
        }

        public void Complete()
        {
            throw new InvalidOperationException(
                new string(
                    '\u0001',
                    BuildResultEvidencePolicy.MaximumDiagnosticCharacters * 4));
        }

        public void Dispose()
        {
        }
    }
}
