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
            Assert.That(result.PackageVersion, Is.EqualTo("1.0.0.1"));
            Assert.That(result.CommitHash, Is.EqualTo("local"));
            Assert.That(result.IdentityOrigin, Is.EqualTo(BuildIdentityOrigin.LocalDevelopment));
        }

        [Test]
        public void Resolve_CapturesExactlyOneProviderSnapshot()
        {
            var provider = new FakeProvider(
                new VersionControlMetadata(
                    "TestVcs",
                    "abcdef123456",
                    "42",
                    "main",
                    "2026-08-02T00:00:00Z"));

            BuildVersionContext result = BuildVersionResolver.Resolve(
                CreateRequest(batchMode: true, debugBuild: false),
                provider);

            Assert.That(provider.CaptureCount, Is.EqualTo(1));
            Assert.That(result.ProviderId, Is.EqualTo("TestVcs"));
            Assert.That(result.PackageVersion, Is.EqualTo("1.0.0.42"));
            Assert.That(result.DetectedProviderId, Is.EqualTo("TestVcs"));
            Assert.That(result.EffectiveSourceRevision, Is.EqualTo("abcdef123456"));
            Assert.That(result.IdentityOrigin, Is.EqualTo(BuildIdentityOrigin.VersionControl));
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
                    "2026-08-02T00:00:00Z"));

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
                    "2026-08-02T00:00:00Z"));

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
                    "2026-08-02T00:00:00Z"));

            Assert.Throws<BuildFailedException>(
                () => BuildVersionResolver.Resolve(
                    CreateRequest(batchMode: true, debugBuild: false),
                    provider));
        }

        private static BuildRequest CreateRequest(
            bool batchMode,
            bool debugBuild,
            BuildIdentityOverride identityOverride = null)
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
                });
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
