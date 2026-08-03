using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class BuildResultAndPublicationFailureTests
    {
        private string sandboxRoot;
        private BuildData buildData;

        [SetUp]
        public void SetUp()
        {
            sandboxRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityStarter-BuildResultTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandboxRoot);
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
        public void ManifestWriter_SerializesStructuredContentFailureWithSchemaThree()
        {
            string manifestPath = Path.Combine(sandboxRoot, "failure.json");
            BuildExecutionContext context = CreateContext();
            context.AddContentResult(AssetContentBuildResult.Failure(
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
            Assert.That(manifest.schemaVersion, Is.EqualTo("3"));
            Assert.That(manifest.content, Has.Length.EqualTo(1));
            Assert.That(manifest.content[0].succeeded, Is.False);
            Assert.That(manifest.content[0].failedTask, Is.EqualTo("BuildBundles"));
            Assert.That(manifest.content[0].errorInfo, Is.EqualTo("Bundle compilation failed."));
            Assert.That(manifest.content[0].errorStack, Is.EqualTo("provider stack"));
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
        public void Runner_WhenCompletionCallbacksFail_IsolatesObserverDiagnosticsFromBuildStatus()
        {
            BuildRequest request = CreateSandboxRequest(companyName: string.Empty);

            BuildRunResult result = new BuildPipelineRunner(
                    new ThrowingCompletionEventSink(),
                    sandboxRoot,
                    () => false)
                .Run(request);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.Not.Null);
            Assert.That(File.Exists(result.ResultManifestPath), Is.True);
            string returnedFailure = result.Failure.ToString();
            StringAssert.Contains("Company name is required", returnedFailure);
            StringAssert.DoesNotContain("sink failure", returnedFailure);
            Assert.That(result.ObserverFailures.Count, Is.EqualTo(2));
            StringAssert.Contains(
                "step-finished sink failure",
                result.ObserverFailures[0].ToString());
            StringAssert.Contains(
                "run-finished sink failure",
                result.ObserverFailures[1].ToString());

            ManifestDocument manifest = JsonUtility.FromJson<ManifestDocument>(
                File.ReadAllText(result.ResultManifestPath));
            Assert.That(manifest.succeeded, Is.EqualTo(result.Succeeded));
            Assert.That(manifest.failure, Is.EqualTo(returnedFailure));
            Assert.That(manifest.observerFailures.Length, Is.EqualTo(2));
            StringAssert.Contains(
                "step-finished sink failure",
                manifest.observerFailures[0]);
            StringAssert.Contains(
                "run-finished sink failure",
                manifest.observerFailures[1]);
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
                new[] { BuildStepIds.AssetContent });

            BuildRunResult result = new BuildPipelineRunner(
                    new NoOpEventSink(),
                    sandboxRoot,
                    () => false)
                .Run(request);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.Not.Null);
            StringAssert.Contains(
                $"requires the '{BuildStepIds.Player}' step",
                result.Failure.ToString());
            Assert.That(result.Steps, Has.Count.EqualTo(1));
            Assert.That(result.Steps[0].StepId, Is.EqualTo("preflight"));
        }

        [Test]
        public void Runner_DirectContentOnlyRequestWithoutProvider_FailsDuringPreflight()
        {
            var hybridConfiguration = ScriptableObject.CreateInstance<HybridCLRBuildConfig>();
            try
            {
                BuildRequest request = CreateContentOnlyRequestWithoutProvider(
                    hybridConfiguration);

                BuildRunResult result = new BuildPipelineRunner(
                        new NoOpEventSink(),
                        sandboxRoot,
                        () => false)
                    .Run(request);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Failure, Is.Not.Null);
                StringAssert.Contains(
                    "requires both an Asset Content Provider and its Configuration",
                    result.Failure.ToString());
                Assert.That(result.Steps, Has.Count.EqualTo(1));
                Assert.That(result.Steps[0].StepId, Is.EqualTo("preflight"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hybridConfiguration);
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
                debugBuild: false,
                incrementality: BuildIncrementality.Clean);
            return new BuildExecutionContext(request, "test-run", new NoOpEventSink());
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
                null,
                BuildTarget.StandaloneWindows64,
                UnityEditor.Build.NamedBuildTarget.Standalone,
                UnityEditor.ScriptingImplementation.Mono2x,
                sandboxRoot,
                buildRoot,
                outputPath,
                outputDirectory,
                outputIsFolder: false,
                incrementality: BuildIncrementality.Clean,
                deleteDebugFiles: true,
                debugBuild: false,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: false,
                applicationVersion: "0.1.0",
                assetContentProviderId: string.Empty,
                assetContentConfiguration: null,
                useHybridClr: false,
                enablePlayerObfuscation: false,
                stepIds: new[] { BuildStepIds.Player });
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
                null,
                BuildTarget.Android,
                NamedBuildTarget.Android,
                ScriptingImplementation.Mono2x,
                sandboxRoot,
                buildRoot,
                outputPath,
                outputPath,
                outputIsFolder: true,
                incrementality: BuildIncrementality.Clean,
                deleteDebugFiles: true,
                debugBuild: false,
                exportAndroidProject: true,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: false,
                applicationVersion: "0.1.0",
                assetContentProviderId: string.Empty,
                assetContentConfiguration: null,
                useHybridClr: false,
                enablePlayerObfuscation: false,
                stepIds: stepIds);
        }

        private BuildRequest CreateContentOnlyRequestWithoutProvider(
            HybridCLRBuildConfig hybridConfiguration)
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
                hybridConfiguration,
                BuildTarget.StandaloneWindows64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.Mono2x,
                sandboxRoot,
                buildRoot,
                outputPath,
                outputDirectory,
                outputIsFolder: false,
                incrementality: BuildIncrementality.Clean,
                deleteDebugFiles: true,
                debugBuild: false,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: false,
                applicationVersion: "0.1.0",
                assetContentProviderId: string.Empty,
                assetContentConfiguration: null,
                useHybridClr: true,
                enablePlayerObfuscation: false,
                stepIds: new[]
                {
                    BuildStepIds.HotUpdate,
                    BuildStepIds.AssetContent
                });
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
            public string schemaVersion = string.Empty;
            public bool succeeded = false;
            public string failure = string.Empty;
            public string[] observerFailures = Array.Empty<string>();
            public ContentDocument[] content = Array.Empty<ContentDocument>();
        }

        [Serializable]
        private sealed class ContentDocument
        {
            public bool succeeded = false;
            public string failedTask = string.Empty;
            public string errorInfo = string.Empty;
            public string errorStack = string.Empty;
        }

        private sealed class NoOpEventSink : IBuildEventSink
        {
            public void RunStarted(
                BuildExecutionContext context,
                System.Collections.Generic.IReadOnlyList<IBuildStep> plan) { }

            public void StepStarted(BuildExecutionContext context, IBuildStep step) { }
            public void StepFinished(BuildExecutionContext context, BuildStepResult result) { }
            public void RunFinished(BuildExecutionContext context, BuildRunResult result) { }
        }

        private sealed class ThrowingCompletionEventSink : IBuildEventSink
        {
            public void RunStarted(
                BuildExecutionContext context,
                System.Collections.Generic.IReadOnlyList<IBuildStep> plan) { }

            public void StepStarted(BuildExecutionContext context, IBuildStep step) { }

            public void StepFinished(BuildExecutionContext context, BuildStepResult result)
            {
                throw new InvalidOperationException("step-finished sink failure");
            }

            public void RunFinished(BuildExecutionContext context, BuildRunResult result)
            {
                throw new InvalidOperationException("run-finished sink failure");
            }
        }
    }
}
