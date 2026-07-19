using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CycloneGames.UIFramework.Editor;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class UIEditorAuthoringTests
    {
        private const string PackageRoot =
            "Assets/ThirdParty/CycloneGames/CycloneGames.UIFramework";
        private const string SamplesFolder = PackageRoot + "/Samples";
        private const string RuntimeAssemblyName = "CycloneGames.UIFramework.Runtime";

        private readonly List<UnityEngine.Object> _ownedObjects =
            new List<UnityEngine.Object>(16);

        [TearDown]
        public void TearDown()
        {
            for (int i = _ownedObjects.Count - 1; i >= 0; i--)
            {
                if (_ownedObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_ownedObjects[i]);
                }
            }

            _ownedObjects.Clear();
        }

        [TestCase("InventoryWindow", "Inventory")]
        [TestCase("UIWindowInventoryWindow", "Inventory")]
        [TestCase("UIHTTPSettingsWindow", "HTTP Settings")]
        [TestCase("Login", "Login")]
        public void CreatorTitleFormatter_ProducesReadableTemplateTitle(
            string typeName,
            string expected)
        {
            Assert.AreEqual(
                expected,
                UIWindowTitleFormatter.BuildTemplateTitleText(typeName));
        }

        [Test]
        public void CreatorValidator_RejectsKeywordsAndInvalidNamespaceSegments()
        {
            Assert.IsTrue(UIWindowCreationValidator.IsValidCSharpIdentifier("Inventory2"));
            Assert.IsFalse(UIWindowCreationValidator.IsValidCSharpIdentifier("class"));
            Assert.IsFalse(UIWindowCreationValidator.IsValidCSharpIdentifier("9Inventory"));
            Assert.IsFalse(UIWindowCreationValidator.IsValidCSharpIdentifier("Inventory-Window"));

            Assert.IsTrue(UIWindowCreationValidator.IsValidNamespace("Game.UI.Windows"));
            Assert.IsTrue(UIWindowCreationValidator.IsValidNamespace(string.Empty));
            Assert.IsFalse(UIWindowCreationValidator.IsValidNamespace("Game..Windows"));
            Assert.IsFalse(UIWindowCreationValidator.IsValidNamespace("Game.class"));
        }

        [Test]
        public void CreatorValidator_BuildsExactProjectAssetPaths()
        {
            const string folderPath = SamplesFolder;
            DefaultAsset folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
            Assert.IsNotNull(folder, "The UIFramework Samples folder must be importable.");
            var request = new UIWindowCreationRequest(
                "CreatorContractWindow",
                "Game.UI",
                folder,
                folder,
                folder,
                folder,
                null,
                true,
                UIWindowConfiguration.PrefabSource.PrefabReference,
                true,
                string.Empty);

            Assert.IsTrue(
                UIWindowCreationValidator.TryBuildPaths(
                    request,
                    out UIWindowCreationPaths paths,
                    out string error),
                error);
            Assert.AreEqual(folderPath + "/CreatorContractWindow.cs", paths.ScriptFilePath);
            Assert.AreEqual(folderPath + "/CreatorContractWindow.prefab", paths.PrefabFilePath);
            Assert.AreEqual(folderPath + "/CreatorContractWindow_Config.asset", paths.ConfigFilePath);
            Assert.AreEqual(folderPath + "/ICreatorContractWindowView.cs", paths.ViewInterfaceFilePath);
            Assert.AreEqual(folderPath + "/CreatorContractWindowPresenter.cs", paths.PresenterFilePath);
        }

        [Test]
        public void CreatorValidator_ResolvesAsmdefAndAsmrefBoundaries()
        {
            UIWindowAssemblyValidator.InvalidateCache();
            Assert.IsTrue(
                UIWindowAssemblyValidator.TryResolveOutputAssemblyName(
                    SamplesFolder + "/GeneratedWindow.cs",
                    out string sampleAssembly,
                    out string sampleError),
                sampleError);
            Assert.AreEqual("CycloneGames.UIFramework.Samples", sampleAssembly);

            string probeFolder = SamplesFolder + "/__UIWindowCreatorAsmrefProbe";
            string asmrefPath = probeFolder + "/Probe.asmref";
            string absoluteFolder = ToAbsoluteAssetPath(probeFolder);
            string absoluteAsmref = ToAbsoluteAssetPath(asmrefPath);
            Directory.CreateDirectory(absoluteFolder);
            try
            {
                File.WriteAllText(
                    absoluteAsmref,
                    "{\"reference\":\"CycloneGames.UIFramework.Runtime\"}",
                    new UTF8Encoding(false));
                Assert.IsTrue(
                    UIWindowAssemblyValidator.TryResolveOutputAssemblyName(
                        probeFolder + "/GeneratedWindow.cs",
                        out string namedReferenceAssembly,
                        out string namedReferenceError),
                    namedReferenceError);
                Assert.AreEqual(RuntimeAssemblyName, namedReferenceAssembly);

                string runtimeAsmdef =
                    CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(
                        RuntimeAssemblyName);
                Assert.IsNotEmpty(
                    runtimeAsmdef,
                    $"Assembly '{RuntimeAssemblyName}' must resolve to an asmdef path.");
                string runtimeGuid = AssetDatabase.AssetPathToGUID(runtimeAsmdef);
                Assert.IsNotEmpty(runtimeGuid);
                File.WriteAllText(
                    absoluteAsmref,
                    "{\"reference\":\"GUID:" + runtimeGuid + "\"}",
                    new UTF8Encoding(false));
                Assert.IsTrue(
                    UIWindowAssemblyValidator.TryResolveOutputAssemblyName(
                        probeFolder + "/GeneratedWindow.cs",
                        out string guidReferenceAssembly,
                        out string guidReferenceError),
                    guidReferenceError);
                Assert.AreEqual(RuntimeAssemblyName, guidReferenceAssembly);
            }
            finally
            {
                if (Directory.Exists(absoluteFolder))
                {
                    Directory.Delete(absoluteFolder, true);
                }
                UIWindowAssemblyValidator.InvalidateCache();
            }
        }

        [Test]
        public void CreatorValidator_RejectsIncompatibleAssemblyOutputs()
        {
            const string editorFolder = PackageRoot + "/Editor/UIWindowCreator/";
            const string noEngineFolder =
                "Assets/ThirdParty/CycloneGames/CycloneGames.DeterministicMath/Core/";
            string runtimeAsmdef =
                CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(
                    RuntimeAssemblyName);
            Assert.IsNotEmpty(
                runtimeAsmdef,
                $"Assembly '{RuntimeAssemblyName}' must resolve to an asmdef path.");
            string runtimeFolder =
                Path.GetDirectoryName(runtimeAsmdef)?.Replace('\\', '/') + "/";
            var errors = new List<string>();

            UIWindowAssemblyValidator.InvalidateCache();
            var editorPaths = new UIWindowCreationPaths(
                editorFolder,
                SamplesFolder + "/",
                SamplesFolder + "/",
                editorFolder,
                "EditorOnlyWindow");
            UIWindowAssemblyValidator.Validate(editorPaths, false, errors);
            Assert.IsTrue(errors.Exists(error => error.Contains("Editor-only")));

            errors.Clear();
            var noEnginePaths = new UIWindowCreationPaths(
                noEngineFolder,
                SamplesFolder + "/",
                SamplesFolder + "/",
                noEngineFolder,
                "NoEngineWindow");
            UIWindowAssemblyValidator.Validate(noEnginePaths, false, errors);
            Assert.IsTrue(errors.Exists(error => error.Contains("noEngineReferences=true")));

            errors.Clear();
            var incompatibleMvpPaths = new UIWindowCreationPaths(
                SamplesFolder + "/",
                SamplesFolder + "/",
                SamplesFolder + "/",
                runtimeFolder,
                "AssemblySplitWindow");
            UIWindowAssemblyValidator.Validate(incompatibleMvpPaths, true, errors);
            Assert.IsTrue(errors.Exists(error => error.Contains("cannot reference generated view assembly")));
        }

        [Test]
        public void CreatorAtomicWrite_DoesNotReplaceExistingScriptOrOrphanMetadata()
        {
            string assetPath = SamplesFolder + "/__UIWindowCreatorAtomicWriteProbe.cs";
            string absolutePath = ToAbsoluteAssetPath(assetPath);
            AssetDatabase.DeleteAsset(assetPath);
            DeleteFileIfPresent(absolutePath);
            DeleteFileIfPresent(absolutePath + ".meta");
            AssetDatabase.StartAssetEditing();
            try
            {
                UIWindowCreatorWindow.WriteAssetText(assetPath, "first\n", false);
                Assert.AreEqual("first\n", File.ReadAllText(absolutePath));
                Assert.Throws<IOException>(
                    () => UIWindowCreatorWindow.WriteAssetText(assetPath, "second\n", false));
                Assert.AreEqual("first\n", File.ReadAllText(absolutePath));

                File.Delete(absolutePath);
                File.WriteAllText(
                    absolutePath + ".meta",
                    "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n",
                    new UTF8Encoding(false));
                Assert.IsFalse(
                    UIWindowCreationValidator.TryEnsureOutputAvailable(
                        assetPath,
                        ".cs",
                        out _,
                        out string collisionError));
                StringAssert.Contains("orphan metadata", collisionError);
            }
            finally
            {
                try
                {
                    DeleteFileIfPresent(absolutePath);
                    DeleteFileIfPresent(absolutePath + ".meta");
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
            }
        }

        [Test]
        public void PendingJournalValidation_RejectsUnboundedAndInconsistentRecords()
        {
            const int prefabReference = (int)UIWindowConfiguration.PrefabSource.PrefabReference;
            Assert.IsTrue(
                UIWindowCreatorPostCompileProcessor.ValidatePendingOperationForTests(
                    "JournalWindow",
                    "Game.UI",
                    "Assets/UI/JournalWindow.prefab",
                    "Assets/UI/JournalWindow_Config.asset",
                    prefabReference,
                    0,
                    false,
                    string.Empty,
                    out string validError),
                validError);

            Assert.IsFalse(
                UIWindowCreatorPostCompileProcessor.ValidatePendingOperationForTests(
                    "JournalWindow",
                    "Game.UI",
                    "Assets/UI/../JournalWindow.prefab",
                    "Assets/UI/JournalWindow_Config.asset",
                    prefabReference,
                    0,
                    false,
                    string.Empty,
                    out _));
            Assert.IsFalse(
                UIWindowCreatorPostCompileProcessor.ValidatePendingOperationForTests(
                    "JournalWindow",
                    "Game.UI",
                    "Assets/UI/JournalWindow.prefab",
                    "Assets/UI/JournalWindow_Config.asset",
                    999,
                    0,
                    false,
                    string.Empty,
                    out _));
            Assert.IsFalse(
                UIWindowCreatorPostCompileProcessor.ValidatePendingOperationForTests(
                    new string('A', 129),
                    "Game.UI",
                    "Assets/UI/JournalWindow.prefab",
                    "Assets/UI/JournalWindow_Config.asset",
                    prefabReference,
                    101,
                    false,
                    "unexpected error",
                    out _));
        }

        [Test]
        public void PendingJournalQuarantine_MovesInvalidFileWithoutReplacingAnotherFile()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "CycloneGames.UIFramework.Tests",
                Guid.NewGuid().ToString("N"));
            string sourcePath = Path.Combine(directory, "Pending.json");
            Directory.CreateDirectory(directory);
            File.WriteAllText(sourcePath, "invalid", new UTF8Encoding(false));
            string quarantinePath = string.Empty;
            try
            {
                Assert.IsTrue(
                    UIWindowCreatorPostCompileProcessor.TryQuarantineFile(
                        sourcePath,
                        out quarantinePath,
                        out string error),
                    error);
                Assert.IsFalse(File.Exists(sourcePath));
                Assert.IsTrue(File.Exists(quarantinePath));
                Assert.AreEqual(directory, Path.GetDirectoryName(quarantinePath));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void CreatorRollback_ContinuesAfterUnsafePathAndDeletesOwnedOutput()
        {
            string assetPath = SamplesFolder + "/__UIWindowCreatorRollbackProbe.asset";
            AssetDatabase.DeleteAsset(assetPath);
            UIWindowConfiguration ownedAsset =
                ScriptableObject.CreateInstance<UIWindowConfiguration>();
            try
            {
                AssetDatabase.CreateAsset(ownedAsset, assetPath);
                string ownedGuid = AssetDatabase.AssetPathToGUID(assetPath);
                UIWindowCreatorWindow.RollbackResult result =
                    UIWindowCreatorWindow.RollbackCreatedPaths(
                        new[]
                        {
                            new UIWindowCreatorWindow.CreatedAssetRecord(assetPath, ownedGuid),
                            new UIWindowCreatorWindow.CreatedAssetRecord(
                                "Assets/../Outside.cs",
                                "11111111111111111111111111111111")
                        });

                Assert.AreEqual(2, result.AttemptedCount);
                Assert.IsFalse(result.IsComplete);
                Assert.IsNull(AssetDatabase.LoadAssetAtPath<UIWindowConfiguration>(assetPath));
                CollectionAssert.Contains(result.ResidualPaths, "Assets/../Outside.cs");
                Assert.Greater(result.Failures.Length, 0);
            }
            finally
            {
                AssetDatabase.DeleteAsset(assetPath);
                if (ownedAsset != null && !EditorUtility.IsPersistent(ownedAsset))
                {
                    UnityEngine.Object.DestroyImmediate(ownedAsset);
                }
            }
        }

        [Test]
        public void CreatorRollback_PreservesReplacementWhenGuidIdentityChanges()
        {
            string assetPath = SamplesFolder + "/__UIWindowCreatorRollbackReplacementProbe.asset";
            string replacementStagingPath =
                SamplesFolder + "/__UIWindowCreatorRollbackReplacementStagingProbe.asset";
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.DeleteAsset(replacementStagingPath);
            UIWindowConfiguration original =
                ScriptableObject.CreateInstance<UIWindowConfiguration>();
            UIWindowConfiguration replacement = null;
            try
            {
                AssetDatabase.CreateAsset(original, assetPath);
                string originalGuid = AssetDatabase.AssetPathToGUID(assetPath);
                var ownership =
                    new UIWindowCreatorWindow.CreatedAssetRecord(assetPath, originalGuid);

                replacement = ScriptableObject.CreateInstance<UIWindowConfiguration>();
                AssetDatabase.CreateAsset(replacement, replacementStagingPath);
                string replacementGuid = AssetDatabase.AssetPathToGUID(replacementStagingPath);
                Assert.AreNotEqual(originalGuid, replacementGuid);
                Assert.IsTrue(AssetDatabase.DeleteAsset(assetPath));
                Assert.IsEmpty(
                    AssetDatabase.MoveAsset(replacementStagingPath, assetPath),
                    "The replacement fixture must occupy the original path with its own GUID.");
                Assert.AreEqual(replacementGuid, AssetDatabase.AssetPathToGUID(assetPath));

                UIWindowCreatorWindow.RollbackResult result =
                    UIWindowCreatorWindow.RollbackCreatedPaths(new[] { ownership });

                Assert.IsFalse(result.IsComplete);
                Assert.AreEqual(
                    replacementGuid,
                    AssetDatabase.AssetPathToGUID(assetPath));
                Assert.IsNotNull(
                    AssetDatabase.LoadAssetAtPath<UIWindowConfiguration>(assetPath));
                CollectionAssert.Contains(result.ResidualPaths, assetPath);
            }
            finally
            {
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.DeleteAsset(replacementStagingPath);
                if (original != null && !EditorUtility.IsPersistent(original))
                {
                    UnityEngine.Object.DestroyImmediate(original);
                }
                if (replacement != null && !EditorUtility.IsPersistent(replacement))
                {
                    UnityEngine.Object.DestroyImmediate(replacement);
                }
            }
        }

        [Test]
        public void TemplateProcessor_RemovesPlaceholderWindowAndUpdatesPreferredTmpTitle()
        {
            GameObject root = new GameObject(
                "Template",
                typeof(RectTransform),
                typeof(UIWindow));
            _ownedObjects.Add(root);
            GameObject titleObject = new GameObject(
                "Text (TMP)",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            titleObject.transform.SetParent(root.transform, false);
            TextMeshProUGUI title = titleObject.GetComponent<TextMeshProUGUI>();
            title.text = "Template";

            var processor = new UIWindowTemplateProcessor();
            processor.Process(root, "UIWindowInventoryWindow");

            Assert.IsNull(root.GetComponent<UIWindow>());
            Assert.AreEqual("Inventory", title.text);
        }

        [Test]
        public void CreatorTemplateInspection_RejectsNestedOrMultipleWindowAuthority()
        {
            GameObject root = new GameObject(
                "Template",
                typeof(RectTransform),
                typeof(UIWindow));
            _ownedObjects.Add(root);
            GameObject nested = new GameObject(
                "NestedWindow",
                typeof(RectTransform),
                typeof(UIWindow));
            nested.transform.SetParent(root.transform, false);

            UIWindowCreatorWindow.TemplateInspection inspection =
                UIWindowCreatorWindow.InspectTemplate(root);

            Assert.AreEqual(2, inspection.WindowComponentCount);
            Assert.IsTrue(inspection.HasRootWindowComponent);
            Assert.IsFalse(inspection.IsValid);
        }

        [Test]
        public void PerformanceAudit_ReusesBuffersAndDoesNotMutateRaycastAuthoring()
        {
            GameObject first = new GameObject("First", typeof(RectTransform));
            _ownedObjects.Add(first);
            first.AddComponent<HorizontalLayoutGroup>();
            first.AddComponent<ContentSizeFitter>();

            var images = new List<Image>(6);
            for (int i = 0; i < 6; i++)
            {
                GameObject child = new GameObject(
                    "Image" + i,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                child.transform.SetParent(first.transform, false);
                Image image = child.GetComponent<Image>();
                image.raycastTarget = true;
                images.Add(image);
            }
            images[0].gameObject.AddComponent<Button>();

            var auditor = new UIPerformanceAuditUtility();
            UIPerformanceAuditUtility.AuditReport firstReport = auditor.Audit(first);

            Assert.AreEqual(6, firstReport.GraphicsCount);
            Assert.AreEqual(6, firstReport.RaycastTargetCount);
            Assert.AreEqual(5, firstReport.LikelyDecorativeRaycastTargetCount);
            Assert.AreEqual(1, firstReport.LayoutAuthorityConflictCount);
            Assert.Greater(firstReport.WarningCount, 0);
            for (int i = 0; i < images.Count; i++)
            {
                Assert.IsTrue(images[i].raycastTarget);
            }

            GameObject second = new GameObject("Second", typeof(RectTransform));
            _ownedObjects.Add(second);
            UIPerformanceAuditUtility.AuditReport secondReport = auditor.Audit(second);
            Assert.AreEqual(0, secondReport.GraphicsCount);
            Assert.AreEqual(0, secondReport.LayoutAuthorityConflictCount);
            Assert.AreEqual(0, secondReport.Issues.Count);
        }

        private static string ToAbsoluteAssetPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                assetPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void DeleteFileIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
