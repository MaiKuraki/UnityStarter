using System;
using System.Linq;
using System.Reflection;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class BuildRecipeAuthoringTests
    {
        private BuildData profile;
        private YooAssetBuildConfig contentConfiguration;
        private HybridCLRBuildConfig hybridClrConfiguration;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<BuildData>();
            contentConfiguration = ScriptableObject.CreateInstance<YooAssetBuildConfig>();
            hybridClrConfiguration = ScriptableObject.CreateInstance<HybridCLRBuildConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            if (profile != null)
            {
                Undo.ClearUndo(profile);
                UnityEngine.Object.DestroyImmediate(profile);
            }

            if (contentConfiguration != null)
            {
                UnityEngine.Object.DestroyImmediate(contentConfiguration);
            }

            if (hybridClrConfiguration != null)
            {
                UnityEngine.Object.DestroyImmediate(hybridClrConfiguration);
            }
        }

        [TestCase(
            (int)BuildRecipePreset.PlayerWithDependencies,
            BuildStepIds.HotUpdate,
            BuildStepIds.AssetContent,
            BuildStepIds.Player)]
        [TestCase(
            (int)BuildRecipePreset.ContentWithDependencies,
            BuildStepIds.HotUpdate,
            BuildStepIds.AssetContent)]
        [TestCase(
            (int)BuildRecipePreset.HotUpdateOnly,
            BuildStepIds.HotUpdate)]
        public void GetStepIds_ReturnsCanonicalDependencySafeSequence(
            int presetValue,
            params string[] expected)
        {
            var preset = (BuildRecipePreset)presetValue;
            string[] first = BuildRecipePresetCatalog.GetStepIds(preset);
            first[0] = "mutated-by-test";

            CollectionAssert.AreEqual(expected, BuildRecipePresetCatalog.GetStepIds(preset));
        }

        [Test]
        public void TryIdentify_RequiresExactOrderedShapeButAcceptsStableIdCasing()
        {
            Assert.That(
                BuildRecipePresetCatalog.TryIdentify(
                    new[] { "HOT-UPDATE", "ASSET-CONTENT" },
                    out BuildRecipePreset identified),
                Is.True);
            Assert.That(identified, Is.EqualTo(BuildRecipePreset.ContentWithDependencies));

            Assert.That(
                BuildRecipePresetCatalog.TryIdentify(
                    new[] { BuildStepIds.AssetContent, BuildStepIds.HotUpdate },
                    out _),
                Is.False);
            Assert.That(
                BuildRecipePresetCatalog.TryIdentify(
                    new[] { BuildStepIds.HotUpdate, "custom-signing", BuildStepIds.AssetContent },
                    out _),
                Is.False);
        }

        [Test]
        public void Analyze_PlayerPreset_ReportsOnlyCurrentlyEffectiveOutputs()
        {
            BuildRecipeAnalysis analysis = BuildRecipePresetCatalog.Analyze(
                BuildRecipePresetCatalog.GetStepIds(BuildRecipePreset.PlayerWithDependencies),
                useHybridClr: false,
                hasAssetContentProvider: false);

            Assert.That(analysis.MatchedPreset, Is.EqualTo(BuildRecipePreset.PlayerWithDependencies));
            Assert.That(analysis.ProducesPlayer, Is.True);
            Assert.That(analysis.ProducesAssetContent, Is.False);
            Assert.That(analysis.ProducesHotUpdate, Is.False);
            Assert.That(analysis.IsReady, Is.True);
        }

        [Test]
        public void Analyze_ContentPreset_DoesNotRequirePlayerAndIncludesEnabledHybridOutput()
        {
            BuildRecipeAnalysis analysis = BuildRecipePresetCatalog.Analyze(
                BuildRecipePresetCatalog.GetStepIds(BuildRecipePreset.ContentWithDependencies),
                useHybridClr: true,
                hasAssetContentProvider: true);

            Assert.That(analysis.ProducesPlayer, Is.False);
            Assert.That(analysis.ProducesAssetContent, Is.True);
            Assert.That(analysis.ProducesHotUpdate, Is.True);
            Assert.That(analysis.IsReady, Is.True);
        }

        [Test]
        public void Analyze_ContentPresetWithoutHybrid_ProducesOnlyAssetContent()
        {
            BuildRecipeAnalysis analysis = BuildRecipePresetCatalog.Analyze(
                BuildRecipePresetCatalog.GetStepIds(BuildRecipePreset.ContentWithDependencies),
                useHybridClr: false,
                hasAssetContentProvider: true);

            Assert.That(analysis.ProducesPlayer, Is.False);
            Assert.That(analysis.ProducesAssetContent, Is.True);
            Assert.That(analysis.ProducesHotUpdate, Is.False);
            Assert.That(analysis.IsReady, Is.True);
        }

        [Test]
        public void Analyze_HotUpdateOnlyWithHybrid_ProducesOnlyHotUpdateOutput()
        {
            BuildRecipeAnalysis analysis = BuildRecipePresetCatalog.Analyze(
                BuildRecipePresetCatalog.GetStepIds(BuildRecipePreset.HotUpdateOnly),
                useHybridClr: true,
                hasAssetContentProvider: false);

            Assert.That(analysis.ProducesPlayer, Is.False);
            Assert.That(analysis.ProducesAssetContent, Is.False);
            Assert.That(analysis.ProducesHotUpdate, Is.True);
            Assert.That(analysis.IsReady, Is.True);
        }

        [Test]
        public void Analyze_ContentWithoutProvider_IsBlockedInsteadOfSucceedingWithoutContent()
        {
            BuildRecipeAnalysis analysis = BuildRecipePresetCatalog.Analyze(
                BuildRecipePresetCatalog.GetStepIds(BuildRecipePreset.ContentWithDependencies),
                useHybridClr: false,
                hasAssetContentProvider: false);

            Assert.That(analysis.IsReady, Is.False);
            Assert.That(
                analysis.BlockingIssues,
                Has.Some.Contains("no Asset Content Provider"));
        }

        [Test]
        public void Analyze_CustomPlayerRecipe_ReportsMissingEnabledDependencies()
        {
            BuildRecipeAnalysis analysis = BuildRecipePresetCatalog.Analyze(
                new[] { BuildStepIds.Player },
                useHybridClr: true,
                hasAssetContentProvider: true);

            Assert.That(analysis.MatchedPreset, Is.Null);
            Assert.That(analysis.IsReady, Is.False);
            Assert.That(analysis.BlockingIssues, Has.Some.Contains(BuildStepIds.HotUpdate));
            Assert.That(analysis.BlockingIssues, Has.Some.Contains(BuildStepIds.AssetContent));
        }

        [Test]
        public void Analyze_DisabledHotUpdateOnlyRecipe_IsBlockedAsNoOutput()
        {
            BuildRecipeAnalysis analysis = BuildRecipePresetCatalog.Analyze(
                BuildRecipePresetCatalog.GetStepIds(BuildRecipePreset.HotUpdateOnly),
                useHybridClr: false,
                hasAssetContentProvider: false);

            Assert.That(analysis.ProducesHotUpdate, Is.False);
            Assert.That(analysis.IsReady, Is.False);
            Assert.That(analysis.BlockingIssues.Count, Is.EqualTo(1));
        }

        [Test]
        public void Apply_ContentPreset_RequiresProviderConfigurationAndPreservesOtherFields()
        {
            SetProfileConfiguration(
                useHybridClr: false,
                providerId: "yooasset",
                providerConfiguration: contentConfiguration,
                hybridConfiguration: null,
                steps: new[] { "custom-step" });
            string originalVersion = profile.ApplicationVersion;

            bool changed = BuildRecipePresetAuthoring.Apply(
                profile,
                BuildRecipePreset.ContentWithDependencies);

            Assert.That(changed, Is.True);
            CollectionAssert.AreEqual(
                new[] { BuildStepIds.HotUpdate, BuildStepIds.AssetContent },
                profile.PipelineSteps);
            Assert.That(profile.AssetContentProviderId, Is.EqualTo("yooasset"));
            Assert.That(profile.AssetContentConfiguration, Is.SameAs(contentConfiguration));
            Assert.That(profile.ApplicationVersion, Is.EqualTo(originalVersion));
        }

        [Test]
        public void Apply_UnavailableContentPreset_FailsWithoutChangingRecipe()
        {
            SetProfileConfiguration(
                useHybridClr: false,
                providerId: string.Empty,
                providerConfiguration: null,
                hybridConfiguration: null,
                steps: new[] { "custom-step" });

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                BuildRecipePresetAuthoring.Apply(
                    profile,
                    BuildRecipePreset.ContentWithDependencies));

            Assert.That(exception.Message, Does.Contain("Asset Content Provider"));
            CollectionAssert.AreEqual(new[] { "custom-step" }, profile.PipelineSteps);
        }

        [Test]
        public void Apply_ContentPresetWithProviderButNoConfiguration_FailsWithoutChangingRecipe()
        {
            SetProfileConfiguration(
                useHybridClr: false,
                providerId: "yooasset",
                providerConfiguration: null,
                hybridConfiguration: null,
                steps: new[] { "custom-step" });

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                BuildRecipePresetAuthoring.Apply(
                    profile,
                    BuildRecipePreset.ContentWithDependencies));

            Assert.That(exception.Message, Does.Contain("Configuration"));
            CollectionAssert.AreEqual(new[] { "custom-step" }, profile.PipelineSteps);
        }

        [TestCase((int)BuildRecipePreset.ContentWithDependencies)]
        [TestCase((int)BuildRecipePreset.HotUpdateOnly)]
        public void Apply_HybridRecipeWithoutHybridConfiguration_FailsWithoutChangingRecipe(
            int presetValue)
        {
            var preset = (BuildRecipePreset)presetValue;
            SetProfileConfiguration(
                useHybridClr: true,
                providerId: "yooasset",
                providerConfiguration: contentConfiguration,
                hybridConfiguration: null,
                steps: new[] { "custom-step" });

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                BuildRecipePresetAuthoring.Apply(profile, preset));

            Assert.That(exception.Message, Does.Contain("HybridCLR Build Config"));
            CollectionAssert.AreEqual(new[] { "custom-step" }, profile.PipelineSteps);
        }

        [Test]
        public void Apply_HotUpdatePreset_SupportsSingleStepUndoAndRedo()
        {
            SetProfileConfiguration(
                useHybridClr: true,
                providerId: string.Empty,
                providerConfiguration: null,
                hybridConfiguration: hybridClrConfiguration,
                steps: new[] { "custom-step" });
            Undo.ClearUndo(profile);

            Assert.That(
                BuildRecipePresetAuthoring.Apply(profile, BuildRecipePreset.HotUpdateOnly),
                Is.True);
            CollectionAssert.AreEqual(new[] { BuildStepIds.HotUpdate }, profile.PipelineSteps);

            Undo.PerformUndo();
            CollectionAssert.AreEqual(new[] { "custom-step" }, profile.PipelineSteps);

            Undo.PerformRedo();
            CollectionAssert.AreEqual(new[] { BuildStepIds.HotUpdate }, profile.PipelineSteps);
        }

        [Test]
        public void Apply_WhenRecipeAlreadyMatches_IsNoOp()
        {
            SetProfileConfiguration(
                useHybridClr: false,
                providerId: string.Empty,
                providerConfiguration: null,
                hybridConfiguration: null,
                steps: BuildRecipePresetCatalog.GetStepIds(
                    BuildRecipePreset.PlayerWithDependencies));

            Assert.That(
                BuildRecipePresetAuthoring.Apply(
                    profile,
                    BuildRecipePreset.PlayerWithDependencies),
                Is.False);
        }

        [Test]
        public void AndroidExportRecipe_RequiresPlayerStep()
        {
            Assert.Throws<ArgumentException>(() =>
                BuildRequestFactory.ValidateAndroidExportRecipe(
                    BuildRecipePresetCatalog.GetStepIds(BuildRecipePreset.ContentWithDependencies),
                    exportAndroidProject: true));

            Assert.DoesNotThrow(() =>
                BuildRequestFactory.ValidateAndroidExportRecipe(
                    BuildRecipePresetCatalog.GetStepIds(BuildRecipePreset.PlayerWithDependencies),
                    exportAndroidProject: true));
            Assert.DoesNotThrow(() =>
                BuildRequestFactory.ValidateAndroidExportRecipe(
                    BuildRecipePresetCatalog.GetStepIds(BuildRecipePreset.ContentWithDependencies),
                    exportAndroidProject: false));
        }

        [Test]
        public void EditorMenus_ExposeSelectedRecipePathsWithoutLegacyRunProfileCommands()
        {
            string[] menuPaths = typeof(BuildEntryPoints)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), inherit: false))
                .Cast<MenuItem>()
                .Select(attribute => attribute.menuItem)
                .ToArray();

            CollectionAssert.Contains(
                menuPaths,
                "Build/Pipeline/Run Selected Recipe/Release (Clean)");
            CollectionAssert.Contains(
                menuPaths,
                "Build/Pipeline/Run Selected Recipe/Development (Incremental)");
            CollectionAssert.Contains(
                menuPaths,
                "Build/Pipeline/Android/Export Player Gradle Project");
            Assert.That(
                menuPaths.Any(path => path.StartsWith(
                    "Build/Pipeline/Run Profile/",
                    StringComparison.Ordinal)),
                Is.False);
        }

        private void SetProfileConfiguration(
            bool useHybridClr,
            string providerId,
            ScriptableObject providerConfiguration,
            HybridCLRBuildConfig hybridConfiguration,
            string[] steps)
        {
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("useHybridCLR").boolValue = useHybridClr;
            serialized.FindProperty("assetContentProviderId").stringValue = providerId;
            serialized.FindProperty("assetContentConfiguration").objectReferenceValue =
                providerConfiguration;
            serialized.FindProperty("hybridCLRBuildConfig").objectReferenceValue =
                hybridConfiguration;

            SerializedProperty pipelineSteps = serialized.FindProperty("pipelineSteps");
            pipelineSteps.arraySize = steps.Length;
            for (int index = 0; index < steps.Length; index++)
            {
                pipelineSteps.GetArrayElementAtIndex(index).stringValue = steps[index];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
