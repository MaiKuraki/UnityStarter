using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class BuildDataAndRegistryTests
    {
        private const string ProjectSandboxOwnerFileName = ".recovery-registry-test-owner";

        private static readonly string[] DefaultStepIds =
        {
            BuildStepIds.HotUpdate,
            BuildStepIds.AssetContent,
            BuildStepIds.Player
        };

        private BuildData buildData;

        [SetUp]
        public void SetUp()
        {
            buildData = ScriptableObject.CreateInstance<BuildData>();
            ConfigureIdentity(buildData);
        }

        [TearDown]
        public void TearDown()
        {
            if (buildData != null)
            {
                UnityEngine.Object.DestroyImmediate(buildData);
            }
        }

        [Test]
        public void PipelineSteps_ForDefaultProfile_ReturnsBuiltInOrder()
        {
            CollectionAssert.AreEqual(DefaultStepIds, buildData.PipelineSteps);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PipelineSteps_WhenSerializedValueIsMissingOrEmpty_DoesNotCreateImplicitPlan(bool useNull)
        {
            SetSerializedPipelineSteps(useNull ? null : Array.Empty<string>());

            Assert.That(buildData.PipelineSteps, Is.Empty);

            BuildRequest request = BuildRequestFactory.CreateInteractive(
                buildData,
                BuildTarget.StandaloneWindows64,
                debugBuild: false,
                incrementality: BuildIncrementality.Clean);
            var context = new BuildExecutionContext(request, "test-run", new NoOpEventSink());
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BuildPlanCompiler.Compile(context));
            StringAssert.Contains("does not contain any steps", exception.Message);
        }

        [Test]
        public void PipelineSteps_WhenReturnedArrayIsMutated_PreservesProfileState()
        {
            string[] firstRead = buildData.PipelineSteps;
            firstRead[0] = "mutated-by-test";

            CollectionAssert.AreEqual(DefaultStepIds, buildData.PipelineSteps);
        }

        [TestCase("sign,artifacts")]
        [TestCase(" sign-artifacts")]
        public void BuildStepRegistration_RejectsIdsThatCannotRoundTripThroughCi(
            string stepId)
        {
            Assert.Throws<ArgumentException>(
                () => new BuildStepRegistrationAttribute(stepId));
        }

        [Test]
        public void BuildStepRegistration_RejectsIdsPastTheExecutionBudget()
        {
            Assert.Throws<ArgumentException>(() =>
                new BuildStepRegistrationAttribute(
                    new string(
                        'a',
                        BuildStepRegistrationAttribute.MaximumIdCharacters + 1)));
        }

        [Test]
        public void ProviderConfigurationCreation_NeverTreatsAnExistingPathAsAvailable()
        {
            Assert.That(
                BuildDataEditor.IsAssetCreationPathOccupied(
                    "Assets/Build/Editor/BuildPipeline/Authoring/BuildData.cs"),
                Is.True);
            Assert.That(
                BuildDataEditor.IsAssetCreationPathOccupied(
                    $"Assets/Build/Tests/Editor/{Guid.NewGuid():N}.asset"),
                Is.False);
        }

        [Test]
        public void BuildRequest_SnapshotsProfileScalarsAndOrderedCollections()
        {
            BuildRequest request = BuildRequestFactory.CreateInteractive(
                buildData,
                BuildTarget.StandaloneWindows64,
                debugBuild: false,
                incrementality: BuildIncrementality.Clean);

            SetSerializedPipelineSteps(new[] { "mutated-after-request" });
            var serialized = new SerializedObject(buildData);
            serialized.FindProperty("companyName").stringValue = "MutatedCompany";
            serialized.FindProperty("productName").stringValue = "MutatedProduct";
            serialized.FindProperty("applicationIdentifier").stringValue = "com.mutated.product";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(request.CompanyName, Is.EqualTo("TestCompany"));
            Assert.That(request.ProductName, Is.EqualTo("TestProduct"));
            Assert.That(request.ApplicationIdentifier, Is.EqualTo("com.example.test"));
            CollectionAssert.AreEqual(DefaultStepIds, request.StepIds);
            Assert.Throws<NotSupportedException>(
                () => ((IList<string>)request.StepIds)[0] = "mutated-through-request");
        }

        [Test]
        public void ResolveContentAdapter_WhenOptionalProviderIsNotInstalled_ReturnsNull()
        {
            const string MissingProviderId = "Build.Pipeline.Tests.Provider.NotInstalled";

            IAssetContentBuildAdapter adapter = BuildPipelineRegistry.ResolveContentAdapter(MissingProviderId);

            Assert.That(adapter, Is.Null);
        }

        [Test]
        public void ResolveAssetContentAdapter_SnapshotsOneAdapterInstancePerBuildRun()
        {
            CountingContentBuildAdapter.ConstructorCallCount = 0;
            var serialized = new UnityEditor.SerializedObject(buildData);
            serialized.FindProperty("assetContentProviderId").stringValue = CountingContentBuildAdapter.Provider;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            BuildRequest request = BuildRequestFactory.CreateInteractive(
                buildData,
                BuildTarget.StandaloneWindows64,
                debugBuild: false,
                incrementality: BuildIncrementality.Clean);
            var context = new BuildExecutionContext(request, "test-run", new NoOpEventSink());

            IAssetContentBuildAdapter first = context.ResolveAssetContentAdapter();
            IAssetContentBuildAdapter second = context.ResolveAssetContentAdapter();

            Assert.That(first, Is.SameAs(second));
            Assert.That(CountingContentBuildAdapter.ConstructorCallCount, Is.EqualTo(1));
        }

        [Test]
        public void ResolveSteps_DoesNotInstantiateUnrequestedRegisteredTypes()
        {
            ExplodingUnrequestedBuildStep.ConstructorCallCount = 0;

            IReadOnlyList<IBuildStep> steps = BuildPipelineRegistry.ResolveSteps(DefaultStepIds);

            CollectionAssert.AreEquivalent(DefaultStepIds, GetStepIds(steps));
            Assert.That(ExplodingUnrequestedBuildStep.ConstructorCallCount, Is.Zero);
        }

        [Test]
        public void GetBuildStepDescriptors_ReturnsOnlyVisibleBuiltInsWithoutInstantiatingSteps()
        {
            ExplodingUnrequestedBuildStep.ConstructorCallCount = 0;

            IReadOnlyList<BuildStepDescriptor> descriptors =
                BuildPipelineRegistry.GetBuildStepDescriptors();

            string[] descriptorIds = descriptors.Select(descriptor => descriptor.Id).ToArray();
            foreach (string builtInId in DefaultStepIds)
            {
                Assert.That(descriptorIds, Does.Contain(builtInId));
            }

            Assert.That(
                descriptorIds,
                Does.Not.Contain("build-pipeline-tests.exploding-unrequested"));
            Assert.That(ExplodingUnrequestedBuildStep.ConstructorCallCount, Is.Zero);
        }

        [Test]
        public void GetAssetContentProviderDescriptors_DeclaresStableConfigurationTypes()
        {
            IReadOnlyList<AssetContentProviderDescriptor> descriptors =
                BuildPipelineRegistry.GetAssetContentProviderDescriptors();

            AssetContentProviderDescriptor addressables = descriptors.Single(
                descriptor => string.Equals(
                    descriptor.ProviderId,
                    AssetContentProviderIds.Addressables,
                    StringComparison.Ordinal));
            AssetContentProviderDescriptor yooAsset = descriptors.Single(
                descriptor => string.Equals(
                    descriptor.ProviderId,
                    AssetContentProviderIds.YooAsset,
                    StringComparison.Ordinal));

            Assert.That(addressables.ConfigurationType, Is.EqualTo(typeof(AddressablesBuildConfig)));
            Assert.That(yooAsset.ConfigurationType, Is.EqualTo(typeof(YooAssetBuildConfig)));
        }

        [Test]
        public void ResolveSteps_OnlyInstantiatesUniqueHighestPriorityOverride()
        {
            ExplodingLowerPriorityBuildStep.ConstructorCallCount = 0;

            IReadOnlyList<IBuildStep> steps = BuildPipelineRegistry.ResolveSteps(
                new[] { HighestPriorityBuildStep.StepId });

            Assert.That(steps.Count, Is.EqualTo(1));
            Assert.That(steps[0], Is.TypeOf<HighestPriorityBuildStep>());
            Assert.That(ExplodingLowerPriorityBuildStep.ConstructorCallCount, Is.Zero);
        }

        [Test]
        public void ResolveContentAdapter_OnlyInstantiatesUniqueHighestPriorityOverride()
        {
            ExplodingLowerPriorityContentAdapter.ConstructorCallCount = 0;

            IAssetContentBuildAdapter adapter = BuildPipelineRegistry.ResolveContentAdapter(
                HighestPriorityContentAdapter.Provider);

            Assert.That(adapter, Is.TypeOf<HighestPriorityContentAdapter>());
            Assert.That(ExplodingLowerPriorityContentAdapter.ConstructorCallCount, Is.Zero);
        }

        [Test]
        public void ResolveRecoveryParticipants_DiscoversAllCoreParticipants()
        {
            IReadOnlyList<IBuildRecoveryParticipant> participants =
                BuildPipelineRegistry.ResolveRecoveryParticipants();
            string[] participantIds = participants.Select(participant => participant.Id).ToArray();

            Assert.That(participantIds, Does.Contain(AddressablesRecoveryCoordinator.ParticipantId));
            Assert.That(participantIds, Does.Contain(GlobalBuildStateRecoveryParticipant.ParticipantId));
            Assert.That(participantIds, Does.Contain(HybridCLROutputRecoveryParticipant.ParticipantId));
            Assert.That(participantIds, Does.Contain(PlayerOutputRecoveryParticipant.ParticipantId));
        }

        [Test]
        public void ResolveRecoveryParticipants_OnlyInstantiatesUniqueHighestPriorityOverride()
        {
            ExplodingLowerPriorityRecoveryParticipant.ConstructorCallCount = 0;

            IReadOnlyList<IBuildRecoveryParticipant> participants =
                BuildPipelineRegistry.ResolveRecoveryParticipants();

            IBuildRecoveryParticipant selected = participants.Single(
                participant => string.Equals(
                    participant.Id,
                    HighestPriorityRecoveryParticipant.ParticipantId,
                    StringComparison.Ordinal));
            Assert.That(selected, Is.TypeOf<HighestPriorityRecoveryParticipant>());
            Assert.That(ExplodingLowerPriorityRecoveryParticipant.ConstructorCallCount, Is.Zero);
        }

        [Test]
        public void Runner_RecoversProjectCentralStateBeforeRequestValidation()
        {
            string projectRoot = GetCurrentProjectRoot();
            string sandboxRoot = CreateProjectSandboxRoot(projectRoot);
            try
            {
                RecoveryOrderingParticipant.BeginProbe(projectRoot);
                BuildRequest request = CreateSandboxRequest(
                    projectRoot,
                    sandboxRoot,
                    companyName: string.Empty,
                    stepIds: new[] { RecoveryOrderingBuildStep.StepId });

                BuildRunResult result = new BuildPipelineRunner(
                        new NoOpEventSink(),
                        projectRoot,
                        () => false)
                    .Run(request);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(RecoveryOrderingParticipant.WasRecovered, Is.True);
                StringAssert.Contains("Company name is required", result.Failure.ToString());
            }
            finally
            {
                RecoveryOrderingParticipant.EndProbe();
                DeleteProjectSandboxRoot(projectRoot, sandboxRoot);
            }
        }

        [Test]
        public void Runner_RecoversProjectCentralStateBeforeStepApplicability()
        {
            string projectRoot = GetCurrentProjectRoot();
            string sandboxRoot = CreateProjectSandboxRoot(projectRoot);
            try
            {
                RecoveryOrderingParticipant.BeginProbe(projectRoot);
                BuildRequest request = CreateSandboxRequest(
                    projectRoot,
                    sandboxRoot,
                    companyName: "TestCompany",
                    stepIds: new[] { RecoveryOrderingBuildStep.StepId });

                BuildRunResult result = new BuildPipelineRunner(
                        new NoOpEventSink(),
                        projectRoot,
                        () => false)
                    .Run(request);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(RecoveryOrderingParticipant.WasRecovered, Is.True);
                StringAssert.Contains(
                    RecoveryOrderingBuildStep.ApplicabilitySentinel,
                    result.Failure.ToString());
                StringAssert.DoesNotContain(
                    RecoveryOrderingBuildStep.RecoveryMissingSentinel,
                    result.Failure.ToString());
            }
            finally
            {
                RecoveryOrderingParticipant.EndProbe();
                DeleteProjectSandboxRoot(projectRoot, sandboxRoot);
            }
        }

        [Test]
        public void Runner_WhenEditorIsBusy_RejectsBeforePlanCompilation()
        {
            string projectRoot = GetCurrentProjectRoot();
            string sandboxRoot = CreateProjectSandboxRoot(projectRoot);
            try
            {
                BuildRequest request = CreateSandboxRequest(
                    projectRoot,
                    sandboxRoot,
                    companyName: "TestCompany",
                    stepIds: new[] { RecoveryOrderingBuildStep.StepId });

                BuildRunResult result = new BuildPipelineRunner(
                        new NoOpEventSink(),
                        projectRoot,
                        () => true)
                    .Run(request);

                Assert.That(result.Succeeded, Is.False);
                StringAssert.Contains(
                    "Unity is compiling or updating assets",
                    result.Failure.ToString());
            }
            finally
            {
                DeleteProjectSandboxRoot(projectRoot, sandboxRoot);
            }
        }

        [Test]
        public void OptionalRecoveryStateGuard_WhenYooAssetParticipantIsUnavailableAndStateExists_FailsClosed()
        {
            string sandboxRoot = CreateSandboxRoot();
            try
            {
                string stateRoot = Path.Combine(
                    sandboxRoot,
                    ".buildpipeline",
                    "transactions",
                    "yooasset3");
                Directory.CreateDirectory(stateRoot);
                string evidencePath = Path.Combine(stateRoot, "pending.evidence");
                File.WriteAllText(evidencePath, "preserve");

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                    OptionalRecoveryStateGuard.EnsureNoUnavailableRecoveryState(
                        sandboxRoot,
                        Array.Empty<IBuildRecoveryParticipant>()));

                Assert.That(File.ReadAllText(evidencePath), Is.EqualTo("preserve"));
                StringAssert.Contains(
                    "pending YooAsset 3 publication transaction exists",
                    exception.Message);
            }
            finally
            {
                DeleteSandboxRoot(sandboxRoot);
            }
        }

        [Test]
        public void Compile_EvaluatesApplicabilityExactlyOnceAndStoresTheDecision()
        {
            SnapshotApplicabilityBuildStep.ApplicabilityCallCount = 0;
            SetSerializedPipelineSteps(new[] { SnapshotApplicabilityBuildStep.StepId });
            BuildRequest request = BuildRequestFactory.CreateInteractive(
                buildData,
                BuildTarget.StandaloneWindows64,
                debugBuild: false,
                incrementality: BuildIncrementality.Clean);
            var context = new BuildExecutionContext(request, "test-run", new NoOpEventSink());

            IReadOnlyList<CompiledBuildStep> plan = BuildPlanCompiler.Compile(context);

            Assert.That(plan.Count, Is.EqualTo(1));
            Assert.That(plan[0].IsApplicable, Is.True);
            Assert.That(SnapshotApplicabilityBuildStep.ApplicabilityCallCount, Is.EqualTo(1));
        }

        [Test]
        public void HybridClrAndCheatConflict_IsOwnedOnlyByThePlayerStep()
        {
            var hybridConfig = ScriptableObject.CreateInstance<HybridCLRBuildConfig>();
            try
            {
                var serialized = new SerializedObject(buildData);
                serialized.FindProperty("useHybridCLR").boolValue = true;
                serialized.FindProperty("cheatBuildMode").enumValueIndex =
                    (int)CheatBuildMode.Enabled;
                serialized.FindProperty("hybridCLRBuildConfig").objectReferenceValue =
                    hybridConfig;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                BuildRequest request = BuildRequestFactory.CreateInteractive(
                    buildData,
                    BuildTarget.StandaloneWindows64,
                    debugBuild: false,
                    incrementality: BuildIncrementality.Clean);
                var context = new BuildExecutionContext(
                    request,
                    "test-run",
                    new NoOpEventSink());

                IReadOnlyList<string> hotUpdateErrors =
                    new HotUpdateBuildStep().Validate(context);
                Assert.That(
                    hotUpdateErrors.Any(error => error.Contains("per-build ENABLE_CHEAT")),
                    Is.False);

                IReadOnlyList<string> playerErrors =
                    new PlayerBuildStep().Validate(context);
                Assert.That(
                    playerErrors.Any(error => error.Contains("per-build ENABLE_CHEAT")),
                    Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hybridConfig);
            }
        }

        [Test]
        public void AddressablesAdapter_ExposesProviderNeutralPlayerBuildSessionHook()
        {
            var adapter = new AddressablesContentBuildAdapter();

            Assert.That(adapter, Is.InstanceOf<IAssetContentPlayerBuildSessionFactory>());
        }

        [TestCase("DefaultPackage")]
        [TestCase("base-content_01")]
        [TestCase("content.release")]
        public void YooAssetPackageName_AcceptsRuntimeCompatibleStableTokens(string value)
        {
            Assert.That(YooAssetBuildTokenPolicy.IsValidPackageName(value), Is.True);
            Assert.DoesNotThrow(() => YooAssetBuildTokenPolicy.ValidatePackageName(value, nameof(value)));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(".")]
        [TestCase("..")]
        [TestCase(".hidden")]
        [TestCase("trailing.")]
        [TestCase("content..release")]
        [TestCase("../escape")]
        [TestCase("folder/name")]
        [TestCase("folder\\name")]
        [TestCase("C:root")]
        [TestCase("package name")]
        [TestCase("包裹")]
        [TestCase("CON")]
        [TestCase("con.data")]
        [TestCase("COM1")]
        [TestCase("lpt9.cache")]
        public void YooAssetPackageName_RejectsRuntimeIncompatibleTokens(string value)
        {
            Assert.That(YooAssetBuildTokenPolicy.IsValidPackageName(value), Is.False);
            Assert.Throws<ArgumentException>(
                () => YooAssetBuildTokenPolicy.ValidatePackageName(value, nameof(value)));
        }

        [TestCase("1")]
        [TestCase("1.0.0")]
        [TestCase("2026.07.13-release_01")]
        [TestCase("release-beta")]
        public void YooAssetPackageVersion_AcceptsRuntimeCompatibleStableTokens(string value)
        {
            Assert.That(YooAssetBuildTokenPolicy.IsValidPackageVersion(value), Is.True);
            Assert.DoesNotThrow(() => YooAssetBuildTokenPolicy.ValidatePackageVersion(value, nameof(value)));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(".")]
        [TestCase("..")]
        [TestCase("1..2")]
        [TestCase("../manifest")]
        [TestCase("1/manifest")]
        [TestCase("1\\manifest")]
        [TestCase("file:manifest")]
        [TestCase("version?query")]
        [TestCase("版本1")]
        public void YooAssetPackageVersion_RejectsRuntimeIncompatibleTokens(string value)
        {
            Assert.That(YooAssetBuildTokenPolicy.IsValidPackageVersion(value), Is.False);
            Assert.Throws<ArgumentException>(
                () => YooAssetBuildTokenPolicy.ValidatePackageVersion(value, nameof(value)));
        }

        [Test]
        public void YooAssetStableTokens_RejectValuesPastBoundsAndControlCharacters()
        {
            Assert.That(
                YooAssetBuildTokenPolicy.IsValidPackageName(
                    new string('a', YooAssetBuildTokenPolicy.MaxPackageNameLength + 1)),
                Is.False);
            Assert.That(
                YooAssetBuildTokenPolicy.IsValidPackageVersion(
                    new string('1', YooAssetBuildTokenPolicy.MaxPackageVersionLength + 1)),
                Is.False);
            Assert.That(YooAssetBuildTokenPolicy.IsValidPackageName("name" + (char)0 + "control"), Is.False);
            Assert.That(YooAssetBuildTokenPolicy.IsValidPackageVersion("version\r\nnext"), Is.False);
        }

        private void SetSerializedPipelineSteps(string[] value)
        {
            FieldInfo field = typeof(BuildData).GetField(
                "pipelineSteps",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(buildData, value);
        }

        private static string[] GetStepIds(IReadOnlyList<IBuildStep> steps)
        {
            var ids = new string[steps.Count];
            for (int index = 0; index < steps.Count; index++)
            {
                ids[index] = steps[index].Id;
            }

            return ids;
        }

        private static BuildRequest CreateSandboxRequest(
            string projectRoot,
            string sandboxRoot,
            string companyName,
            IReadOnlyList<string> stepIds)
        {
            string buildRoot = Path.Combine(sandboxRoot, "Build");
            string outputDirectory = Path.Combine(buildRoot, "Windows", "Release");
            string outputPath = Path.Combine(outputDirectory, "TestProduct.exe");
            return new BuildRequest(
                companyName,
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
                outputPath,
                outputDirectory,
                outputIsFolder: false,
                incrementality: BuildIncrementality.Clean,
                deleteDebugFiles: true,
                debugBuild: true,
                exportAndroidProject: false,
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

        private static string CreateSandboxRoot()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "UnityStarter-RecoveryRegistryTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string GetCurrentProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string CreateProjectSandboxRoot(string projectRoot)
        {
            string parent = Path.GetFullPath(Path.Combine(
                projectRoot,
                "Build",
                ".buildpipeline-tests",
                "recovery-registry"));
            string path = Path.Combine(
                parent,
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            File.WriteAllText(
                Path.Combine(path, ProjectSandboxOwnerFileName),
                Path.GetFileName(path));
            return path;
        }

        private static void DeleteProjectSandboxRoot(string projectRoot, string path)
        {
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string allowedParent = Path.GetFullPath(Path.Combine(
                normalizedProjectRoot,
                "Build",
                ".buildpipeline-tests",
                "recovery-registry"));
            string normalizedPath = Path.GetFullPath(path);
            string expectedPrefix = allowedParent.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!normalizedPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(Path.GetFileName(normalizedPath), "N", out _))
            {
                throw new InvalidOperationException(
                    $"Refusing to delete an unowned recovery-registry test sandbox: '{normalizedPath}'.");
            }

            EnsureDeletePathHasNoReparsePoints(normalizedProjectRoot, normalizedPath);
            string ownerPath = Path.Combine(normalizedPath, ProjectSandboxOwnerFileName);
            if (!File.Exists(ownerPath)
                || (File.GetAttributes(ownerPath) & FileAttributes.ReparsePoint) != 0
                || !string.Equals(
                    File.ReadAllText(ownerPath),
                    Path.GetFileName(normalizedPath),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Refusing to delete a recovery-registry test sandbox without its exact owner marker: '{normalizedPath}'.");
            }

            Directory.Delete(normalizedPath, recursive: true);
            DeleteEmptyOwnedTestDirectory(allowedParent);
            DeleteEmptyOwnedTestDirectory(Path.GetDirectoryName(allowedParent));
        }

        private static void EnsureDeletePathHasNoReparsePoints(string projectRoot, string targetPath)
        {
            string relativePath = targetPath.Substring(
                projectRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar).Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string current = projectRoot;
            foreach (string segment in relativePath.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Refusing to delete through a reparse point: '{current}'.");
                }
            }
        }

        private static void DeleteEmptyOwnedTestDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)
                || !Directory.Exists(path)
                || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
                || Directory.EnumerateFileSystemEntries(path).Any())
            {
                return;
            }

            Directory.Delete(path);
        }

        private static void DeleteSandboxRoot(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        private static void ConfigureIdentity(BuildData profile)
        {
            var serialized = new UnityEditor.SerializedObject(profile);
            serialized.FindProperty("companyName").stringValue = "TestCompany";
            serialized.FindProperty("productName").stringValue = "TestProduct";
            serialized.FindProperty("applicationIdentifier").stringValue = "com.example.test";
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private sealed class NoOpEventSink : IBuildEventSink
        {
            public void RunStarted(BuildExecutionContext context, System.Collections.Generic.IReadOnlyList<IBuildStep> plan) { }
            public void StepStarted(BuildExecutionContext context, IBuildStep step) { }
            public void StepFinished(BuildExecutionContext context, BuildStepResult result) { }
            public void RunFinished(BuildExecutionContext context, BuildRunResult result) { }
        }
    }

    [BuildStepRegistration("build-pipeline-tests.exploding-unrequested", HiddenFromAuthoring = true)]
    public sealed class ExplodingUnrequestedBuildStep : IBuildStep
    {
        public static int ConstructorCallCount;

        public ExplodingUnrequestedBuildStep()
        {
            ConstructorCallCount++;
            throw new InvalidOperationException("This constructor must not run for an unrelated build plan.");
        }

        public string Id => "build-pipeline-tests.exploding-unrequested";
        public int Priority => 0;
        public bool IsApplicable(BuildExecutionContext context) => true;
        public IReadOnlyList<string> GetRequiredStepIds(BuildExecutionContext context) => Array.Empty<string>();
        public IReadOnlyList<string> Validate(BuildExecutionContext context) => Array.Empty<string>();
        public void Execute(BuildExecutionContext context) { }
        public void Cleanup(BuildExecutionContext context) { }
    }

    [BuildStepRegistration(SnapshotApplicabilityBuildStep.StepId, HiddenFromAuthoring = true)]
    public sealed class SnapshotApplicabilityBuildStep : IBuildStep
    {
        public const string StepId = "build-pipeline-tests.applicability-snapshot";
        public static int ApplicabilityCallCount;

        public string Id => StepId;
        public int Priority => 0;
        public bool IsApplicable(BuildExecutionContext context) => ++ApplicabilityCallCount == 1;
        public IReadOnlyList<string> GetRequiredStepIds(BuildExecutionContext context) => Array.Empty<string>();
        public IReadOnlyList<string> Validate(BuildExecutionContext context) => Array.Empty<string>();
        public void Execute(BuildExecutionContext context) { }
        public void Cleanup(BuildExecutionContext context) { }
    }

    [AssetContentAdapterRegistration(CountingContentBuildAdapter.Provider)]
    public sealed class CountingContentBuildAdapter : IAssetContentBuildAdapter
    {
        public const string Provider = "build-pipeline-tests.adapter-snapshot";
        public static int ConstructorCallCount;

        public CountingContentBuildAdapter()
        {
            ConstructorCallCount++;
        }

        public string ProviderId => Provider;
        public int Priority => 0;

        public AssetContentBuildResult Validate(AssetContentBuildRequest request)
        {
            return AssetContentBuildResult.Success(Provider, "test", request.PackageVersion);
        }

        public IReadOnlyList<AssetContentBuildResult> Build(AssetContentBuildRequest request)
        {
            return new[] { Validate(request) };
        }
    }

    [BuildStepRegistration(HighestPriorityBuildStep.StepId, priority: 100, HiddenFromAuthoring = true)]
    public sealed class HighestPriorityBuildStep : IBuildStep
    {
        public const string StepId = "build-pipeline-tests.priority-override";

        public string Id => StepId;
        public int Priority => 100;
        public bool IsApplicable(BuildExecutionContext context) => true;
        public IReadOnlyList<string> GetRequiredStepIds(BuildExecutionContext context) => Array.Empty<string>();
        public IReadOnlyList<string> Validate(BuildExecutionContext context) => Array.Empty<string>();
        public void Execute(BuildExecutionContext context) { }
        public void Cleanup(BuildExecutionContext context) { }
    }

    [BuildStepRegistration(HighestPriorityBuildStep.StepId, priority: -100, HiddenFromAuthoring = true)]
    public sealed class ExplodingLowerPriorityBuildStep : IBuildStep
    {
        public static int ConstructorCallCount;

        public ExplodingLowerPriorityBuildStep()
        {
            ConstructorCallCount++;
            throw new InvalidOperationException("The lower-priority build step must never be instantiated.");
        }

        public string Id => HighestPriorityBuildStep.StepId;
        public int Priority => -100;
        public bool IsApplicable(BuildExecutionContext context) => true;
        public IReadOnlyList<string> GetRequiredStepIds(BuildExecutionContext context) => Array.Empty<string>();
        public IReadOnlyList<string> Validate(BuildExecutionContext context) => Array.Empty<string>();
        public void Execute(BuildExecutionContext context) { }
        public void Cleanup(BuildExecutionContext context) { }
    }

    [AssetContentAdapterRegistration(HighestPriorityContentAdapter.Provider, priority: 100)]
    public sealed class HighestPriorityContentAdapter : IAssetContentBuildAdapter
    {
        public const string Provider = "build-pipeline-tests.adapter-priority-override";

        public string ProviderId => Provider;
        public int Priority => 100;
        public AssetContentBuildResult Validate(AssetContentBuildRequest request) =>
            AssetContentBuildResult.Success(Provider, "test", request.PackageVersion);
        public IReadOnlyList<AssetContentBuildResult> Build(AssetContentBuildRequest request) =>
            new[] { Validate(request) };
    }

    [AssetContentAdapterRegistration(HighestPriorityContentAdapter.Provider, priority: -100)]
    public sealed class ExplodingLowerPriorityContentAdapter : IAssetContentBuildAdapter
    {
        public static int ConstructorCallCount;

        public ExplodingLowerPriorityContentAdapter()
        {
            ConstructorCallCount++;
            throw new InvalidOperationException("The lower-priority content adapter must never be instantiated.");
        }

        public string ProviderId => HighestPriorityContentAdapter.Provider;
        public int Priority => -100;
        public AssetContentBuildResult Validate(AssetContentBuildRequest request) => null;
        public IReadOnlyList<AssetContentBuildResult> Build(AssetContentBuildRequest request) => null;
    }

    [BuildRecoveryRegistration(HighestPriorityRecoveryParticipant.ParticipantId, priority: 100)]
    public sealed class HighestPriorityRecoveryParticipant : IBuildRecoveryParticipant
    {
        public const string ParticipantId = "build-pipeline-tests.recovery-priority-override";

        public string Id => ParticipantId;
        public int Priority => 100;
        public void Recover(string projectRoot) { }
    }

    [BuildRecoveryRegistration(HighestPriorityRecoveryParticipant.ParticipantId, priority: -100)]
    public sealed class ExplodingLowerPriorityRecoveryParticipant : IBuildRecoveryParticipant
    {
        public static int ConstructorCallCount;

        public ExplodingLowerPriorityRecoveryParticipant()
        {
            ConstructorCallCount++;
            throw new InvalidOperationException(
                "The lower-priority recovery participant must never be instantiated.");
        }

        public string Id => HighestPriorityRecoveryParticipant.ParticipantId;
        public int Priority => -100;
        public void Recover(string projectRoot) { }
    }

    [BuildRecoveryRegistration(RecoveryOrderingParticipant.ParticipantId)]
    public sealed class RecoveryOrderingParticipant : IBuildRecoveryParticipant
    {
        public const string ParticipantId = "build-pipeline-tests.recovery-ordering";
        private static readonly object ProbeGate = new object();
        private static string expectedProjectRoot;
        private static bool wasRecovered;

        public string Id => ParticipantId;
        public int Priority => 0;

        public static bool WasRecovered
        {
            get
            {
                lock (ProbeGate)
                {
                    return wasRecovered;
                }
            }
        }

        public void Recover(string projectRoot)
        {
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            lock (ProbeGate)
            {
                if (expectedProjectRoot != null
                    && string.Equals(
                        normalizedProjectRoot,
                        expectedProjectRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    wasRecovered = true;
                }
            }
        }

        public static void BeginProbe(string projectRoot)
        {
            lock (ProbeGate)
            {
                expectedProjectRoot = Path.GetFullPath(projectRoot);
                wasRecovered = false;
            }
        }

        public static void EndProbe()
        {
            lock (ProbeGate)
            {
                expectedProjectRoot = null;
                wasRecovered = false;
            }
        }
    }

    [BuildStepRegistration(RecoveryOrderingBuildStep.StepId, HiddenFromAuthoring = true)]
    public sealed class RecoveryOrderingBuildStep : IBuildStep
    {
        public const string StepId = "build-pipeline-tests.recovery-ordering-step";
        public const string ApplicabilitySentinel =
            "Recovery ordering step reached applicability after recovery.";
        public const string RecoveryMissingSentinel =
            "Recovery ordering step reached applicability before recovery.";

        public string Id => StepId;
        public int Priority => 0;

        public bool IsApplicable(BuildExecutionContext context)
        {
            if (!RecoveryOrderingParticipant.WasRecovered)
            {
                throw new InvalidOperationException(RecoveryMissingSentinel);
            }

            throw new InvalidOperationException(ApplicabilitySentinel);
        }

        public IReadOnlyList<string> GetRequiredStepIds(BuildExecutionContext context) =>
            Array.Empty<string>();
        public IReadOnlyList<string> Validate(BuildExecutionContext context) => Array.Empty<string>();
        public void Execute(BuildExecutionContext context) { }
        public void Cleanup(BuildExecutionContext context) { }
    }
}
