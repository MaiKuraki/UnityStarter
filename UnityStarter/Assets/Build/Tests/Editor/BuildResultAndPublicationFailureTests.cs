using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Build.Pipeline.Editor;
using Build.VersionControl.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.TestTools;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class BuildResultAndPublicationFailureTests
    {
        private string sandboxRoot;
        private BuildData buildData;
        private readonly List<string> createdAssetPaths = new List<string>();

        [SetUp]
        public void SetUp()
        {
            sandboxRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityStarter-BuildResultTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandboxRoot);
            Directory.CreateDirectory(Path.Combine(sandboxRoot, "Assets"));
            Directory.CreateDirectory(Path.Combine(sandboxRoot, "ProjectSettings"));
            string resourcesDirectory = Path.Combine(sandboxRoot, "Assets", "Resources");
            Directory.CreateDirectory(resourcesDirectory);
            File.WriteAllText(resourcesDirectory + ".meta", "test-owned-folder-meta");
            buildData = ScriptableObject.CreateInstance<BuildData>();
            var serialized = new SerializedObject(buildData);
            serialized.FindProperty("companyName").stringValue = "TestCompany";
            serialized.FindProperty("productName").stringValue = "TestProduct";
            serialized.FindProperty("applicationIdentifier").stringValue = "com.example.test";
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = createdAssetPaths.Count - 1; index >= 0; index--)
            {
                AssetDatabase.DeleteAsset(createdAssetPaths[index]);
            }

            createdAssetPaths.Clear();
            if (buildData != null)
            {
                UnityEngine.Object.DestroyImmediate(buildData);
            }

            if (Directory.Exists(sandboxRoot))
            {
                Directory.Delete(sandboxRoot, true);
            }
        }

        [Test]
        public void ManifestWriter_SerializesInvocationProvenanceAndStructuredFailureWithCurrentFormat()
        {
            string manifestPath = Path.Combine(sandboxRoot, "failure.json");
            BuildExecutionContext context = CreateContext();
            context.Version = new BuildVersionContext(
                "0.1.0",
                "0.1.0+42",
                42,
                "effective-revision",
                "42",
                "effective-branch",
                "2026-08-07T00:00:00Z",
                "ci-override",
                new VersionControlWorkspaceEvidence(
                    new VersionControlWorkspaceComponentEvidence(
                        VersionControlWorkspaceComponentStatus.Dirty,
                        2),
                    new VersionControlWorkspaceComponentEvidence(
                        VersionControlWorkspaceComponentStatus.Dirty,
                        1),
                    new VersionControlWorkspaceComponentEvidence(
                        VersionControlWorkspaceComponentStatus.Clean,
                        0),
                    new VersionControlWorkspaceComponentEvidence(
                        VersionControlWorkspaceComponentStatus.NotApplicable,
                        0)),
                BuildIdentityOrigin.ExplicitOverride,
                detectedCommitHash: "detected-revision",
                detectedCommitCount: "41",
                detectedBranch: "detected-branch",
                detectedCommitDate: "2026-08-06T00:00:00Z",
                detectedProviderId: "git",
                detectedBuildNumber: 41,
                ciProvider: "teamcity",
                ciRunId: "build-42");
            context.AddContentResult(
                context.Request.Steps[0].InvocationId,
                AssetContentBuildResult.Failure(
                    "TestProvider",
                    "BasePackage",
                    "1.2.3",
                    "BuildBundles",
                    "Bundle compilation failed.",
                    "provider stack",
                    new[] { "provider warning" }));
            var result = new BuildRunResult(
                "test-run",
                false,
                "test-output",
                manifestPath,
                Array.Empty<BuildStepResult>(),
                new InvalidOperationException("run failed"));

            InvokeManifestWrite(context, result);

            ManifestDocument manifest = JsonUtility.FromJson<ManifestDocument>(
                File.ReadAllText(manifestPath));
            Assert.That(manifest.documentType, Is.EqualTo("build-result"));
            Assert.That(manifest.target, Is.EqualTo(context.Request.Target.ToString()));
            Assert.That(
                manifest.namedBuildTarget,
                Is.EqualTo(context.Request.NamedTarget.TargetName));
            Assert.That(
                manifest.scriptingBackend,
                Is.EqualTo(context.Request.ScriptingBackend.ToString()));
            Assert.That(manifest.debugBuild, Is.EqualTo(context.Request.DebugBuild));
            Assert.That(
                manifest.buildPurpose,
                Is.EqualTo(context.Request.Purpose.ToString()));
            Assert.That(
                manifest.releaseBaselinePolicyEligible,
                Is.EqualTo(context.Request.CanPublishReleaseBaseline));
            Assert.That(manifest.detectedIdentity.hasBuildNumber, Is.True);
            Assert.That(manifest.detectedIdentity.buildNumber, Is.EqualTo(41));
            Assert.That(manifest.detectedIdentity.sourceProvider, Is.EqualTo("git"));
            Assert.That(
                manifest.detectedIdentity.sourceRevision,
                Is.EqualTo("detected-revision"));
            Assert.That(manifest.effectiveIdentity.hasBuildNumber, Is.True);
            Assert.That(manifest.effectiveIdentity.buildNumber, Is.EqualTo(42));
            Assert.That(
                manifest.effectiveIdentity.sourceProvider,
                Is.EqualTo("ci-override"));
            Assert.That(
                manifest.effectiveIdentity.sourceRevision,
                Is.EqualTo("effective-revision"));
            Assert.That(
                manifest.identityOrigin,
                Is.EqualTo(BuildIdentityOrigin.ExplicitOverride.ToString()));
            Assert.That(manifest.ciIdentity.provider, Is.EqualTo("teamcity"));
            Assert.That(manifest.ciIdentity.runId, Is.EqualTo("build-42"));
            Assert.That(manifest.sourceWorkspace.required, Is.True);
            Assert.That(manifest.sourceWorkspace.overallStatus, Is.EqualTo("Dirty"));
            Assert.That(manifest.sourceWorkspace.failureCode, Is.EqualTo("None"));
            Assert.That(manifest.sourceWorkspace.trackedChanges.status, Is.EqualTo("Dirty"));
            Assert.That(manifest.sourceWorkspace.trackedChanges.hasChangeCount, Is.True);
            Assert.That(manifest.sourceWorkspace.trackedChanges.changeCount, Is.EqualTo(2));
            Assert.That(manifest.sourceWorkspace.gitLfs.status, Is.EqualTo("NotApplicable"));
            Assert.That(
                manifest.deleteDebugFiles,
                Is.EqualTo(context.Request.DeleteDebugFiles));
            Assert.That(
                manifest.exportAndroidProject,
                Is.EqualTo(context.Request.ExportAndroidProject));
            Assert.That(
                manifest.allowExternalOutput,
                Is.EqualTo(context.Request.AllowExternalOutput));
            Assert.That(
                manifest.outputIsFolder,
                Is.EqualTo(context.Request.OutputIsFolder));
            Assert.That(manifest.buildRoot, Is.EqualTo(context.Request.BuildRoot));
            Assert.That(
                manifest.versionInfoAssetPath,
                Is.EqualTo(context.Request.VersionInfoAssetPath));
            Assert.That(
                manifest.buildScenePaths,
                Is.EqualTo(context.Request.BuildScenePaths));
            Assert.That(
                manifest.cheatBuildMode,
                Is.EqualTo(context.Request.CheatBuildMode.ToString()));
            Assert.That(manifest.cheatEnabled, Is.EqualTo(context.Request.CheatEnabled));
            Assert.That(
                manifest.playerExtensionFingerprint,
                Is.EqualTo(PlayerBuildExtensionFingerprint.ComputeForRequest(
                    context.Request)));
            Assert.That(manifest.recipeInvocations, Has.Length.EqualTo(1));
            Assert.That(manifest.recipeInvocations[0].order, Is.EqualTo(0));
            Assert.That(
                manifest.recipeInvocations[0].invocationId,
                Is.EqualTo(context.Request.Steps[0].InvocationId));
            Assert.That(
                manifest.recipeInvocations[0].stepTypeId,
                Is.EqualTo(BuildStepTypeIds.Player));
            Assert.That(
                manifest.recipeInvocations[0].incrementality,
                Is.EqualTo(context.Request.Steps[0].Incrementality.ToString()));
            Assert.That(
                manifest.recipeInvocations[0].dependencies,
                Has.Length.EqualTo(context.Request.Steps[0].Dependencies.Count));
            for (int dependencyIndex = 0;
                 dependencyIndex < context.Request.Steps[0].Dependencies.Count;
                 dependencyIndex++)
            {
                Assert.That(
                    manifest.recipeInvocations[0].dependencies[dependencyIndex].invocationId,
                    Is.EqualTo(context.Request.Steps[0].Dependencies[dependencyIndex].InvocationId));
                Assert.That(
                    manifest.recipeInvocations[0].dependencies[dependencyIndex].mode,
                    Is.EqualTo(context.Request.Steps[0].Dependencies[dependencyIndex].Mode.ToString()));
            }
            Assert.That(manifest.recipeInvocations[0].hasConfiguration, Is.False);
            Assert.That(manifest.recipeInvocations[0].configurationAssetPath, Is.Empty);
            Assert.That(manifest.recipeInvocations[0].configurationAssetSha256, Is.Empty);
            Assert.That(manifest.recipeInvocations[0].configurationDependencyHash, Is.Empty);
            Assert.That(manifest.content, Has.Length.EqualTo(1));
            Assert.That(
                manifest.content[0].invocationId,
                Is.EqualTo(context.Request.Steps[0].InvocationId));
            Assert.That(manifest.content[0].succeeded, Is.False);
            Assert.That(manifest.content[0].failedTask, Is.EqualTo("BuildBundles"));
            Assert.That(manifest.content[0].errorInfo, Is.EqualTo("Bundle compilation failed."));
            Assert.That(manifest.content[0].errorStack, Is.EqualTo("provider stack"));
        }

        [Test]
        public void ManifestWriter_LocalReleasePreview_RecordsDirtyEvidenceAsNonBaselineEligible()
        {
            SetRecipe(new[]
            {
                new BuildRecipeInvocation(
                    "player-client",
                    BuildStepTypeIds.Player,
                    enabled: true,
                    incrementality: BuildIncrementality.Clean)
            });
            BuildRequest request = BuildRequestFactory.CreateLocalReleasePreview(
                buildData,
                BuildTarget.StandaloneWindows64,
                invocationIdsOverride: null);
            var context = new BuildExecutionContext(
                request,
                "local-preview-run",
                new NoOpEventSink());
            var dirty = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.Dirty,
                3);
            var clean = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.Clean,
                0);
            var notApplicable = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.NotApplicable,
                0);
            context.Version = new BuildVersionContext(
                "0.1.0",
                "0.1.0+42",
                42,
                "0123456789ab",
                "42",
                "feature/local-preview",
                "2026-08-12T00:00:00Z",
                "Git",
                sourceWorkspace: new VersionControlWorkspaceEvidence(
                    dirty,
                    clean,
                    notApplicable,
                    notApplicable));
            string manifestPath = Path.Combine(sandboxRoot, "local-preview.json");
            var result = new BuildRunResult(
                context.RunId,
                succeeded: true,
                request.OutputPath,
                manifestPath,
                Array.Empty<BuildStepResult>(),
                failure: null);

            InvokeManifestWrite(context, result);

            ManifestDocument manifest = JsonUtility.FromJson<ManifestDocument>(
                File.ReadAllText(manifestPath));
            Assert.That(manifest.documentType, Is.EqualTo("build-result"));
            Assert.That(
                manifest.buildPurpose,
                Is.EqualTo(BuildPurpose.LocalReleasePreview.ToString()));
            Assert.That(manifest.releaseBaselinePolicyEligible, Is.False);
            Assert.That(manifest.sourceWorkspace.required, Is.False);
            Assert.That(manifest.sourceWorkspace.overallStatus, Is.EqualTo("Dirty"));
            Assert.That(manifest.sourceWorkspace.trackedChanges.changeCount, Is.EqualTo(3));
            StringAssert.Contains(
                Path.Combine("Build", "LocalPreview", "Windows", "Release"),
                manifest.outputPath);
        }

        [Test]
        public void ManifestWriter_PersistentConfiguration_RecordsStableAssetIdentityAndHashes()
        {
            AddressablesBuildConfig configuration = CreatePersistentConfiguration();
            SetRecipe(new[]
            {
                new BuildRecipeInvocation(
                    BuildStepTypeIds.AssetContent,
                    BuildStepTypeIds.AssetContent,
                    enabled: true,
                    configuration: configuration)
            });
            string configurationPath = AssetDatabase.GetAssetPath(configuration);
            Assert.That(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    configuration,
                    out string expectedGuid,
                    out long expectedLocalFileId),
                Is.True);
            string expectedAssetHash = ComputeSha256(Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                configurationPath));
            string manifestPath = Path.Combine(sandboxRoot, "provenance.json");
            BuildExecutionContext context = CreateContext();
            var result = new BuildRunResult(
                "test-run",
                true,
                "test-output",
                manifestPath,
                Array.Empty<BuildStepResult>(),
                null);

            InvokeManifestWrite(context, result);

            Assert.That(context.RecipeProvenance, Has.Count.EqualTo(1));
            Assert.That(
                context.RecipeProvenance[0].ConfigurationAssetGuid,
                Is.EqualTo(expectedGuid),
                "Captured configuration GUID must remain distinct from content hashes.");
            Assert.That(
                context.RecipeProvenance[0].ConfigurationAssetSha256,
                Is.EqualTo(expectedAssetHash),
                "Captured configuration asset hash must match the persisted asset bytes.");

            ManifestDocument manifest = JsonUtility.FromJson<ManifestDocument>(
                File.ReadAllText(manifestPath));
            Assert.That(manifest.recipeInvocations, Has.Length.EqualTo(1));
            RecipeStepDocument entry = manifest.recipeInvocations[0];
            Assert.That(entry.stepTypeId, Is.EqualTo(BuildStepTypeIds.AssetContent));
            Assert.That(entry.hasConfiguration, Is.True);
            Assert.That(entry.configurationAssetPath, Is.EqualTo(configurationPath));
            Assert.That(
                entry.configurationAssetGuid,
                Is.EqualTo(expectedGuid),
                "Manifest configuration GUID must preserve captured provenance identity.");
            Assert.That(
                entry.configurationLocalFileId,
                Is.EqualTo(expectedLocalFileId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(
                entry.configurationType,
                Is.EqualTo(
                    typeof(AddressablesBuildConfig).FullName + ", " +
                    typeof(AddressablesBuildConfig).Assembly.GetName().Name));
            Assert.That(entry.configurationAssetSha256, Is.EqualTo(expectedAssetHash));
            Assert.That(
                entry.configurationDependencyHash,
                Is.EqualTo(context.RecipeProvenance[0].ConfigurationDependencyHash));
            Assert.That(entry.configurationDependencyHash, Has.Length.EqualTo(64));
            Assert.That(entry.configurationDependencyCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(entry.validationError, Is.Empty);
        }

        [Test]
        public void ManifestWriter_NonPersistentConfiguration_FailsBeforeWritingManifest()
        {
            var configuration = ScriptableObject.CreateInstance<AddressablesBuildConfig>();
            try
            {
                string manifestPath = Path.Combine(sandboxRoot, "transient.json");
                BuildRequest request = CreateContentOnlyRequest(configuration);
                var context = new BuildExecutionContext(
                    request,
                    "test-run",
                    new NoOpEventSink());
                var result = new BuildRunResult(
                    "test-run",
                    true,
                    "test-output",
                    manifestPath,
                    Array.Empty<BuildStepResult>(),
                    null);

                BuildFailedException exception = Assert.Throws<BuildFailedException>(
                    () => InvokeManifestWrite(context, result));

                StringAssert.Contains("not a persistent Unity asset", exception.Message);
                Assert.That(File.Exists(manifestPath), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void ManifestWriter_SubAssetConfiguration_FailsBeforeWritingManifest()
        {
            AddressablesBuildConfig configuration =
                CreatePersistentSubAssetConfiguration();
            BuildRequest request = CreateContentOnlyRequest(configuration);
            var context = new BuildExecutionContext(
                request,
                "test-run",
                new NoOpEventSink());
            string manifestPath = Path.Combine(sandboxRoot, "sub-asset.json");
            var result = new BuildRunResult(
                "test-run",
                true,
                "test-output",
                manifestPath,
                Array.Empty<BuildStepResult>(),
                null);

            BuildFailedException exception = Assert.Throws<BuildFailedException>(
                () => InvokeManifestWrite(context, result));

            StringAssert.Contains("must be the main asset", exception.Message);
            Assert.That(File.Exists(manifestPath), Is.False);
        }

        [Test]
        public void ManifestWriter_DirtyConfiguration_FailsWithoutSavingIt()
        {
            AddressablesBuildConfig configuration = CreatePersistentConfiguration();
            SetRecipe(new[]
            {
                new BuildRecipeInvocation(
                    BuildStepTypeIds.AssetContent,
                    BuildStepTypeIds.AssetContent,
                    enabled: true,
                    configuration: configuration)
            });
            BuildExecutionContext context = CreateContext();
            string configurationPath = AssetDatabase.GetAssetPath(configuration);
            string absolutePath = Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                configurationPath);
            byte[] savedBytes = File.ReadAllBytes(absolutePath);
            configuration.copyToOutputDirectory =
                !configuration.copyToOutputDirectory;
            EditorUtility.SetDirty(configuration);

            string manifestPath = Path.Combine(sandboxRoot, "dirty.json");
            var result = new BuildRunResult(
                "test-run",
                true,
                "test-output",
                manifestPath,
                Array.Empty<BuildStepResult>(),
                null);

            BuildFailedException exception = Assert.Throws<BuildFailedException>(
                () => InvokeManifestWrite(context, result));

            StringAssert.Contains("unsaved changes", exception.Message);
            Assert.That(File.Exists(manifestPath), Is.False);
            Assert.That(File.ReadAllBytes(absolutePath), Is.EqualTo(savedBytes));
            Assert.That(EditorUtility.IsDirty(configuration), Is.True);
        }

        [Test]
        public void AssetContentBuildResult_SnapshotsProviderOwnedCollections()
        {
            var artifacts = new List<string> { "first.bundle" };
            var warnings = new List<string> { "first warning" };
            AssetContentBuildResult result = AssetContentBuildResult.Success(
                "TestProvider",
                "BasePackage",
                "1.2.3",
                producedArtifacts: artifacts,
                warnings: warnings);

            artifacts[0] = "mutated.bundle";
            artifacts.Add("second.bundle");
            warnings.Clear();

            Assert.That(result.ProducedArtifacts, Is.EqualTo(new[] { "first.bundle" }));
            Assert.That(result.Warnings, Is.EqualTo(new[] { "first warning" }));
        }

        [Test]
        public void ManifestWriter_WhenAtomicMoveFails_RemovesOwnedTemporaryFile()
        {
            string manifestPath = Path.Combine(sandboxRoot, "existing.json");
            File.WriteAllText(manifestPath, "existing");
            BuildExecutionContext context = CreateContext();
            var result = new BuildRunResult(
                "test-run",
                true,
                "test-output",
                manifestPath,
                Array.Empty<BuildStepResult>(),
                null);

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                () => InvokeManifestWriteRaw(context, result));

            Assert.That(exception.InnerException, Is.TypeOf<IOException>());
            Assert.That(File.ReadAllText(manifestPath), Is.EqualTo("existing"));
            Assert.That(File.Exists(manifestPath + ".tmp"), Is.False);
        }

        [Test]
        public void ManifestWriter_WhenTemporarySiblingAlreadyExists_PreservesForeignEvidence()
        {
            string manifestPath = Path.Combine(sandboxRoot, "blocked.json");
            string temporaryPath = manifestPath + ".tmp";
            File.WriteAllText(temporaryPath, "preserve");
            BuildExecutionContext context = CreateContext();
            var result = new BuildRunResult(
                "test-run",
                true,
                "test-output",
                manifestPath,
                Array.Empty<BuildStepResult>(),
                null);

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                () => InvokeManifestWriteRaw(context, result));

            Assert.That(exception.InnerException, Is.TypeOf<IOException>());
            Assert.That(File.Exists(manifestPath), Is.False);
            Assert.That(File.ReadAllText(temporaryPath), Is.EqualTo("preserve"));
        }

        [Test]
        public void Runner_WhenCompletionCallbacksFail_DoesNotRewriteTerminalEvidence()
        {
            BuildRequest request = CreateSandboxRequest(companyName: string.Empty);
            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex(
                    "run-finished sink failure"));

            BuildRunResult result = new BuildPipelineRunner(
                    new ThrowingCompletionEventSink(),
                    sandboxRoot,
                    () => false,
                    BuildTestVersionResolver.ResolveClean)
                .Run(request);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.Not.Null);
            Assert.That(File.Exists(result.ResultManifestPath), Is.True);
            string returnedFailure = result.Failure.ToString();
            StringAssert.Contains("Company name is required", returnedFailure);
            StringAssert.DoesNotContain("sink failure", returnedFailure);
            Assert.That(result.NonFatalFailures.Count, Is.EqualTo(1));
            StringAssert.Contains(
                "step-finished sink failure",
                result.NonFatalFailures[0].ToString());

            ManifestDocument manifest = JsonUtility.FromJson<ManifestDocument>(
                File.ReadAllText(result.ResultManifestPath));
            Assert.That(manifest.succeeded, Is.EqualTo(result.Succeeded));
            Assert.That(manifest.failure, Is.EqualTo(returnedFailure));
            Assert.That(manifest.nonFatalFailures.Length, Is.EqualTo(1));
            StringAssert.Contains(
                "step-finished sink failure",
                manifest.nonFatalFailures[0]);
        }

        [Test]
        public void Runner_ReleaseDirtySource_FailsBeforeStepOrOutputMutationAndWritesTerminalEvidence()
        {
            BuildRequest request = CreateSandboxRequest();
            var dirty = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.Dirty,
                2);
            var clean = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.Clean,
                0);
            var notApplicable = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.NotApplicable,
                0);
            var workspace = new VersionControlWorkspaceEvidence(
                dirty,
                clean,
                notApplicable,
                notApplicable);
            var sink = new CountingEventSink();

            BuildRunResult result = new BuildPipelineRunner(
                    sink,
                    sandboxRoot,
                    () => false,
                    buildRequest => new BuildVersionContext(
                        buildRequest.ApplicationVersion,
                        buildRequest.ApplicationVersion + ".42",
                        42,
                        "0123456789ab",
                        "42",
                        "release",
                        "2026-08-12T00:00:00Z",
                        "Git",
                        sourceWorkspace: workspace))
                .Run(request);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Steps, Has.Count.EqualTo(1));
            Assert.That(result.Steps[0].InvocationId, Is.EqualTo("preflight"));
            Assert.That(result.Steps[0].Status, Is.EqualTo(BuildStepStatus.Failed));
            StringAssert.Contains("requires a verified clean source workspace", result.Failure.ToString());
            Assert.That(sink.RunStartedCount, Is.Zero);
            Assert.That(sink.StepStartedCount, Is.Zero);
            Assert.That(Directory.Exists(request.OutputDirectory), Is.False);
            Assert.That(File.Exists(request.OutputPath), Is.False);
            Assert.That(File.Exists(result.ResultManifestPath), Is.True);

            ManifestDocument manifest = JsonUtility.FromJson<ManifestDocument>(
                File.ReadAllText(result.ResultManifestPath));
            Assert.That(manifest.documentType, Is.EqualTo("build-result"));
            Assert.That(manifest.succeeded, Is.False);
            Assert.That(manifest.buildPurpose, Is.EqualTo(BuildPurpose.Release.ToString()));
            Assert.That(manifest.releaseBaselinePolicyEligible, Is.True);
            Assert.That(manifest.sourceWorkspace.policy, Is.EqualTo("RequireClean"));
            Assert.That(manifest.sourceWorkspace.required, Is.True);
            Assert.That(manifest.sourceWorkspace.overallStatus, Is.EqualTo("Dirty"));
            Assert.That(manifest.sourceWorkspace.failureCode, Is.EqualTo("None"));
            Assert.That(manifest.sourceWorkspace.trackedChanges.status, Is.EqualTo("Dirty"));
            Assert.That(manifest.sourceWorkspace.trackedChanges.hasChangeCount, Is.True);
            Assert.That(manifest.sourceWorkspace.trackedChanges.changeCount, Is.EqualTo(2));
            Assert.That(manifest.sourceWorkspace.untrackedChanges.status, Is.EqualTo("Clean"));
            Assert.That(manifest.sourceWorkspace.submodules.status, Is.EqualTo("NotApplicable"));
            Assert.That(manifest.sourceWorkspace.gitLfs.status, Is.EqualTo("NotApplicable"));
        }

        [Test]
        public void BuildRunResult_PostTerminalObserverDiagnostic_PreservesTerminalSuccess()
        {
            var terminalResult = new BuildRunResult(
                "committed-run",
                true,
                "published-output",
                "result.json",
                Array.Empty<BuildStepResult>(),
                null);
            var observerFailure = new IOException("observer destination became unavailable");

            BuildRunResult diagnosed = terminalResult.WithNonFatalFailure(
                new InvalidOperationException(
                    "A diagnostic observer failed after the terminal decision.",
                    observerFailure));

            Assert.That(diagnosed.Succeeded, Is.True);
            Assert.That(diagnosed.Failure, Is.Null);
            Assert.That(diagnosed.OutputPath, Is.EqualTo("published-output"));
            Assert.That(diagnosed.NonFatalFailures, Has.Count.EqualTo(1));
            Assert.That(
                diagnosed.NonFatalFailures[0].InnerException,
                Is.SameAs(observerFailure));
        }

        [Test]
        public void RunnerEventNotification_RequiredEvidenceFailureIsNotDowngraded()
        {
            MethodInfo method = typeof(BuildPipelineRunner).GetMethod(
                "NotifyEventSink",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var diagnostics = new List<Exception>();
            Action callback = () => throw new BuildResultEvidenceException(
                "required evidence failed",
                new IOException("disk unavailable"));

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                () => method.Invoke(
                    null,
                    new object[] { callback, "RunStarted", diagnostics }));

            Assert.That(exception.InnerException, Is.TypeOf<BuildResultEvidenceException>());
            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void RunnerTerminalNotification_RequiredEvidenceFailureIsNotDowngraded()
        {
            MethodInfo method = typeof(BuildPipelineRunner).GetMethod(
                "NotifyTerminalEventSink",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            Action callback = () => throw new BuildResultEvidenceException(
                "required terminal evidence failed",
                new IOException("disk unavailable"));

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                () => method.Invoke(
                    null,
                    new object[] { callback, "RunFinished" }));

            Assert.That(exception.InnerException, Is.TypeOf<BuildResultEvidenceException>());
        }

        [Test]
        public void Runner_WhenRequiredManifestWriteFails_NotifiesFinalFailureExactlyOnce()
        {
            BuildRequest request = CreateSandboxRequest(companyName: string.Empty);
            var sink = new ManifestBlockingEventSink();
            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex(
                    "IOException:.*already exists"));

            BuildRunResult result = new BuildPipelineRunner(
                    sink,
                    sandboxRoot,
                    () => false,
                    BuildTestVersionResolver.ResolveClean)
                .Run(request);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(sink.RunFinishedCount, Is.EqualTo(1));
            Assert.That(sink.TerminalResult, Is.SameAs(result));
            StringAssert.Contains(
                "Failed to persist the required build result manifest",
                result.Failure.ToString());
            Assert.That(File.Exists(result.ResultManifestPath), Is.False);
            Assert.That(Directory.Exists(result.ResultManifestPath), Is.True);
        }

        [Test]
        public void Runner_PublicEntryRejectsForeignProjectBeforeRecoveryOrManifestWrite()
        {
            BuildRequest request = CreateSandboxRequest();

            BuildFailedException exception = Assert.Throws<BuildFailedException>(
                () => new BuildPipelineRunner(new NoOpEventSink()).Run(request));

            StringAssert.Contains(
                "must identify the Unity project loaded by this Editor process",
                exception.Message);
            Assert.That(
                Directory.Exists(Path.Combine(sandboxRoot, ".buildpipeline")),
                Is.False);
        }

        [Test]
        public void Runner_DirectAndroidExportRequestWithoutPlayer_FailsDuringPreflight()
        {
            BuildRequest request = CreateAndroidExportRequest(
                new[] { BuildStepTypeIds.AssetContent });

            BuildRunResult result = new BuildPipelineRunner(
                    new NoOpEventSink(),
                    sandboxRoot,
                    () => false,
                    BuildTestVersionResolver.ResolveClean)
                .Run(request);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.Not.Null);
            StringAssert.Contains(
                $"requires a '{BuildStepTypeIds.Player}' invocation",
                result.Failure.ToString());
            Assert.That(result.Steps, Has.Count.EqualTo(1));
            Assert.That(result.Steps[0].InvocationId, Is.EqualTo("preflight"));
        }

        [Test]
        public void Runner_DirectContentOnlyRequestWithoutConfiguration_FailsDuringPreflight()
        {
            BuildRequest request = CreateContentOnlyRequest(configuration: null);

            BuildRunResult result = new BuildPipelineRunner(
                    new NoOpEventSink(),
                    sandboxRoot,
                    () => false,
                    BuildTestVersionResolver.ResolveClean)
                .Run(request);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.Not.Null);
            StringAssert.Contains(
                "AssetContentBuildConfiguration configuration asset",
                result.Failure.ToString());
            Assert.That(result.Steps, Has.Count.EqualTo(1));
            Assert.That(result.Steps[0].InvocationId, Is.EqualTo("preflight"));
        }

        [Test]
        public void Runner_NonPersistentConfiguration_FailsPreflightAndRecordsInvalidProvenance()
        {
            var configuration = ScriptableObject.CreateInstance<AddressablesBuildConfig>();
            try
            {
                BuildRequest request = CreateContentOnlyRequest(configuration);

                BuildRunResult result = new BuildPipelineRunner(
                        new NoOpEventSink(),
                        sandboxRoot,
                        () => false,
                        BuildTestVersionResolver.ResolveClean)
                    .Run(request);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Failure, Is.Not.Null);
                StringAssert.Contains(
                    "not a persistent Unity asset",
                    result.Failure.ToString());
                Assert.That(File.Exists(result.ResultManifestPath), Is.True);

                ManifestDocument manifest = JsonUtility.FromJson<ManifestDocument>(
                    File.ReadAllText(result.ResultManifestPath));
                Assert.That(manifest.recipeInvocations, Has.Length.EqualTo(1));
                Assert.That(
                    manifest.recipeInvocations[0].stepTypeId,
                    Is.EqualTo(BuildStepTypeIds.AssetContent));
                Assert.That(manifest.recipeInvocations[0].hasConfiguration, Is.True);
                StringAssert.Contains(
                    "not a persistent Unity asset",
                    manifest.recipeInvocations[0].validationError);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void PlayerFailureCombiner_PreservesFailedReportBeforeSessionRestoreFailure()
        {
            var reportFailure = new UnityEditor.Build.BuildFailedException(
                "Player build failed with result 'Failed'.");
            var restoreFailure = new IOException("session restore failed");

            Exception combined = InvokeCombinePlayerBuildFailures(
                reportFailure,
                restoreFailure);

            Assert.That(combined, Is.TypeOf<AggregateException>());
            var aggregate = (AggregateException)combined;
            Assert.That(aggregate.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(aggregate.InnerExceptions[0], Is.SameAs(reportFailure));
            Assert.That(aggregate.InnerExceptions[1], Is.SameAs(restoreFailure));
        }

        private BuildExecutionContext CreateContext()
        {
            BuildRequest request = BuildRequestFactory.CreateInteractive(
                buildData,
                BuildTarget.StandaloneWindows64,
                debugBuild: false);
            return new BuildExecutionContext(request, "test-run", new NoOpEventSink());
        }

        private AddressablesBuildConfig CreatePersistentConfiguration()
        {
            string assetPath =
                "Assets/Build/Tests/Editor/BuildResultProvenance-" +
                Guid.NewGuid().ToString("N") +
                ".asset";
            var configuration = ScriptableObject.CreateInstance<AddressablesBuildConfig>();
            AssetDatabase.CreateAsset(configuration, assetPath);
            AssetDatabase.SaveAssetIfDirty(configuration);
            createdAssetPaths.Add(assetPath);
            Assert.That(EditorUtility.IsDirty(configuration), Is.False);
            return configuration;
        }

        private AddressablesBuildConfig CreatePersistentSubAssetConfiguration()
        {
            AddressablesBuildConfig mainAsset = CreatePersistentConfiguration();
            var subAsset = ScriptableObject.CreateInstance<AddressablesBuildConfig>();
            subAsset.name = "SubAssetConfiguration";
            AssetDatabase.AddObjectToAsset(subAsset, mainAsset);
            AssetDatabase.SaveAssetIfDirty(subAsset);
            AssetDatabase.SaveAssetIfDirty(mainAsset);
            Assert.That(EditorUtility.IsPersistent(subAsset), Is.True);
            Assert.That(EditorUtility.IsDirty(subAsset), Is.False);
            return subAsset;
        }

        private void SetRecipe(IReadOnlyList<BuildRecipeInvocation> entries)
        {
            var serialized = new SerializedObject(buildData);
            SerializedProperty recipe = serialized.FindProperty("recipeInvocations");
            recipe.arraySize = entries.Count;
            for (int index = 0; index < entries.Count; index++)
            {
                SerializedProperty element = recipe.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("enabled").boolValue = entries[index].Enabled;
                element.FindPropertyRelative("invocationId").stringValue =
                    entries[index].InvocationId;
                element.FindPropertyRelative("stepTypeId").stringValue =
                    entries[index].StepTypeId;
                element.FindPropertyRelative("configuration").objectReferenceValue =
                    entries[index].Configuration;
                element.FindPropertyRelative("incrementality").enumValueIndex =
                    (int)entries[index].Incrementality;
                SerializedProperty dependencies = element.FindPropertyRelative("dependencies");
                dependencies.arraySize = entries[index].Dependencies.Count;
                for (int dependencyIndex = 0;
                     dependencyIndex < entries[index].Dependencies.Count;
                     dependencyIndex++)
                {
                    BuildInvocationDependency value = entries[index].Dependencies[dependencyIndex];
                    SerializedProperty dependency =
                        dependencies.GetArrayElementAtIndex(dependencyIndex);
                    dependency.FindPropertyRelative("invocationId").stringValue =
                        value.InvocationId;
                    dependency.FindPropertyRelative("mode").enumValueIndex =
                        (int)value.Mode;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha256.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    builder.Append(hash[index].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private BuildRequest CreateSandboxRequest(string companyName = "TestCompany")
        {
            string buildRoot = Path.Combine(sandboxRoot, "Build");
            string outputDirectory = Path.Combine(buildRoot, "Windows", "Release");
            string outputPath = Path.Combine(outputDirectory, "TestProduct.exe");
            return new BuildRequest(
                companyName,
                "TestProduct",
                "com.test.product",
                "Assets/Resources/VersionInfoData.asset",
                Array.Empty<string>(),
                CheatBuildMode.Disabled,
                BuildTarget.StandaloneWindows64,
                UnityEditor.Build.NamedBuildTarget.Standalone,
                UnityEditor.ScriptingImplementation.Mono2x,
                sandboxRoot,
                buildRoot,
                outputPath,
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
                steps: new[]
                {
                    new BuildStepInvocation(BuildStepTypeIds.Player, BuildStepTypeIds.Player)
                },
                sourceCleanlinessPolicy: BuildSourceCleanlinessPolicy.RequireClean,
                purpose: BuildPurpose.Release);
        }

        private BuildRequest CreateAndroidExportRequest(IReadOnlyList<string> stepIds)
        {
            string buildRoot = Path.Combine(sandboxRoot, "Build");
            string outputPath = Path.Combine(buildRoot, "Android", "Release", "GradleProject");
            return new BuildRequest(
                "TestCompany",
                "TestProduct",
                "com.test.product",
                "Assets/Resources/VersionInfoData.asset",
                Array.Empty<string>(),
                CheatBuildMode.Disabled,
                BuildTarget.Android,
                NamedBuildTarget.Android,
                ScriptingImplementation.Mono2x,
                sandboxRoot,
                buildRoot,
                outputPath,
                outputPath,
                outputIsFolder: true,
                deleteDebugFiles: true,
                debugBuild: false,
                exportAndroidProject: true,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: false,
                applicationVersion: "0.1.0",
                identityOverride: BuildIdentityOverride.Empty,
                steps: CreateInvocations(stepIds),
                sourceCleanlinessPolicy: BuildSourceCleanlinessPolicy.RequireClean,
                purpose: BuildPurpose.Release);
        }

        private BuildRequest CreateContentOnlyRequest(
            AssetContentBuildConfiguration configuration)
        {
            string buildRoot = Path.Combine(sandboxRoot, "Build");
            string outputDirectory = Path.Combine(buildRoot, "Windows", "Release");
            string outputPath = Path.Combine(outputDirectory, "TestProduct.exe");
            return new BuildRequest(
                "TestCompany",
                "TestProduct",
                "com.test.product",
                "Assets/Resources/VersionInfoData.asset",
                Array.Empty<string>(),
                CheatBuildMode.Disabled,
                BuildTarget.StandaloneWindows64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.Mono2x,
                sandboxRoot,
                buildRoot,
                outputPath,
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
                steps: new[]
                {
                    new BuildStepInvocation(
                        BuildStepTypeIds.AssetContent,
                        BuildStepTypeIds.AssetContent,
                        configuration)
                },
                sourceCleanlinessPolicy: BuildSourceCleanlinessPolicy.RequireClean,
                purpose: BuildPurpose.Release);
        }

        private static BuildStepInvocation[] CreateInvocations(
            IReadOnlyList<string> stepIds)
        {
            var result = new BuildStepInvocation[stepIds.Count];
            for (int index = 0; index < stepIds.Count; index++)
            {
                result[index] = new BuildStepInvocation(stepIds[index], stepIds[index]);
            }

            return result;
        }

        private static void InvokeManifestWrite(
            BuildExecutionContext context,
            BuildRunResult result)
        {
            try
            {
                InvokeManifestWriteRaw(context, result);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw exception.InnerException;
            }
        }

        private static void InvokeManifestWriteRaw(
            BuildExecutionContext context,
            BuildRunResult result)
        {
            Type writerType = typeof(BuildPipelineRunner).Assembly.GetType(
                "Build.Pipeline.Editor.BuildResultManifestWriter",
                throwOnError: true);
            MethodInfo writeMethod = writerType.GetMethod(
                "Write",
                BindingFlags.Static | BindingFlags.Public);
            Assert.That(writeMethod, Is.Not.Null);
            writeMethod.Invoke(null, new object[] { context, result });
        }

        private static Exception InvokeCombinePlayerBuildFailures(
            Exception playerBuildFailure,
            Exception sessionRestoreFailure)
        {
            MethodInfo combineMethod = typeof(PlayerBuildStep).GetMethod(
                "CombinePlayerBuildFailures",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(combineMethod, Is.Not.Null);
            return (Exception)combineMethod.Invoke(
                null,
                new object[] { playerBuildFailure, sessionRestoreFailure });
        }

        [Serializable]
        private sealed class ManifestDocument
        {
            public string documentType = string.Empty;
            public string target = string.Empty;
            public string namedBuildTarget = string.Empty;
            public string scriptingBackend = string.Empty;
            public bool debugBuild = false;
            public string buildPurpose = string.Empty;
            public bool releaseBaselinePolicyEligible = false;
            public bool deleteDebugFiles = false;
            public bool exportAndroidProject = false;
            public bool allowExternalOutput = false;
            public bool outputIsFolder = false;
            public BuildIdentityDocument detectedIdentity = new BuildIdentityDocument();
            public BuildIdentityDocument effectiveIdentity = new BuildIdentityDocument();
            public string identityOrigin = string.Empty;
            public CiIdentityDocument ciIdentity = new CiIdentityDocument();
            public SourceWorkspaceDocument sourceWorkspace = new SourceWorkspaceDocument();
            public string buildRoot = string.Empty;
            public string outputPath = string.Empty;
            public string versionInfoAssetPath = string.Empty;
            public string[] buildScenePaths = Array.Empty<string>();
            public string cheatBuildMode = string.Empty;
            public bool cheatEnabled = false;
            public string playerExtensionFingerprint = string.Empty;
            public bool succeeded = false;
            public string failure = string.Empty;
            public string[] nonFatalFailures = Array.Empty<string>();
            public RecipeStepDocument[] recipeInvocations = Array.Empty<RecipeStepDocument>();
            public ContentDocument[] content = Array.Empty<ContentDocument>();
        }

        [Serializable]
        private sealed class SourceWorkspaceDocument
        {
            public string policy = string.Empty;
            public bool required = false;
            public string overallStatus = string.Empty;
            public string failureCode = string.Empty;
            public WorkspaceComponentDocument trackedChanges = new WorkspaceComponentDocument();
            public WorkspaceComponentDocument untrackedChanges = new WorkspaceComponentDocument();
            public WorkspaceComponentDocument submodules = new WorkspaceComponentDocument();
            public WorkspaceComponentDocument gitLfs = new WorkspaceComponentDocument();
        }

        [Serializable]
        private sealed class WorkspaceComponentDocument
        {
            public string status = string.Empty;
            public bool hasChangeCount = false;
            public int changeCount = 0;
        }

        [Serializable]
        private sealed class RecipeStepDocument
        {
            public int order = 0;
            public string invocationId = string.Empty;
            public string stepTypeId = string.Empty;
            public string incrementality = string.Empty;
            public DependencyDocument[] dependencies = Array.Empty<DependencyDocument>();
            public bool hasConfiguration = false;
            public string configurationAssetPath = string.Empty;
            public string configurationAssetGuid = string.Empty;
            public string configurationLocalFileId = string.Empty;
            public string configurationType = string.Empty;
            public string configurationAssetSha256 = string.Empty;
            public string configurationDependencyHash = string.Empty;
            public int configurationDependencyCount = 0;
            public string validationError = string.Empty;
        }

        [Serializable]
        private sealed class ContentDocument
        {
            public string invocationId = string.Empty;
            public bool succeeded = false;
            public string failedTask = string.Empty;
            public string errorInfo = string.Empty;
            public string errorStack = string.Empty;
        }

        [Serializable]
        private sealed class DependencyDocument
        {
            public string invocationId = string.Empty;
            public string mode = string.Empty;
        }

        [Serializable]
        private sealed class BuildIdentityDocument
        {
            public bool hasBuildNumber = false;
            public long buildNumber = 0;
            public string sourceProvider = string.Empty;
            public string sourceRevision = string.Empty;
            public string sourceBranch = string.Empty;
            public string sourceCommitCount = string.Empty;
            public string sourceCommitDate = string.Empty;
        }

        [Serializable]
        private sealed class CiIdentityDocument
        {
            public string provider = string.Empty;
            public string runId = string.Empty;
        }

        private sealed class NoOpEventSink : IBuildEventSink
        {
            public void RunStarted(
                BuildExecutionContext context,
                System.Collections.Generic.IReadOnlyList<CompiledBuildStep> plan) { }

            public void StepStarted(BuildExecutionContext context, CompiledBuildStep step) { }
            public void StepFinished(BuildExecutionContext context, BuildStepResult result) { }
            public void RunFinished(BuildExecutionContext context, BuildRunResult result) { }
        }

        private sealed class CountingEventSink : IBuildEventSink
        {
            public int RunStartedCount { get; private set; }
            public int StepStartedCount { get; private set; }

            public void RunStarted(
                BuildExecutionContext context,
                IReadOnlyList<CompiledBuildStep> plan)
            {
                RunStartedCount++;
            }

            public void StepStarted(BuildExecutionContext context, CompiledBuildStep step)
            {
                StepStartedCount++;
            }

            public void StepFinished(BuildExecutionContext context, BuildStepResult result) { }
            public void RunFinished(BuildExecutionContext context, BuildRunResult result) { }
        }

        private sealed class ThrowingCompletionEventSink : IBuildEventSink
        {
            public void RunStarted(
                BuildExecutionContext context,
                System.Collections.Generic.IReadOnlyList<CompiledBuildStep> plan) { }

            public void StepStarted(BuildExecutionContext context, CompiledBuildStep step) { }

            public void StepFinished(BuildExecutionContext context, BuildStepResult result)
            {
                throw new InvalidOperationException("step-finished sink failure");
            }

            public void RunFinished(BuildExecutionContext context, BuildRunResult result)
            {
                throw new InvalidOperationException("run-finished sink failure");
            }
        }

        private sealed class ManifestBlockingEventSink : IBuildEventSink
        {
            private bool blocked;

            public int RunFinishedCount { get; private set; }
            public BuildRunResult TerminalResult { get; private set; }

            public void RunStarted(
                BuildExecutionContext context,
                System.Collections.Generic.IReadOnlyList<CompiledBuildStep> plan) { }

            public void StepStarted(BuildExecutionContext context, CompiledBuildStep step) { }

            public void StepFinished(BuildExecutionContext context, BuildStepResult result)
            {
                if (blocked)
                {
                    return;
                }

                blocked = true;
                Directory.CreateDirectory(
                    BuildResultManifestWriter.GetManifestPath(
                        context.Request,
                        context.RunId));
            }

            public void RunFinished(BuildExecutionContext context, BuildRunResult result)
            {
                RunFinishedCount++;
                TerminalResult = result;
            }
        }
    }
}
