using System;
using System.IO;
using Build.Pipeline.Editor;
using Build.VersionControl.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    /// <summary>
    /// Focused regression tests for the interactive-iteration build-pipeline fix:
    /// non-batch Development builds and LocalReleasePreview builds must resolve to a
    /// stable, non-version-controlled local version (so they never collide with committed
    /// output) and must be flagged as overwritable without weakening release immutability.
    /// </summary>
    public sealed class LocalReleasePreviewBuildTests
    {
        [Test]
        public void LocalReleasePreview_ResolvesStableLocalVersionIgnoringVersionControl()
        {
            BuildRequest preview = CreateRequest("0.1.0", BuildPurpose.LocalReleasePreview);
            BuildVersionContext context = BuildVersionResolver.Resolve(
                preview,
                new StubVersionControlProvider());

            Assert.That(context.IdentityOrigin, Is.EqualTo(BuildIdentityOrigin.LocalPreview));
            Assert.That(context.PackageVersion, Is.EqualTo("0.1.0.9000002"));
            Assert.That(context.BuildNumber, Is.EqualTo(9000002L));
            Assert.That(context.CommitHash, Is.EqualTo("local-preview"));
            // The preview must NOT consume the version-controlled commit-count namespace.
            Assert.That(context.PackageVersion, Is.Not.EqualTo("0.1.0.22"));
        }

        [Test]
        public void LocalReleasePreview_StaysLocalWhenVersionControlUnavailable()
        {
            BuildRequest preview = CreateRequest("0.1.0", BuildPurpose.LocalReleasePreview);
            BuildVersionContext context = BuildVersionResolver.Resolve(preview, null);

            Assert.That(context.IdentityOrigin, Is.EqualTo(BuildIdentityOrigin.LocalPreview));
            Assert.That(context.PackageVersion, Is.EqualTo("0.1.0.9000002"));
        }

        [Test]
        public void InteractiveDevelopment_ResolvesStableLocalVersionIgnoringVersionControl()
        {
            BuildRequest development = CreateRequest("0.1.0", BuildPurpose.Development);
            BuildVersionContext context = BuildVersionResolver.Resolve(
                development,
                new StubVersionControlProvider());

            Assert.That(context.IdentityOrigin, Is.EqualTo(BuildIdentityOrigin.LocalDevelopment));
            Assert.That(context.PackageVersion, Is.EqualTo("0.1.0.9000001"));
            Assert.That(context.BuildNumber, Is.EqualTo(9000001L));
            Assert.That(context.CommitHash, Is.EqualTo("local"));
            // The interactive Development build must NOT consume the commit-count namespace.
            Assert.That(context.PackageVersion, Is.Not.EqualTo("0.1.0.22"));
        }

        [Test]
        public void LocalDevelopment_And_LocalPreview_ResolveDistinctStableVersions()
        {
            BuildVersionContext development = BuildVersionResolver.Resolve(
                CreateRequest("0.1.0", BuildPurpose.Development),
                new StubVersionControlProvider());
            BuildVersionContext preview = BuildVersionResolver.Resolve(
                CreateRequest("0.1.0", BuildPurpose.LocalReleasePreview),
                new StubVersionControlProvider());

            // The two interactive local purposes must resolve to distinct package
            // versions so their YooAsset/Player output directories never overwrite each other.
            Assert.That(development.PackageVersion, Is.Not.EqualTo(preview.PackageVersion));
            Assert.That(development.BuildNumber, Is.Not.EqualTo(preview.BuildNumber));
            Assert.That(development.BuildNumber, Is.EqualTo(9000001L));
            Assert.That(preview.BuildNumber, Is.EqualTo(9000002L));
            Assert.That(development.IdentityOrigin, Is.EqualTo(BuildIdentityOrigin.LocalDevelopment));
            Assert.That(preview.IdentityOrigin, Is.EqualTo(BuildIdentityOrigin.LocalPreview));
            // Neither consumes the version-controlled commit-count namespace.
            Assert.That(development.PackageVersion, Is.Not.EqualTo("0.1.0.22"));
            Assert.That(preview.PackageVersion, Is.Not.EqualTo("0.1.0.22"));
        }

        [Test]
        public void BatchDevelopment_UsesCommitCountVersionNamespace()
        {
            BuildRequest development = CreateRequest(
                "0.1.0",
                BuildPurpose.Development,
                batchMode: true);
            BuildVersionContext context = BuildVersionResolver.Resolve(
                development,
                new StubVersionControlProvider());

            Assert.That(context.IdentityOrigin, Is.EqualTo(BuildIdentityOrigin.VersionControl));
            Assert.That(context.PackageVersion, Is.EqualTo("0.1.0.22"));
        }

        [Test]
        public void InteractiveRelease_UsesCommitCountVersionNamespace()
        {
            BuildRequest release = CreateRequest("0.1.0", BuildPurpose.Release);
            BuildVersionContext context = BuildVersionResolver.Resolve(
                release,
                new StubVersionControlProvider());

            Assert.That(context.IdentityOrigin, Is.EqualTo(BuildIdentityOrigin.VersionControl));
            Assert.That(context.PackageVersion, Is.EqualTo("0.1.0.22"));
        }

        [Test]
        public void AssetContentBuildRequest_Purpose_DrivesIsLocalPreview()
        {
            var preview = new AssetContentBuildRequest(
                "id",
                BuildTarget.StandaloneWindows64,
                "0.1.0.1",
                Path.GetTempPath(),
                null,
                BuildIncrementality.Clean,
                false,
                BuildPurpose.LocalReleasePreview);
            var release = new AssetContentBuildRequest(
                "id",
                BuildTarget.StandaloneWindows64,
                "0.1.0.1",
                Path.GetTempPath(),
                null,
                BuildIncrementality.Clean,
                false,
                BuildPurpose.Release);

            Assert.That(preview.IsLocalPreview, Is.True);
            Assert.That(preview.Purpose, Is.EqualTo(BuildPurpose.LocalReleasePreview));
            Assert.That(release.IsLocalPreview, Is.False);
        }

        [Test]
        public void AssetContentBuildRequest_IsLocalIterationAndReplaceExactVersion_AreContractDriven()
        {
            var interactiveDevelopment = new AssetContentBuildRequest(
                "id",
                BuildTarget.StandaloneWindows64,
                "0.1.0.1",
                Path.GetTempPath(),
                null,
                BuildIncrementality.Clean,
                batchMode: false,
                purpose: BuildPurpose.Development);
            var interactivePreview = new AssetContentBuildRequest(
                "id",
                BuildTarget.StandaloneWindows64,
                "0.1.0.1",
                Path.GetTempPath(),
                null,
                BuildIncrementality.Clean,
                batchMode: false,
                purpose: BuildPurpose.LocalReleasePreview);
            var batchDevelopment = new AssetContentBuildRequest(
                "id",
                BuildTarget.StandaloneWindows64,
                "0.1.0.22",
                Path.GetTempPath(),
                null,
                BuildIncrementality.Clean,
                batchMode: true,
                purpose: BuildPurpose.Development);
            var replaceExact = new AssetContentBuildRequest(
                "id",
                BuildTarget.StandaloneWindows64,
                "0.1.0.22",
                Path.GetTempPath(),
                null,
                BuildIncrementality.Clean,
                batchMode: true,
                purpose: BuildPurpose.Release,
                replaceExactVersion: true);

            Assert.That(interactiveDevelopment.IsLocalIteration, Is.True);
            Assert.That(interactiveDevelopment.IsLocalPreview, Is.False);
            Assert.That(interactivePreview.IsLocalIteration, Is.True);
            Assert.That(interactivePreview.IsLocalPreview, Is.True);
            Assert.That(batchDevelopment.IsLocalIteration, Is.False);
            Assert.That(replaceExact.ReplaceExactVersion, Is.True);
            Assert.That(replaceExact.IsLocalIteration, Is.False);
            Assert.That(interactiveDevelopment.ReplaceExactVersion, Is.False);
        }

        private static BuildRequest CreateRequest(
            string applicationVersion,
            BuildPurpose purpose,
            bool batchMode = false)
        {
            return new BuildRequest(
                "TestCompany",
                "TestProduct",
                "com.example.test",
                "Assets/Build/Runtime/Resources/VersionInfoData.asset",
                Array.Empty<string>(),
                CheatBuildMode.Disabled,
                BuildTarget.StandaloneWindows64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.Mono2x,
                Path.GetTempPath(),
                Path.GetTempPath(),
                Path.Combine(Path.GetTempPath(), "out.exe"),
                Path.GetTempPath(),
                outputIsFolder: false,
                deleteDebugFiles: true,
                debugBuild: purpose == BuildPurpose.Development,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: batchMode,
                applicationVersion: applicationVersion,
                identityOverride: BuildIdentityOverride.Empty,
                steps: Array.Empty<BuildStepInvocation>(),
                sourceCleanlinessPolicy: purpose == BuildPurpose.LocalReleasePreview
                    ? BuildSourceCleanlinessPolicy.AllowDirtyLocalRelease
                    : purpose == BuildPurpose.Development
                        ? BuildSourceCleanlinessPolicy.AllowDirtyDevelopment
                        : BuildSourceCleanlinessPolicy.RequireClean,
                purpose: purpose);
        }

        private sealed class StubVersionControlProvider : IVersionControlProvider
        {
            public VersionControlMetadata Capture()
            {
                return new VersionControlMetadata(
                    "git",
                    "deadbeef",
                    "22",
                    "main",
                    "2024-01-01",
                    VersionControlWorkspaceEvidence.Unknown(
                        VersionControlWorkspaceEvidence.MetadataUnavailable));
            }
        }
    }
}
