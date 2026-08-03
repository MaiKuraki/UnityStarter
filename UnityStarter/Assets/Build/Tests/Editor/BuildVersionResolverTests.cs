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
            Assert.That(result.PackageVersion, Is.EqualTo("1.0.0.0"));
            Assert.That(result.CommitHash, Is.EqualTo("local"));
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

        private static BuildRequest CreateRequest(bool batchMode, bool debugBuild)
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
                null,
                BuildTarget.StandaloneWindows64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.Mono2x,
                projectRoot,
                buildRoot,
                Path.Combine(outputDirectory, "TestProduct.exe"),
                outputDirectory,
                outputIsFolder: false,
                incrementality: BuildIncrementality.Clean,
                deleteDebugFiles: true,
                debugBuild: debugBuild,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: batchMode,
                applicationVersion: "1.0.0",
                assetContentProviderId: string.Empty,
                assetContentConfiguration: null,
                useHybridClr: false,
                enablePlayerObfuscation: false,
                stepIds: new[] { BuildStepIds.Player });
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
