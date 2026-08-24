using System;
using System.IO;
using Build.Pipeline.Editor;
using Build.VersionControl.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class BuildVersionResolverTests
    {
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, false)]
        public void Resolve_WhenReliableMetadataIsRequiredAndProviderIsMissing_Fails(
            bool batchMode,
            bool debugBuild)
        {
            BuildRequest request = CreateRequest(batchMode, debugBuild);

            Assert.Throws<BuildFailedException>(
                () => BuildVersionResolver.Resolve(request, null));
        }

        [Test]
        public void Resolve_InteractiveDevelopmentWithoutProvider_UsesExplicitLocalMetadata()
        {
            BuildVersionContext result = BuildVersionResolver.Resolve(
                CreateRequest(batchMode: false, debugBuild: true),
                null);

            Assert.That(result.ProviderId, Is.EqualTo("LocalDevelopment"));
            Assert.That(result.PackageVersion, Is.EqualTo("1.0.0.9000001"));
            Assert.That(result.CommitHash, Is.EqualTo("local"));
            Assert.That(result.IdentityOrigin, Is.EqualTo(BuildIdentityOrigin.LocalDevelopment));
        }

        [Test]
        public void Resolve_LocalReleasePreviewWithoutProvider_UsesDistinctLocalPreviewMetadata()
        {
            BuildVersionContext result = BuildVersionResolver.Resolve(
                CreateRequest(
                    batchMode: false,
                    debugBuild: false,
                    purpose: BuildPurpose.LocalReleasePreview),
                null);

            Assert.That(result.ProviderId, Is.EqualTo("LocalPreview"));
            Assert.That(result.PackageVersion, Is.EqualTo("1.0.0.9000002"));
            Assert.That(result.CommitHash, Is.EqualTo("local-preview"));
            Assert.That(result.IdentityOrigin, Is.EqualTo(BuildIdentityOrigin.LocalPreview));
            Assert.That(result.SourceWorkspace.IsVerifiedClean, Is.False);
        }

        [Test]
        public void Resolve_CapturesExactlyOneProviderSnapshot()
        {
            VersionControlWorkspaceEvidence workspace = CreateWorkspace(
                VersionControlWorkspaceComponentStatus.Clean);
            var provider = new FakeProvider(
                new VersionControlMetadata(
                    "TestVcs",
                    "abcdef123456",
                    "42",
                    "main",
                    "2026-08-02T00:00:00Z",
                    workspace));

            BuildVersionContext result = BuildVersionResolver.Resolve(
                CreateRequest(batchMode: true, debugBuild: false),
                provider);

            Assert.That(provider.CaptureCount, Is.EqualTo(1));
            Assert.That(result.ProviderId, Is.EqualTo("TestVcs"));
            Assert.That(result.PackageVersion, Is.EqualTo("1.0.0.42"));
            Assert.That(result.DetectedProviderId, Is.EqualTo("TestVcs"));
            Assert.That(result.EffectiveSourceRevision, Is.EqualTo("abcdef123456"));
            Assert.That(result.IdentityOrigin, Is.EqualTo(BuildIdentityOrigin.VersionControl));
            Assert.That(result.SourceWorkspace, Is.SameAs(workspace));
        }

        [Test]
        public void SourceWorkspacePolicy_RequiredDirtyWorkspaceFailsWithOnlyAggregateEvidence()
        {
            VersionControlWorkspaceEvidence workspace = CreateWorkspace(
                VersionControlWorkspaceComponentStatus.Dirty,
                changeCount: 3);
            BuildRequest request = CreateRequest(
                batchMode: false,
                debugBuild: false,
                sourceCleanlinessPolicy: BuildSourceCleanlinessPolicy.RequireClean);
            BuildVersionContext version = CreateVersion(workspace);

            BuildFailedException exception = Assert.Throws<BuildFailedException>(
                () => BuildSourceWorkspacePolicy.EnsureAllowed(request, version));

            StringAssert.Contains("tracked=Dirty(3)", exception.Message);
            StringAssert.DoesNotContain("Assets/", exception.Message);
            StringAssert.DoesNotContain("secret", exception.Message);
        }

        [Test]
        public void SourceWorkspacePolicy_ExplicitDirtyDevelopmentExceptionAllowsUnknownWorkspace()
        {
            BuildRequest request = CreateRequest(
                batchMode: false,
                debugBuild: true,
                sourceCleanlinessPolicy:
                    BuildSourceCleanlinessPolicy.AllowDirtyDevelopment);
            BuildVersionContext version = CreateVersion(
                VersionControlWorkspaceEvidence.Unknown(
                    VersionControlWorkspaceEvidence.CommandTimedOut));

            Assert.DoesNotThrow(
                () => BuildSourceWorkspacePolicy.EnsureAllowed(request, version));
            Assert.That(request.RequireCleanSource, Is.False);
        }

        [Test]
        public void SourceWorkspacePolicy_BatchDevelopmentCannotUseLocalDirtyException()
        {
            BuildRequest request = CreateRequest(
                batchMode: true,
                debugBuild: true,
                sourceCleanlinessPolicy:
                    BuildSourceCleanlinessPolicy.AllowDirtyDevelopment);
            BuildVersionContext version = CreateVersion(
                VersionControlWorkspaceEvidence.Unknown(
                    VersionControlWorkspaceEvidence.CommandTimedOut));

            Assert.That(request.RequireCleanSource, Is.True);
            Assert.Throws<BuildFailedException>(
                () => BuildSourceWorkspacePolicy.EnsureAllowed(request, version));
        }

        [TestCase(false, false, BuildSourceCleanlinessPolicy.RequireClean, true)]
        [TestCase(false, false, BuildSourceCleanlinessPolicy.AllowDirtyDevelopment, true)]
        [TestCase(false, true, BuildSourceCleanlinessPolicy.RequireClean, true)]
        [TestCase(false, true, BuildSourceCleanlinessPolicy.AllowDirtyDevelopment, false)]
        [TestCase(false, false, BuildSourceCleanlinessPolicy.AllowDirtyLocalRelease, true)]
        [TestCase(false, true, BuildSourceCleanlinessPolicy.AllowDirtyLocalRelease, false)]
        [TestCase(true, false, BuildSourceCleanlinessPolicy.RequireClean, true)]
        [TestCase(true, false, BuildSourceCleanlinessPolicy.AllowDirtyDevelopment, true)]
        [TestCase(true, false, BuildSourceCleanlinessPolicy.AllowDirtyLocalRelease, true)]
        [TestCase(true, true, BuildSourceCleanlinessPolicy.RequireClean, true)]
        [TestCase(true, true, BuildSourceCleanlinessPolicy.AllowDirtyDevelopment, true)]
        [TestCase(true, true, BuildSourceCleanlinessPolicy.AllowDirtyLocalRelease, true)]
        public void SourceWorkspacePolicy_RequirementMatrix_PreservesReleaseAndCiQualification(
            bool batchMode,
            bool debugBuild,
            BuildSourceCleanlinessPolicy policy,
            bool expected)
        {
            Assert.That(
                BuildSourceWorkspacePolicy.RequiresVerifiedClean(
                    batchMode,
                    debugBuild,
                    policy),
                Is.EqualTo(expected));
        }

        [TestCase(
            BuildSourceCleanlinessPolicy.RequireClean,
            false,
            true,
            "Blocked")]
        [TestCase(
            BuildSourceCleanlinessPolicy.AllowDirtyDevelopment,
            false,
            true,
            "Blocked")]
        [TestCase(
            BuildSourceCleanlinessPolicy.AllowDirtyLocalRelease,
            false,
            true,
            "LocalReleasePreview")]
        [TestCase(
            BuildSourceCleanlinessPolicy.AllowDirtyLocalRelease,
            true,
            true,
            "QualifiedRelease")]
        [TestCase(
            BuildSourceCleanlinessPolicy.AllowDirtyLocalRelease,
            false,
            false,
            "Blocked")]
        public void SourceWorkspacePolicy_InteractiveReleaseRoute_IsExplicitAndFailClosed(
            BuildSourceCleanlinessPolicy policy,
            bool qualifiedReleaseAllowed,
            bool localPreviewAvailable,
            string expected)
        {
            Assert.That(
                BuildSourceWorkspacePolicy.ResolveInteractiveReleaseRoute(
                    policy,
                    qualifiedReleaseAllowed,
                    localPreviewAvailable).ToString(),
                Is.EqualTo(expected));
        }

        [TestCase(VersionControlWorkspaceComponentStatus.Clean, true, "VerifiedClean")]
        [TestCase(VersionControlWorkspaceComponentStatus.Dirty, false, "VerifiedCleanRequired")]
        [TestCase(VersionControlWorkspaceComponentStatus.Unknown, false, "VerifiedCleanRequired")]
        public void SourceWorkspacePolicy_ReleaseDecision_IsFailClosed(
            VersionControlWorkspaceComponentStatus trackedStatus,
            bool expectedAllowed,
            string expectedReason)
        {
            VersionControlWorkspaceEvidence workspace = trackedStatus ==
                VersionControlWorkspaceComponentStatus.Unknown
                ? VersionControlWorkspaceEvidence.Unknown(
                    VersionControlWorkspaceEvidence.CommandTimedOut)
                : CreateWorkspace(
                    trackedStatus,
                    trackedStatus == VersionControlWorkspaceComponentStatus.Dirty ? 3 : 0);

            BuildSourceWorkspaceDecision decision = BuildSourceWorkspacePolicy.Evaluate(
                batchMode: false,
                debugBuild: false,
                BuildSourceCleanlinessPolicy.AllowDirtyDevelopment,
                workspace);

            Assert.That(decision.Allowed, Is.EqualTo(expectedAllowed));
            Assert.That(decision.RequiresVerifiedClean, Is.True);
            Assert.That(decision.ReasonCode, Is.EqualTo(expectedReason));
        }

        [TestCase(VersionControlWorkspaceComponentStatus.Clean, "VerifiedClean")]
        [TestCase(VersionControlWorkspaceComponentStatus.Dirty, "LocalDirtyDevelopmentAllowed")]
        [TestCase(VersionControlWorkspaceComponentStatus.Unknown, "LocalDirtyDevelopmentAllowed")]
        public void SourceWorkspacePolicy_LocalDevelopmentDecision_HonorsExplicitException(
            VersionControlWorkspaceComponentStatus trackedStatus,
            string expectedReason)
        {
            VersionControlWorkspaceEvidence workspace = trackedStatus ==
                VersionControlWorkspaceComponentStatus.Unknown
                ? VersionControlWorkspaceEvidence.Unknown(
                    VersionControlWorkspaceEvidence.CommandTimedOut)
                : CreateWorkspace(
                    trackedStatus,
                    trackedStatus == VersionControlWorkspaceComponentStatus.Dirty ? 3 : 0);

            BuildSourceWorkspaceDecision decision = BuildSourceWorkspacePolicy.Evaluate(
                batchMode: false,
                debugBuild: true,
                BuildSourceCleanlinessPolicy.AllowDirtyDevelopment,
                workspace);

            Assert.That(decision.Allowed, Is.True);
            Assert.That(decision.RequiresVerifiedClean, Is.False);
            Assert.That(decision.ReasonCode, Is.EqualTo(expectedReason));
        }

        [Test]
        public void SourceWorkspacePolicy_LocalReleasePreview_IsLocalOnlyAndNonQualified()
        {
            var dirtyComponent = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.Dirty,
                1);
            var cleanComponent = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.Clean,
                0);
            var notApplicable = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.NotApplicable,
                0);
            var dirty = new VersionControlWorkspaceEvidence(
                dirtyComponent,
                dirtyComponent,
                notApplicable,
                cleanComponent);

            BuildSourceWorkspaceDecision decision = BuildSourceWorkspacePolicy.Evaluate(
                batchMode: false,
                BuildPurpose.LocalReleasePreview,
                BuildSourceCleanlinessPolicy.RequireClean,
                dirty);

            Assert.That(decision.Allowed, Is.True);
            Assert.That(decision.RequiresVerifiedClean, Is.False);
            Assert.That(
                decision.ReasonCode,
                Is.EqualTo(BuildSourceWorkspacePolicy.LocalPreviewAllowedReason));
            Assert.Throws<ArgumentException>(() =>
                BuildSourceWorkspacePolicy.RequiresVerifiedClean(
                    batchMode: true,
                    BuildPurpose.LocalReleasePreview,
                    BuildSourceCleanlinessPolicy.RequireClean));
        }

        [TestCase("untracked")]
        [TestCase("submodules")]
        [TestCase("gitLfs")]
        public void SourceWorkspacePolicy_ReleaseRejectsEveryDirtySourceComponent(
            string dirtyComponent)
        {
            var clean = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.Clean,
                0);
            var dirty = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.Dirty,
                1);
            var notApplicable = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.NotApplicable,
                0);
            var workspace = new VersionControlWorkspaceEvidence(
                clean,
                dirtyComponent == "untracked" ? dirty : clean,
                dirtyComponent == "submodules" ? dirty : notApplicable,
                dirtyComponent == "gitLfs" ? dirty : notApplicable);

            BuildSourceWorkspaceDecision decision = BuildSourceWorkspacePolicy.Evaluate(
                batchMode: false,
                debugBuild: false,
                BuildSourceCleanlinessPolicy.AllowDirtyDevelopment,
                workspace);

            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.ReasonCode, Is.EqualTo("VerifiedCleanRequired"));
        }

        [Test]
        public void Resolve_ExplicitIdentityOverridesBuildNumberAndBranch_WhenSourceMatches()
        {
            var identityOverride = new BuildIdentityOverride(
                9001,
                "TestVcs",
                "ABCDEF123456",
                "release/1.0",
                "TeamCity",
                "build-9001");
            var provider = new FakeProvider(
                new VersionControlMetadata(
                    "TestVcs",
                    "abcdef123456",
                    "42",
                    "main",
                    "2026-08-02T00:00:00Z",
                    CreateWorkspace(VersionControlWorkspaceComponentStatus.Clean)));

            BuildVersionContext result = BuildVersionResolver.Resolve(
                CreateRequest(
                    batchMode: true,
                    debugBuild: false,
                    identityOverride: identityOverride),
                provider);

            Assert.That(result.BuildNumber, Is.EqualTo(9001));
            Assert.That(result.PackageVersion, Is.EqualTo("1.0.0.9001"));
            Assert.That(result.ProviderId, Is.EqualTo("TestVcs"));
            Assert.That(result.CommitHash, Is.EqualTo("ABCDEF123456"));
            Assert.That(result.Branch, Is.EqualTo("release/1.0"));
            Assert.That(result.DetectedCommitHash, Is.EqualTo("abcdef123456"));
            Assert.That(result.DetectedBranch, Is.EqualTo("main"));
            Assert.That(result.DetectedBuildNumber, Is.EqualTo(42));
            Assert.That(result.IdentityOrigin, Is.EqualTo(BuildIdentityOrigin.ExplicitOverride));
            Assert.That(result.CiProvider, Is.EqualTo("TeamCity"));
            Assert.That(result.CiRunId, Is.EqualTo("build-9001"));
        }

        [TestCase("OtherVcs", "abcdef123456")]
        [TestCase("TestVcs", "different")]
        public void Resolve_WhenExplicitSourceDisagreesWithDetectedWorkspace_Fails(
            string sourceProvider,
            string sourceRevision)
        {
            var identityOverride = new BuildIdentityOverride(
                100,
                sourceProvider,
                sourceRevision,
                "main",
                null,
                null);
            var provider = new FakeProvider(
                new VersionControlMetadata(
                    "TestVcs",
                    "abcdef123456",
                    "42",
                    "main",
                    "2026-08-02T00:00:00Z",
                    CreateWorkspace(VersionControlWorkspaceComponentStatus.Clean)));

            Assert.Throws<BuildFailedException>(
                () => BuildVersionResolver.Resolve(
                    CreateRequest(true, false, identityOverride),
                    provider));
        }

        [Test]
        public void Resolve_WithoutDetectedProvider_CompleteExplicitIdentitySucceeds()
        {
            var identityOverride = new BuildIdentityOverride(
                73,
                "Git",
                "0123456789abcdef",
                "refs/heads/release",
                "Jenkins",
                "release-73");

            BuildVersionContext result = BuildVersionResolver.Resolve(
                CreateRequest(true, false, identityOverride),
                null);

            Assert.That(result.PackageVersion, Is.EqualTo("1.0.0.73"));
            Assert.That(result.DetectedProviderId, Is.Empty);
            Assert.That(result.ProviderId, Is.EqualTo("Git"));
            Assert.That(result.IdentityOrigin, Is.EqualTo(BuildIdentityOrigin.ExplicitOverride));
        }

        [Test]
        public void BuildIdentityOverride_RejectsPartialGroupsAndInvalidBuildNumber()
        {
            Assert.Throws<ArgumentException>(
                () => new BuildIdentityOverride(
                    null,
                    "Git",
                    null,
                    "main",
                    null,
                    null));
            Assert.Throws<ArgumentException>(
                () => new BuildIdentityOverride(
                    null,
                    null,
                    null,
                    null,
                    "Jenkins",
                    null));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BuildIdentityOverride(
                    0,
                    null,
                    null,
                    null,
                    null,
                    null));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BuildIdentityOverride(
                    (long)int.MaxValue + 1L,
                    null,
                    null,
                    null,
                    null,
                    null));
            Assert.Throws<ArgumentException>(
                () => new BuildIdentityOverride(
                    null,
                    null,
                    null,
                    null,
                    "Jenkins\n",
                    "run-1"));
        }

        [Test]
        public void Resolve_WhenProviderSnapshotIsInvalid_FailsClosedForBatchBuild()
        {
            var provider = new FakeProvider(
                new VersionControlMetadata(
                    "TestVcs",
                    "abcdef123456",
                    "not-a-number",
                    "main",
                    "2026-08-02T00:00:00Z",
                    CreateWorkspace(VersionControlWorkspaceComponentStatus.Clean)));

            Assert.Throws<BuildFailedException>(
                () => BuildVersionResolver.Resolve(
                    CreateRequest(batchMode: true, debugBuild: false),
                    provider));
        }

        private static BuildRequest CreateRequest(
            bool batchMode,
            bool debugBuild,
            BuildIdentityOverride identityOverride = null,
            BuildSourceCleanlinessPolicy sourceCleanlinessPolicy =
                BuildSourceCleanlinessPolicy.RequireClean,
            BuildPurpose? purpose = null)
        {
            string projectRoot = Path.GetFullPath(
                Path.Combine(Path.GetTempPath(), "BuildVersionResolverTests"));
            string buildRoot = Path.Combine(projectRoot, "Build");
            string outputDirectory = Path.Combine(buildRoot, "Windows", "Release");
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
                debugBuild: debugBuild,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: batchMode,
                applicationVersion: "1.0.0",
                identityOverride: identityOverride ?? BuildIdentityOverride.Empty,
                steps: new[]
                {
                    new BuildStepInvocation(BuildStepTypeIds.Player, BuildStepTypeIds.Player)
                },
                sourceCleanlinessPolicy: sourceCleanlinessPolicy,
                purpose: purpose ?? (debugBuild
                    ? BuildPurpose.Development
                    : BuildPurpose.Release));
        }

        private static BuildVersionContext CreateVersion(
            VersionControlWorkspaceEvidence workspace)
        {
            return new BuildVersionContext(
                "1.0.0",
                "1.0.0.42",
                42,
                "abcdef123456",
                "42",
                "main",
                "2026-08-02T00:00:00Z",
                "TestVcs",
                sourceWorkspace: workspace);
        }

        private static VersionControlWorkspaceEvidence CreateWorkspace(
            VersionControlWorkspaceComponentStatus trackedStatus,
            int? changeCount = 0)
        {
            var tracked = new VersionControlWorkspaceComponentEvidence(
                trackedStatus,
                changeCount);
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

        private sealed class FakeProvider : IVersionControlProvider
        {
            private readonly VersionControlMetadata metadata;

            public FakeProvider(VersionControlMetadata metadata)
            {
                this.metadata = metadata;
            }

            public int CaptureCount { get; private set; }

            public VersionControlMetadata Capture()
            {
                CaptureCount++;
                return metadata;
            }
        }
    }
}
