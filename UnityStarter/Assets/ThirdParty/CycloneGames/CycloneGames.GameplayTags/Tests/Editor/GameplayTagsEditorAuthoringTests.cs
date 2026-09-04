using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;
using CycloneGames.GameplayTags.Unity.Editor;
using CycloneGames.Logging;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

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
      private GameplayTagTestHostPlatform m_Host;

      [SetUp]
      public void SetUp()
      {
         m_TemporaryProjectRoot = Path.Combine(
            Path.GetTempPath(),
            "CycloneGames.GameplayTags.Tests",
            Guid.NewGuid().ToString("N"));
         m_SettingsRoot = Path.Combine(m_TemporaryProjectRoot, "ProjectSettings", "GameplayTags");
         Directory.CreateDirectory(Path.Combine(m_TemporaryProjectRoot, "ProjectSettings"));
         Directory.CreateDirectory(Path.Combine(m_TemporaryProjectRoot, "Assets"));

         m_Host = GameplayTagTestHostPlatform.Install();
         m_Host.SettingsDirectory = m_SettingsRoot;
         m_Host.SetRuntimePlaying(false);
         m_Host.SetBuildData(null);
         m_Host.ClearRegisteredProjectTagSources();
         GameplayTagManager.ResetForTests();
      }

      [TearDown]
      public void TearDown()
      {
         GameplayTagManager.ResetForTests();
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
         FileGameplayTagSource source = new(path);

         Exception exception = AssertLoadFailure(source);

         Assert.That(exception, Is.TypeOf<InvalidDataException>());
         StringAssert.Contains("Unsupported JSON property", exception.Message);
      }

      [Test]
      public void FileSource_RejectsJsonComments()
      {
         string path = WriteRawSource(
            "Comments.json",
            "{/* comments are not supported */\"tags\":{}}");
         FileGameplayTagSource source = new(path);

         Exception exception = AssertLoadFailure(source);

         Assert.That(exception, Is.TypeOf<InvalidDataException>());
         StringAssert.Contains("comments are not supported", exception.Message);
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

         GameplayTagBuildResult result = context.Build();
         int hiddenIndex = Array.IndexOf(result.Names, "UI.Hidden");
         Assert.That(hiddenIndex, Is.GreaterThan(0));
         Assert.That(result.Descriptions[hiddenIndex], Is.EqualTo("Hidden tag"));
         Assert.That(result.Flags[hiddenIndex], Is.EqualTo(GameplayTagFlags.HideInEditor));
      }

      [Test]
      public void FileSource_RejectsTagDefinitionsOutsideTagsObject()
      {
         string path = WriteRawSource("InvalidRoot.json", "{\"Combat.Damage\":{}}");
         FileGameplayTagSource source = new(path);

         Exception exception = AssertLoadFailure(source);

         Assert.That(exception, Is.TypeOf<InvalidDataException>());
         StringAssert.Contains("Unsupported JSON property", exception.Message);
      }

      [Test]
      public void FileSource_RejectsInvalidUtf8()
      {
         Directory.CreateDirectory(m_SettingsRoot);
         string path = Path.Combine(m_SettingsRoot, "InvalidUtf8.json");
         File.WriteAllBytes(path, new byte[] { 0x7B, 0x22, 0xFF, 0x22, 0x7D });
         FileGameplayTagSource source = new(path);

         Exception exception = AssertLoadFailure(source);

         Assert.That(ContainsException<DecoderFallbackException>(exception), Is.True);
      }

      [Test]
      public void FileSource_RejectsUtf16ByteOrderMark()
      {
         Directory.CreateDirectory(m_SettingsRoot);
         string path = Path.Combine(m_SettingsRoot, "Utf16.json");
         File.WriteAllText(path, "{\"tags\":{}}", Encoding.Unicode);
         FileGameplayTagSource source = new(path);

         Exception exception = AssertLoadFailure(source);

         Assert.That(exception, Is.TypeOf<InvalidDataException>());
         StringAssert.Contains("UTF-8 without a byte-order mark", exception.Message);
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
         FileGameplayTagSource source = new(path);

         Exception exception = AssertLoadFailure(source);

         Assert.That(exception, Is.TypeOf<InvalidDataException>());
         StringAssert.Contains("UTF-8 without a byte-order mark", exception.Message);
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
         GameplayTagBuildResult result = context.Build();
         Assert.That(Array.IndexOf(result.Names, "Combat.First"), Is.GreaterThan(0));
         Assert.That(Array.IndexOf(result.Names, "Combat.Second"), Is.GreaterThan(0));
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
      public void BuildTransaction_PublishesToIsolatedResourcesPathAndCleansExactly()
      {
         byte[] payload = { 1, 2, 3, 4 };
         GameplayTagsBuildAssetTransaction transaction = GameplayTagsBuildAssetTransaction.Begin(
            m_TemporaryProjectRoot,
            payload,
            synchronizeAssetDatabase: false);
         try
         {
            string outputPath = ProjectPath(GameplayTagsBuildAssetTransaction.GeneratedAssetPath);
            Assert.That(File.ReadAllBytes(outputPath), Is.EqualTo(payload));
            Assert.That(File.Exists(outputPath + ".meta"), Is.True);
            Assert.That(File.Exists(ProjectPath(
               BuildTags.RecoveryStateDirectoryRelativePath + "/active.json")), Is.True);
            string journal = File.ReadAllText(ProjectPath(
               BuildTags.RecoveryStateDirectoryRelativePath + "/active.json"), new UTF8Encoding(false, true));
            StringAssert.Contains("\"beforeExists\":false", journal);
            StringAssert.Contains("\"expectedSha256\":", journal);
            StringAssert.Contains(GameplayTagsBuildAssetTransaction.GeneratedAssetPath, journal);

            transaction.Complete();

            Assert.That(File.Exists(outputPath), Is.False);
            Assert.That(File.Exists(outputPath + ".meta"), Is.False);
            Assert.That(Directory.Exists(ProjectPath("Assets/Generated")), Is.False);
            Assert.That(File.Exists(ProjectPath(
               BuildTags.RecoveryStateDirectoryRelativePath + "/active.json")), Is.False);
         }
         finally
         {
            transaction.Dispose();
         }
      }

      [Test]
      public void BuildRecovery_PublicFacadeIsAvailableForDependencyFreeReflection()
      {
         MethodInfo recover = typeof(BuildTags).GetMethod(
            nameof(BuildTags.Recover),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null);

         Assert.That(recover, Is.Not.Null);
         Assert.That(
            typeof(BuildTags).FullName,
            Is.EqualTo("CycloneGames.GameplayTags.Unity.Editor.BuildTags"));
         Assert.That(
            BuildTags.RecoveryStateDirectoryRelativePath,
            Is.EqualTo(".buildpipeline/transactions/gameplay-tags"));
      }

      [Test]
      public void BuildTransaction_PendingJournalFailsClosedUntilExplicitRecovery()
      {
         GameplayTagsBuildAssetTransaction transaction = GameplayTagsBuildAssetTransaction.Begin(
            m_TemporaryProjectRoot,
            new byte[] { 1, 2, 3 },
            synchronizeAssetDatabase: false);
         transaction.Dispose();

         BuildFailedException exception = Assert.Throws<BuildFailedException>(() =>
            GameplayTagsBuildAssetTransaction.Begin(
               m_TemporaryProjectRoot,
               new byte[] { 4, 5, 6 },
               synchronizeAssetDatabase: false));
         StringAssert.Contains("explicit recovery", exception.Message);

         GameplayTagsBuildAssetTransaction.Recover(
            m_TemporaryProjectRoot,
            synchronizeAssetDatabase: false);
         Assert.That(
            File.Exists(ProjectPath(GameplayTagsBuildAssetTransaction.GeneratedAssetPath)),
            Is.False);
      }

      [Test]
      public void BuildTransaction_CleanupFailureThrowsAndRetainsEvidence()
      {
         GameplayTagsBuildAssetTransaction transaction = GameplayTagsBuildAssetTransaction.Begin(
            m_TemporaryProjectRoot,
            new byte[] { 1, 2, 3 },
            synchronizeAssetDatabase: false);
         string outputPath = ProjectPath(GameplayTagsBuildAssetTransaction.GeneratedAssetPath);
         File.WriteAllBytes(outputPath, new byte[] { 9, 9, 9 });

         BuildFailedException exception;
         try
         {
            exception = Assert.Throws<BuildFailedException>(() => transaction.Complete());
         }
         finally
         {
            transaction.Dispose();
         }

         StringAssert.Contains("journaled hash", exception.Message);
         Assert.That(File.Exists(outputPath), Is.True);
         Assert.That(File.Exists(ProjectPath(
            BuildTags.RecoveryStateDirectoryRelativePath + "/active.json")), Is.True);
      }

      [Test]
      public void BuildRecovery_UnknownContentPreventsEveryDeletion()
      {
         GameplayTagsBuildAssetTransaction transaction = GameplayTagsBuildAssetTransaction.Begin(
            m_TemporaryProjectRoot,
            new byte[] { 1, 2, 3 },
            synchronizeAssetDatabase: false);
         transaction.Dispose();
         string ownedDirectory = ProjectPath(
            "Assets/Generated/CycloneGames.GameplayTags/Resources/CycloneGames.GameplayTags");
         string unknownPath = Path.Combine(ownedDirectory, "UserOwned.txt");
         File.WriteAllText(unknownPath, "keep", new UTF8Encoding(false, true));

         BuildFailedException exception = Assert.Throws<BuildFailedException>(() =>
            GameplayTagsBuildAssetTransaction.Recover(
               m_TemporaryProjectRoot,
               synchronizeAssetDatabase: false));

         StringAssert.Contains("Unknown content", exception.Message);
         Assert.That(File.Exists(unknownPath), Is.True);
         Assert.That(
            File.Exists(ProjectPath(GameplayTagsBuildAssetTransaction.GeneratedAssetPath)),
            Is.True,
            "Recovery must preflight all effects before deleting any owned output.");
      }

      [Test]
      public void BuildRecovery_RejectsOversizedMarkerBeforeReadingText()
      {
         string path = Path.Combine(m_TemporaryProjectRoot, "OversizedMarker.json");
         File.WriteAllBytes(path, new byte[17]);

         Assert.Throws<InvalidDataException>(() =>
            GameplayTagsBuildAssetTransaction.ReadBoundedUtf8File(path, maxLength: 16));
      }

      [Test]
      public void BuildRecovery_RejectsOversizedPayloadBeforeHashing()
      {
         string path = Path.Combine(m_TemporaryProjectRoot, "OversizedPayload.bytes");
         using (FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.SetLength(17);

         Assert.Throws<InvalidDataException>(() =>
            GameplayTagsBuildAssetTransaction.ComputeSha256File(path, maxLength: 16));
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
         TestHost.IsRuntimePlaying = false;
         TestHost.ClearRegisteredProjectTagSources();
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
         TestHost.IsRuntimePlaying = false;
         TestHost.ClearRegisteredProjectTagSources();
         GameplayTagManager.RegisterDynamicTag(
            "Build.Ability",
            "Ability category",
            GameplayTagFlags.HideInEditor);
         GameplayTagManager.RegisterDynamicTag("Build.Ability.Fire", "Fire ability");

         byte[] data = BuildTags.CreateBuildData();
         Assert.That(BuildTags.CalculateBuildDataSize(GameplayTagManager.Current.CreateAllTagsArray()), Is.EqualTo(data.Length));
         TestHost.SetBuildData(data);
         GameplayTagRegistrationContext context = new GameplayTagRegistrationContext();
         new BuildGameplayTagSource().RegisterTags(context);
         GameplayTagBuildResult result = context.Build();

         int parentIndex = Array.IndexOf(result.Names, "Build.Ability");
         int childIndex = Array.IndexOf(result.Names, "Build.Ability.Fire");
         Assert.That(parentIndex, Is.GreaterThan(0));
         Assert.That(childIndex, Is.GreaterThan(0));
         Assert.That(result.Descriptions[parentIndex], Is.EqualTo("Ability category"));
         Assert.That(result.Flags[parentIndex], Is.EqualTo(GameplayTagFlags.HideInEditor));
         Assert.That(result.Descriptions[childIndex], Is.EqualTo("Fire ability"));
         Assert.That(result.ParentIndices[childIndex], Is.EqualTo(parentIndex));
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
            TestHost.ClearRegisteredProjectTagSources();
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
            SerializedProperty name = serializedObject.FindProperty("Tag").FindPropertyRelative("tagName");

            Assert.That(GameplayTagPropertyDrawer.HasMixedTagValues(name), Is.True);
         }
         finally
         {
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
         }
      }

      [Test]
      public void SerializedBridge_ExposesTheReadOnlyContainerContract()
      {
         GameplayTagRegistrationContext context = new GameplayTagRegistrationContext();
         context.RegisterTag("Bridge.A", null, GameplayTagFlags.None);
         context.RegisterTag("Bridge.A.B", null, GameplayTagFlags.None);
         context.RegisterTag("Bridge.C", null, GameplayTagFlags.None);
         GameplayTagBuildResult result = context.Build();

         var bridge = new SerializableGameplayTagContainer();
         bridge.LoadPersisted(new[] { "Bridge.A.B", "Bridge.C" });

         // Interface members through the bridge itself - the GAS editor's PropertyField path walks these.
         Assert.That(bridge.ExplicitTagCount, Is.EqualTo(2));
         Assert.That(bridge.TagCount, Is.EqualTo(4));
         Assert.That(bridge.ContainsRuntimeIndex(
            GameplayTagManager.Request("Bridge.A.B").RuntimeIndex, true), Is.True);

         // Extension methods over the interface work on the bridge directly.
         var other = new SerializableGameplayTagContainer();
         other.LoadPersisted(new[] { "Bridge.A", "Bridge.C" });
         Assert.That(bridge.HasTag(GameplayTagManager.Request("Bridge.A")), Is.True);
         Assert.That(bridge.HasAll(other), Is.True);
         Assert.That(bridge.HasAny(other), Is.True);

         // Implicit conversion reads the same set.
         GameplayTagContainer converted = bridge;
         Assert.That(converted.HasTagExact(GameplayTagManager.Request("Bridge.A.B")), Is.True);

         // The requirement bridge converts to the Core struct and evaluates the pair.
         var requirements = new SerializableGameplayTagRequirements();
         requirements.RequiredTags.LoadPersisted(new[] { "Bridge.A" });
         requirements.ForbiddenTags.LoadPersisted(new[] { "Bridge.C" });
         var met = new SerializableGameplayTagContainer();
         met.LoadPersisted(new[] { "Bridge.A.B" });
         Assert.That(requirements.IsEmpty, Is.False);
         Assert.That(requirements.Matches(met), Is.True);

         var unmet = new SerializableGameplayTagContainer();
         unmet.LoadPersisted(new[] { "Bridge.C" });
         Assert.That(requirements.Matches(unmet), Is.False);
      }

      [Test]
      public void CatalogRefresh_DoesNotRemoveSerializedContainerAssignment()
      {
         GameplayTagTestHolder holder = ScriptableObject.CreateInstance<GameplayTagTestHolder>();
         try
         {
            using SerializedObject serializedObject = new SerializedObject(holder);
            SerializedProperty explicitTags = serializedObject.FindProperty("Container")
               .FindPropertyRelative("explicitTagNames");
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

      private static Exception AssertLoadFailure(FileGameplayTagSource source)
      {
         var writer = new RecordingLogWriter();
         ILogWriter previousWriter = InstallWriter(writer);
         bool loaded;
         try
         {
            loaded = source.TryLoad();
         }
         finally
         {
            RestoreWriter(previousWriter, writer);
         }

         Assert.That(loaded, Is.False);
         Assert.That(source.LastLoadException, Is.Not.Null);
         Assert.That(writer.Count, Is.EqualTo(1));
         LogRecord record = writer.LastRecord;
         Assert.That(record.Severity, Is.EqualTo(LogSeverity.Error));
         Assert.That(record.Category, Is.EqualTo("CycloneGames.GameplayTags"));
         Assert.That(record.Exception, Is.SameAs(source.LastLoadException));
         StringAssert.Contains($"Failed to load gameplay tags from '{source.Name}'.", record.Message);
         return source.LastLoadException;
      }

      private static bool ContainsException<TException>(Exception exception)
         where TException : Exception
      {
         while (exception != null)
         {
            if (exception is TException)
               return true;
            exception = exception.InnerException;
         }

         return false;
      }

      private static void RestoreWriter(ILogWriter previousWriter, RecordingLogWriter writer)
      {
         LogRuntime.TryReplaceWriter(writer, previousWriter);
      }

      private static ILogWriter InstallWriter(ILogWriter writer)
      {
         ILogWriter previousWriter = LogRuntime.Writer;
         Assert.IsTrue(LogRuntime.TryReplaceWriter(previousWriter, writer));
         return previousWriter;
      }

      private readonly struct LogRecord
      {
         public LogRecord(
            LogSeverity severity,
            string category,
            string message,
            Exception exception)
         {
            Severity = severity;
            Category = category;
            Message = message;
            Exception = exception;
         }

         public LogSeverity Severity { get; }
         public string Category { get; }
         public string Message { get; }
         public Exception Exception { get; }
      }

      private sealed class RecordingLogWriter : ILogWriter
      {
         private readonly object m_Gate = new object();
         private int m_Count;
         private LogRecord m_LastRecord;

         public int Count
         {
            get
            {
               lock (m_Gate)
                  return m_Count;
            }
         }

         public LogRecord LastRecord
         {
            get
            {
               lock (m_Gate)
                  return m_LastRecord;
            }
         }

         public bool IsEnabled(LogSeverity severity, string category)
         {
            return severity >= LogSeverity.Error && severity < LogSeverity.None;
         }

         public void Write(
            LogSeverity severity,
            string category,
            string message,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
         {
            Record(severity, category, message, null);
         }

         public void Write(
            LogSeverity severity,
            string category,
            Action<StringBuilder> messageBuilder,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
         {
            if (!IsEnabled(severity, category))
               return;

            var builder = new StringBuilder(128);
            messageBuilder?.Invoke(builder);
            Record(severity, category, builder.ToString(), null);
         }

         public void Write<TState>(
            LogSeverity severity,
            string category,
            TState state,
            Action<TState, StringBuilder> messageBuilder,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
         {
            if (!IsEnabled(severity, category))
               return;

            var builder = new StringBuilder(128);
            messageBuilder?.Invoke(state, builder);
            Record(severity, category, builder.ToString(), null);
         }

         public void WriteException(
            LogSeverity severity,
            string category,
            Exception exception,
            string message = null,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
         {
            Record(severity, category, message, exception);
         }

         private void Record(
            LogSeverity severity,
            string category,
            string message,
            Exception exception)
         {
            if (!IsEnabled(severity, category))
               return;

            lock (m_Gate)
            {
               m_Count++;
               m_LastRecord = new LogRecord(severity, category, message, exception);
            }
         }
      }

      private static void SetSerializedTagName(GameplayTagTestHolder holder, string value)
      {
         using SerializedObject serializedObject = new SerializedObject(holder);
         SerializedProperty name = serializedObject.FindProperty("Tag").FindPropertyRelative("tagName");
         name.stringValue = value;
         serializedObject.ApplyModifiedPropertiesWithoutUndo();
      }

      private string ProjectPath(string projectRelativePath)
      {
         string path = m_TemporaryProjectRoot;
         foreach (string segment in projectRelativePath.Split('/'))
            path = Path.Combine(path, segment);
         return path;
      }
   }

}
