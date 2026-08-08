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

        private static readonly string[] DefaultStepTypeIds =
        {
            BuildStepTypeIds.HotUpdate,
            BuildStepTypeIds.AssetContent,
            BuildStepTypeIds.Player
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
        public void RecipeInvocations_ForDefaultProfile_ExposeConfiguredEntriesAndOnlyEnablePlayer()
        {
            CollectionAssert.AreEqual(
                DefaultStepTypeIds,
                buildData.RecipeInvocations.Select(entry => entry.InvocationId));
            CollectionAssert.AreEqual(
                new[] { BuildStepTypeIds.Player },
                buildData.EnabledInvocationIds);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RecipeInvocations_WhenSerializedValueIsMissingOrEmpty_DoesNotCreateImplicitPlan(bool useNull)
        {
            SetSerializedRecipeInvocations(useNull ? null : Array.Empty<BuildRecipeInvocation>());

            Assert.That(buildData.EnabledInvocationIds, Is.Empty);

            BuildRequest request = BuildRequestFactory.CreateInteractive(
                buildData,
                BuildTarget.StandaloneWindows64,
                debugBuild: false);
            var context = new BuildExecutionContext(request, "test-run", new NoOpEventSink());
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BuildPlanCompiler.Compile(context));
            StringAssert.Contains("does not contain any steps", exception.Message);
        }

        [Test]
        public void RecipeInvocations_WhenReturnedSnapshotIsMutated_PreservesProfileState()
        {
            BuildRecipeInvocation[] firstRead = buildData.RecipeInvocations.ToArray();
            firstRead[0] = new BuildRecipeInvocation("mutated-by-test", "mutated-by-test");

            CollectionAssert.AreEqual(
                DefaultStepTypeIds,
                buildData.RecipeInvocations.Select(entry => entry.InvocationId));
        }

        [Test]
        public void Compile_MultipleInvocationsOfOneType_UsesInvocationSpecificEdges()
        {
            BuildExecutionContext context = CreatePlanContext(
                CreateMultipleInvocation(
                    "a1",
                    new BuildInvocationDependency("b1", BuildDependencyMode.Required)),
                CreateMultipleInvocation("b1"),
                CreateMultipleInvocation("b2"));

            IReadOnlyList<CompiledBuildStep> plan = BuildPlanCompiler.Compile(context);

            CollectionAssert.AreEqual(
                new[] { "b1", "a1", "b2" },
                plan.Select(step => step.Invocation.InvocationId));
        }

        [Test]
        public void Compile_IfSelectedMissingDependency_UsesStableInvocationIdentityOrder()
        {
            BuildExecutionContext context = CreatePlanContext(
                CreateMultipleInvocation(
                    "a1",
                    new BuildInvocationDependency("not-selected", BuildDependencyMode.IfSelected)),
                CreateMultipleInvocation("b1"),
                CreateMultipleInvocation("b2"));

            IReadOnlyList<CompiledBuildStep> plan = BuildPlanCompiler.Compile(context);

            CollectionAssert.AreEqual(
                new[] { "a1", "b1", "b2" },
                plan.Select(step => step.Invocation.InvocationId));
        }

        [Test]
        public void Compile_IndependentInvocations_IsInvariantToSerializedOrder()
        {
            BuildExecutionContext first = CreatePlanContext(
                CreateMultipleInvocation("z-last"),
                CreateMultipleInvocation("a-first"),
                CreateMultipleInvocation("m-middle"));
            BuildExecutionContext second = CreatePlanContext(
                CreateMultipleInvocation("m-middle"),
                CreateMultipleInvocation("z-last"),
                CreateMultipleInvocation("a-first"));

            string[] firstOrder = BuildPlanCompiler.Compile(first)
                .Select(step => step.Invocation.InvocationId)
                .ToArray();
            string[] secondOrder = BuildPlanCompiler.Compile(second)
                .Select(step => step.Invocation.InvocationId)
                .ToArray();

            CollectionAssert.AreEqual(
                new[] { "a-first", "m-middle", "z-last" },
                firstOrder);
            CollectionAssert.AreEqual(firstOrder, secondOrder);
        }

        [Test]
        public void Compile_RequiredMissingDependency_Throws()
        {
            BuildExecutionContext context = CreatePlanContext(
                CreateMultipleInvocation(
                    "a1",
                    new BuildInvocationDependency("not-selected", BuildDependencyMode.Required)));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BuildPlanCompiler.Compile(context));
            StringAssert.Contains("requires missing invocation 'not-selected'", exception.Message);
        }

        [Test]
        public void Compile_SelfDuplicateAndCyclicDependencies_Throw()
        {
            BuildExecutionContext self = CreatePlanContext(
                CreateMultipleInvocation(
                    "a1",
                    new BuildInvocationDependency("a1", BuildDependencyMode.Required)));
            StringAssert.Contains(
                "dependency cycle",
                Assert.Throws<InvalidOperationException>(() => BuildPlanCompiler.Compile(self)).Message);

            BuildExecutionContext duplicate = CreatePlanContext(
                CreateMultipleInvocation(
                    "a1",
                    new BuildInvocationDependency("b1", BuildDependencyMode.Required),
                    new BuildInvocationDependency("B1", BuildDependencyMode.IfSelected)),
                CreateMultipleInvocation("b1"));
            StringAssert.Contains(
                "duplicate invocation dependency",
                Assert.Throws<InvalidOperationException>(() => BuildPlanCompiler.Compile(duplicate)).Message);

            BuildExecutionContext cycle = CreatePlanContext(
                CreateMultipleInvocation(
                    "a1",
                    new BuildInvocationDependency("b1", BuildDependencyMode.Required)),
                CreateMultipleInvocation(
                    "b1",
                    new BuildInvocationDependency("a1", BuildDependencyMode.Required)));
            StringAssert.Contains(
                "dependency cycle",
                Assert.Throws<InvalidOperationException>(() => BuildPlanCompiler.Compile(cycle)).Message);
        }

        [Test]
        public void Compile_DuplicateInvocationId_Throws()
        {
            BuildExecutionContext context = CreatePlanContext(
                CreateMultipleInvocation("content"),
                CreateMultipleInvocation("content"));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BuildPlanCompiler.Compile(context));
            StringAssert.Contains("configured more than once", exception.Message);
        }

        [Test]
        public void Compile_SingleMultiplicityStepType_RejectsMultipleInvocations()
        {
            BuildExecutionContext context = CreatePlanContext(
                new BuildStepInvocation("player-one", BuildStepTypeIds.Player),
                new BuildStepInvocation("player-two", BuildStepTypeIds.Player));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BuildPlanCompiler.Compile(context));
            StringAssert.Contains("allows one invocation per recipe", exception.Message);
        }

        [TestCase("sign,artifacts")]
        [TestCase("sign=artifacts")]
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
                        BuildIdentityPolicy.MaximumBuildIdentifierCharacters + 1)));
        }

        [Test]
        public void InvocationContracts_RejectUnknownPolicyEnumValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new BuildInvocationDependency(
                    "dependency",
                    (BuildDependencyMode)99));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new BuildStepInvocation(
                    "invocation",
                    "custom-step",
                    incrementality: (BuildIncrementality)99));
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
                debugBuild: false);

            SetSerializedRecipeInvocations(new[]
            {
                new BuildRecipeInvocation("mutated-after-request", "mutated-after-request")
            });
            var serialized = new SerializedObject(buildData);
            serialized.FindProperty("companyName").stringValue = "MutatedCompany";
            serialized.FindProperty("productName").stringValue = "MutatedProduct";
            serialized.FindProperty("applicationIdentifier").stringValue = "com.mutated.product";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(request.CompanyName, Is.EqualTo("TestCompany"));
            Assert.That(request.ProductName, Is.EqualTo("TestProduct"));
            Assert.That(request.ApplicationIdentifier, Is.EqualTo("com.example.test"));
            CollectionAssert.AreEqual(
                new[] { BuildStepTypeIds.Player },
                request.StepTypeIds);
            Assert.Throws<NotSupportedException>(
                () => ((IList<string>)request.StepTypeIds)[0] = "mutated-through-request");
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
            var configuration = ScriptableObject.CreateInstance<CountingContentBuildConfiguration>();
            string configurationPath =
                $"Assets/Build/Tests/Editor/CountingContent-{Guid.NewGuid():N}.asset";
            AssetDatabase.CreateAsset(configuration, configurationPath);
            try
            {
                SetSerializedRecipeInvocations(new[]
                {
                    new BuildRecipeInvocation(
                        BuildStepTypeIds.AssetContent,
                        BuildStepTypeIds.AssetContent,
                        configuration: configuration)
                });
                BuildRequest request = BuildRequestFactory.CreateInteractive(
                    buildData,
                    BuildTarget.StandaloneWindows64,
                    debugBuild: false);
                var context = new BuildExecutionContext(request, "test-run", new NoOpEventSink());
                BuildStepInvocation invocation = request.GetInvocation(BuildStepTypeIds.AssetContent);

                IAssetContentBuildAdapter first = context.ResolveAssetContentAdapter(invocation);
                IAssetContentBuildAdapter second = context.ResolveAssetContentAdapter(invocation);

                Assert.That(first, Is.SameAs(second));
                Assert.That(CountingContentBuildAdapter.ConstructorCallCount, Is.EqualTo(1));
            }
            finally
            {
                AssetDatabase.DeleteAsset(configurationPath);
            }
        }

        [Test]
        public void ResolveSteps_DoesNotInstantiateUnrequestedRegisteredTypes()
        {
            ExplodingUnrequestedBuildStep.ConstructorCallCount = 0;

            IReadOnlyList<IBuildStep> steps = BuildPipelineRegistry.ResolveSteps(DefaultStepTypeIds);

            CollectionAssert.AreEquivalent(DefaultStepTypeIds, GetStepTypeIds(steps));
            Assert.That(ExplodingUnrequestedBuildStep.ConstructorCallCount, Is.Zero);
        }

        [Test]
        public void GetBuildStepDescriptors_ReturnsOnlyVisibleBuiltInsWithoutInstantiatingSteps()
        {
            ExplodingUnrequestedBuildStep.ConstructorCallCount = 0;

            IReadOnlyList<BuildStepDescriptor> descriptors =
                BuildPipelineRegistry.GetBuildStepDescriptors();

            string[] descriptorIds = descriptors
                .Select(descriptor => descriptor.StepTypeId)
                .ToArray();
            foreach (string builtInId in DefaultStepTypeIds)
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
                    AddressablesBuildConfig.ProviderIdValue,
                    StringComparison.Ordinal));
            AssetContentProviderDescriptor yooAsset = descriptors.Single(
                descriptor => string.Equals(
                    descriptor.ProviderId,
                    YooAssetBuildConfig.ProviderIdValue,
                    StringComparison.Ordinal));

            Assert.That(addressables.ConfigurationType, Is.EqualTo(typeof(AddressablesBuildConfig)));
            Assert.That(yooAsset.ConfigurationType, Is.EqualTo(typeof(YooAssetBuildConfig)));
            Assert.That(
                descriptors.All(descriptor =>
                    typeof(AssetContentBuildConfiguration).IsAssignableFrom(
                        descriptor.ConfigurationType)),
                Is.True);
        }

        [Test]
        public void ResolveSteps_DuplicateStepTypeId_FailsBeforeInstantiationAndListsAllTypes()
        {
            DuplicateBuildStepA.ConstructorCallCount = 0;
            DuplicateBuildStepB.ConstructorCallCount = 0;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BuildPipelineRegistry.ResolveSteps(
                    new[] { DuplicateBuildStepA.StepTypeIdValue }));

            StringAssert.Contains(DuplicateBuildStepA.StepTypeIdValue, exception.Message);
            StringAssert.Contains(typeof(DuplicateBuildStepA).FullName, exception.Message);
            StringAssert.Contains(typeof(DuplicateBuildStepB).FullName, exception.Message);
            Assert.That(
                exception.Message.IndexOf(typeof(DuplicateBuildStepA).FullName, StringComparison.Ordinal),
                Is.LessThan(exception.Message.IndexOf(
                    typeof(DuplicateBuildStepB).FullName,
                    StringComparison.Ordinal)));
            Assert.That(DuplicateBuildStepA.ConstructorCallCount, Is.Zero);
            Assert.That(DuplicateBuildStepB.ConstructorCallCount, Is.Zero);
        }

        [Test]
        public void ResolveContentAdapter_DuplicateProviderId_FailsBeforeInstantiationAndListsAllTypes()
        {
            DuplicateContentAdapterA.ConstructorCallCount = 0;
            DuplicateContentAdapterB.ConstructorCallCount = 0;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BuildPipelineRegistry.ResolveContentAdapter(
                    DuplicateContentAdapterA.Provider));

            StringAssert.Contains(DuplicateContentAdapterA.Provider, exception.Message);
            StringAssert.Contains(typeof(DuplicateContentAdapterA).FullName, exception.Message);
            StringAssert.Contains(typeof(DuplicateContentAdapterB).FullName, exception.Message);
            Assert.That(
                exception.Message.IndexOf(typeof(DuplicateContentAdapterA).FullName, StringComparison.Ordinal),
                Is.LessThan(exception.Message.IndexOf(
                    typeof(DuplicateContentAdapterB).FullName,
                    StringComparison.Ordinal)));
            Assert.That(DuplicateContentAdapterA.ConstructorCallCount, Is.Zero);
            Assert.That(DuplicateContentAdapterB.ConstructorCallCount, Is.Zero);
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
        public void ResolveRecoveryParticipants_DuplicateIdFailsBeforeInstantiationAndListsAllTypes()
        {
            DuplicateRecoveryParticipantB.ConstructorCallCount = 0;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BuildPipelineRegistry.ResolveRecoveryParticipants(
                    new[]
                    {
                        new BuildPipelineRegistry.RecoveryRegistrationCandidate(
                            typeof(DuplicateRecoveryParticipantA),
                            new BuildRecoveryRegistrationAttribute(
                                DuplicateRecoveryParticipantA.ParticipantId,
                                priority: 100)),
                        new BuildPipelineRegistry.RecoveryRegistrationCandidate(
                            typeof(DuplicateRecoveryParticipantB),
                            new BuildRecoveryRegistrationAttribute(
                                DuplicateRecoveryParticipantA.ParticipantId,
                                priority: -100))
                    }));

            StringAssert.Contains(DuplicateRecoveryParticipantA.ParticipantId, exception.Message);
            StringAssert.Contains(typeof(DuplicateRecoveryParticipantA).FullName, exception.Message);
            StringAssert.Contains(typeof(DuplicateRecoveryParticipantB).FullName, exception.Message);
            Assert.That(DuplicateRecoveryParticipantB.ConstructorCallCount, Is.Zero);
        }

        [Test]
        public void AuthoringRegistration_RejectsNonPortableProviderIdentifiers()
        {
            Assert.Throws<ArgumentException>(
                () => new AssetContentProviderAuthoringAttribute("Content Provider", "Content"));
            Assert.Throws<ArgumentException>(
                () => new AssetContentAdapterRegistrationAttribute("Content/Adapter"));
            Assert.Throws<ArgumentException>(
                () => new HotUpdateProviderAuthoringAttribute("Hot/Update", "Hot Update"));
            Assert.Throws<ArgumentException>(
                () => new HotUpdateAdapterRegistrationAttribute(
                    "Hot Adapter",
                    typeof(HybridCLRBuildConfig)));
        }

        [Test]
        public void Runner_DoesNotRecoverProjectCentralStateDuringRequestValidation()
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
                    stepTypeIds: new[] { RecoveryOrderingBuildStep.StepTypeIdValue },
                    applicationVersion: "invalid\nversion");

                BuildRunResult result = new BuildPipelineRunner(
                        new NoOpEventSink(),
                        projectRoot,
                        () => false)
                    .Run(request);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(RecoveryOrderingParticipant.WasRecovered, Is.False);
                StringAssert.Contains("Application version", result.Failure.ToString());
            }
            finally
            {
                RecoveryOrderingParticipant.EndProbe();
                DeleteProjectSandboxRoot(projectRoot, sandboxRoot);
            }
        }

        [Test]
        public void Runner_DoesNotRecoverProjectCentralStateDuringStepApplicability()
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
                    stepTypeIds: new[] { RecoveryOrderingBuildStep.StepTypeIdValue });

                BuildRunResult result = new BuildPipelineRunner(
                        new NoOpEventSink(),
                        projectRoot,
                        () => false)
                    .Run(request);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(RecoveryOrderingParticipant.WasRecovered, Is.False);
                StringAssert.Contains(
                    RecoveryOrderingBuildStep.RecoveryMissingSentinel,
                    result.Failure.ToString());
                StringAssert.DoesNotContain(
                    RecoveryOrderingBuildStep.ApplicabilitySentinel,
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
                    stepTypeIds: new[] { RecoveryOrderingBuildStep.StepTypeIdValue });

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
        public void WorkspaceInspection_WhenOptionalParticipantIsUnavailableAndStateExists_FailsClosed()
        {
            string sandboxRoot = CreateSandboxRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(sandboxRoot, "Assets"));
                Directory.CreateDirectory(Path.Combine(sandboxRoot, "ProjectSettings"));
                string stateRoot = Path.Combine(
                    sandboxRoot,
                    ".buildpipeline",
                    "transactions",
                    "yooasset3");
                Directory.CreateDirectory(stateRoot);
                string evidencePath = Path.Combine(stateRoot, "pending.evidence");
                File.WriteAllText(evidencePath, "preserve");

                BuildWorkspaceSnapshot snapshot = BuildWorkspaceService.Inspect(
                    sandboxRoot,
                    Array.Empty<IBuildRecoveryParticipant>(),
                    editorIsBusy: false);

                Assert.That(File.ReadAllText(evidencePath), Is.EqualTo("preserve"));
                Assert.That(snapshot.Status, Is.EqualTo(BuildWorkspaceHealthStatus.Blocked));
                Assert.That(snapshot.CanRecover, Is.False);
                Assert.That(snapshot.Issues.Single().Title, Is.EqualTo("Unavailable recovery participant"));
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
            SetSerializedRecipeInvocations(new[]
            {
                new BuildRecipeInvocation(
                    SnapshotApplicabilityBuildStep.StepTypeIdValue,
                    SnapshotApplicabilityBuildStep.StepTypeIdValue)
            });
            BuildRequest request = BuildRequestFactory.CreateInteractive(
                buildData,
                BuildTarget.StandaloneWindows64,
                debugBuild: false);
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
            string configurationPath =
                $"Assets/Build/Tests/Editor/HybridCLR-{Guid.NewGuid():N}.asset";
            AssetDatabase.CreateAsset(hybridConfig, configurationPath);
            try
            {
                var serialized = new SerializedObject(buildData);
                serialized.FindProperty("cheatBuildMode").enumValueIndex =
                    (int)CheatBuildMode.Enabled;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                SetSerializedRecipeInvocations(new[]
                {
                    new BuildRecipeInvocation(
                        BuildStepTypeIds.HotUpdate,
                        BuildStepTypeIds.HotUpdate,
                        configuration: hybridConfig),
                    new BuildRecipeInvocation(
                        BuildStepTypeIds.Player,
                        BuildStepTypeIds.Player,
                        dependencies: new[]
                        {
                            new BuildInvocationDependency(
                                BuildStepTypeIds.HotUpdate)
                        })
                });

                BuildRequest request = BuildRequestFactory.CreateInteractive(
                    buildData,
                    BuildTarget.StandaloneWindows64,
                    debugBuild: false);
                var context = new BuildExecutionContext(
                    request,
                    "test-run",
                    new NoOpEventSink());
                BuildStepInvocation hotUpdateInvocation =
                    request.GetInvocation(BuildStepTypeIds.HotUpdate);
                BuildStepInvocation playerInvocation =
                    request.GetInvocation(BuildStepTypeIds.Player);
                context.SetPlan(new[]
                {
                    new CompiledBuildStep(
                        hotUpdateInvocation,
                        new HotUpdateBuildStep(),
                        isApplicable: true),
                    new CompiledBuildStep(
                        playerInvocation,
                        new PlayerBuildStep(),
                        isApplicable: true)
                });

                IReadOnlyList<string> hotUpdateErrors =
                    new HotUpdateBuildStep().Validate(
                        context,
                        hotUpdateInvocation);
                Assert.That(
                    hotUpdateErrors.Any(error => error.Contains("per-build ENABLE_CHEAT")),
                    Is.False);

                IReadOnlyList<string> playerErrors =
                    new PlayerBuildStep().Validate(
                        context,
                        playerInvocation);
                Assert.That(
                    playerErrors.Any(error => error.Contains("per-build ENABLE_CHEAT")),
                    Is.True);
            }
            finally
            {
                AssetDatabase.DeleteAsset(configurationPath);
            }
        }

        [Test]
        public void Runner_RequirementsFreeRecipe_DoesNotRequirePlayerAuthoringFields()
        {
            string projectRoot = GetCurrentProjectRoot();
            string sandboxRoot = CreateProjectSandboxRoot(projectRoot);
            try
            {
                RequirementsFreeBuildStep.Executed = false;
                BuildRequest request = CreateSandboxRequest(
                    projectRoot,
                    sandboxRoot,
                    companyName: string.Empty,
                    stepTypeIds: new[] { RequirementsFreeBuildStep.StepTypeIdValue });

                BuildRunResult result = new BuildPipelineRunner(
                        new NoOpEventSink(),
                        projectRoot,
                        () => false)
                    .Run(request);

                Assert.That(result.Succeeded, Is.True, result.Failure?.ToString());
                Assert.That(RequirementsFreeBuildStep.Executed, Is.True);
            }
            finally
            {
                DeleteProjectSandboxRoot(projectRoot, sandboxRoot);
            }
        }

        [Test]
        public void AddressablesAdapter_ExposesProviderNeutralPlayerBuildSessionHook()
        {
            var adapter = new AddressablesContentBuildAdapter();

            Assert.That(adapter, Is.InstanceOf<IAssetContentPlayerBuildSessionFactory>());
        }

        [Test]
        public void AddressablesAdapter_DefaultPublicationRoot_IsScopedByInvocation()
        {
            string projectRoot = GetCurrentProjectRoot();
            var configuration = ScriptableObject.CreateInstance<AddressablesBuildConfig>();
            try
            {
                configuration.copyToOutputDirectory = true;
                configuration.buildOutputDirectory = string.Empty;
                var adapter = new AddressablesContentBuildAdapter();
                var firstRequest = new AssetContentBuildRequest(
                    "content-base",
                    BuildTarget.StandaloneWindows64,
                    "1.0.0",
                    projectRoot,
                    configuration,
                    BuildIncrementality.Clean,
                    batchMode: false);
                var secondRequest = new AssetContentBuildRequest(
                    "content-dlc",
                    BuildTarget.StandaloneWindows64,
                    "1.0.0",
                    projectRoot,
                    configuration,
                    BuildIncrementality.Clean,
                    batchMode: false);

                string first = adapter.GetExclusiveOutputPaths(firstRequest).Single();
                string second = adapter.GetExclusiveOutputPaths(secondRequest).Single();

                Assert.That(
                    first,
                    Is.EqualTo(Path.GetFullPath(Path.Combine(
                        projectRoot,
                        "Build",
                        "AddressablesContent",
                        "content-base",
                        BuildTarget.StandaloneWindows64.ToString()))));
                Assert.That(
                    second,
                    Is.EqualTo(Path.GetFullPath(Path.Combine(
                        projectRoot,
                        "Build",
                        "AddressablesContent",
                        "content-dlc",
                        BuildTarget.StandaloneWindows64.ToString()))));
                Assert.That(first, Is.Not.EqualTo(second));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
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

        private void SetSerializedRecipeInvocations(BuildRecipeInvocation[] value)
        {
            FieldInfo field = typeof(BuildData).GetField(
                "recipeInvocations",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(buildData, value);
        }

        private static string[] GetStepTypeIds(IReadOnlyList<IBuildStep> steps)
        {
            var ids = new string[steps.Count];
            for (int index = 0; index < steps.Count; index++)
            {
                ids[index] = steps[index].StepTypeId;
            }

            return ids;
        }

        private static BuildRequest CreateSandboxRequest(
            string projectRoot,
            string sandboxRoot,
            string companyName,
            IReadOnlyList<string> stepTypeIds,
            string applicationVersion = "0.1.0")
        {
            return CreateSandboxRequest(
                projectRoot,
                sandboxRoot,
                companyName,
                CreateInvocations(stepTypeIds),
                applicationVersion);
        }

        private static BuildRequest CreateSandboxRequest(
            string projectRoot,
            string sandboxRoot,
            string companyName,
            IReadOnlyList<BuildStepInvocation> invocations,
            string applicationVersion = "0.1.0")
        {
            string buildRoot = Path.Combine(sandboxRoot, "Build");
            string outputDirectory = Path.Combine(buildRoot, "Windows", "Release");
            string outputPath = Path.Combine(outputDirectory, "TestProduct.exe");
            return new BuildRequest(
                companyName,
                "TestProduct",
                "com.example.test",
                "Assets/Build/Runtime/Resources/VersionInfoData.asset",
                Array.Empty<string>(),
                CheatBuildMode.Disabled,
                BuildTarget.StandaloneWindows64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.Mono2x,
                projectRoot,
                buildRoot,
                outputPath,
                outputDirectory,
                outputIsFolder: false,
                deleteDebugFiles: true,
                debugBuild: true,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: false,
                applicationVersion: applicationVersion,
                identityOverride: BuildIdentityOverride.Empty,
                steps: invocations);
        }

        private static BuildExecutionContext CreatePlanContext(
            params BuildStepInvocation[] invocations)
        {
            string sandboxRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityStarter-BuildPlanCompilerTests");
            BuildRequest request = CreateSandboxRequest(
                GetCurrentProjectRoot(),
                sandboxRoot,
                "TestCompany",
                invocations);
            return new BuildExecutionContext(request, "test-run", new NoOpEventSink());
        }

        private static BuildStepInvocation CreateMultipleInvocation(
            string invocationId,
            params BuildInvocationDependency[] dependencies)
        {
            return new BuildStepInvocation(
                invocationId,
                MultipleInvocationBuildStep.StepTypeIdValue,
                dependencies: dependencies);
        }

        private static BuildStepInvocation[] CreateInvocations(
            IReadOnlyList<string> stepTypeIds)
        {
            var result = new BuildStepInvocation[stepTypeIds.Count];
            for (int index = 0; index < stepTypeIds.Count; index++)
            {
                result[index] = new BuildStepInvocation(
                    stepTypeIds[index],
                    stepTypeIds[index]);
            }

            return result;
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
            public void RunStarted(BuildExecutionContext context, System.Collections.Generic.IReadOnlyList<CompiledBuildStep> plan) { }
            public void StepStarted(BuildExecutionContext context, CompiledBuildStep step) { }
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

        public string StepTypeId => "build-pipeline-tests.exploding-unrequested";
        public bool IsApplicable(BuildExecutionContext context, BuildStepInvocation invocation) => true;
        public IReadOnlyList<string> Validate(BuildExecutionContext context, BuildStepInvocation invocation) => Array.Empty<string>();
        public void Execute(BuildExecutionContext context, BuildStepInvocation invocation) { }
    }

    [BuildStepRegistration(SnapshotApplicabilityBuildStep.StepTypeIdValue, HiddenFromAuthoring = true)]
    public sealed class SnapshotApplicabilityBuildStep : IBuildStep
    {
        public const string StepTypeIdValue = "build-pipeline-tests.applicability-snapshot";
        public static int ApplicabilityCallCount;

        public string StepTypeId => StepTypeIdValue;
        public bool IsApplicable(BuildExecutionContext context, BuildStepInvocation invocation) => ++ApplicabilityCallCount == 1;
        public IReadOnlyList<string> Validate(BuildExecutionContext context, BuildStepInvocation invocation) => Array.Empty<string>();
        public void Execute(BuildExecutionContext context, BuildStepInvocation invocation) { }
    }

    [BuildStepRegistration(
        MultipleInvocationBuildStep.StepTypeIdValue,
        HiddenFromAuthoring = true,
        Multiplicity = BuildStepMultiplicity.Multiple)]
    public sealed class MultipleInvocationBuildStep : IBuildStep
    {
        public const string StepTypeIdValue = "build-pipeline-tests.multiple-invocation";

        public string StepTypeId => StepTypeIdValue;
        public bool IsApplicable(BuildExecutionContext context, BuildStepInvocation invocation) => true;
        public IReadOnlyList<string> Validate(BuildExecutionContext context, BuildStepInvocation invocation) => Array.Empty<string>();
        public void Execute(BuildExecutionContext context, BuildStepInvocation invocation) { }
    }

    [BuildStepRegistration(RequirementsFreeBuildStep.StepTypeIdValue, HiddenFromAuthoring = true)]
    public sealed class RequirementsFreeBuildStep : IBuildStep
    {
        public const string StepTypeIdValue = "build-pipeline-tests.requirements-free";
        public static bool Executed;

        public string StepTypeId => StepTypeIdValue;
        public bool IsApplicable(BuildExecutionContext context, BuildStepInvocation invocation) => true;
        public IReadOnlyList<string> Validate(BuildExecutionContext context, BuildStepInvocation invocation) => Array.Empty<string>();
        public void Execute(BuildExecutionContext context, BuildStepInvocation invocation) => Executed = true;
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

        public AssetContentBuildResult Validate(AssetContentBuildRequest request)
        {
            return AssetContentBuildResult.Success(Provider, "test", request.PackageVersion);
        }

        public AssetContentBuildOperation Build(AssetContentBuildRequest request)
        {
            return new AssetContentBuildOperation(new[] { Validate(request) });
        }
    }

    [BuildStepRegistration(DuplicateBuildStepA.StepTypeIdValue, HiddenFromAuthoring = true)]
    public sealed class DuplicateBuildStepA : IBuildStep
    {
        public const string StepTypeIdValue = "build-pipeline-tests.duplicate-step-id";
        public static int ConstructorCallCount;

        public DuplicateBuildStepA()
        {
            ConstructorCallCount++;
        }

        public string StepTypeId => StepTypeIdValue;
        public bool IsApplicable(BuildExecutionContext context, BuildStepInvocation invocation) => true;
        public IReadOnlyList<string> Validate(BuildExecutionContext context, BuildStepInvocation invocation) => Array.Empty<string>();
        public void Execute(BuildExecutionContext context, BuildStepInvocation invocation) { }
    }

    [BuildStepRegistration(DuplicateBuildStepA.StepTypeIdValue, HiddenFromAuthoring = true)]
    public sealed class DuplicateBuildStepB : IBuildStep
    {
        public static int ConstructorCallCount;

        public DuplicateBuildStepB()
        {
            ConstructorCallCount++;
        }

        public string StepTypeId => DuplicateBuildStepA.StepTypeIdValue;
        public bool IsApplicable(BuildExecutionContext context, BuildStepInvocation invocation) => true;
        public IReadOnlyList<string> Validate(BuildExecutionContext context, BuildStepInvocation invocation) => Array.Empty<string>();
        public void Execute(BuildExecutionContext context, BuildStepInvocation invocation) { }
    }

    [AssetContentAdapterRegistration(DuplicateContentAdapterA.Provider)]
    public sealed class DuplicateContentAdapterA : IAssetContentBuildAdapter
    {
        public const string Provider = "build-pipeline-tests.duplicate-adapter-id";
        public static int ConstructorCallCount;

        public DuplicateContentAdapterA()
        {
            ConstructorCallCount++;
        }

        public string ProviderId => Provider;
        public AssetContentBuildResult Validate(AssetContentBuildRequest request) =>
            AssetContentBuildResult.Success(Provider, "test", request.PackageVersion);
        public AssetContentBuildOperation Build(AssetContentBuildRequest request) =>
            new AssetContentBuildOperation(new[] { Validate(request) });
    }

    [AssetContentAdapterRegistration(DuplicateContentAdapterA.Provider)]
    public sealed class DuplicateContentAdapterB : IAssetContentBuildAdapter
    {
        public static int ConstructorCallCount;

        public DuplicateContentAdapterB()
        {
            ConstructorCallCount++;
        }

        public string ProviderId => DuplicateContentAdapterA.Provider;
        public AssetContentBuildResult Validate(AssetContentBuildRequest request) => null;
        public AssetContentBuildOperation Build(AssetContentBuildRequest request) => null;
    }

    public sealed class DuplicateRecoveryParticipantA : IBuildRecoveryParticipant
    {
        public const string ParticipantId = "build-pipeline-tests.duplicate-recovery-id";
        private static readonly string[] StatePaths =
        {
            ".buildpipeline/transactions/test-duplicate-recovery-a"
        };

        public string Id => ParticipantId;
        public int Priority => 100;
        public IReadOnlyList<string> StateDirectoryRelativePaths => StatePaths;
        public void Recover(string projectRoot) { }
    }

    public sealed class DuplicateRecoveryParticipantB : IBuildRecoveryParticipant
    {
        public static int ConstructorCallCount;

        public DuplicateRecoveryParticipantB()
        {
            ConstructorCallCount++;
            throw new InvalidOperationException(
                "Duplicate recovery registrations must be rejected before instantiation.");
        }

        public string Id => DuplicateRecoveryParticipantA.ParticipantId;
        public int Priority => -100;
        public IReadOnlyList<string> StateDirectoryRelativePaths =>
            Array.Empty<string>();
        public void Recover(string projectRoot) { }
    }

    [BuildRecoveryRegistration(RecoveryOrderingParticipant.ParticipantId)]
    public sealed class RecoveryOrderingParticipant : IBuildRecoveryParticipant
    {
        public const string ParticipantId = "build-pipeline-tests.recovery-ordering";
        private static readonly string[] StatePaths =
        {
            ".buildpipeline/transactions/test-recovery-ordering"
        };
        private static readonly object ProbeGate = new object();
        private static string expectedProjectRoot;
        private static bool wasRecovered;

        public string Id => ParticipantId;
        public int Priority => 0;
        public IReadOnlyList<string> StateDirectoryRelativePaths => StatePaths;

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

    [BuildStepRegistration(RecoveryOrderingBuildStep.StepTypeIdValue, HiddenFromAuthoring = true)]
    public sealed class RecoveryOrderingBuildStep : IBuildStep
    {
        public const string StepTypeIdValue = "build-pipeline-tests.recovery-ordering-step";
        public const string ApplicabilitySentinel =
            "Recovery ordering step reached applicability after recovery.";
        public const string RecoveryMissingSentinel =
            "Recovery ordering step reached applicability before recovery.";

        public string StepTypeId => StepTypeIdValue;

        public bool IsApplicable(BuildExecutionContext context, BuildStepInvocation invocation)
        {
            if (!RecoveryOrderingParticipant.WasRecovered)
            {
                throw new InvalidOperationException(RecoveryMissingSentinel);
            }

            throw new InvalidOperationException(ApplicabilitySentinel);
        }

        public IReadOnlyList<string> Validate(BuildExecutionContext context, BuildStepInvocation invocation) => Array.Empty<string>();
        public void Execute(BuildExecutionContext context, BuildStepInvocation invocation) { }
    }
}
