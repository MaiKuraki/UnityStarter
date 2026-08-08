using System;
using System.IO;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class AddressablesContentUpdateTests
    {
        private string sandboxRoot;

        [SetUp]
        public void SetUp()
        {
            sandboxRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityStarter-AddressablesContentUpdateTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandboxRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(sandboxRoot))
            {
                Directory.Delete(sandboxRoot, recursive: true);
            }
        }

        [Test]
        public void ResolveBaseline_ProjectRelativeBin_ReturnsCanonicalFile()
        {
            string relativePath = "Artifacts/Baselines/addressables_content_state.bin";
            string absolutePath = CreateFile(relativePath, "official-state");
            AddressablesBuildConfig config = CreateConfig(relativePath);
            try
            {
                Assert.That(
                    AddressablesBuilder.ResolveContentUpdateBaselinePath(
                        config,
                        sandboxRoot),
                    Is.EqualTo(Path.GetFullPath(absolutePath)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [TestCase("../escape.bin")]
        [TestCase("Library/addressables_content_state.bin")]
        [TestCase("Artifacts/Baselines/state.json")]
        public void ResolveBaseline_UnsafeOrUnsupportedPath_FailsClosed(string relativePath)
        {
            AddressablesBuildConfig config = CreateConfig(relativePath);
            try
            {
                Assert.That(
                    () => AddressablesBuilder.ResolveContentUpdateBaselinePath(
                        config,
                        sandboxRoot),
                    Throws.Exception);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ValidateArtifactManifest_MatchingPublishedState_Succeeds()
        {
            PublishedBaseline baseline = CreatePublishedBaseline(
                BuildTarget.StandaloneWindows64,
                "profile-id",
                "https://cdn.example.test/content",
                formatVersion: AddressablesArtifactManifestFormat.CurrentVersion);

            Assert.DoesNotThrow(() =>
                AddressablesBuilder.ValidateContentUpdateArtifactManifest(
                    sandboxRoot,
                    baseline.StatePath,
                    BuildTarget.StandaloneWindows64,
                    "profile-id",
                    "https://cdn.example.test/content",
                    "player-1",
                    Application.unityVersion,
                    "https://cdn.example.test/content",
                    baseline.Size,
                    baseline.Sha256));
        }

        [TestCase(0, "profile-id", BuildTarget.StandaloneWindows64)]
        [TestCase(1, "other-profile", BuildTarget.StandaloneWindows64)]
        [TestCase(1, "profile-id", BuildTarget.Android)]
        public void ValidateArtifactManifest_IncompatibleBaseline_FailsClosed(
            int formatVersion,
            string requestedProfileId,
            BuildTarget requestedTarget)
        {
            PublishedBaseline baseline = CreatePublishedBaseline(
                BuildTarget.StandaloneWindows64,
                "profile-id",
                "https://cdn.example.test/content",
                formatVersion);

            Assert.That(
                () => AddressablesBuilder.ValidateContentUpdateArtifactManifest(
                    sandboxRoot,
                    baseline.StatePath,
                    requestedTarget,
                    requestedProfileId,
                    "https://cdn.example.test/content",
                    "player-1",
                    Application.unityVersion,
                    "https://cdn.example.test/content",
                    baseline.Size,
                    baseline.Sha256),
                Throws.Exception);
        }

        [Test]
        public void OfficialApiSelectors_RequireExactSupportedSignatures()
        {
            Assert.That(
                AddressablesVersionBuildProcessor.FindContentUpdateBuildMethod(
                    typeof(SupportedContentUpdateApi),
                    typeof(FakeSettings)),
                Is.Not.Null);
            Assert.That(
                AddressablesVersionBuildProcessor.FindContentStateLoadMethod(
                    typeof(SupportedContentUpdateApi)),
                Is.Not.Null);
            Assert.That(
                AddressablesVersionBuildProcessor.FindContentUpdateBuildMethod(
                    typeof(UnsupportedContentUpdateApi),
                    typeof(FakeSettings)),
                Is.Null);
        }

        private AddressablesBuildConfig CreateConfig(string relativePath)
        {
            AddressablesBuildConfig config =
                ScriptableObject.CreateInstance<AddressablesBuildConfig>();
            config.contentUpdateBaselinePath = relativePath;
            return config;
        }

        private string CreateFile(string relativePath, string content)
        {
            string path = Path.Combine(
                sandboxRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
            return path;
        }

        private PublishedBaseline CreatePublishedBaseline(
            BuildTarget target,
            string profileId,
            string remoteCatalogLoadPath,
            int formatVersion)
        {
            const string relativeStatePath =
                "Build/AddressablesContent/StandaloneWindows64/BuildMetadata/addressables_content_state.bin";
            string statePath = CreateFile(relativeStatePath, "official-content-state");
            var fileInfo = new FileInfo(statePath);
            string sha256 = AddressablesBuilder.ComputeSha256(statePath);
            string publicationRoot = Path.GetFullPath(Path.Combine(
                sandboxRoot,
                "Build",
                "AddressablesContent",
                "StandaloneWindows64"));
            var manifest = new AddressablesArtifactManifest
            {
                formatVersion = formatVersion,
                buildTarget = target.ToString(),
                contentVersion = "pipeline-2",
                incrementality = BuildIncrementality.Clean.ToString(),
                unityVersion = Application.unityVersion,
                activeProfileId = profileId,
                activeProfileName = "Default",
                addressablesPlayerVersion = "player-1",
                remoteCatalogLoadPath = remoteCatalogLoadPath,
                files = new[]
                {
                    new AddressablesArtifactManifestEntry
                    {
                        kind = "BuildMetadata",
                        path = "BuildMetadata/addressables_content_state.bin",
                        size = fileInfo.Length,
                        sha256 = sha256
                    }
                }
            };
            string manifestJson = formatVersion
                                  == AddressablesArtifactManifestFormat.CurrentVersion
                ? AddressablesArtifactManifestFormat.Serialize(
                    manifest,
                    prettyPrint: true)
                : JsonUtility.ToJson(manifest, true);
            File.WriteAllText(
                Path.Combine(
                    publicationRoot,
                    AddressablesArtifactManifestFormat.FileName),
                manifestJson);
            return new PublishedBaseline(
                statePath,
                fileInfo.Length,
                sha256);
        }

        private sealed class PublishedBaseline
        {
            public PublishedBaseline(string statePath, long size, string sha256)
            {
                StatePath = statePath;
                Size = size;
                Sha256 = sha256;
            }

            public string StatePath { get; }
            public long Size { get; }
            public string Sha256 { get; }
        }

        private sealed class FakeSettings
        {
        }

        private sealed class FakeState
        {
        }

        private sealed class FakeResult
        {
            public string Error => string.Empty;
        }

        private static class SupportedContentUpdateApi
        {
            public static FakeResult BuildContentUpdate(
                FakeSettings settings,
                string contentStatePath)
            {
                return new FakeResult();
            }

            public static FakeState LoadContentState(string contentStatePath)
            {
                return new FakeState();
            }
        }

        private static class UnsupportedContentUpdateApi
        {
            public static void BuildContentUpdate(
                FakeSettings settings,
                string contentStatePath)
            {
            }
        }
    }
}
