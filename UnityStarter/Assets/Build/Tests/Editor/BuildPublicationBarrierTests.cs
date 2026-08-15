using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class BuildPublicationBarrierTests
    {
        private string projectRoot;
        private readonly List<string> order = new List<string>();

        [SetUp]
        public void SetUp()
        {
            order.Clear();
            projectRoot = Path.Combine(
                Path.GetTempPath(),
                "BuildPublicationBarrierTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets", "Resources"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void Finalize_WhenEveryPublicationSucceeds_UsesOneDurableDecisionAndClearsEvidence()
        {
            BuildExecutionContext context = CreateContext();
            var first = new FakePublication(projectRoot, "first", order);
            var second = new FakePublication(projectRoot, "second", order);
            context.RegisterDeferredPublication(first);
            context.RegisterDeferredPublication(second);

            Exception failure = InvokeFinalize(context, null);

            Assert.That(failure, Is.Null);
            Assert.That(order, Is.EqualTo(new[]
            {
                "publish:first",
                "publish:second",
                "complete:first",
                "complete:second",
                "dispose:second",
                "dispose:first"
            }));
            Assert.That(
                Directory.Exists(BuildPublicationBarrier.GetStateRoot(projectRoot)),
                Is.False);
        }

        [Test]
        public void Finalize_WhenLaterPublishFails_RollsBackEveryPreparedPublicationInReverseOrder()
        {
            BuildExecutionContext context = CreateContext();
            var first = new FakePublication(projectRoot, "first", order);
            var second = new FakePublication(
                projectRoot,
                "second",
                order,
                failPublish: true);
            context.RegisterDeferredPublication(first);
            context.RegisterDeferredPublication(second);

            Exception failure = InvokeFinalize(context, null);

            Assert.That(failure, Is.Not.Null);
            StringAssert.Contains("before the terminal decision", failure.ToString());
            Assert.That(order, Is.EqualTo(new[]
            {
                "publish:first",
                "publish:second",
                "dispose:second",
                "dispose:first"
            }));
            Assert.That(
                Directory.Exists(BuildPublicationBarrier.GetStateRoot(projectRoot)),
                Is.False);
            Assert.That(first.HasRecoveryEvidence, Is.False);
            Assert.That(second.HasRecoveryEvidence, Is.False);
        }

        [Test]
        public void Finalize_WhenCommittedCompletionFails_CompletesPeersAndRetainsCommitEvidence()
        {
            BuildExecutionContext context = CreateContext();
            var first = new FakePublication(projectRoot, "first", order);
            var second = new FakePublication(
                projectRoot,
                "second",
                order,
                failComplete: true);
            context.RegisterDeferredPublication(first);
            context.RegisterDeferredPublication(second);

            Exception failure = InvokeFinalize(context, null);

            Assert.That(failure, Is.Not.Null);
            StringAssert.Contains("requires explicit recovery", failure.ToString());
            Assert.That(order, Is.EqualTo(new[]
            {
                "publish:first",
                "publish:second",
                "complete:first",
                "complete:second",
                "dispose:second",
                "dispose:first"
            }));
            Assert.That(
                BuildPublicationBarrier.GetDecision(
                    projectRoot,
                    second.Id,
                    second.RecoveryStateRelativePath),
                Is.EqualTo(BuildPublicationDecision.Commit));
            Assert.That(first.HasRecoveryEvidence, Is.False);
            Assert.That(second.HasRecoveryEvidence, Is.True);

            second.ClearRecoveryEvidence();
            BuildPublicationBarrier.Recover(projectRoot);
            Assert.That(
                Directory.Exists(BuildPublicationBarrier.GetStateRoot(projectRoot)),
                Is.False);
        }

        [Test]
        public void BarrierComplete_WithChildEvidence_FailsClosedAndPreservesDecision()
        {
            var publication = new FakePublication(projectRoot, "single", order);
            BuildPublicationBarrier barrier = BuildPublicationBarrier.Begin(
                projectRoot,
                "test-run",
                new[] { publication });
            publication.Publish();
            barrier.CommitDecision();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => barrier.Complete());

            StringAssert.Contains("still has recovery evidence", exception.Message);
            Assert.That(
                BuildPublicationBarrier.GetDecision(
                    projectRoot,
                    publication.Id,
                    publication.RecoveryStateRelativePath),
                Is.EqualTo(BuildPublicationDecision.Commit));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DurableDecision_DuringAtomicReplacementWindow_PrefersCommittedSequence(
            bool committedCandidateIsTemporary)
        {
            var publication = new FakePublication(projectRoot, "atomic", order);
            BuildPublicationBarrier barrier = BuildPublicationBarrier.Begin(
                projectRoot,
                "atomic-run",
                new[] { publication });
            string journalPath = Path.Combine(
                BuildPublicationBarrier.GetStateRoot(projectRoot),
                "active.json");
            byte[] preparedJournal = File.ReadAllBytes(journalPath);
            publication.Publish();
            barrier.CommitDecision();
            byte[] committedJournal = File.ReadAllBytes(journalPath);

            File.WriteAllBytes(journalPath + ".bak", preparedJournal);
            if (committedCandidateIsTemporary)
            {
                File.Delete(journalPath);
                File.WriteAllBytes(journalPath + ".tmp", committedJournal);
            }

            Assert.That(
                barrier.ReadDurableDecision(),
                Is.EqualTo(BuildPublicationDecision.Commit));
            Assert.That(
                BuildPublicationBarrier.GetDecision(
                    projectRoot,
                    publication.Id,
                    publication.RecoveryStateRelativePath),
                Is.EqualTo(BuildPublicationDecision.Commit));

            publication.Complete();
            barrier.Complete();
            Assert.That(
                Directory.Exists(BuildPublicationBarrier.GetStateRoot(projectRoot)),
                Is.False);
        }

        [Test]
        public void BarrierComplete_WithArbitraryLockExtension_FailsClosed()
        {
            var publication = new FakePublication(projectRoot, "lock-policy", order);
            BuildPublicationBarrier barrier = BuildPublicationBarrier.Begin(
                projectRoot,
                "lock-policy-run",
                new[] { publication });
            barrier.CommitDecision();
            string childStateRoot = Path.Combine(
                projectRoot,
                publication.RecoveryStateRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            string unexpectedLock = Path.Combine(childStateRoot, "unexpected.lock");
            File.WriteAllText(unexpectedLock, "owner");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => barrier.Complete());

            StringAssert.Contains("still has recovery evidence", exception.Message);
            File.Delete(unexpectedLock);
            barrier.Complete();
        }

        [Test]
        public void Barrier_WithInvocationScopedNestedStatePath_CompletesNormally()
        {
            var publication = new FakePublication(
                projectRoot,
                "nested",
                order,
                recoveryStateRelativePath:
                ".buildpipeline/transactions/test-provider/nested");
            BuildPublicationBarrier barrier = BuildPublicationBarrier.Begin(
                projectRoot,
                "nested-run",
                new[] { publication });

            publication.Publish();
            barrier.CommitDecision();
            publication.Complete();
            barrier.Complete();

            Assert.That(
                Directory.Exists(BuildPublicationBarrier.GetStateRoot(projectRoot)),
                Is.False);
        }

        [Test]
        public void Context_AllowsMoreThanSixteenIndependentPublications()
        {
            BuildExecutionContext context = CreateContext();
            for (int index = 0; index < 17; index++)
            {
                context.RegisterDeferredPublication(new FakePublication(
                    projectRoot,
                    "publication-" + index,
                    order));
            }

            Assert.That(context.DeferredPublications, Has.Count.EqualTo(17));
        }

        [Test]
        public void Context_ExclusiveOutputClaims_AllowIndependentSiblingRoots()
        {
            BuildExecutionContext context = CreateContext();
            context.RegisterExclusiveOutputPaths(
                "content-base",
                new[] { Path.Combine(projectRoot, "Build", "Base") });

            Assert.DoesNotThrow(() => context.RegisterExclusiveOutputPaths(
                "content-dlc",
                new[] { Path.Combine(projectRoot, "Build", "Dlc") }));
        }

        [Test]
        public void Context_ExclusiveOutputClaims_AreIdempotentForOneInvocation()
        {
            BuildExecutionContext context = CreateContext();
            string output = Path.Combine(projectRoot, "Build", "Content");
            context.RegisterExclusiveOutputPaths("content-base", new[] { output });

            Assert.DoesNotThrow(() => context.RegisterExclusiveOutputPaths(
                "content-base",
                new[] { output }));
        }

        [Test]
        public void Context_ExclusiveOutputClaims_RejectCrossInvocationAncestryOverlap()
        {
            BuildExecutionContext context = CreateContext();
            string baseRoot = Path.Combine(projectRoot, "Build", "Content");
            context.RegisterExclusiveOutputPaths(
                "content-base",
                new[] { baseRoot });

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => context.RegisterExclusiveOutputPaths(
                    "content-dlc",
                    new[] { Path.Combine(baseRoot, "Nested") }));

            StringAssert.Contains("content-base", exception.Message);
            StringAssert.Contains("content-dlc", exception.Message);
            StringAssert.Contains("overlaps", exception.Message);
        }

        [Test]
        public void Context_ExclusiveOutputClaims_RejectPortableCaseAliases()
        {
            BuildExecutionContext context = CreateContext();
            context.RegisterExclusiveOutputPaths(
                "content-base",
                new[] { Path.Combine(projectRoot, "Build", "Content") });

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => context.RegisterExclusiveOutputPaths(
                    "content-dlc",
                    new[] { Path.Combine(projectRoot, "build", "content") }));

            StringAssert.Contains("overlaps", exception.Message);
        }

        private BuildExecutionContext CreateContext()
        {
            string buildRoot = Path.Combine(projectRoot, "Build");
            string outputDirectory = Path.Combine(buildRoot, "Windows", "Release");
            var request = new BuildRequest(
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
                debugBuild: false,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: false,
                applicationVersion: "0.1.0",
                identityOverride: BuildIdentityOverride.Empty,
                steps: Array.Empty<BuildStepInvocation>(),
                sourceCleanlinessPolicy: BuildSourceCleanlinessPolicy.RequireClean,
                purpose: BuildPurpose.Release);
            return new BuildExecutionContext(request, "test-run", new NoOpEventSink());
        }

        private static Exception InvokeFinalize(
            BuildExecutionContext context,
            Exception failure)
        {
            MethodInfo method = typeof(BuildPipelineRunner).GetMethod(
                "FinalizeDeferredPublications",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (Exception)method.Invoke(null, new object[] { context, failure });
        }

        private sealed class FakePublication : IBuildDeferredPublication
        {
            private readonly string evidencePath;
            private readonly IList<string> order;
            private readonly bool failPublish;
            private readonly bool failComplete;
            private bool terminalDecisionObserved;

            public FakePublication(
                string projectRoot,
                string id,
                IList<string> order,
                bool failPublish = false,
                bool failComplete = false,
                string recoveryStateRelativePath = null)
            {
                Id = id;
                RecoveryStateRelativePath = recoveryStateRelativePath
                    ?? ".buildpipeline/transactions/test-" + id;
                this.order = order;
                this.failPublish = failPublish;
                this.failComplete = failComplete;
                string stateRoot = Path.Combine(
                    projectRoot,
                    RecoveryStateRelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(stateRoot);
                evidencePath = Path.Combine(stateRoot, "active.json");
            }

            public string Id { get; }
            public string RecoveryStateRelativePath { get; }
            public bool HasRecoveryEvidence => File.Exists(evidencePath);

            public void Publish()
            {
                order.Add("publish:" + Id);
                if (failPublish)
                {
                    throw new InvalidOperationException("publish failed");
                }

                File.WriteAllText(evidencePath, "published");
            }

            public void Complete()
            {
                order.Add("complete:" + Id);
                terminalDecisionObserved = true;
                if (failComplete)
                {
                    throw new InvalidOperationException("complete failed");
                }

                ClearRecoveryEvidence();
            }

            public void Dispose()
            {
                order.Add("dispose:" + Id);
                if (!terminalDecisionObserved)
                {
                    ClearRecoveryEvidence();
                }
            }

            public void ClearRecoveryEvidence()
            {
                if (File.Exists(evidencePath))
                {
                    File.Delete(evidencePath);
                }
            }
        }

        private sealed class NoOpEventSink : IBuildEventSink
        {
            public void RunStarted(
                BuildExecutionContext context,
                IReadOnlyList<CompiledBuildStep> plan) { }

            public void StepStarted(BuildExecutionContext context, CompiledBuildStep step) { }
            public void StepFinished(BuildExecutionContext context, BuildStepResult result) { }
            public void RunFinished(BuildExecutionContext context, BuildRunResult result) { }
        }
    }
}
