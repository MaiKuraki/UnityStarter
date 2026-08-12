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
    public sealed class PlayerBuildExtensionTests
    {
        private string assetFolder;

        [SetUp]
        public void SetUp()
        {
            assetFolder = "Assets/__BuildPlayerExtensionTests_" +
                          Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder(
                "Assets",
                Path.GetFileName(assetFolder));
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(assetFolder))
            {
                AssetDatabase.DeleteAsset(assetFolder);
            }
        }

        [Test]
        public void EmptyConfiguration_HasStableFingerprint()
        {
            string first = PlayerBuildExtensionFingerprint.Compute(null);
            string second = PlayerBuildExtensionFingerprint.Compute(null);

            Assert.That(first, Has.Length.EqualTo(64));
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void EmptyConfigurationAsset_MatchesNoConfiguration()
        {
            PlayerBuildConfiguration configuration =
                CreateAsset<PlayerBuildConfiguration>("PlayerBuildConfiguration.asset");
            AssetDatabase.SaveAssets();

            Assert.That(
                PlayerBuildExtensionFingerprint.Compute(configuration),
                Is.EqualTo(PlayerBuildExtensionFingerprint.Compute(null)));
        }

        [Test]
        public void FingerprintAssetBudget_RejectsAggregateOverflowWithoutReadingFiles()
        {
            long current = PlayerBuildExtensionFingerprint.MaximumTotalExtensionAssetBytes
                           - PlayerBuildExtensionFingerprint.MaximumExtensionAssetBytes;
            long accepted = PlayerBuildExtensionFingerprint.AddAssetBytesToBudget(
                current,
                PlayerBuildExtensionFingerprint.MaximumExtensionAssetBytes,
                "Accepted extension");
            Assert.That(
                accepted,
                Is.EqualTo(PlayerBuildExtensionFingerprint.MaximumTotalExtensionAssetBytes));

            IOException exception = Assert.Throws<IOException>(() =>
                PlayerBuildExtensionFingerprint.AddAssetBytesToBudget(
                    accepted,
                    1,
                    "Overflow extension"));
            Assert.That(exception.Message, Does.Contain("aggregate"));
        }

        [Test]
        public void ContextFingerprintSnapshot_IsSetOnceAndRejectsMismatch()
        {
            var context = new BuildExecutionContext(
                CreateRequest(),
                "player-fingerprint-snapshot",
                new ConsoleBuildEventSink());
            string first = new string('a', 64);

            Assert.DoesNotThrow(() => context.SetPlayerExtensionFingerprint(first));
            Assert.DoesNotThrow(() => context.SetPlayerExtensionFingerprint(first));
            Assert.That(
                context.TryGetPlayerExtensionFingerprint(out string captured),
                Is.True);
            Assert.That(captured, Is.EqualTo(first));
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                context.SetPlayerExtensionFingerprint(new string('b', 64)));
            Assert.That(exception.Message, Does.Contain("immutable run snapshot"));
            Assert.Throws<ArgumentException>(() =>
                context.SetPlayerExtensionFingerprint(
                    PlayerBuildExtensionFingerprint.InvalidEvidencePrefix +
                    new string('c', 64)));
        }

        [Test]
        public void Registry_ResolvesProviderScopedFakeAdapter()
        {
            FakePlayerExtensionConfiguration configuration =
                ScriptableObject.CreateInstance<FakePlayerExtensionConfiguration>();
            try
            {
                IPlayerBuildExtensionAdapter adapter =
                    PlayerBuildExtensionRegistry.ResolveAdapter(configuration);

                Assert.That(adapter, Is.TypeOf<FakePlayerExtensionAdapter>());
                Assert.That(
                    adapter.CompatibilityId,
                    Is.EqualTo(FakePlayerExtensionAdapter.CompatibilityIdValue));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [TestCase("Invalid Compatibility")]
        [TestCase(" valid-compatibility ")]
        [TestCase("")]
        public void Registration_InvalidCompatibilityId_FailsClosed(
            string compatibilityId)
        {
            Assert.Throws<ArgumentException>(() =>
                new PlayerBuildExtensionAdapterRegistrationAttribute(
                    "valid-provider",
                    compatibilityId));
        }

        [Test]
        public void Registry_RuntimeCompatibilityIdMismatch_FailsClosed()
        {
            CompatibilityMismatchPlayerExtensionConfiguration configuration =
                ScriptableObject.CreateInstance<CompatibilityMismatchPlayerExtensionConfiguration>();
            try
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => PlayerBuildExtensionRegistry.ResolveAdapter(configuration));

                Assert.That(exception.Message, Does.Contain("CompatibilityId"));
                Assert.That(exception.Message, Does.Contain("does not match"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void EnvironmentRequest_UsesProviderIdentitySnapshot()
        {
            MutableProviderPlayerExtensionConfiguration configuration =
                ScriptableObject.CreateInstance<MutableProviderPlayerExtensionConfiguration>();
            try
            {
                BuildRequest buildRequest = CreateRequest();
                var invocation = new BuildStepInvocation(
                    BuildStepTypeIds.Player,
                    BuildStepTypeIds.Player);
                configuration.Provider = "snapshot-provider";
                var extensionRequest = new PlayerBuildExtensionRequest(
                    buildRequest,
                    invocation,
                    configuration);
                configuration.Provider = "mutated-provider";
                var environmentRequest = new PlayerBuildEnvironmentRequest(
                    buildRequest,
                    invocation,
                    Array.Empty<AssetContentBuildRequest>(),
                    new[] { extensionRequest });

                Assert.That(
                    environmentRequest.HasPlayerExtension("snapshot-provider"),
                    Is.True);
                Assert.That(
                    environmentRequest.HasPlayerExtension("mutated-provider"),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void Registry_DuplicateProviderAdapters_FailClosed()
        {
            DuplicatePlayerExtensionConfiguration configuration =
                ScriptableObject.CreateInstance<DuplicatePlayerExtensionConfiguration>();
            try
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => PlayerBuildExtensionRegistry.ResolveAdapter(configuration));

                Assert.That(exception.Message, Does.Contain("Multiple"));
                Assert.That(exception.Message, Does.Contain("globally unique"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void PlayerPreflight_WhenExtensionAdapterIsMissing_FailsClosed()
        {
            PlayerBuildConfiguration playerConfiguration = CreateAsset<PlayerBuildConfiguration>(
                "PlayerBuildConfiguration.asset");
            UnregisteredPlayerExtensionConfiguration extension =
                CreateAsset<UnregisteredPlayerExtensionConfiguration>("Unregistered.asset");
            SetExtensions(playerConfiguration, extension);
            AssetDatabase.SaveAssets();
            BuildRequest request = CreateRequest(playerConfiguration);
            var context = new BuildExecutionContext(
                request,
                "player-extension-missing-adapter",
                new ConsoleBuildEventSink());

            IReadOnlyList<string> errors = new PlayerBuildStep().Validate(
                context,
                request.Steps[0]);

            Assert.That(
                string.Join("\n", errors),
                Does.Contain("No Player extension adapter is registered"));
        }

        [Test]
        public void ResultEvidence_WhenAdapterIsMissing_PreservesPreflightFailure()
        {
            UnregisteredPlayerExtensionConfiguration extension =
                CreateAsset<UnregisteredPlayerExtensionConfiguration>("Unregistered.asset");

            AssertInvalidPreflightEvidence(
                extension,
                "No Player extension adapter is registered");
        }

        [Test]
        public void ResultEvidence_WhenAdapterRegistrationIsDuplicate_PreservesPreflightFailure()
        {
            DuplicatePlayerExtensionConfiguration extension =
                CreateAsset<DuplicatePlayerExtensionConfiguration>("Duplicate.asset");

            AssertInvalidPreflightEvidence(extension, "globally unique");
        }

        [Test]
        public void ResultWriter_ReusesCapturedFingerprintWithoutResolvingAdapterAgain()
        {
            PlayerBuildConfiguration playerConfiguration = CreateAsset<PlayerBuildConfiguration>(
                "PlayerBuildConfiguration.asset");
            FakePlayerExtensionConfiguration extension =
                CreateAsset<FakePlayerExtensionConfiguration>("Extension.asset");
            SetExtensions(playerConfiguration, extension);
            AssetDatabase.SaveAssets();
            BuildRequest request = CreateRequest(playerConfiguration);
            var context = new BuildExecutionContext(
                request,
                "captured-player-extension-fingerprint",
                new ConsoleBuildEventSink());
            FakePlayerExtensionAdapter.ResetConstructionCount();
            string fingerprint = PlayerBuildExtensionFingerprint.Compute(playerConfiguration);
            context.SetPlayerExtensionFingerprint(fingerprint);
            int constructionsAfterCapture =
                FakePlayerExtensionAdapter.ConstructionCount;
            Assert.That(constructionsAfterCapture, Is.GreaterThan(0));

            string manifestPath = Path.Combine(
                Path.GetTempPath(),
                "BuildPipelineCapturedPlayerExtensionEvidence-" +
                Guid.NewGuid().ToString("N") +
                ".json");
            try
            {
                var result = new BuildRunResult(
                    context.RunId,
                    succeeded: true,
                    request.OutputPath,
                    manifestPath,
                    Array.Empty<BuildStepResult>(),
                    failure: null);

                context.SealForPublication();
                BuildResultManifestSnapshot snapshot =
                    BuildResultManifestWriter.FreezeForPublication(
                        context,
                        result);
                Assert.That(
                    FakePlayerExtensionAdapter.ConstructionCount,
                    Is.EqualTo(constructionsAfterCapture));
                BuildResultManifestWriter.ValidatePublicationCapacity(snapshot);
                Assert.That(File.Exists(manifestPath), Is.False);
                Assert.That(
                    FakePlayerExtensionAdapter.ConstructionCount,
                    Is.EqualTo(constructionsAfterCapture));
                BuildResultManifestWriter.Write(snapshot, result);
                EvidenceManifestRecord manifest = JsonUtility.FromJson<EvidenceManifestRecord>(
                    File.ReadAllText(manifestPath));
                Assert.That(manifest.playerExtensionFingerprint, Is.EqualTo(fingerprint));
                Assert.That(
                    FakePlayerExtensionAdapter.ConstructionCount,
                    Is.EqualTo(constructionsAfterCapture));
            }
            finally
            {
                if (File.Exists(manifestPath))
                {
                    File.Delete(manifestPath);
                }
            }
        }

        [Test]
        public void Fingerprint_WhenReferencedExtensionChanges_Changes()
        {
            PlayerBuildConfiguration playerConfiguration = CreateAsset<PlayerBuildConfiguration>(
                "PlayerBuildConfiguration.asset");
            FakePlayerExtensionConfiguration extension =
                CreateAsset<FakePlayerExtensionConfiguration>("Extension.asset");
            SetExtensions(playerConfiguration, extension);
            AssetDatabase.SaveAssets();
            string before = PlayerBuildExtensionFingerprint.Compute(playerConfiguration);

            var serialized = new SerializedObject(extension);
            serialized.FindProperty("revision").intValue++;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(extension);
            AssetDatabase.SaveAssets();
            string after = PlayerBuildExtensionFingerprint.Compute(playerConfiguration);

            Assert.That(after, Has.Length.EqualTo(64));
            Assert.That(after, Is.Not.EqualTo(before));
        }

        [Test]
        public void Fingerprint_DuplicateConfiguredProvider_FailsClosed()
        {
            PlayerBuildConfiguration playerConfiguration = CreateAsset<PlayerBuildConfiguration>(
                "PlayerBuildConfiguration.asset");
            FakePlayerExtensionConfiguration first =
                CreateAsset<FakePlayerExtensionConfiguration>("First.asset");
            FakePlayerExtensionConfiguration second =
                CreateAsset<FakePlayerExtensionConfiguration>("Second.asset");
            SetExtensions(playerConfiguration, first, second);
            AssetDatabase.SaveAssets();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => PlayerBuildExtensionFingerprint.Compute(playerConfiguration));

            Assert.That(exception.Message, Does.Contain("configured more than once"));
        }

        [Test]
        public void Fingerprint_DuplicateRegisteredAdapters_FailsClosed()
        {
            PlayerBuildConfiguration playerConfiguration = CreateAsset<PlayerBuildConfiguration>(
                "PlayerBuildConfiguration.asset");
            DuplicatePlayerExtensionConfiguration extension =
                CreateAsset<DuplicatePlayerExtensionConfiguration>("Duplicate.asset");
            SetExtensions(playerConfiguration, extension);
            AssetDatabase.SaveAssets();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => PlayerBuildExtensionFingerprint.Compute(playerConfiguration));

            Assert.That(exception.Message, Does.Contain("Multiple"));
            Assert.That(exception.Message, Does.Contain("globally unique"));
        }

        [Test]
        public void Fingerprint_RuntimeCompatibilityIdMismatch_FailsClosed()
        {
            PlayerBuildConfiguration playerConfiguration = CreateAsset<PlayerBuildConfiguration>(
                "PlayerBuildConfiguration.asset");
            CompatibilityMismatchPlayerExtensionConfiguration extension =
                CreateAsset<CompatibilityMismatchPlayerExtensionConfiguration>(
                    "CompatibilityMismatch.asset");
            SetExtensions(playerConfiguration, extension);
            AssetDatabase.SaveAssets();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => PlayerBuildExtensionFingerprint.Compute(playerConfiguration));

            Assert.That(exception.Message, Does.Contain("CompatibilityId"));
            Assert.That(exception.Message, Does.Contain("does not match"));
        }

        [Test]
        public void AuthoringGuard_ReportsDirtyReferencedPlayerExtension()
        {
            BuildData profile = CreateAsset<BuildData>("BuildData.asset");
            PlayerBuildConfiguration playerConfiguration = CreateAsset<PlayerBuildConfiguration>(
                "PlayerBuildConfiguration.asset");
            FakePlayerExtensionConfiguration extension =
                CreateAsset<FakePlayerExtensionConfiguration>("Extension.asset");
            SetExtensions(playerConfiguration, extension);
            SetPlayerConfiguration(profile, playerConfiguration);
            AssetDatabase.SaveAssets();

            var serialized = new SerializedObject(extension);
            serialized.FindProperty("revision").intValue++;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            IReadOnlyList<UnityEngine.Object> dirty =
                BuildAuthoringAssetGuard.GetDirtyAssets(profile);

            Assert.That(dirty, Does.Contain(extension));
        }

        [Test]
        public void RecipeProvenance_TracksReferencedExtensionAssetChanges()
        {
            PlayerBuildConfiguration playerConfiguration = CreateAsset<PlayerBuildConfiguration>(
                "PlayerBuildConfiguration.asset");
            FakePlayerExtensionConfiguration extension =
                CreateAsset<FakePlayerExtensionConfiguration>("Extension.asset");
            SetExtensions(playerConfiguration, extension);
            AssetDatabase.SaveAssets();
            BuildRequest request = CreateRequest(playerConfiguration);

            BuildRecipeProvenanceEntry before =
                BuildRecipeProvenanceCapture.Capture(request).Entries[0];
            var serialized = new SerializedObject(extension);
            serialized.FindProperty("revision").intValue++;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(extension);
            AssetDatabase.SaveAssets();
            BuildRecipeProvenanceEntry after =
                BuildRecipeProvenanceCapture.Capture(request).Entries[0];

            Assert.That(before.ConfigurationDependencyCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(
                after.ConfigurationDependencyHash,
                Is.Not.EqualTo(before.ConfigurationDependencyHash));
        }

        [Test]
        public void ObfuzAdapter_WhenPackageIsMissing_FailsPreflightWithoutCompilationDependency()
        {
            Assume.That(ObfuzIntegrator.IsBaseObfuzAvailable(), Is.False);
            ObfuzPlayerBuildExtensionConfiguration configuration =
                ScriptableObject.CreateInstance<ObfuzPlayerBuildExtensionConfiguration>();
            BuildRequest request = CreateRequest();
            var invocation = new BuildStepInvocation(
                BuildStepTypeIds.Player,
                BuildStepTypeIds.Player);
            try
            {
                var adapter = new ObfuzPlayerBuildExtensionAdapter();
                IReadOnlyList<string> errors = adapter.Validate(
                    new PlayerBuildExtensionRequest(
                        request,
                        invocation,
                        configuration));

                Assert.That(errors, Has.Count.GreaterThan(0));
                Assert.That(string.Join("\n", errors), Does.Contain("unavailable"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        private T CreateAsset<T>(string fileName) where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetFolder + "/" + fileName);
            return asset;
        }

        private static void SetExtensions(
            PlayerBuildConfiguration configuration,
            params PlayerBuildExtensionConfiguration[] extensions)
        {
            var serialized = new SerializedObject(configuration);
            SerializedProperty list = serialized.FindProperty("extensions");
            list.arraySize = extensions.Length;
            for (int index = 0; index < extensions.Length; index++)
            {
                list.GetArrayElementAtIndex(index).objectReferenceValue = extensions[index];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(configuration);
        }

        private static void SetPlayerConfiguration(
            BuildData profile,
            PlayerBuildConfiguration configuration)
        {
            var serialized = new SerializedObject(profile);
            SerializedProperty invocations = serialized.FindProperty("recipeInvocations");
            bool assigned = false;
            for (int index = 0; index < invocations.arraySize; index++)
            {
                SerializedProperty invocation = invocations.GetArrayElementAtIndex(index);
                if (!string.Equals(
                        invocation.FindPropertyRelative("stepTypeId").stringValue,
                        BuildStepTypeIds.Player,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                invocation.FindPropertyRelative("configuration").objectReferenceValue =
                    configuration;
                assigned = true;
                break;
            }

            Assert.That(assigned, Is.True);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
        }

        private void AssertInvalidPreflightEvidence(
            PlayerBuildExtensionConfiguration extension,
            string expectedFailureFragment)
        {
            PlayerBuildConfiguration playerConfiguration = CreateAsset<PlayerBuildConfiguration>(
                "PlayerBuildConfiguration.asset");
            SetExtensions(playerConfiguration, extension);
            AssetDatabase.SaveAssets();
            BuildRequest request = CreateRequest(playerConfiguration);
            InvalidOperationException preflightFailure = Assert.Throws<InvalidOperationException>(
                () => PlayerBuildExtensionFingerprint.Compute(playerConfiguration));
            Assert.That(
                preflightFailure.Message,
                Does.Contain(expectedFailureFragment));
            string evidenceFingerprint =
                PlayerBuildExtensionFingerprint.ComputeForEvidence(request);
            Assert.That(
                evidenceFingerprint,
                Does.StartWith(PlayerBuildExtensionFingerprint.InvalidEvidencePrefix));
            Assert.That(
                PlayerBuildExtensionFingerprint.ComputeForEvidence(request),
                Is.EqualTo(evidenceFingerprint));

            string manifestPath = Path.Combine(
                Path.GetTempPath(),
                "BuildPipelineInvalidPlayerExtensionEvidence-" +
                Guid.NewGuid().ToString("N") +
                ".json");
            try
            {
                var context = new BuildExecutionContext(
                    request,
                    "invalid-player-extension-evidence",
                    new ConsoleBuildEventSink());
                var result = new BuildRunResult(
                    "invalid-player-extension-evidence",
                    succeeded: false,
                    request.OutputPath,
                    manifestPath,
                    new[]
                    {
                        new BuildStepResult(
                            "preflight",
                            "pipeline-preflight",
                            BuildStepStatus.Failed,
                            TimeSpan.Zero,
                            preflightFailure.Message,
                            preflightFailure)
                    },
                    preflightFailure);

                Assert.DoesNotThrow(() => BuildResultManifestWriter.Write(context, result));
                EvidenceManifestRecord manifest = JsonUtility.FromJson<EvidenceManifestRecord>(
                    File.ReadAllText(manifestPath));
                Assert.That(manifest, Is.Not.Null);
                Assert.That(manifest.succeeded, Is.False);
                Assert.That(manifest.failure, Does.Contain(expectedFailureFragment));
                Assert.That(
                    manifest.playerExtensionFingerprint,
                    Is.EqualTo(evidenceFingerprint));
            }
            finally
            {
                if (File.Exists(manifestPath))
                {
                    File.Delete(manifestPath);
                }
            }
        }

        private static BuildRequest CreateRequest(
            PlayerBuildConfiguration configuration = null)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string buildRoot = Path.Combine(projectRoot, "Build");
            string outputDirectory = Path.Combine(buildRoot, "PlayerExtensionTests");
            return new BuildRequest(
                "Company",
                "Product",
                "com.example.product",
                "Assets/Build/Runtime/Resources/VersionInfoData.asset",
                Array.Empty<string>(),
                CheatBuildMode.Disabled,
                BuildTarget.StandaloneWindows64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.Mono2x,
                projectRoot,
                buildRoot,
                Path.Combine(outputDirectory, "Product.exe"),
                outputDirectory,
                outputIsFolder: false,
                deleteDebugFiles: true,
                debugBuild: false,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: true,
                applicationVersion: "1.0.0",
                identityOverride: BuildIdentityOverride.Empty,
                steps: new[]
                {
                    new BuildStepInvocation(
                        BuildStepTypeIds.Player,
                        BuildStepTypeIds.Player,
                        configuration)
                },
                sourceCleanlinessPolicy: BuildSourceCleanlinessPolicy.RequireClean,
                purpose: BuildPurpose.Release);
        }

        [PlayerBuildExtensionAdapterRegistration(
            FakePlayerExtensionConfiguration.ProviderIdValue,
            FakePlayerExtensionAdapter.CompatibilityIdValue,
            ConfigurationType = typeof(FakePlayerExtensionConfiguration))]
        public sealed class FakePlayerExtensionAdapter : IPlayerBuildExtensionAdapter
        {
            internal const string CompatibilityIdValue = "test-player-extension";
            internal static int ConstructionCount { get; private set; }

            public FakePlayerExtensionAdapter()
            {
                ConstructionCount++;
            }

            public string ProviderId => FakePlayerExtensionConfiguration.ProviderIdValue;
            public string CompatibilityId => CompatibilityIdValue;

            internal static void ResetConstructionCount()
            {
                ConstructionCount = 0;
            }

            public IReadOnlyList<string> Validate(PlayerBuildExtensionRequest request)
            {
                return Array.Empty<string>();
            }

            public IDisposable BeginPlayerBuild(PlayerBuildExtensionRequest request)
            {
                return null;
            }
        }

        [PlayerBuildExtensionAdapterRegistration(
            DuplicatePlayerExtensionConfiguration.ProviderIdValue,
            "duplicate-player-extension-a",
            ConfigurationType = typeof(DuplicatePlayerExtensionConfiguration))]
        public sealed class DuplicatePlayerExtensionAdapterA : IPlayerBuildExtensionAdapter
        {
            public string ProviderId => DuplicatePlayerExtensionConfiguration.ProviderIdValue;
            public string CompatibilityId => "duplicate-player-extension-a";
            public IReadOnlyList<string> Validate(PlayerBuildExtensionRequest request) =>
                Array.Empty<string>();
            public IDisposable BeginPlayerBuild(PlayerBuildExtensionRequest request) => null;
        }

        [PlayerBuildExtensionAdapterRegistration(
            DuplicatePlayerExtensionConfiguration.ProviderIdValue,
            "duplicate-player-extension-b",
            ConfigurationType = typeof(DuplicatePlayerExtensionConfiguration))]
        public sealed class DuplicatePlayerExtensionAdapterB : IPlayerBuildExtensionAdapter
        {
            public string ProviderId => DuplicatePlayerExtensionConfiguration.ProviderIdValue;
            public string CompatibilityId => "duplicate-player-extension-b";
            public IReadOnlyList<string> Validate(PlayerBuildExtensionRequest request) =>
                Array.Empty<string>();
            public IDisposable BeginPlayerBuild(PlayerBuildExtensionRequest request) => null;
        }

        public sealed class MutableProviderPlayerExtensionConfiguration :
            PlayerBuildExtensionConfiguration
        {
            public string Provider { get; set; }

            public override string ProviderId => Provider;
        }

        [PlayerBuildExtensionAdapterRegistration(
            CompatibilityMismatchPlayerExtensionConfiguration.ProviderIdValue,
            "registered-adapter",
            ConfigurationType = typeof(CompatibilityMismatchPlayerExtensionConfiguration))]
        public sealed class CompatibilityMismatchPlayerExtensionAdapter :
            IPlayerBuildExtensionAdapter
        {
            public string ProviderId =>
                CompatibilityMismatchPlayerExtensionConfiguration.ProviderIdValue;
            public string CompatibilityId => "different-runtime-adapter";
            public IReadOnlyList<string> Validate(PlayerBuildExtensionRequest request) =>
                Array.Empty<string>();
            public IDisposable BeginPlayerBuild(PlayerBuildExtensionRequest request) => null;
        }

        [Serializable]
        private sealed class EvidenceManifestRecord
        {
            public bool succeeded = true;
            public string failure = string.Empty;
            public string playerExtensionFingerprint = string.Empty;
        }
    }
}
