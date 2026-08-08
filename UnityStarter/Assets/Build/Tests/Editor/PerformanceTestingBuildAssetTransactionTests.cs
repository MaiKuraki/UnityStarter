using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Build.Pipeline.Editor.Tests
{
    [TestFixture]
    public sealed class PerformanceTestingBuildAssetTransactionTests
    {
        private const string SupportedVersion = "3.5.0";
        private string projectRoot;
        private FakePreferenceStore preferences;

        [SetUp]
        public void SetUp()
        {
            projectRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityStarter",
                nameof(PerformanceTestingBuildAssetTransactionTests),
                Guid.NewGuid().ToString("N"),
                "UnityProject");
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            preferences = new FakePreferenceStore();
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(projectRoot) && Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [TestCase(null, "Missing")]
        [TestCase("", "Missing")]
        [TestCase("3.5.0", "Supported")]
        [TestCase("3.5.99-preview.1", "Supported")]
        [TestCase("3.4.9", "Unsupported")]
        [TestCase("3.6.0", "Unsupported")]
        [TestCase("3.5", "Unsupported")]
        [TestCase("not-a-version", "Unsupported")]
        public void PackageGate_AllowsOnlyAudited35Range(
            string version,
            string expected)
        {
            Assert.That(
                PerformanceTestingPackageGate.EvaluateVersion(version).Status.ToString(),
                Is.EqualTo(expected));
        }

        [Test]
        public void RoundTrip_PreexistingAssetsAndPreference_AreRestoredExactly()
        {
            string resources = CreatePreexistingResources();
            string runInfo = Path.Combine(resources, "PerformanceTestRunInfo.json");
            string runInfoMeta = runInfo + ".meta";
            string settings = Path.Combine(resources, "PerformanceTestRunSettings.json");
            string settingsMeta = settings + ".meta";
            Write(runInfo, "original-run");
            Write(runInfoMeta, "original-run-meta");
            Write(settings, "original-settings");
            Write(settingsMeta, "original-settings-meta");
            preferences.SetBool(
                PerformanceTestingBuildAssetTransaction.CleanupPreferenceKey,
                true);

            Dictionary<string, byte[]> originals = new[]
                {
                    runInfo,
                    runInfoMeta,
                    settings,
                    settingsMeta,
                    resources + ".meta"
                }
                .ToDictionary(path => path, File.ReadAllBytes);

            PerformanceTestingBuildAssetTransaction.Begin(
                projectRoot,
                SupportedVersion,
                preferences,
                refreshAssets: false);
            Assert.That(
                preferences.GetBool(PerformanceTestingBuildAssetTransaction.CleanupPreferenceKey),
                Is.False);

            Write(runInfo, "generated-run");
            Write(settings, "generated-settings");
            PerformanceTestingBuildAssetTransaction.AdoptGeneratedImage(projectRoot, preferences);
            Delete(runInfo);
            Delete(runInfoMeta);
            Delete(settings);
            Delete(settingsMeta);

            PerformanceTestingBuildAssetTransaction.RestoreAndComplete(
                projectRoot,
                preferences,
                refreshAssets: false);

            foreach (KeyValuePair<string, byte[]> original in originals)
            {
                Assert.That(File.ReadAllBytes(original.Key), Is.EqualTo(original.Value));
            }

            Assert.That(
                preferences.GetBool(PerformanceTestingBuildAssetTransaction.CleanupPreferenceKey),
                Is.True);
            Assert.That(
                PerformanceTestingBuildAssetTransaction.InspectReadiness(projectRoot, preferences).Status,
                Is.EqualTo(PerformanceTestingBuildAssetReadinessStatus.Clean));
        }

        [Test]
        public void RoundTrip_OriginallyAbsentResources_RemovesOnlyOwnedEmptyDirectory()
        {
            PerformanceTestingBuildAssetTransaction.Begin(
                projectRoot,
                SupportedVersion,
                preferences,
                refreshAssets: false);
            string resources = Path.Combine(projectRoot, "Assets", "Resources");
            Assert.That(Directory.Exists(resources), Is.True);
            Assert.That(File.Exists(resources + ".meta"), Is.True);

            string runInfo = Path.Combine(resources, "PerformanceTestRunInfo.json");
            string settings = Path.Combine(resources, "PerformanceTestRunSettings.json");
            Write(runInfo, "generated-run");
            Write(settings, "generated-settings");
            PerformanceTestingBuildAssetTransaction.AdoptGeneratedImage(projectRoot, preferences);
            Delete(runInfo);
            Delete(settings);

            PerformanceTestingBuildAssetTransaction.RestoreAndComplete(
                projectRoot,
                preferences,
                refreshAssets: false);

            Assert.That(Directory.Exists(resources), Is.False);
            Assert.That(File.Exists(resources + ".meta"), Is.False);
            Assert.That(
                preferences.HasKey(PerformanceTestingBuildAssetTransaction.CleanupPreferenceKey),
                Is.False);
        }

        [Test]
        public void Restore_UnknownConcurrentFileImage_FailsClosedAndRetainsEvidence()
        {
            string resources = CreatePreexistingResources();
            PerformanceTestingBuildAssetTransaction.Begin(
                projectRoot,
                SupportedVersion,
                preferences,
                refreshAssets: false);
            string runInfo = Path.Combine(resources, "PerformanceTestRunInfo.json");
            string settings = Path.Combine(resources, "PerformanceTestRunSettings.json");
            Write(runInfo, "generated-run");
            Write(settings, "generated-settings");
            PerformanceTestingBuildAssetTransaction.AdoptGeneratedImage(projectRoot, preferences);
            Write(runInfo, "unknown-concurrent-image");
            Delete(settings);

            Assert.Throws<InvalidOperationException>(() =>
                PerformanceTestingBuildAssetTransaction.RestoreAndComplete(
                    projectRoot,
                    preferences,
                    refreshAssets: false));

            Assert.That(File.ReadAllText(runInfo), Is.EqualTo("unknown-concurrent-image"));
            PerformanceTestingBuildAssetReadiness readiness =
                PerformanceTestingBuildAssetTransaction.InspectReadiness(projectRoot, preferences);
            Assert.That(readiness.Status, Is.EqualTo(PerformanceTestingBuildAssetReadinessStatus.Blocked));
            Assert.That(readiness.CanRecover, Is.False);
        }

        [Test]
        public void Restore_UnknownEntryInOwnedResources_DoesNotDeleteDirectory()
        {
            PerformanceTestingBuildAssetTransaction.Begin(
                projectRoot,
                SupportedVersion,
                preferences,
                refreshAssets: false);
            string resources = Path.Combine(projectRoot, "Assets", "Resources");
            string runInfo = Path.Combine(resources, "PerformanceTestRunInfo.json");
            string settings = Path.Combine(resources, "PerformanceTestRunSettings.json");
            Write(runInfo, "generated-run");
            Write(settings, "generated-settings");
            PerformanceTestingBuildAssetTransaction.AdoptGeneratedImage(projectRoot, preferences);
            Delete(runInfo);
            Delete(settings);
            string unknown = Path.Combine(resources, "UserCreated.asset");
            Write(unknown, "user-data");

            Assert.Throws<InvalidOperationException>(() =>
                PerformanceTestingBuildAssetTransaction.RestoreAndComplete(
                    projectRoot,
                    preferences,
                    refreshAssets: false));

            Assert.That(Directory.Exists(resources), Is.True);
            Assert.That(File.ReadAllText(unknown), Is.EqualTo("user-data"));
        }

        [Test]
        public void Begin_WithPendingTransaction_DoesNotRecoverImplicitly()
        {
            CreatePreexistingResources();
            PerformanceTestingBuildAssetTransaction.Begin(
                projectRoot,
                SupportedVersion,
                preferences,
                refreshAssets: false);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                PerformanceTestingBuildAssetTransaction.Begin(
                    projectRoot,
                    SupportedVersion,
                    preferences,
                    refreshAssets: false));
            Assert.That(exception.Message, Does.Contain("explicitly"));

            PerformanceTestingBuildAssetTransaction.RestoreAndComplete(
                projectRoot,
                preferences,
                refreshAssets: false);
        }

        [Test]
        public void ExplicitRecovery_AfterAdoptionAndPackageCleanup_RestoresOriginalState()
        {
            string resources = CreatePreexistingResources();
            string runInfo = Path.Combine(resources, "PerformanceTestRunInfo.json");
            string settings = Path.Combine(resources, "PerformanceTestRunSettings.json");
            Write(runInfo, "original-run");
            preferences.SetBool(
                PerformanceTestingBuildAssetTransaction.CleanupPreferenceKey,
                true);

            PerformanceTestingBuildAssetTransaction.Begin(
                projectRoot,
                SupportedVersion,
                preferences,
                refreshAssets: false);
            Write(runInfo, "generated-run");
            Write(settings, "generated-settings");
            PerformanceTestingBuildAssetTransaction.AdoptGeneratedImage(projectRoot, preferences);
            Delete(runInfo);
            Delete(settings);

            PerformanceTestingBuildAssetReadiness readiness =
                PerformanceTestingBuildAssetTransaction.InspectReadiness(projectRoot, preferences);
            Assert.That(readiness.Status, Is.EqualTo(PerformanceTestingBuildAssetReadinessStatus.RecoveryRequired));
            Assert.That(readiness.CanRecover, Is.True);

            PerformanceTestingBuildAssetTransaction.RestoreAndComplete(
                projectRoot,
                preferences,
                refreshAssets: false);

            Assert.That(File.ReadAllText(runInfo), Is.EqualTo("original-run"));
            Assert.That(File.Exists(settings), Is.False);
            Assert.That(
                preferences.GetBool(PerformanceTestingBuildAssetTransaction.CleanupPreferenceKey),
                Is.True);
        }

        [Test]
        public void RecoveryParticipant_ClaimsDirectTransactionDirectory()
        {
            var participant = new PerformanceTestingBuildAssetRecoveryParticipant();
            Assert.That(
                participant.StateDirectoryRelativePaths,
                Is.EqualTo(new[] { ".buildpipeline/transactions/performance-testing" }));
            Assert.That(
                BuildPipelineRegistry.ResolveRecoveryParticipants().Any(candidate =>
                    string.Equals(
                        candidate.Id,
                        PerformanceTestingBuildAssetRecoveryParticipant.ParticipantId,
                        StringComparison.Ordinal)),
                Is.True);
            Assert.That(new PerformanceTestingBuildAssetEarlyProcessor().callbackOrder, Is.EqualTo(int.MinValue));
            Assert.That(new PerformanceTestingBuildAssetLateProcessor().callbackOrder, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void InspectReadiness_CleanProject_IsZeroWrite()
        {
            string stateRoot = Path.Combine(projectRoot, ".buildpipeline");

            PerformanceTestingBuildAssetReadiness readiness =
                PerformanceTestingBuildAssetTransaction.InspectReadiness(projectRoot, preferences);

            Assert.That(readiness.Status, Is.EqualTo(PerformanceTestingBuildAssetReadinessStatus.Clean));
            Assert.That(Directory.Exists(stateRoot), Is.False);
        }

        private string CreatePreexistingResources()
        {
            string resources = Path.Combine(projectRoot, "Assets", "Resources");
            Directory.CreateDirectory(resources);
            Write(resources + ".meta", "original-resources-meta");
            return resources;
        }

        private static void Write(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        private static void Delete(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private sealed class FakePreferenceStore : IPerformanceTestingPreferenceStore
        {
            private readonly Dictionary<string, bool> values =
                new Dictionary<string, bool>(StringComparer.Ordinal);

            public bool HasKey(string key) => values.ContainsKey(key);
            public bool GetBool(string key) => values.TryGetValue(key, out bool value) && value;
            public void SetBool(string key, bool value) => values[key] = value;
            public void DeleteKey(string key) => values.Remove(key);
        }
    }
}
