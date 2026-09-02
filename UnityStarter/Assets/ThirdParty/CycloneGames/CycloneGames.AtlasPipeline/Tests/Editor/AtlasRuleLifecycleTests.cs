#if UNITY_EDITOR
// Unity EditMode integration tests for the rule lifecycle (必修 1 / 必修 2).
//
// These tests drive the REAL settings asset, the REAL asset database and the REAL undo stack, so
// they only compile and run inside Unity's Test Runner. They are guarded out of every other
// compilation, including offline harnesses that stub the Unity API.

using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CycloneGames.AtlasPipeline;
using CycloneGames.AtlasPipeline.Pure;

namespace CycloneGames.AtlasPipeline.Tests
{
    [TestFixture]
    public sealed class AtlasRuleLifecycleTests
    {
        private readonly List<string> _createdAssetPaths = new List<string>();
        private readonly List<string> _tempFolders = new List<string>();

        [TearDown]
        public void Cleanup()
        {
            // Unregister everything this fixture created first, while the assets still exist and
            // the slots still resolve to paths. Uses the same double-delete as the window's remove
            // callback: for object-reference arrays the first DeleteArrayElementAtIndex clears the
            // reference and only the second removes the slot.
            SerializedObject settingsObject = new SerializedObject(Settings);
            SerializedProperty list = settingsObject.FindProperty("ruleAssets");
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                UnityEngine.Object reference = list.GetArrayElementAtIndex(i).objectReferenceValue;
                string path = reference != null ? AssetDatabase.GetAssetPath(reference) : null;
                if (reference == null || _createdAssetPaths.Contains(path))
                {
                    int sizeBefore = list.arraySize;
                    list.DeleteArrayElementAtIndex(i);
                    if (list.arraySize == sizeBefore)
                    {
                        list.DeleteArrayElementAtIndex(i);
                    }
                }
            }

            settingsObject.ApplyModifiedProperties();

            for (int i = 0; i < _createdAssetPaths.Count; i++)
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_createdAssetPaths[i]) != null)
                {
                    AssetDatabase.DeleteAsset(_createdAssetPaths[i]);
                }
            }

            _createdAssetPaths.Clear();

            for (int i = 0; i < _tempFolders.Count; i++)
            {
                if (AssetDatabase.IsValidFolder(_tempFolders[i]))
                {
                    AssetDatabase.DeleteAsset(_tempFolders[i]);
                }
            }

            _tempFolders.Clear();
            AssetDatabase.Refresh();
        }

        private AtlasPipelineSettings Settings => AtlasPipeline.Settings;

        private AtlasRuleAsset CreateRegisteredRule(string name, string sourceFolder = null)
        {
            AtlasImportRule rule = AtlasImportRule.Create(
                name,
                sourceFolder ?? string.Empty,
                AtlasPlatformFormats.GetDefaultFormat(AtlasPlatform.Android),
                AtlasPlatformFormats.GetDefaultFormat(AtlasPlatform.Iphone),
                AtlasGranularity.PerSourceFolder,
                name + "Group");

            AtlasRuleAsset asset = AtlasPipeline.CreateAndRegisterRuleAsset(rule);
            if (asset != null)
            {
                _createdAssetPaths.Add(AssetDatabase.GetAssetPath(asset));
            }

            return asset;
        }

        private bool SettingsReferences(AtlasRuleAsset asset)
        {
            IReadOnlyList<AtlasRuleAsset> assets = Settings.RuleAssets;
            for (int i = 0; i < assets.Count; i++)
            {
                if (assets[i] == asset)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Creates a folder under Assets holding one imported sprite, so a test rule has a bucket:
        /// "mark all dirty" is only observable when there is something to mark.
        /// </summary>
        private string CreateSpriteFolder(string folderName)
        {
            string folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            _tempFolders.Add(folder);

            var texture = new Texture2D(4, 4);
            byte[] png = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);

            string pngPath = folder + "/dot.png";
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            File.WriteAllBytes(Path.Combine(projectRoot, pngPath), png);
            AssetDatabase.ImportAsset(pngPath);

            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();

            return folder;
        }

        // ── 必修 1: creation is one undoable unit ──────────────────────────────

        [Test]
        public void Create_RegistersReferenceAndWritesAsset()
        {
            AtlasRuleAsset asset = CreateRegisteredRule("LifecycleCreate");

            Assert.IsNotNull(asset, "creation failed");
            Assert.IsTrue(SettingsReferences(asset), "settings must reference the new rule");
            string path = AssetDatabase.GetAssetPath(asset);
            Assert.IsTrue(File.Exists(path), "the asset must exist on disk");
        }

        [Test]
        public void Create_Undo_RemovesReferenceAndAsset_Redo_RestoresBoth()
        {
            AtlasRuleAsset asset = CreateRegisteredRule("LifecycleUndo");
            Assert.IsNotNull(asset);
            string path = AssetDatabase.GetAssetPath(asset);
            string guid = AssetDatabase.AssetPathToGUID(path);

            Undo.PerformUndo();

            Assert.IsFalse(SettingsReferences(asset), "undo must remove the registration");
            Assert.IsNull(
                AssetDatabase.LoadAssetAtPath<AtlasRuleAsset>(path),
                "undo must revert the created asset itself");

            Undo.PerformRedo();

            AtlasRuleAsset restored = AssetDatabase.LoadAssetAtPath<AtlasRuleAsset>(path);
            Assert.IsNotNull(restored, "redo must restore the asset");
            Assert.AreEqual(
                guid,
                AssetDatabase.AssetPathToGUID(path),
                "redo restores the same asset");
            Assert.IsTrue(SettingsReferences(restored), "redo must restore the registration");
        }

        [Test]
        public void Create_WithNullRule_FailsWithoutSideEffects()
        {
            int before = Settings.RuleAssets.Count;

            Assert.IsNull(AtlasPipeline.CreateAndRegisterRuleAsset(null));
            Assert.AreEqual(before, Settings.RuleAssets.Count, "no entry may be added");
        }

        // ── 必修 1: unregister keeps the file, undo restores the same GUID ─────

        [Test]
        public void Unregister_KeepsAssetOnDisk_AndUndoRestoresSameGuid()
        {
            AtlasRuleAsset asset = CreateRegisteredRule("LifecycleUnregister");
            Assert.IsNotNull(asset);
            string path = AssetDatabase.GetAssetPath(asset);
            string guid = AssetDatabase.AssetPathToGUID(path);
            int countBefore = Settings.RuleAssets.Count;

            // The exact operation the window's onRemoveCallback performs: delete the array slot on
            // the settings SerializedObject and let ApplyModifiedProperties record the undo. The
            // second delete is the object-reference-array quirk: the first clears the reference,
            // only the second removes the slot.
            SerializedObject settingsObject = new SerializedObject(Settings);
            SerializedProperty list = settingsObject.FindProperty("ruleAssets");
            int slot = -1;
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == asset)
                {
                    slot = i;
                    break;
                }
            }

            Assert.GreaterOrEqual(slot, 0, "the rule must be registered before unregistering");

            // Start a fresh undo group. The creation inside CreateAndRegisterRuleAsset collapsed
            // its own group, and this test runs several undoable operations within one editor
            // frame — without the increment, PerformUndo would revert the unregister AND the
            // creation in one go (the editor itself advances groups on every interaction, which is
            // why production does not hit this).
            Undo.IncrementCurrentGroup();

            int sizeBefore = list.arraySize;
            list.DeleteArrayElementAtIndex(slot);
            if (list.arraySize == sizeBefore)
            {
                list.DeleteArrayElementAtIndex(slot);
            }

            settingsObject.ApplyModifiedProperties();

            Assert.AreEqual(
                countBefore - 1,
                Settings.RuleAssets.Count,
                "the slot itself must be removed, not just the reference");
            Assert.IsFalse(SettingsReferences(asset), "unregistered");
            Assert.IsNotNull(
                AssetDatabase.LoadAssetAtPath<AtlasRuleAsset>(path),
                "unregistering must keep the asset file (it is now an orphan)");

            Undo.PerformUndo();

            Assert.IsTrue(
                SettingsReferences(asset),
                "undo must restore the reference to the same asset");
            Assert.AreEqual(
                guid,
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)),
                "same GUID after undo");
        }

        // ── 必修 2: external rule-asset changes invalidate and rebuild ─────────

        [Test]
        public void DeletingRegisteredRule_KeepsMissingReference_AndAuditBlocks()
        {
            AtlasRuleAsset asset = CreateRegisteredRule("LifecycleDelete");
            Assert.IsNotNull(asset);
            string path = AssetDatabase.GetAssetPath(asset);

            AssetDatabase.DeleteAsset(path);
            _createdAssetPaths.Remove(path);

            // Routed exactly like the postprocessor routes it: this is the path that works with
            // the pipeline window closed.
            AtlasPipeline.HandleAssetChanges(new[] { path }, null, null, null);

            IReadOnlyList<AtlasRuleAsset> assets = Settings.RuleAssets;
            bool slotStillThere = false;
            for (int i = 0; i < assets.Count; i++)
            {
                if (assets[i] == null)
                {
                    slotStillThere = true;
                    break;
                }
            }

            Assert.IsTrue(
                slotStillThere,
                "the missing reference must stay so the audit can report it");

            IReadOnlyList<AtlasRuleAuditEntry> auditFindings = AtlasPipeline.AuditRules();
            bool missingReported = false;
            for (int i = 0; i < auditFindings.Count; i++)
            {
                if (auditFindings[i].Kind == AtlasRuleAuditKind.MissingReference)
                {
                    missingReported = true;
                    break;
                }
            }

            Assert.IsTrue(missingReported, "the audit must report the missing reference");

            IReadOnlyList<string> errors =
                AtlasPipeline.ValidateForBuild(includeNameScan: false);
            Assert.Greater(errors.Count, 0, "a missing rule reference must block the build");
        }

        [Test]
        public void ImportingRuleAsset_MarksAtlasesDirty()
        {
            // A rule asset import (an external edit, a git pull) must invalidate the rule cache and
            // mark atlases dirty even though membership did not change. A sprite is required: mark
            // all dirty is only observable when the index holds a bucket to mark.
            string spritesFolder = CreateSpriteFolder("TempAtlasRuleSprites");
            AtlasRuleAsset asset = CreateRegisteredRule("LifecycleReimport", spritesFolder);
            Assert.IsNotNull(asset);

            AtlasPipeline.RebuildIndex(markDirty: false);
            int dirtyBefore = AtlasPipeline.DirtyAtlasCount;

            // Change the rule's packing configuration through serialization, exactly the way an
            // external edit lands: the rule's AtlasMaxTextureSize property is read-only, so the
            // serialized field is what an editor or a git pull actually modifies.
            SerializedObject ruleObject = new SerializedObject(asset);
            ruleObject.FindProperty("rule.atlasMaxTextureSize").intValue = 1024;
            ruleObject.ApplyModifiedProperties();
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(asset));

            AtlasPipeline.HandleAssetChanges(
                new[] { AssetDatabase.GetAssetPath(asset) }, null, null, null);

            Assert.Greater(
                AtlasPipeline.DirtyAtlasCount,
                dirtyBefore,
                "a rule asset import must mark atlases dirty even without membership changes");
        }

        [Test]
        public void MovingRuleAsset_SettingsFollowsByGuid()
        {
            AtlasRuleAsset asset = CreateRegisteredRule("LifecycleMove");
            Assert.IsNotNull(asset);
            AssetDatabase.CreateFolder("Assets", "TempAtlasRuleMove");
            string folder = "Assets/TempAtlasRuleMove";
            _tempFolders.Add(folder);

            string newPath = folder + "/MovedRule.asset";
            string moveError = AssetDatabase.MoveAsset(
                AssetDatabase.GetAssetPath(asset), newPath);
            Assert.IsTrue(
                string.IsNullOrEmpty(moveError),
                "move must succeed" + (string.IsNullOrEmpty(moveError) ? "" : ": " + moveError));

            AtlasPipeline.HandleAssetChanges(
                new[] { newPath }, null, new[] { newPath }, new[] { AssetDatabase.GetAssetPath(asset) });

            Assert.IsTrue(SettingsReferences(asset), "the reference follows the GUID");
            Assert.AreEqual(
                newPath,
                AssetDatabase.GetAssetPath(asset),
                "the settings keep pointing at the moved asset");
        }
    }
}
#endif
