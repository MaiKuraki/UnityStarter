using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.TestTools;

#if UNITY_6000_0_OR_NEWER
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#else
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState;
#endif

namespace CycloneGames.GameplayTags.Tests.Editor
{
   public sealed class GameplayTagsEditorAuthoringTests
   {
      private string m_TemporaryProjectRoot;
      private string m_SettingsRoot;
      private Func<string> m_PreviousSettingsDirectory;
      private Func<IEnumerable<IGameplayTagSource>> m_PreviousProjectSources;
      private Action<string> m_PreviousLogWarning;
      private Action<string> m_PreviousLogError;
      private Func<bool> m_PreviousIsRuntimePlaying;
      private Func<byte[]> m_PreviousLoadBuildTagData;

      [SetUp]
      public void SetUp()
      {
         m_TemporaryProjectRoot = Path.Combine(
            Path.GetTempPath(),
            "CycloneGames.GameplayTags.Tests",
            Guid.NewGuid().ToString("N"));
         m_SettingsRoot = Path.Combine(m_TemporaryProjectRoot, "ProjectSettings", "GameplayTags");
         Directory.CreateDirectory(Path.Combine(m_TemporaryProjectRoot, "ProjectSettings"));

         m_PreviousSettingsDirectory = GameplayTagRuntimePlatform.GetProjectTagSettingsDirectory;
         m_PreviousProjectSources = GameplayTagRuntimePlatform.EnumerateProjectTagSources;
         m_PreviousLogWarning = GameplayTagRuntimePlatform.LogWarning;
         m_PreviousLogError = GameplayTagRuntimePlatform.LogError;
         m_PreviousIsRuntimePlaying = GameplayTagRuntimePlatform.IsRuntimePlaying;
         m_PreviousLoadBuildTagData = GameplayTagRuntimePlatform.LoadBuildTagData;
         GameplayTagManager.ResetForTests();
         GameplayTagRuntimePlatform.GetProjectTagSettingsDirectory = () => m_SettingsRoot;
         GameplayTagRuntimePlatform.EnumerateProjectTagSources = () => Array.Empty<IGameplayTagSource>();
         GameplayTagRuntimePlatform.LogWarning = message => Debug.LogWarning(message);
         GameplayTagRuntimePlatform.LogError = message => Debug.LogError(message);
      }

      [TearDown]
      public void TearDown()
      {
         GameplayTagManager.ResetForTests();
         GameplayTagRuntimePlatform.GetProjectTagSettingsDirectory = m_PreviousSettingsDirectory;
         GameplayTagRuntimePlatform.EnumerateProjectTagSources = m_PreviousProjectSources;
         GameplayTagRuntimePlatform.LogWarning = m_PreviousLogWarning;
         GameplayTagRuntimePlatform.LogError = m_PreviousLogError;
         GameplayTagRuntimePlatform.IsRuntimePlaying = m_PreviousIsRuntimePlaying;
         GameplayTagRuntimePlatform.LoadBuildTagData = m_PreviousLoadBuildTagData;
         if (!string.IsNullOrEmpty(m_TemporaryProjectRoot) && Directory.Exists(m_TemporaryProjectRoot))
            Directory.Delete(m_TemporaryProjectRoot, true);
      }

      [Test]
      public void FileSource_WritesCatalogAndPreservesDescription()
      {
         string path = Path.Combine(m_SettingsRoot, "Default.json");
         FileGameplayTagSource source = new(path);

         Assert.That(source.TryLoad(), Is.True);
         source.AddTag("Combat.Damage.Fire", "Fire damage");

         string json = File.ReadAllText(path, new UTF8Encoding(false, true));
         StringAssert.Contains("\"tags\"", json);
         StringAssert.Contains("\"description\": \"Fire damage\"", json);
         Assert.That(new FileGameplayTagSource(path).TryLoad(), Is.True);
      }

      [Test]
      public void FileSource_RejectsUnknownRootProperty()
      {
         string path = WriteRawSource("Unknown.json", "{\"metadata\":{},\"tags\":{}}");
         LogAssert.Expect(LogType.Error, new Regex("Unsupported JSON property", RegexOptions.IgnoreCase));

         Assert.That(new FileGameplayTagSource(path).TryLoad(), Is.False);
      }

      [Test]
      public void FileSource_RejectsJsonComments()
      {
         string path = WriteRawSource(
            "Comments.json",
            "{/* comments are not supported */\"tags\":{}}");
         LogAssert.Expect(LogType.Error, new Regex("comments are not supported", RegexOptions.IgnoreCase));

         Assert.That(new FileGameplayTagSource(path).TryLoad(), Is.False);
      }

      [Test]
      public void FileSource_LoadsDescriptionAndFlags()
      {
         string path = WriteRawSource(
            "Metadata.json",
            "{\"tags\":{\"UI.Hidden\":{\"description\":\"Hidden tag\",\"flags\":1}}}");
         FileGameplayTagSource source = new FileGameplayTagSource(path);
         Assert.That(source.TryLoad(), Is.True);
         GameplayTagRegistrationContext context = new GameplayTagRegistrationContext();
         source.RegisterTags(context);

         GameplayTagDefinition definition = context.GenerateDefinitions(true)
            .Find(static candidate => candidate.TagName == "UI.Hidden");
         Assert.That(definition, Is.Not.Null);
         Assert.That(definition.Description, Is.EqualTo("Hidden tag"));
         Assert.That(definition.Flags, Is.EqualTo(GameplayTagFlags.HideInEditor));
      }

      [Test]
      public void FileSource_RejectsTagDefinitionsOutsideTagsObject()
      {
         string path = WriteRawSource("InvalidRoot.json", "{\"Combat.Damage\":{}}");
         LogAssert.Expect(LogType.Error, new Regex("Unsupported JSON property", RegexOptions.IgnoreCase));

         Assert.That(new FileGameplayTagSource(path).TryLoad(), Is.False);
      }

      [Test]
      public void FileSource_RejectsInvalidUtf8()
      {
         Directory.CreateDirectory(m_SettingsRoot);
         string path = Path.Combine(m_SettingsRoot, "InvalidUtf8.json");
         File.WriteAllBytes(path, new byte[] { 0x7B, 0x22, 0xFF, 0x22, 0x7D });
         LogAssert.Expect(LogType.Error, new Regex("Unable to translate bytes|invalid", RegexOptions.IgnoreCase));

         Assert.That(new FileGameplayTagSource(path).TryLoad(), Is.False);
      }

      [Test]
      public void FileSource_RejectsUtf16ByteOrderMark()
      {
         Directory.CreateDirectory(m_SettingsRoot);
         string path = Path.Combine(m_SettingsRoot, "Utf16.json");
         File.WriteAllText(path, "{\"tags\":{}}", Encoding.Unicode);
         LogAssert.Expect(LogType.Error, new Regex("UTF-8 without a byte-order mark", RegexOptions.IgnoreCase));

         Assert.That(new FileGameplayTagSource(path).TryLoad(), Is.False);
      }

      [Test]
      public void FileSource_RejectsUtf8ByteOrderMark()
      {
         Directory.CreateDirectory(m_SettingsRoot);
         string path = Path.Combine(m_SettingsRoot, "Utf8Bom.json");
         File.WriteAllText(
            path,
            "{\"tags\":{}}",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true));
         LogAssert.Expect(LogType.Error, new Regex("UTF-8 without a byte-order mark", RegexOptions.IgnoreCase));

         Assert.That(new FileGameplayTagSource(path).TryLoad(), Is.False);
      }

      [Test]
      public void FileSource_RejectsTraversalOutsideSettingsRoot()
      {
         string outsidePath = Path.Combine(m_SettingsRoot, "..", "Outside.json");

         Assert.Throws<UnauthorizedAccessException>(() => new FileGameplayTagSource(outsidePath));
      }

      [Test]
      public void FileSource_IdentityPrecheckSharingViolationPreservesOriginalOnWindows()
      {
         if (Path.DirectorySeparatorChar != '\\')
            Assert.Ignore("File sharing denial is only deterministic on Windows.");

         string path = Path.Combine(m_SettingsRoot, "Locked.json");
         FileGameplayTagSource source = new(path);
         Assert.That(source.TryLoad(), Is.True);
         source.AddTag("Combat.Initial", "Initial");
         byte[] original = File.ReadAllBytes(path);

         using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
         {
            Assert.Throws<IOException>(() => source.AddTag("Combat.Second", "Second"));
         }
         CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
      }

      [Test]
      public void FileSource_RejectsSavingOverExternalContentChange()
      {
         string path = Path.Combine(m_SettingsRoot, "ExternalChange.json");
         FileGameplayTagSource source = new(path);
         Assert.That(source.TryLoad(), Is.True);
         source.AddTag("Combat.Initial", "Initial");

         const string externalContent = "{\"tags\":{\"External.Owner\":{\"description\":\"External\"}}}";
         File.WriteAllText(path, externalContent, new UTF8Encoding(false, true));
         byte[] externalBytes = File.ReadAllBytes(path);

         InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => source.AddTag("Combat.Second", "Second"));
         StringAssert.Contains("changed after it was loaded", exception.Message);
         CollectionAssert.AreEqual(externalBytes, File.ReadAllBytes(path));
      }

      [Test]
      public void FileSource_RejectsSavingWhenInitiallyMissingFileAppears()
      {
         string path = Path.Combine(m_SettingsRoot, "Appeared.json");
         FileGameplayTagSource source = new(path);
         Assert.That(source.TryLoad(), Is.True);

         Directory.CreateDirectory(m_SettingsRoot);
         const string externalContent = "{\"tags\":{\"External.Created\":{}}}";
         File.WriteAllText(path, externalContent, new UTF8Encoding(false, true));
         byte[] externalBytes = File.ReadAllBytes(path);

         Assert.Throws<InvalidOperationException>(() => source.AddTag("Combat.Local", "Local"));
         CollectionAssert.AreEqual(externalBytes, File.ReadAllBytes(path));
      }

      [Test]
      public void FileSource_ContentHashDetectsSameLengthExternalChange()
      {
         string path = WriteRawSource("SameLength.json", "{\"tags\":{\"External.A\":{}}}");
         FileGameplayTagSource source = new(path);
         Assert.That(source.TryLoad(), Is.True);

         File.WriteAllText(path, "{\"tags\":{\"External.B\":{}}}", new UTF8Encoding(false, true));
         byte[] externalBytes = File.ReadAllBytes(path);

         Assert.Throws<InvalidOperationException>(() => source.AddTag("Combat.Local", "Local"));
         CollectionAssert.AreEqual(externalBytes, File.ReadAllBytes(path));
      }

      [Test]
      public void FileSource_UpdatesIdentityAfterEachSuccessfulSave()
      {
         string path = Path.Combine(m_SettingsRoot, "Sequential.json");
         FileGameplayTagSource source = new(path);
         Assert.That(source.TryLoad(), Is.True);

         Assert.DoesNotThrow(() => source.AddTag("Combat.First", "First"));
         Assert.DoesNotThrow(() => source.AddTag("Combat.Second", "Second"));

         FileGameplayTagSource reloaded = new(path);
         Assert.That(reloaded.TryLoad(), Is.True);
         GameplayTagRegistrationContext context = new();
         reloaded.RegisterTags(context);
         List<GameplayTagDefinition> definitions = context.GenerateDefinitions(false);
         Assert.That(definitions.Exists(static definition => definition.TagName == "Combat.First"), Is.True);
         Assert.That(definitions.Exists(static definition => definition.TagName == "Combat.Second"), Is.True);
      }

      [Test]
      public void FileSource_PostReplaceConflictPreservesTargetAndRecoveryCopy()
      {
         const string loadedContent = "{\"tags\":{\"Loaded.Owner\":{}}}";
         const string externalContent = "{\"tags\":{\"External.Owner\":{}}}";
         const string candidateContent = "{\"tags\":{\"Candidate.Owner\":{}}}";
         string path = WriteRawSource("AtomicConflict.json", loadedContent);
         FileGameplayTagSource source = new(path);
         Assert.That(source.TryLoad(), Is.True);

         File.WriteAllText(path, externalContent, new UTF8Encoding(false, true));
         string temporaryPath = Path.Combine(m_SettingsRoot, ".AtomicConflict.candidate.tmp");
         string backupPath = Path.Combine(m_SettingsRoot, ".AtomicConflict.recovery.bak");
         File.WriteAllText(temporaryPath, candidateContent, new UTF8Encoding(false, true));

         IOException exception = Assert.Throws<IOException>(
            () => source.ReplaceExistingFile(temporaryPath, backupPath));

         StringAssert.Contains("preserved", exception.Message);
         Assert.That(File.ReadAllText(path, new UTF8Encoding(false, true)), Is.EqualTo(candidateContent));
         Assert.That(File.ReadAllText(backupPath, new UTF8Encoding(false, true)), Is.EqualTo(externalContent));
      }

      [Test]
      public void FileSource_DiscoveryRejectsRecoveryArtifacts()
      {
         Directory.CreateDirectory(m_SettingsRoot);
         string recoveryPath = Path.Combine(
            m_SettingsRoot,
            $".Catalog.json.{Guid.NewGuid():N}.bak");
         File.WriteAllText(recoveryPath, "recovery", new UTF8Encoding(false, true));

         InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => FileGameplayTagSource.ThrowIfRecoveryArtifactsExist(m_SettingsRoot));
         StringAssert.Contains("manual reconciliation", exception.Message);
      }

      [Test]
      public void BuildTransaction_RejectsOrphanAssetMeta()
      {
         Assert.Throws<BuildFailedException>(() =>
            BuildTags.ValidateUnownedOutputState(false, true, true, true, false));
      }

      [Test]
      public void BuildTransaction_RejectsOrphanDirectoryMeta()
      {
         Assert.Throws<BuildFailedException>(() =>
            BuildTags.ValidateUnownedOutputState(false, false, false, true, false));
      }

      [Test]
      public void BuildTransaction_RejectsExistingDirectoryWithoutMeta()
      {
         Assert.Throws<BuildFailedException>(() =>
            BuildTags.ValidateUnownedOutputState(false, false, true, false, false));
      }

      [Test]
      public void BuildTransaction_AcceptsUnoccupiedExistingResourcesDirectory()
      {
         Assert.DoesNotThrow(() =>
             BuildTags.ValidateUnownedOutputState(false, false, true, true, false));
      }

      [Test]
      public void BuildTransaction_PrePromotionMarkerNeverOwnsAnAppearingPayload()
      {
         Assert.Throws<InvalidDataException>(() =>
            BuildTags.ValidatePayloadCleanupPhase(phase: 0, payloadExists: true, metaExists: false));
      }

      [Test]
      public void BuildTransaction_PromotedPayloadMayBeCleanedBeforeImport()
      {
         Assert.DoesNotThrow(() =>
            BuildTags.ValidatePayloadCleanupPhase(phase: 1, payloadExists: true, metaExists: false));
      }

      [Test]
      public void BuildTransaction_PreImportMarkerNeverOwnsAssetMetadata()
      {
         Assert.Throws<InvalidDataException>(() =>
            BuildTags.ValidatePayloadCleanupPhase(phase: 1, payloadExists: true, metaExists: true));
      }

      [Test]
      public void BuildRecovery_RejectsOversizedMarkerBeforeReadingText()
      {
         string path = Path.Combine(m_TemporaryProjectRoot, "OversizedMarker.json");
         File.WriteAllBytes(path, new byte[17]);

         Assert.Throws<InvalidDataException>(() => BuildTags.ReadBoundedUtf8File(path, maxLength: 16));
      }

      [Test]
      public void BuildRecovery_RejectsOversizedPayloadBeforeHashing()
      {
         string path = Path.Combine(m_TemporaryProjectRoot, "OversizedPayload.bytes");
         using (FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.SetLength(17);

         Assert.Throws<InvalidDataException>(() => BuildTags.ComputeSha256File(path, maxLength: 16));
      }

      [Test]
      public void BuildBinary_RejectsBudgetOverflowBeforeWriting()
      {
         Assert.Throws<BuildFailedException>(() =>
            BuildTags.ValidateBuildDataSize((long)BuildTagBinaryFormat.MaxDataSizeBytes + 1));
      }

      [Test]
      public void BuildBinary_RejectsInvalidUtf16BeforeAllocatingOutput()
      {
         GameplayTagRuntimePlatform.IsRuntimePlaying = static () => false;
         GameplayTagRuntimePlatform.EnumerateProjectTagSources = static () => Array.Empty<IGameplayTagSource>();
         GameplayTagManager.RegisterDynamicTag("Build.InvalidText", "\uD800");

         Assert.Throws<BuildFailedException>(() => BuildTags.CreateBuildData());
      }

      [TestCase(0, 1)]
      [TestCase(127, 1)]
      [TestCase(128, 2)]
      [TestCase(16383, 2)]
      [TestCase(16384, 3)]
      public void BuildBinary_StringLengthPrefixSizeUsesCanonicalBoundaries(int value, int expected)
      {
         Assert.That(BuildTags.Get7BitEncodedIntSize(value), Is.EqualTo(expected));
      }

      [Test]
      public void ExactLengthReader_RejectsGrowthBeyondOpenedLength()
      {
         using MemoryStream source = new(new byte[] { 1, 2 });
         using ExactLengthReadStream reader = new(source, expectedLength: 1, leaveOpen: true);
         byte[] buffer = new byte[1];
         Assert.That(reader.Read(buffer, 0, 1), Is.EqualTo(1));

         Assert.Throws<InvalidDataException>(() => reader.Read(buffer, 0, 1));
      }

      [Test]
      public void BuildBinary_RoundTripsAllDefinitionsAndMetadata()
      {
         GameplayTagManager.ResetForTests();
         GameplayTagRuntimePlatform.IsRuntimePlaying = static () => false;
         GameplayTagRuntimePlatform.EnumerateProjectTagSources = static () => Array.Empty<IGameplayTagSource>();
         GameplayTagManager.RegisterDynamicTag(
            "Build.Ability",
            "Ability category",
            GameplayTagFlags.HideInEditor);
         GameplayTagManager.RegisterDynamicTag("Build.Ability.Fire", "Fire ability");

         byte[] data = BuildTags.CreateBuildData();
         Assert.That(BuildTags.CalculateBuildDataSize(GameplayTagManager.GetAllTags()), Is.EqualTo(data.Length));
         GameplayTagRuntimePlatform.LoadBuildTagData = () => data;
         GameplayTagRegistrationContext context = new GameplayTagRegistrationContext();
         new BuildGameplayTagSource().RegisterTags(context);
         List<GameplayTagDefinition> definitions = context.GenerateDefinitions(true);

         GameplayTagDefinition parent = definitions.Find(
            static definition => definition.TagName == "Build.Ability");
         GameplayTagDefinition child = definitions.Find(
            static definition => definition.TagName == "Build.Ability.Fire");
         Assert.That(parent, Is.Not.Null);
         Assert.That(parent.Description, Is.EqualTo("Ability category"));
         Assert.That(parent.Flags, Is.EqualTo(GameplayTagFlags.HideInEditor));
         Assert.That(child, Is.Not.Null);
         Assert.That(child.Description, Is.EqualTo("Fire ability"));
      }

      [TestCase((int)GameplayTagValidationScanStatus.Completed, 0, true)]
      [TestCase((int)GameplayTagValidationScanStatus.Completed, 1, false)]
      [TestCase((int)GameplayTagValidationScanStatus.Canceled, 0, false)]
      [TestCase((int)GameplayTagValidationScanStatus.Failed, 0, false)]
      public void ValidationCleanResult_RequiresCompletedFullScan(
         int rawStatus,
         int invalidCount,
         bool expected)
      {
         GameplayTagValidationScanStatus status = (GameplayTagValidationScanStatus)rawStatus;
         Assert.That(GameplayTagValidationReporter.IsCleanScanResult(status, invalidCount), Is.EqualTo(expected));
      }

      [Test]
      public void FileWatcher_RetriesOnlyTransientIoFailures()
      {
         Assert.That(
            GameplayTagsFileWatcher.IsTransientReloadFailure(
               new InvalidDataException("load failed", new IOException("sharing violation"))),
            Is.True);
         Assert.That(
            GameplayTagsFileWatcher.IsTransientReloadFailure(
               new InvalidDataException("invalid catalog")),
            Is.False);
      }

      [Test]
      public void ValidationScan_IncludesScriptableObjectSubassets()
      {
         string assetPath = AssetDatabase.GenerateUniqueAssetPath(
            "Assets/CycloneGames.GameplayTags.ValidationTest.asset");
         GameplayTagTestHolder mainAsset = ScriptableObject.CreateInstance<GameplayTagTestHolder>();
         GameplayTagTestHolder subAsset = ScriptableObject.CreateInstance<GameplayTagTestHolder>();
         GameplayTagValidationReporter reporter = ScriptableObject.CreateInstance<GameplayTagValidationReporter>();
         try
         {
            mainAsset.name = "Main";
            subAsset.name = "Sub";
            SetSerializedTagName(mainAsset, "Missing.Main");
            SetSerializedTagName(subAsset, "Missing.Sub");
            AssetDatabase.CreateAsset(mainAsset, assetPath);
            AssetDatabase.AddObjectToAsset(subAsset, mainAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            GameplayTagManager.ResetForTests();
            GameplayTagRuntimePlatform.EnumerateProjectTagSources = static () => Array.Empty<IGameplayTagSource>();
            GameplayTagManager.InitializeIfNeeded();
            reporter.ScanProjectAsset(assetPath, canFix: true);

            Assert.That(reporter.InvalidTagCount, Is.EqualTo(2));
         }
         finally
         {
            UnityEngine.Object.DestroyImmediate(reporter);
            bool deleted = AssetDatabase.DeleteAsset(assetPath);
            if (mainAsset != null)
            {
               UnityEngine.Object.DestroyImmediate(mainAsset, allowDestroyingAssets: true);
            }
            if (subAsset != null)
            {
               UnityEngine.Object.DestroyImmediate(subAsset, allowDestroyingAssets: true);
            }

            string absoluteAssetPath = Path.GetFullPath(assetPath);
            if (!deleted && (File.Exists(absoluteAssetPath) || File.Exists(absoluteAssetPath + ".meta")))
               Assert.Fail($"Failed to clean validation test asset '{assetPath}'.");
         }
      }

      [Test]
      public void PropertyDrawer_DetectsMixedValuesAcrossTargets()
      {
         GameplayTagTestHolder first = ScriptableObject.CreateInstance<GameplayTagTestHolder>();
         GameplayTagTestHolder second = ScriptableObject.CreateInstance<GameplayTagTestHolder>();
         try
         {
            SetSerializedTagName(first, "Combat.First");
            SetSerializedTagName(second, "Combat.Second");
            using SerializedObject serializedObject = new SerializedObject(new UnityEngine.Object[] { first, second });
            SerializedProperty name = serializedObject.FindProperty("Tag").FindPropertyRelative("m_Name");

            Assert.That(GameplayTagPropertyDrawer.HasMixedTagValues(name), Is.True);
         }
         finally
         {
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
         }
      }

      [Test]
      public void CatalogRefresh_DoesNotRemoveSerializedContainerAssignment()
      {
         GameplayTagTestHolder holder = ScriptableObject.CreateInstance<GameplayTagTestHolder>();
         try
         {
            using SerializedObject serializedObject = new SerializedObject(holder);
            SerializedProperty explicitTags = serializedObject.FindProperty("Container")
               .FindPropertyRelative("m_SerializedExplicitTags");
            explicitTags.arraySize = 1;
            explicitTags.GetArrayElementAtIndex(0).stringValue = "Catalog.RemainsAssigned";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            GameplayTagContainerTreeView tree = new GameplayTagContainerTreeView(
               new TreeViewState(), explicitTags);
            tree.RefreshAfterCatalogChange();
            serializedObject.Update();

            Assert.That(explicitTags.arraySize, Is.EqualTo(1));
            Assert.That(explicitTags.GetArrayElementAtIndex(0).stringValue,
               Is.EqualTo("Catalog.RemainsAssigned"));
         }
         finally
         {
            UnityEngine.Object.DestroyImmediate(holder);
         }
      }

      private string WriteRawSource(string fileName, string content)
      {
         Directory.CreateDirectory(m_SettingsRoot);
         string path = Path.Combine(m_SettingsRoot, fileName);
         File.WriteAllText(path, content, new UTF8Encoding(false, true));
         return path;
      }

      private static void SetSerializedTagName(GameplayTagTestHolder holder, string value)
      {
         using SerializedObject serializedObject = new SerializedObject(holder);
         SerializedProperty name = serializedObject.FindProperty("Tag").FindPropertyRelative("m_Name");
         name.stringValue = value;
         serializedObject.ApplyModifiedPropertiesWithoutUndo();
      }
   }

}
