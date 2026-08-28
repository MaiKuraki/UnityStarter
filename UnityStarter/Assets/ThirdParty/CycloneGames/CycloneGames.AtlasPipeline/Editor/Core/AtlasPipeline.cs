using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using CycloneGames.Logging;

namespace CycloneGames.AtlasPipeline
{
    /// <summary>
    /// Incremental, data-driven editor pipeline for CycloneGames atlas sprite importers and SpriteAtlas assets.
    /// The importer hook applies settings before import (avoiding reimport loops), while the atlas
    /// index is updated only from postprocessor asset changes. Full scans are reserved for explicit
    /// rebuild requests instead of running on every image import.
    /// </summary>
    public static class AtlasPipeline
    {
        public const string SettingsAssetPath = AtlasPipelineSettings.DefaultAssetPath;

        private static readonly Dictionary<string, HashSet<string>> AtlasToAssets =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> AssetToAtlas =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> DirtyAtlasKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<AtlasImportRule> RuleCache =
            new List<AtlasImportRule>();

        private static readonly List<string> GeneratedAtlasPaths =
            new List<string>();
        private static readonly List<string> DeletedAtlasPaths =
            new List<string>();
        private static readonly List<string> PendingAtlasConfigure =
            new List<string>();

        private static AtlasPipelineSettings _settingsCache;
        private static bool _initialized;
        private static double _nextProcessTime;
        private static bool _invalidNamePromptScheduled;
        private static bool _textureSizePromptScheduled;
        private static bool _outputFolderIntrusionPromptScheduled;
        private static bool _spritePackerPromptScheduled;
        private static bool _projectChangedRefreshScheduled;
        private static int _batchedEditingDepth;

        private const double MaxEditorFrameBudgetSeconds = 0.008d;

        private static readonly HashSet<string> TextureSizeWarnings =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> OutputFolderIntrusions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static AtlasPipelineSettings Settings
        {
            get
            {
                EnsureSettingsAsset();
                return _settingsCache;
            }
        }

        public static AtlasPipelineSettings TryGetSettings()
        {
            if (_settingsCache != null)
            {
                return _settingsCache;
            }

            _settingsCache = AssetDatabase.LoadAssetAtPath<AtlasPipelineSettings>(
                SettingsAssetPath);
            if (_settingsCache != null)
            {
                RefreshRuleOrder();
            }

            return _settingsCache;
        }

        public static bool IsUsingSupportedSpriteAtlasMode()
        {
            return EditorSettings.spritePackerMode == SpritePackerMode.SpriteAtlasV2
                   || EditorSettings.spritePackerMode == SpritePackerMode.SpriteAtlasV2Build;
        }

        public static bool IsSpriteAtlasAlwaysEnabled()
        {
            return EditorSettings.spritePackerMode == SpritePackerMode.SpriteAtlasV2;
        }

        public static AtlasPipelineSnapshot GetSnapshot()
        {
            EnsureInitialized();
            return new AtlasPipelineSnapshot(
                RuleCache.Count,
                AssetToAtlas.Count,
                AtlasToAssets.Count,
                DirtyAtlasKeys.Count);
        }

        [InitializeOnLoadMethod]
        private static void InitializeOnLoad()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            if (DirtyAtlasKeys.Count == 0
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.timeSinceStartup < _nextProcessTime)
            {
                return;
            }

            // Slice by time budget rather than a fixed count: generating one large atlas can cost
            // far more than several small ones, so a fixed 8-per-batch would stutter periodically.
            ProcessDirtyAtlases(
                maxCount: 8,
                timeBudgetSeconds: MaxEditorFrameBudgetSeconds);
            _nextProcessTime = EditorApplication.timeSinceStartup + 0.15d;
        }

        /// <summary>
        /// Unified entry point for projectChanged (the window callback forwards here). Two layers
        /// of protection:
        /// 1. projectChanged events raised by this tool's own batch operations (atlas generation /
        ///    import-setting application) are skipped — they are the source of the full-rescan
        ///    feedback loop;
        /// 2. external changes (git operations, direct file edits) still trigger a rescan, but are
        ///    coalesced through delayCall so dozens of events from one refresh collapse into one
        ///    full rescan.
        /// </summary>
        public static void HandleProjectChanged()
        {
            if (_batchedEditingDepth > 0)
            {
                return;
            }

            if (_projectChangedRefreshScheduled)
            {
                return;
            }

            _projectChangedRefreshScheduled = true;
            EditorApplication.delayCall += () =>
            {
                _projectChangedRefreshScheduled = false;
                if (_batchedEditingDepth > 0)
                {
                    return;
                }

                RefreshForProjectChanged();
            };
        }

        /// <summary>
        /// Enter/exit pair for batched asset editing. Uses a reference count so nested calls are
        /// safe. StartAssetEditing pauses imports and StopAssetEditing flushes them once; combined
        /// with HandleProjectChanged this suppresses the full rescans our own edits would trigger.
        /// </summary>
        public static void BeginBatchedAssetEditing()
        {
            _batchedEditingDepth++;
            AssetDatabase.StartAssetEditing();
        }

        public static void EndBatchedAssetEditing()
        {
            _batchedEditingDepth--;
            if (_batchedEditingDepth < 0)
            {
                _batchedEditingDepth = 0;
            }

            AssetDatabase.StopAssetEditing();
        }

        public static void EnsureSettingsAsset()
        {
            if (_settingsCache != null)
            {
                return;
            }

            _settingsCache = AssetDatabase.LoadAssetAtPath<AtlasPipelineSettings>(
                SettingsAssetPath);
            if (_settingsCache != null)
            {
                RefreshRuleOrder();
                return;
            }

            EnsureAssetFolderExists("Assets/Settings");
            _settingsCache = AtlasPipelineSettings.CreateDefault();
            AssetDatabase.CreateAsset(_settingsCache, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            RefreshRuleOrder();
        }

        public static void InvalidateCache()
        {
            _settingsCache = null;
            _initialized = false;
            RuleCache.Clear();
            ClearIndex();
        }

        /// <summary>
        /// Entry point called after the user edits settings. Rebuilds the index and marks every
        /// atlas dirty: rule-level changes (rotation / pixel-art / format) affect packing config and
        /// only take effect by regenerating. Runs progressively under the background time budget,
        /// so it stays responsive even with tens of thousands of assets.
        /// </summary>
        public static void HandleSettingsChanged()
        {
            InvalidateCache();
            RebuildIndex(markDirty: true);
            ScheduleProcessing();
        }

        public static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            try
            {
                EnsureSettingsAsset();
                BuildIndexFromAssetDatabase(markDirty: false);
                _initialized = true;
            }
            catch
            {
                _initialized = false;
                throw;
            }
        }

        public static void RebuildIndex(bool markDirty)
        {
            EnsureSettingsAsset();
            BuildIndexFromAssetDatabase(markDirty);
        }

        public static void RefreshForProjectChanged()
        {
            _settingsCache = null;
            _initialized = false;
            RuleCache.Clear();
            EnsureSettingsAsset();
            BuildIndexFromAssetDatabase(markDirty: false, clearDirtyKeys: false);
            _initialized = true;
        }

        public static void HandleAssetChanges(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            EnsureInitialized();
            AtlasPipelineSettings settings = _settingsCache;
            DetectNewInvalidAtlasNames(importedAssets, movedAssets);
            DetectOutputFolderIntrusions(importedAssets, movedAssets, settings);

            if (settings == null || !settings.AutoGenerateAtlases)
            {
                return;
            }

            bool changed = false;

            if (deletedAssets != null)
            {
                for (int i = 0; i < deletedAssets.Length; i++)
                {
                    changed |= RemoveIndexedAsset(deletedAssets[i]);
                }
            }

            if (movedFromAssetPaths != null)
            {
                for (int i = 0; i < movedFromAssetPaths.Length; i++)
                {
                    changed |= RemoveIndexedAsset(movedFromAssetPaths[i]);
                }
            }

            if (importedAssets != null)
            {
                for (int i = 0; i < importedAssets.Length; i++)
                {
                    changed |= IndexAsset(importedAssets[i]);
                }
            }

            if (movedAssets != null)
            {
                for (int i = 0; i < movedAssets.Length; i++)
                {
                    changed |= IndexAsset(movedAssets[i]);
                }
            }

            if (changed)
            {
                ScheduleProcessing();
            }

        }

        public static bool ApplyImportSettingsToAll()
        {
            EnsureSettingsAsset();
            RefreshRuleOrder();
            if (_settingsCache == null)
            {
                return false;
            }

            var changedPaths = new List<string>();
            var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < RuleCache.Count; i++)
            {
                string folder = RuleCache[i].NormalizedSourceFolder;
                if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                {
                    continue;
                }

                string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
                for (int g = 0; g < guids.Length; g++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[g]);
                    if (!IsSupportedImagePath(path))
                    {
                        continue;
                    }

                    candidatePaths.Add(path);
                }
            }

            foreach (string path in candidatePaths)
            {
                if (ResolveRule(path) == null)
                {
                    continue;
                }

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                if (ApplyImportSettings(importer, path))
                {
                    changedPaths.Add(path);
                }
            }

            if (changedPaths.Count == 0)
            {
                return false;
            }

            try
            {
                if (changedPaths.Count > 12)
                {
                    EditorUtility.DisplayProgressBar(
                        "CycloneGames Atlas Pipeline",
                        "Applying sprite import settings...",
                        0f);
                }

                // Batch editing: pause imports until all SaveAndReimport calls are queued, which
                // both skips an intermediate refresh and suppresses our own projectChanged storm.
                BeginBatchedAssetEditing();
                try
                {
                    for (int i = 0; i < changedPaths.Count; i++)
                    {
                        string path = changedPaths[i];
                        TextureImporter importer =
                            AssetImporter.GetAtPath(path) as TextureImporter;
                        if (importer == null)
                        {
                            continue;
                        }

                        // Apply and Save on the same instance. The old code mutated an in-memory
                        // instance without ever saving, then fetched a fresh instance and called
                        // SaveAndReimport — so settings only took effect indirectly through
                        // OnPreprocessTexture, which is gated behind settings.AutoImport. With
                        // AutoImport off this ran a full reimport while applying nothing.
                        ApplyImportSettings(importer, path);
                        importer.SaveAndReimport();

                        if (changedPaths.Count > 12)
                        {
                            EditorUtility.DisplayProgressBar(
                                "CycloneGames Atlas Pipeline",
                                $"Reimporting {Path.GetFileName(path)}",
                                (float)(i + 1) / changedPaths.Count);
                        }
                    }
                }
                finally
                {
                    EndBatchedAssetEditing();
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return true;
        }

        public static IReadOnlyList<string> ValidateForBuild(bool includeNameScan = false)
        {
            TryGetSettings();

            var errors = new List<string>();

            if (_settingsCache == null)
            {
                errors.Add(
                    $"CycloneGames atlas settings were not found at '{SettingsAssetPath}'.");
                return errors;
            }

            RefreshRuleOrder();

            if (!IsUsingSupportedSpriteAtlasMode())
            {
                errors.Add(
                    "Project Settings Sprite Packer Mode must use 'Sprite Atlas V2 - Enabled' "
                    + "or 'Sprite Atlas V2 - Enabled For Builds'. The CycloneGames atlas pipeline generates "
                    + ".spriteatlasv2 (V2) assets and is not compatible with Disabled or Sprite Atlas V1 mode.");
            }

            if (!_settingsCache.IsOutputFolderValid)
            {
                errors.Add(
                    $"Output atlas folder '{_settingsCache.OutputAtlasFolder}' is invalid.");
            }

            if (RuleCache.Count == 0)
            {
                errors.Add("At least one CycloneGames atlas import rule is required.");
            }

            for (int i = 0; i < RuleCache.Count; i++)
            {
                AtlasImportRule rule = RuleCache[i];
                if (!rule.IsValid)
                {
                    errors.Add($"Import rule '{rule.Name}' has an invalid source folder.");
                }
                else if (!AssetDatabase.IsValidFolder(rule.NormalizedSourceFolder))
                {
                    errors.Add(
                        $"Import rule '{rule.Name}' source folder does not exist: '{rule.NormalizedSourceFolder}'.");
                }

                AtlasPlatformFormats.ValidateRule(rule, errors);
            }

            // Atlas-key collision detection. Under PerSourceFolder the atlas key equals AtlasGroup,
            // so two rules with the same group write into the same .spriteatlasv2, and the winning
            // format is decided by whichever rule ResolveAtlasRule picks. Requiring a globally
            // unique AtlasGroup removes this silent merge at the root.
            var atlasGroupOwners =
                new Dictionary<string, AtlasImportRule>(StringComparer.Ordinal);
            for (int i = 0; i < RuleCache.Count; i++)
            {
                AtlasImportRule rule = RuleCache[i];
                string groupKey = SanitizeAtlasPart(rule.AtlasGroup);
                if (atlasGroupOwners.TryGetValue(groupKey, out AtlasImportRule owner))
                {
                    errors.Add(
                        $"Import rules '{owner.Name}' and '{rule.Name}' both resolve to atlas group "
                        + $"'{groupKey}'. Atlas Group must be unique across rules: with the "
                        + "PerSourceFolder granularity the atlas key is derived from the group "
                        + "alone, so colliding rules silently merge into one atlas and the winning "
                        + "format is picked non-deterministically.");
                    continue;
                }

                atlasGroupOwners.Add(groupKey, rule);
            }

            // Output-folder vs. source-folder overlap check. When they overlap (equal or one is an
            // ancestor of the other), every source image is judged an "output-folder intrusion" and
            // one confirmation moves the whole art folder into quarantine. Block this before build.
            string overlapOutputFolder = _settingsCache.NormalizedOutputAtlasFolder;
            for (int i = 0; i < RuleCache.Count; i++)
            {
                string sourceFolder = RuleCache[i].NormalizedSourceFolder;
                if (string.IsNullOrEmpty(sourceFolder))
                {
                    continue;
                }

                if (PathsOverlap(overlapOutputFolder, sourceFolder))
                {
                    errors.Add(
                        $"Output atlas folder '{overlapOutputFolder}' overlaps with import rule "
                        + $"'{RuleCache[i].Name}' source folder '{sourceFolder}'. Every source "
                        + "image inside the output folder is treated as an intrusion and would "
                        + "be moved to quarantine. Choose a disjoint output folder.");
                }
            }

            if (includeNameScan)
            {
                List<AtlasRenameRequest> invalidAtlasNames = CollectInvalidAtlasNames();
                if (invalidAtlasNames.Count > 0)
                {
                    errors.Add(
                        $"{invalidAtlasNames.Count} atlas source asset(s) have invalid file names. "
                        + "Use Tools/CycloneGames/Atlas Pipeline/Review Atlas Names to approve renames.");
                }
            }

            return errors;
        }

        public static List<AtlasRenameRequest> CollectInvalidAtlasNames()
        {
            return AtlasNaming.CollectInvalidAtlasNames(TryGetSettings());
        }

        public static void CheckTextureSize(string assetPath)
        {
            AtlasImportRule rule = ResolveRule(assetPath);
            if (rule == null || !rule.WarnTextureSize)
            {
                return;
            }

            string absolutePath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                assetPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar)));
            if (!AtlasImageInfo.TryReadSize(absolutePath, out int width, out int height))
            {
                return;
            }

            if (Mathf.Max(width, height) <= rule.RecommendedMaxTextureSize)
            {
                return;
            }

            string warning = $"{assetPath}: {width}x{height} > recommended "
                             + $"{rule.RecommendedMaxTextureSize}px for rule '{rule.Name}'.";
            if (!TextureSizeWarnings.Add(warning))
            {
                return;
            }

            ScheduleTextureSizePrompt();
        }

        public static void RunForBuild(bool throwOnError)
        {
            EnsureSettingsAsset();
            IReadOnlyList<string> errors = ValidateForBuild(includeNameScan: true);
            if (errors.Count > 0)
            {
                string message = string.Join(Environment.NewLine, errors);
                if (throwOnError)
                {
                    throw new UnityEditor.Build.BuildFailedException(
                        $"CycloneGames atlas pipeline validation failed:{Environment.NewLine}{message}");
                }

                AtlasPipelineLog.Channel.Error(message);
                return;
            }

            if (_settingsCache.AutoImport)
            {
                ApplyImportSettingsToAll();
            }
            else
            {
                // Respect the user's toggle — do not dirty the workspace by rewriting .meta files
                // in CI — but log a warning, since "no import settings were applied" would be silent.
                AtlasPipelineLog.Channel.Warning(
                    "[CycloneGames Atlas Pipeline] AutoImport is disabled; import settings were not "
                    + "applied for this build. Atlases will use whatever settings are already "
                    + "written in the source .meta files.");
            }

            // The old InvalidateCache + EnsureInitialized + RebuildIndex ran two full scans
            // (EnsureInitialized already calls BuildIndexFromAssetDatabase). RebuildIndex clears the
            // index itself, so call it directly.
            RebuildIndex(markDirty: true);

            var failures = new List<string>();
            ProcessDirtyAtlases(failures: failures);

            // Post-generation check (generalized runtime contract): every non-empty atlas the index
            // expects must actually exist. Whether the runtime loads by hardcoded path (e.g.
            // SpriteProvider) or by key, a renamed config or silent generation failure surfaces here
            // instead of as missing sprites at runtime.
            VerifyExpectedAtlases(failures);

            // Sweep orphan atlases: stale .spriteatlasv2 files left in the output folder after a
            // rule rename/deletion would otherwise ship in the player forever.
            SweepOrphanAtlases();

            if (failures.Count > 0)
            {
                string message = string.Join(Environment.NewLine, failures);
                if (throwOnError)
                {
                    throw new UnityEditor.Build.BuildFailedException(
                        "CycloneGames atlas pipeline failed to generate atlases:"
                        + Environment.NewLine
                        + message);
                }

                AtlasPipelineLog.Channel.Error(message);
            }
        }

        public static void ProcessAllDirtyAtlases()
        {
            RebuildIndex(markDirty: true);

            // The manual entry point collects failures too, so the window cannot report "all
            // rebuilt" while some atlases actually failed.
            var failures = new List<string>();
            ProcessDirtyAtlases(failures: failures);

            // Same as the build path: post-generation check + orphan sweep.
            VerifyExpectedAtlases(failures);
            SweepOrphanAtlases();

            if (failures.Count > 0)
            {
                AtlasPipelineLog.Channel.Error(
                    "[CycloneGames Atlas Pipeline] Atlas regeneration finished with "
                    + $"{failures.Count} failure(s):{Environment.NewLine}"
                    + string.Join(Environment.NewLine, failures));
            }
        }

        /// <summary>
        /// Verifies every non-empty atlas the index expects actually exists in the output folder.
        /// This is a build-time, generalized guard for the "atlas missing at runtime" class of bugs.
        /// </summary>
        private static void VerifyExpectedAtlases(ICollection<string> failures)
        {
            AtlasPipelineSettings settings = _settingsCache;
            if (settings == null || !settings.IsOutputFolderValid)
            {
                return;
            }

            string folder = settings.NormalizedOutputAtlasFolder;
            foreach (KeyValuePair<string, HashSet<string>> entry in AtlasToAssets)
            {
                if (entry.Value.Count == 0)
                {
                    // An empty set is the normal path for an atlas that was just cleared and deleted.
                    continue;
                }

                string expectedPath = BuildAtlasAssetPath(folder, entry.Key);
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(expectedPath) == null)
                {
                    failures?.Add(
                        $"Expected atlas '{expectedPath}' (key '{entry.Key}', "
                        + $"{entry.Value.Count} sprite(s)) was not generated. "
                        + "Anything loading atlases by path at runtime will fail.");
                }
            }
        }

        /// <summary>
        /// Deletes orphan .spriteatlasv2 files in the output folder that the index no longer
        /// references (after a rule/group rename or deletion), which would otherwise ship in the
        /// player. Only the top level of the output folder is scanned (the generator only writes
        /// there) and only the .spriteatlasv2 extension is considered.
        /// </summary>
        public static void SweepOrphanAtlases()
        {
            AtlasPipelineSettings settings = _settingsCache;
            if (settings == null
                || !settings.IsOutputFolderValid
                || RuleCache.Count == 0)
            {
                // With no valid rules the index is necessarily empty, so "everything is an orphan"
                // would be a false conclusion — skip to avoid deleting everything.
                return;
            }

            string folder = settings.NormalizedOutputAtlasFolder;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, HashSet<string>> entry in AtlasToAssets)
            {
                expected.Add(BuildAtlasAssetPath(folder, entry.Key));
            }

            // Enumerate the filesystem directly instead of using FindAssets: it avoids search-type
            // differences between atlas V1/V2 and saves an asset-database index query.
            string fullPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", folder));
            if (!Directory.Exists(fullPath))
            {
                return;
            }

            string[] files = Directory.GetFiles(fullPath);
            int removed = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                if (!file.EndsWith(".spriteatlasv2", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string assetPath = folder + "/"
                                   + Path.GetFileName(file).Replace('\\', '/');
                if (expected.Contains(assetPath))
                {
                    continue;
                }

                AssetDatabase.DeleteAsset(assetPath);
                removed++;
                AtlasPipelineLog.Channel.Info($"[CycloneGames Atlas Pipeline] Removed orphan atlas '{assetPath}'.");
            }

            if (removed > 0)
            {
                AtlasPipelineLog.Channel.Info(
                    $"[CycloneGames Atlas Pipeline] Orphan atlas sweep removed {removed} file(s).");
            }
        }

        public static bool ApplyImportSettings(TextureImporter importer, string assetPath)
        {
            AtlasImportRule rule = ResolveRule(assetPath);
            if (importer == null || rule == null)
            {
                return false;
            }

            bool changed = false;

            changed |= SetIfChanged(
                importer.textureType,
                TextureImporterType.Sprite,
                value => importer.textureType = value);
            changed |= SetIfChanged(
                importer.maxTextureSize,
                rule.RecommendedMaxTextureSize,
                value => importer.maxTextureSize = value);

            SpriteImportMode spriteMode = rule.SpriteMode == AtlasSpriteMode.Multiple
                ? SpriteImportMode.Multiple
                : SpriteImportMode.Single;
            changed |= SetIfChanged(
                importer.spriteImportMode,
                spriteMode,
                value => importer.spriteImportMode = value);

            if (spriteMode == SpriteImportMode.Single)
            {
                changed |= SetIfChanged(
                    importer.spritePixelsPerUnit,
                    rule.PixelsPerUnit,
                    value => importer.spritePixelsPerUnit = value);
            }

            changed |= SetIfChanged(
                importer.mipmapEnabled,
                rule.Mipmaps,
                value => importer.mipmapEnabled = value);
            changed |= SetIfChanged(
                importer.isReadable,
                rule.Readable,
                value => importer.isReadable = value);
            changed |= SetIfChanged(
                importer.filterMode,
                rule.FilterMode,
                value => importer.filterMode = value);
            changed |= SetIfChanged(
                importer.wrapMode,
                rule.WrapMode,
                value => importer.wrapMode = value);
            changed |= SetIfChanged(
                importer.alphaIsTransparency,
                true,
                value => importer.alphaIsTransparency = value);

            changed |= ApplyPlatformSettings(
                importer,
                AtlasPlatformFormats.AndroidPlatformName,
                GetEffectiveFormat(rule, AtlasPlatform.Android),
                rule.CompressionQuality);
            changed |= ApplyPlatformSettings(
                importer,
                AtlasPlatformFormats.IphonePlatformName,
                GetEffectiveFormat(rule, AtlasPlatform.Iphone),
                rule.CompressionQuality);
            changed |= ApplyPlatformSettings(
                importer,
                AtlasPlatformFormats.WebglPlatformName,
                GetEffectiveFormat(rule, AtlasPlatform.Webgl),
                rule.CompressionQuality);
            changed |= ApplyPlatformSettings(
                importer,
                AtlasPlatformFormats.StandalonePlatformName,
                GetEffectiveFormat(rule, AtlasPlatform.Standalone),
                rule.CompressionQuality);

            if (rule.PixelArt)
            {
                changed |= SetIfChanged(
                    importer.textureCompression,
                    TextureImporterCompression.Uncompressed,
                    value => importer.textureCompression = value);
            }
            else
            {
                // Must reset: otherwise the global (default tab) stays Uncompressed after pixel art
                // is turned off. The four primary platforms are covered by ApplyPlatformSettings
                // overrides, but uncovered platforms (PS4/Xbox/Switch/Linux) fall through to the
                // default tab and bloat the player.
                changed |= SetIfChanged(
                    importer.textureCompression,
                    TextureImporterCompression.Compressed,
                    value => importer.textureCompression = value);
            }

            return changed;
        }

        private static bool ApplyPlatformSettings(
            TextureImporter importer,
            string platform,
            AtlasTextureFormat format,
            int quality)
        {
            if (!AtlasPlatformFormats.TryGetPlatformByName(
                    platform,
                    out AtlasPlatform platformTarget))
            {
                return false;
            }

            format = AtlasPlatformFormats.GetSafeFormat(platformTarget, format);
            quality = Mathf.Clamp(quality, 0, 100);
            TextureImporterPlatformSettings current = importer.GetPlatformTextureSettings(platform);
            if (current == null)
            {
                return false;
            }

            TextureImporterFormat targetFormat =
                AtlasPlatformFormats.ToTextureImporterFormat(format);
            TextureImporterCompression targetCompression =
                AtlasPlatformFormats.ToTextureImporterCompression(format);

            bool nameChanged = !string.Equals(
                current.name,
                platform,
                StringComparison.OrdinalIgnoreCase);
            bool changed = current.overridden == false
                           || nameChanged
                           || current.format != targetFormat
                           || current.textureCompression != targetCompression
                           || current.compressionQuality != quality;
            if (!changed)
            {
                return false;
            }

            current.overridden = true;
            current.name = platform;
            current.format = targetFormat;
            current.textureCompression = targetCompression;
            current.compressionQuality = quality;
            importer.SetPlatformTextureSettings(current);
            return true;
        }

        private static bool SetIfChanged<T>(
            T current,
            T target,
            Action<T> setter)
        {
            if (EqualityComparer<T>.Default.Equals(current, target))
            {
                return false;
            }

            setter(target);
            return true;
        }

        private static void ScheduleProcessing()
        {
            _nextProcessTime = EditorApplication.timeSinceStartup + 0.35d;
        }

        private static void DetectNewInvalidAtlasNames(
            string[] importedAssets,
            string[] movedAssets)
        {
            if (importedAssets == null && movedAssets == null)
            {
                return;
            }

            bool detected = false;
            if (importedAssets != null)
            {
                for (int i = 0; i < importedAssets.Length; i++)
                {
                    detected |= IsInvalidAtlasAssetName(importedAssets[i]);
                }
            }

            if (!detected && movedAssets != null)
            {
                for (int i = 0; i < movedAssets.Length; i++)
                {
                    detected |= IsInvalidAtlasAssetName(movedAssets[i]);
                }
            }

            if (detected)
            {
                ScheduleInvalidNamePrompt();
            }
        }

        private static bool IsInvalidAtlasAssetName(string assetPath)
        {
            return IsSupportedImagePath(assetPath)
                   && ResolveRule(assetPath) != null
                   && !AtlasNaming.IsValidAtlasAssetPath(assetPath);
        }

        private static void ScheduleInvalidNamePrompt()
        {
            if (_invalidNamePromptScheduled)
            {
                return;
            }

            // Never use delayCall + native dialogs in batch mode: -quit may exit before the
            // callback is queued, so the outcome is either "callback never ran, warning lost" or
            // "callback ran, build process hung" — harder to diagnose than a stable failure.
            if (Application.isBatchMode)
            {
                AtlasPipelineLog.Channel.Warning(
                    "[CycloneGames Atlas Pipeline] Running in batch mode: the atlas source rename "
                    + "dialog is unavailable. Invalid file names are reported by the build "
                    + "validation step instead.");
                return;
            }

            _invalidNamePromptScheduled = true;
            EditorApplication.delayCall += ShowInvalidNamePrompt;
        }

        private static void ShowInvalidNamePrompt()
        {
            _invalidNamePromptScheduled = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ScheduleInvalidNamePrompt();
                return;
            }

            List<AtlasRenameRequest> requests = CollectInvalidAtlasNames();
            if (requests.Count == 0)
            {
                return;
            }

            string preview = AtlasNaming.BuildPreview(requests);
            int choice = EditorUtility.DisplayDialogComplex(
                "CycloneGames Atlas Pipeline",
                $"Detected {requests.Count} atlas source name(s) that should be renamed.\n\n"
                + preview
                + "\n\nReview the proposed names before applying.",
                "Review...",
                "Ignore",
                "Close");

            if (choice == 0)
            {
                AtlasRenameWindow.ShowWindow(requests);
            }
            else
            {
                AtlasPipelineLog.Channel.Info(
                    $"[CycloneGames Atlas Pipeline] Detected {requests.Count} invalid atlas source name(s). "
                    + "Use Tools/CycloneGames/Atlas Pipeline/Review Atlas Names to review them.");
            }
        }

        public static void ScheduleSpritePackerModePrompt()
        {
            if (IsSpriteAtlasAlwaysEnabled() || _spritePackerPromptScheduled)
            {
                return;
            }

            _spritePackerPromptScheduled = true;
            EditorApplication.delayCall += ShowSpritePackerModePrompt;
        }

        private static void ShowSpritePackerModePrompt()
        {
            _spritePackerPromptScheduled = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ScheduleSpritePackerModePrompt();
                return;
            }

            if (IsSpriteAtlasAlwaysEnabled())
            {
                return;
            }

            string current = EditorSettings.spritePackerMode.ToString();
            bool apply = EditorUtility.DisplayDialog(
                "CycloneGames Atlas Pipeline - Sprite Packer Mode",
                "The project is not using 'Sprite Atlas V2 - Enabled'.\n\n"
                + $"Current: {current}\n"
                + "Recommended: SpriteAtlasV2 (Sprite Atlas V2 - Enabled)\n\n"
                + "CycloneGames atlas assets are .spriteatlasv2 (V2) assets. Enable Sprite Atlas V2 now?",
                "Enable Sprite Atlas V2",
                "Keep Current");

            if (apply)
            {
                EditorSettings.spritePackerMode = SpritePackerMode.SpriteAtlasV2;
                AtlasPipelineLog.Channel.Info(
                    "[CycloneGames Atlas Pipeline] Sprite Packer Mode set to "
                    + "'Sprite Atlas V2 - Enabled'.");
            }
            else
            {
                AtlasPipelineLog.Channel.Warning(
                    $"[CycloneGames Atlas Pipeline] Sprite Packer Mode is '{current}'. "
                    + ".spriteatlasv2 (V2) assets may not batch in Play Mode or builds until it "
                    + "is set to 'Sprite Atlas V2 - Enabled'.");
            }
        }

        private static void ScheduleTextureSizePrompt()
        {
            if (_textureSizePromptScheduled)
            {
                return;
            }

            if (Application.isBatchMode)
            {
                LogTextureSizeWarningsForBatchMode();
                return;
            }

            _textureSizePromptScheduled = true;
            EditorApplication.delayCall += ShowTextureSizePrompt;
        }

        private static void LogTextureSizeWarningsForBatchMode()
        {
            if (TextureSizeWarnings.Count == 0)
            {
                return;
            }

            var builder = new StringBuilder();
            builder.Append("[CycloneGames Atlas Pipeline] ").Append(TextureSizeWarnings.Count);
            builder.Append(" oversized source texture(s) detected:");
            foreach (string warning in TextureSizeWarnings)
            {
                builder.AppendLine();
                builder.Append("  ").Append(warning);
            }

            TextureSizeWarnings.Clear();
            AtlasPipelineLog.Channel.Warning(builder.ToString());
        }

        private static void ShowTextureSizePrompt()
        {
            _textureSizePromptScheduled = false;
            if (TextureSizeWarnings.Count == 0)
            {
                return;
            }

            var warnings = new string[TextureSizeWarnings.Count];
            TextureSizeWarnings.CopyTo(warnings);
            TextureSizeWarnings.Clear();

            Array.Sort(warnings, StringComparer.Ordinal);
            string preview = string.Join(Environment.NewLine, warnings);
            bool review = EditorUtility.DisplayDialog(
                "CycloneGames Atlas Pipeline - Texture Size",
                "The following source textures exceed the recommended size for their rule:\n\n"
                + preview,
                "OK");

            if (!review)
            {
                AtlasPipelineLog.Channel.Info(
                    $"[CycloneGames Atlas Pipeline] {warnings.Length} texture(s) exceed their "
                    + "recommended source size.");
            }
        }

        private static void DetectOutputFolderIntrusions(
            string[] importedAssets,
            string[] movedAssets,
            AtlasPipelineSettings settings)
        {
            if (settings == null
                || string.IsNullOrEmpty(settings.NormalizedOutputAtlasFolder))
            {
                return;
            }

            string outputFolder = settings.NormalizedOutputAtlasFolder;
            DetectOutputFolderIntrusions(importedAssets, outputFolder);
            DetectOutputFolderIntrusions(movedAssets, outputFolder);

            if (OutputFolderIntrusions.Count > 0)
            {
                ScheduleOutputFolderIntrusionPrompt();
            }
        }

        private static void DetectOutputFolderIntrusions(
            string[] assets,
            string outputFolder)
        {
            if (assets == null)
            {
                return;
            }

            for (int i = 0; i < assets.Length; i++)
            {
                string path = NormalizeAssetPath(assets[i]);
                if (!IsSupportedImagePath(path))
                {
                    continue;
                }

                if (string.Equals(path, outputFolder, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(
                        outputFolder + "/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    OutputFolderIntrusions.Add(path);
                }
            }
        }

        private static void ScheduleOutputFolderIntrusionPrompt()
        {
            if (_outputFolderIntrusionPromptScheduled)
            {
                return;
            }

            // Never show dialogs in batch mode: button 0 is the only destructive option that moves
            // files, and a native dialog may return its default unattended — silently relocating the
            // artist's source images.
            if (Application.isBatchMode)
            {
                LogOutputFolderIntrusionsForBatchMode();
                return;
            }

            _outputFolderIntrusionPromptScheduled = true;
            EditorApplication.delayCall += ShowOutputFolderIntrusionPrompt;
        }

        private static void LogOutputFolderIntrusionsForBatchMode()
        {
            if (OutputFolderIntrusions.Count == 0)
            {
                return;
            }

            var builder = new StringBuilder();
            builder.Append("[CycloneGames Atlas Pipeline] Running in batch mode: ").Append(
                OutputFolderIntrusions.Count);
            builder.Append(
                " image(s) sit inside the atlas output folder and were NOT moved. "
                + "They will be treated as atlas output and may be overwritten:");
            foreach (string intrusion in OutputFolderIntrusions)
            {
                builder.AppendLine();
                builder.Append("  ").Append(intrusion);
            }

            OutputFolderIntrusions.Clear();
            AtlasPipelineLog.Channel.Warning(builder.ToString());
        }

        private static void ShowOutputFolderIntrusionPrompt()
        {
            _outputFolderIntrusionPromptScheduled = false;
            if (OutputFolderIntrusions.Count == 0)
            {
                return;
            }

            var intrusions = new string[OutputFolderIntrusions.Count];
            OutputFolderIntrusions.CopyTo(intrusions);
            OutputFolderIntrusions.Clear();
            Array.Sort(intrusions, StringComparer.Ordinal);

            string preview = string.Join(Environment.NewLine, intrusions);
            int choice = EditorUtility.DisplayDialogComplex(
                "CycloneGames Atlas Pipeline - Invalid Output Folder",
                "Source images should not be placed directly inside the generated Atlas folder.\n\n"
                + preview
                + "\n\nMove these files to Assets/_AtlasRejected?",
                // Button 0 must be non-destructive. A native dialog may fall back to the default
                // (0) when unattended or on an unexpected return; if 0 were "move files", the
                // artist's source images would be silently relocated. Putting "Keep" at 0 makes the
                // default harmless.
                "Keep",
                "Move to Quarantine",
                "Close");

            if (choice == 1)
            {
                MoveIntrusionsToQuarantine(intrusions);
            }
            else
            {
                AtlasPipelineLog.Channel.Info(
                    $"[CycloneGames Atlas Pipeline] {intrusions.Length} source image(s) were left "
                    + "untouched inside the generated Atlas folder.");
            }
        }

        private static void MoveIntrusionsToQuarantine(string[] intrusions)
        {
            EnsureAssetFolderExists("Assets/_AtlasRejected");
            int movedCount = 0;
            for (int i = 0; i < intrusions.Length; i++)
            {
                string fileName = Path.GetFileName(intrusions[i]);
                string target = "Assets/_AtlasRejected/" + fileName;
                if (string.Equals(
                        intrusions[i],
                        target,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(target) != null)
                {
                    target = AssetDatabase.GenerateUniqueAssetPath(target);
                }

                string error = AssetDatabase.MoveAsset(intrusions[i], target);
                if (string.IsNullOrEmpty(error))
                {
                    movedCount++;
                }
                else
                {
                    AtlasPipelineLog.Channel.Error(
                        $"[CycloneGames Atlas Pipeline] Failed to move '{intrusions[i]}': {error}");
                }
            }

            if (movedCount > 0)
            {
                AtlasPipelineLog.Channel.Info(
                    $"[CycloneGames Atlas Pipeline] Moved {movedCount} source image(s) to "
                    + "Assets/_AtlasRejected.");
            }
        }

        private static void BuildIndexFromAssetDatabase(
            bool markDirty,
            bool clearDirtyKeys = true)
        {
            if (clearDirtyKeys)
            {
                ClearIndex();
            }
            else
            {
                AtlasToAssets.Clear();
                AssetToAtlas.Clear();
            }

            RefreshRuleOrder();

            var visitedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < RuleCache.Count; i++)
            {
                string folder = RuleCache[i].NormalizedSourceFolder;
                if (string.IsNullOrEmpty(folder)
                    || !AssetDatabase.IsValidFolder(folder)
                    || !visitedFolders.Add(folder))
                {
                    continue;
                }

                string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { folder });
                for (int g = 0; g < guids.Length; g++)
                {
                    string path = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guids[g]));
                    if (!IsSupportedImagePath(path))
                    {
                        continue;
                    }

                    AtlasImportRule rule = ResolveRule(path);
                    if (rule == null || rule.AtlasGranularity == AtlasGranularity.None)
                    {
                        continue;
                    }

                    AddAsset(rule, path, markDirty);
                }
            }
        }

        private static void ClearIndex()
        {
            AtlasToAssets.Clear();
            AssetToAtlas.Clear();
            DirtyAtlasKeys.Clear();
        }

        private static bool IndexAsset(string assetPath)
        {
            string path = NormalizeAssetPath(assetPath);
            if (!IsSupportedImagePath(path))
            {
                return false;
            }

            AtlasImportRule rule = ResolveRule(path);
            if (rule == null || rule.AtlasGranularity == AtlasGranularity.None)
            {
                return false;
            }

            AddAsset(rule, path, markDirty: true);
            return true;
        }

        private static bool RemoveIndexedAsset(string assetPath)
        {
            string path = NormalizeAssetPath(assetPath);
            if (!IsSupportedImagePath(path))
            {
                return false;
            }

            return RemoveAsset(path);
        }

        private static void AddAsset(
            AtlasImportRule rule,
            string assetPath,
            bool markDirty)
        {
            string atlasKey = ResolveAtlasKey(rule, assetPath);
            if (string.IsNullOrEmpty(atlasKey))
            {
                return;
            }

            if (AssetToAtlas.TryGetValue(assetPath, out string previousKey))
            {
                if (!string.Equals(previousKey, atlasKey, StringComparison.OrdinalIgnoreCase)
                    && AtlasToAssets.TryGetValue(previousKey, out HashSet<string> previousSet))
                {
                    previousSet.Remove(assetPath);
                    if (markDirty)
                    {
                        DirtyAtlasKeys.Add(previousKey);
                    }
                }
            }

            AssetToAtlas[assetPath] = atlasKey;
            if (!AtlasToAssets.TryGetValue(atlasKey, out HashSet<string> atlasSet))
            {
                atlasSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                AtlasToAssets.Add(atlasKey, atlasSet);
            }

            if (atlasSet.Add(assetPath) && markDirty)
            {
                DirtyAtlasKeys.Add(atlasKey);
            }
        }

        private static bool RemoveAsset(string assetPath)
        {
            if (!AssetToAtlas.TryGetValue(assetPath, out string atlasKey))
            {
                return false;
            }

            AssetToAtlas.Remove(assetPath);
            if (AtlasToAssets.TryGetValue(atlasKey, out HashSet<string> atlasSet))
            {
                atlasSet.Remove(assetPath);
            }

            DirtyAtlasKeys.Add(atlasKey);
            return true;
        }

        private static string ResolveAtlasKey(AtlasImportRule rule, string assetPath)
        {
            if (rule.AtlasGranularity == AtlasGranularity.None)
            {
                return null;
            }

            string group = SanitizeAtlasPart(rule.AtlasGroup);
            string folder = rule.NormalizedSourceFolder;
            string relative = assetPath.Substring(folder.Length).TrimStart('/');

            if (rule.AtlasGranularity == AtlasGranularity.PerSprite)
            {
                // Take only the final segment (file name) without splitting into an array; this is
                // equivalent to the original segments[last].
                string spriteName = SanitizeAtlasPart(
                    Path.GetFileNameWithoutExtension(relative));
                return $"{group}_{spriteName}";
            }

            if (rule.AtlasGranularity == AtlasGranularity.PerChildFolder)
            {
                int firstSlash = relative.IndexOf('/');
                string child = firstSlash >= 0
                    ? SanitizeAtlasPart(relative.Substring(0, firstSlash))
                    : "Root";
                return $"{group}_{child}";
            }

            return group;
        }

        /// <summary>
        /// Deterministic output path for an atlas asset. Shared by generation, existence checks,
        /// and orphan sweeping so the path-assembly logic cannot drift.
        /// </summary>
        private static string BuildAtlasAssetPath(string outputFolder, string atlasKey)
        {
            return outputFolder + "/" + SanitizeAtlasPart(atlasKey) + ".spriteatlasv2";
        }

        /// <summary>
        /// Whether two Assets/-relative directories are equal or one is an ancestor of the other.
        /// Used by the output-folder vs. source-folder overlap check: when they overlap, every
        /// source image is treated as an intrusion and moved to quarantine, i.e. the whole art
        /// folder is emptied.
        /// </summary>
        private static bool PathsOverlap(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }

            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (b.Length > a.Length
                && b.StartsWith(a, StringComparison.OrdinalIgnoreCase)
                && b[a.Length] == '/')
            {
                return true;
            }

            return a.Length > b.Length
                   && a.StartsWith(b, StringComparison.OrdinalIgnoreCase)
                   && a[b.Length] == '/';
        }

        private static string SanitizeAtlasPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Atlas";
            }

            // Fast path: most groups/sprite names are already clean ("UI", "Scene", "icon_01"),
            // so return them as-is and skip the StringBuilder allocation. The slow path runs only
            // when the value contains illegal characters or leading/trailing underscores.
            bool needsSanitize = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                {
                    needsSanitize = true;
                    break;
                }
            }

            if (!needsSanitize && value[0] != '_' && value[value.Length - 1] != '_')
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);
            bool previousWasSeparator = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    builder.Append(c);
                    previousWasSeparator = false;
                    continue;
                }

                if (!previousWasSeparator)
                {
                    builder.Append('_');
                    previousWasSeparator = true;
                }
            }

            string result = builder.ToString().Trim('_');
            return string.IsNullOrEmpty(result) ? "Atlas" : result;
        }

        private static void ProcessDirtyAtlases(
            int? maxCount = null,
            double? timeBudgetSeconds = null,
            ICollection<string> failures = null)
        {
            if (DirtyAtlasKeys.Count == 0)
            {
                return;
            }

            AtlasPipelineSettings settings = _settingsCache;
            if (settings == null || !settings.AutoGenerateAtlases)
            {
                DirtyAtlasKeys.Clear();
                return;
            }

            EnsureAssetFolderExists(settings.NormalizedOutputAtlasFolder);

            var keys = new string[DirtyAtlasKeys.Count];
            DirtyAtlasKeys.CopyTo(keys);
            DirtyAtlasKeys.Clear();
            GeneratedAtlasPaths.Clear();
            DeletedAtlasPaths.Clear();
            PendingAtlasConfigure.Clear();

            int processCount = keys.Length;
            if (maxCount.HasValue && processCount > maxCount.Value)
            {
                processCount = maxCount.Value;
            }

            double deadline = timeBudgetSeconds.HasValue
                ? EditorApplication.timeSinceStartup + timeBudgetSeconds.Value
                : double.MaxValue;

            bool showProgress = processCount > 3;
            if (showProgress)
            {
                EditorUtility.DisplayProgressBar(
                    "CycloneGames Atlas Pipeline",
                    "Generating sprite atlases...",
                    0f);
            }

            int processed = 0;
            for (int i = 0; i < processCount; i++)
            {
                if (showProgress)
                {
                    EditorUtility.DisplayProgressBar(
                        "CycloneGames Atlas Pipeline",
                        $"Generating {keys[i]}",
                        (float)(i + 1) / processCount);
                }

                try
                {
                    GenerateAtlas(keys[i]);
                }
                catch (Exception exception)
                {
                    DirtyAtlasKeys.Add(keys[i]);
                    string failure =
                        $"Failed to generate atlas '{keys[i]}': {exception.Message}";
                    AtlasPipelineLog.Channel.Error($"[CycloneGames Atlas Pipeline] {failure}");

                    // Must be surfaced to the caller: otherwise an atlas generation failure during
                    // build only logs a line, CI still reports Success, and the output ships stale or
                    // missing atlases.
                    failures?.Add(failure);
                }

                processed = i + 1;

                // Stop once the time budget is spent; remaining atlases are re-queued for the next
                // batch.
                if (timeBudgetSeconds.HasValue
                    && EditorApplication.timeSinceStartup > deadline)
                {
                    break;
                }
            }

            if (processed < keys.Length)
            {
                for (int i = processed; i < keys.Length; i++)
                {
                    DirtyAtlasKeys.Add(keys[i]);
                }
            }

            EditorUtility.ClearProgressBar();

            // Batch editing: pause imports, then flush once after all ImportAsset/DeleteAsset calls.
            // This avoids a full library refresh per atlas, which also feeds the projectChanged storm.
            BeginBatchedAssetEditing();
            try
            {
                for (int i = 0; i < GeneratedAtlasPaths.Count; i++)
                {
                    string atlasPath = GeneratedAtlasPaths[i];
                    AssetDatabase.ImportAsset(
                        atlasPath,
                        ImportAssetOptions.ForceUpdate);
                    SpriteAtlasImporter importer =
                        AssetImporter.GetAtPath(atlasPath) as SpriteAtlasImporter;
                    if (importer != null)
                    {
                        ConfigureAtlasImporter(importer, PendingAtlasConfigure[i]);
                        AssetDatabase.WriteImportSettingsIfDirty(atlasPath);
                    }
                }

                for (int i = 0; i < DeletedAtlasPaths.Count; i++)
                {
                    if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(DeletedAtlasPaths[i]) != null)
                    {
                        AssetDatabase.DeleteAsset(DeletedAtlasPaths[i]);
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                EndBatchedAssetEditing();
            }

            LogAtlasChangesSummary();
        }

        private static void GenerateAtlas(string atlasKey)
        {
            AtlasPipelineSettings settings = _settingsCache;
            if (settings == null || !AtlasToAssets.TryGetValue(atlasKey, out HashSet<string> assetSet))
            {
                return;
            }

            var orderedAssetPaths = new List<string>(assetSet);
            orderedAssetPaths.Sort(StringComparer.Ordinal);

            var sprites = new List<Sprite>(assetSet.Count);
            var orderedAssetSprites = new List<Sprite>();
            foreach (string assetPath in orderedAssetPaths)
            {
                if (!IsSupportedImagePath(assetPath))
                {
                    continue;
                }

                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                if (assets == null)
                {
                    continue;
                }

                orderedAssetSprites.Clear();
                for (int i = 0; i < assets.Length; i++)
                {
                    if (assets[i] is Sprite sprite && sprite != null)
                    {
                        orderedAssetSprites.Add(sprite);
                    }
                }

                orderedAssetSprites.Sort(
                    (left, right) => string.CompareOrdinal(left.name, right.name));
                sprites.AddRange(orderedAssetSprites);
            }

            // Overflow check: sprites larger than the atlas limit cannot be packed — Unity silently
            // drops them and they show up as white quads at runtime, one of the hardest bugs to
            // trace with tens of thousands of assets, so warn explicitly.
            AtlasImportRule overflowRule = ResolveAtlasRule(atlasKey);
            int atlasMaxSize = overflowRule != null
                ? overflowRule.AtlasMaxTextureSize
                : 2048;
            for (int i = 0; i < sprites.Count; i++)
            {
                Sprite oversized = sprites[i];
                if (oversized == null)
                {
                    continue;
                }

                if (oversized.rect.width > atlasMaxSize
                    || oversized.rect.height > atlasMaxSize)
                {
                    AtlasPipelineLog.Channel.Warning(
                        $"[CycloneGames Atlas Pipeline] Sprite '{oversized.name}' "
                        + $"({(int)oversized.rect.width}x{(int)oversized.rect.height}) exceeds "
                        + $"the atlas max texture size {atlasMaxSize} of atlas '{atlasKey}'. "
                        + "Unity will silently drop it from the packed atlas; shrink the "
                        + "source image or raise 'Atlas Max' on the owning rule.");
                }
            }

            string outputPath = BuildAtlasAssetPath(
                settings.NormalizedOutputAtlasFolder,
                atlasKey);
            if (sprites.Count == 0)
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(outputPath) != null)
                {
                    DeletedAtlasPaths.Add(outputPath);
                }

                return;
            }

            SpriteAtlasAsset v2Asset = SpriteAtlasAsset.Load(outputPath);
            SpriteAtlas masterAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(outputPath);
            bool existed = v2Asset != null;
            if (existed
                && masterAtlas != null
                && AtlasPackablesMatch(masterAtlas, sprites)
                && AtlasConfigurationMatches(outputPath, atlasKey))
            {
                return;
            }

            bool createdNew = false;
            if (v2Asset == null)
            {
                v2Asset = new SpriteAtlasAsset();
                createdNew = true;
            }
            else if (masterAtlas == null)
            {
                // Corrupt asset: the .spriteatlasv2 file exists but no SpriteAtlas can be loaded.
                // The old object has no usable Remove entry point and further Adds would accumulate
                // duplicate packables, so delete and recreate.
                AssetDatabase.DeleteAsset(outputPath);
                v2Asset = new SpriteAtlasAsset();
                createdNew = true;
            }
            else
            {
                UnityEngine.Object[] oldPackables = masterAtlas.GetPackables();
                if (oldPackables != null && oldPackables.Length > 0)
                {
                    v2Asset.Remove(oldPackables);
                }
            }

            v2Asset.Add(sprites.ToArray());
            SpriteAtlasAsset.Save(v2Asset, outputPath);

            // Release the newly created wrapper right after Save. Without this, a full rebuild of
            // tens of thousands of atlases keeps every new instance alive until GC, and native-side
            // memory grows linearly across the batch.
            if (createdNew)
            {
                UnityEngine.Object.DestroyImmediate(v2Asset);
            }

            GeneratedAtlasPaths.Add(outputPath);
            PendingAtlasConfigure.Add(atlasKey);
        }

        private static void LogAtlasChangesSummary()
        {
            if (GeneratedAtlasPaths.Count == 0 && DeletedAtlasPaths.Count == 0)
            {
                return;
            }

            GeneratedAtlasPaths.Sort(StringComparer.Ordinal);
            DeletedAtlasPaths.Sort(StringComparer.Ordinal);

            var builder = new StringBuilder();
            builder.Append("[CycloneGames Atlas Pipeline] Atlas changes summary");
            if (GeneratedAtlasPaths.Count > 0)
            {
                builder.AppendLine();
                builder.Append("  Generated/updated: ");
                builder.Append(GeneratedAtlasPaths.Count);
                builder.AppendLine(" atlas asset(s).");
                AppendPathLines(builder, GeneratedAtlasPaths);
            }

            if (DeletedAtlasPaths.Count > 0)
            {
                builder.AppendLine();
                builder.Append("  Deleted: ");
                builder.Append(DeletedAtlasPaths.Count);
                builder.AppendLine(" atlas asset(s).");
                AppendPathLines(builder, DeletedAtlasPaths);
            }

            AtlasPipelineLog.Channel.Info(builder.ToString());
        }

        private static void AppendPathLines(
            StringBuilder builder,
            List<string> paths)
        {
            for (int i = 0; i < paths.Count; i++)
            {
                builder.Append("    ");
                builder.AppendLine(paths[i]);
            }
        }

        private static void ConfigureAtlasImporter(SpriteAtlasImporter importer, string atlasKey)
        {
            AtlasPipelineSettings settings = _settingsCache;
            AtlasImportRule rule = ResolveAtlasRule(atlasKey);

            AtlasTextureFormat androidFormat =
                GetEffectiveFormat(rule, AtlasPlatform.Android);
            AtlasTextureFormat iphoneFormat =
                GetEffectiveFormat(rule, AtlasPlatform.Iphone);
            AtlasTextureFormat webglFormat =
                GetEffectiveFormat(rule, AtlasPlatform.Webgl);
            AtlasTextureFormat standaloneFormat =
                GetEffectiveFormat(rule, AtlasPlatform.Standalone);
            int quality = Mathf.Clamp(
                rule?.CompressionQuality ?? AtlasPlatformFormats.DefaultCompressionQuality,
                0,
                100);
            int atlasMaxSize = rule?.AtlasMaxTextureSize ?? 2048;
            FilterMode filterMode = rule?.FilterMode ?? FilterMode.Bilinear;

            importer.includeInBuild = settings.IncludeInBuild;
            importer.packingSettings = CreatePackingSettings(settings, rule);
            importer.textureSettings = CreateTextureSettings(filterMode);

            importer.SetPlatformSettings(CreatePlatformSettings(
                AtlasPlatform.Android,
                androidFormat,
                quality,
                atlasMaxSize));
            importer.SetPlatformSettings(CreatePlatformSettings(
                AtlasPlatform.Iphone,
                iphoneFormat,
                quality,
                atlasMaxSize));
            importer.SetPlatformSettings(CreatePlatformSettings(
                AtlasPlatform.Webgl,
                webglFormat,
                quality,
                atlasMaxSize));
            importer.SetPlatformSettings(CreatePlatformSettings(
                AtlasPlatform.Standalone,
                standaloneFormat,
                quality,
                atlasMaxSize));
        }

        private static bool AtlasPackablesMatch(SpriteAtlas atlas, List<Sprite> expectedSprites)
        {
            UnityEngine.Object[] current = atlas.GetPackables();
            if (current == null || current.Length != expectedSprites.Count)
            {
                return false;
            }

            // Compare by "asset path + sprite name" rather than sprite.name alone: identically named
            // sub-sprites in different textures (two sprite sheets both having idle_0) were mistaken
            // for "the same packables" in the old implementation, silently leaving the atlas stale
            // (BUG-004).
            var currentKeys = new List<string>(current.Length);
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i] is Sprite sprite && sprite != null)
                {
                    currentKeys.Add(BuildSpriteIdentity(sprite));
                }
            }

            if (currentKeys.Count != expectedSprites.Count)
            {
                return false;
            }

            var expectedKeys = new List<string>(expectedSprites.Count);
            for (int i = 0; i < expectedSprites.Count; i++)
            {
                expectedKeys.Add(BuildSpriteIdentity(expectedSprites[i]));
            }

            currentKeys.Sort(StringComparer.Ordinal);
            expectedKeys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < currentKeys.Count; i++)
            {
                if (!string.Equals(
                        currentKeys[i],
                        expectedKeys[i],
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string BuildSpriteIdentity(Sprite sprite)
        {
            // GetAssetPath on a sub-sprite returns its main texture path, the stable identity we want.
            string assetPath = AssetDatabase.GetAssetPath(sprite);
            return string.IsNullOrEmpty(assetPath)
                ? sprite.name
                : assetPath + "/" + sprite.name;
        }

        private static bool AtlasConfigurationMatches(string outputPath, string atlasKey)
        {
            SpriteAtlasImporter importer = AssetImporter.GetAtPath(outputPath) as SpriteAtlasImporter;
            if (importer == null)
            {
                return false;
            }

            AtlasPipelineSettings settings = _settingsCache;
            AtlasImportRule rule = ResolveAtlasRule(atlasKey);
            AtlasTextureFormat androidFormat =
                GetEffectiveFormat(rule, AtlasPlatform.Android);
            AtlasTextureFormat iphoneFormat =
                GetEffectiveFormat(rule, AtlasPlatform.Iphone);
            AtlasTextureFormat webglFormat =
                GetEffectiveFormat(rule, AtlasPlatform.Webgl);
            AtlasTextureFormat standaloneFormat =
                GetEffectiveFormat(rule, AtlasPlatform.Standalone);
            int quality = Mathf.Clamp(
                rule?.CompressionQuality ?? AtlasPlatformFormats.DefaultCompressionQuality,
                0,
                100);
            int atlasMaxSize = rule?.AtlasMaxTextureSize ?? 2048;
            FilterMode filterMode = rule?.FilterMode ?? FilterMode.Bilinear;

            if (importer.includeInBuild != settings.IncludeInBuild
                || !PackingSettingsEqual(importer.packingSettings, CreatePackingSettings(settings, rule))
                || !TextureSettingsEqual(importer.textureSettings, CreateTextureSettings(filterMode))
                || !PlatformSettingsEqual(
                    importer.GetPlatformSettings(AtlasPlatformFormats.AndroidPlatformName),
                    CreatePlatformSettings(
                        AtlasPlatform.Android,
                        androidFormat,
                        quality,
                        atlasMaxSize))
                || !PlatformSettingsEqual(
                    importer.GetPlatformSettings(AtlasPlatformFormats.IphonePlatformName),
                    CreatePlatformSettings(
                        AtlasPlatform.Iphone,
                        iphoneFormat,
                        quality,
                        atlasMaxSize))
                || !PlatformSettingsEqual(
                    importer.GetPlatformSettings(AtlasPlatformFormats.WebglPlatformName),
                    CreatePlatformSettings(
                        AtlasPlatform.Webgl,
                        webglFormat,
                        quality,
                        atlasMaxSize))
                || !PlatformSettingsEqual(
                    importer.GetPlatformSettings(AtlasPlatformFormats.StandalonePlatformName),
                    CreatePlatformSettings(
                        AtlasPlatform.Standalone,
                        standaloneFormat,
                        quality,
                        atlasMaxSize)))
            {
                return false;
            }

            return true;
        }

        private static SpriteAtlasPackingSettings CreatePackingSettings(
            AtlasPipelineSettings settings,
            AtlasImportRule rule = null)
        {
            bool enableRotation = rule != null
                ? rule.ResolveAtlasRotation(settings.EnableRotation)
                : settings.EnableRotation;

            return new SpriteAtlasPackingSettings
            {
                padding = settings.AtlasPadding,
                blockOffset = settings.BlockOffset,
                enableRotation = enableRotation,
                enableTightPacking = settings.EnableTightPacking,
                enableAlphaDilation = true,
            };
        }

        private static SpriteAtlasTextureSettings CreateTextureSettings(FilterMode filterMode)
        {
            return new SpriteAtlasTextureSettings
            {
                readable = false,
                generateMipMaps = false,
                sRGB = true,
                filterMode = filterMode,
            };
        }

        private static TextureImporterPlatformSettings CreatePlatformSettings(
            AtlasPlatform platform,
            AtlasTextureFormat format,
            int quality,
            int maxTextureSize)
        {
            return new TextureImporterPlatformSettings
            {
                name = AtlasPlatformFormats.GetPlatformName(platform),
                overridden = true,
                format = AtlasPlatformFormats.ToTextureImporterFormat(format),
                textureCompression =
                    AtlasPlatformFormats.ToTextureImporterCompression(format),
                compressionQuality = quality,
                maxTextureSize = maxTextureSize,
            };
        }

        private static AtlasTextureFormat GetEffectiveFormat(
            AtlasImportRule rule,
            AtlasPlatform platform)
        {
            AtlasTextureFormat format;
            if (rule != null && rule.PixelArt)
            {
                format = AtlasTextureFormat.Rgba32;
            }
            else if (rule == null)
            {
                format = AtlasPlatformFormats.GetDefaultFormat(platform);
            }
            else
            {
                switch (platform)
                {
                    case AtlasPlatform.Android:
                        format = rule.AndroidFormat;
                        break;
                    case AtlasPlatform.Iphone:
                        format = rule.IphoneFormat;
                        break;
                    case AtlasPlatform.Webgl:
                        format = rule.WebglFormat;
                        break;
                    case AtlasPlatform.Standalone:
                        format = rule.StandaloneFormat;
                        break;
                    default:
                        format = AtlasPlatformFormats.GetDefaultFormat(platform);
                        break;
                }
            }

            return AtlasPlatformFormats.GetSafeFormat(platform, format);
        }

        private static bool PackingSettingsEqual(
            SpriteAtlasPackingSettings left,
            SpriteAtlasPackingSettings right)
        {
            return left.padding == right.padding
                   && left.blockOffset == right.blockOffset
                   && left.enableRotation == right.enableRotation
                   && left.enableTightPacking == right.enableTightPacking
                   && left.enableAlphaDilation == right.enableAlphaDilation;
        }

        private static bool TextureSettingsEqual(
            SpriteAtlasTextureSettings left,
            SpriteAtlasTextureSettings right)
        {
            return left.readable == right.readable
                   && left.generateMipMaps == right.generateMipMaps
                   && left.sRGB == right.sRGB
                   && left.filterMode == right.filterMode;
        }

        private static bool PlatformSettingsEqual(
            TextureImporterPlatformSettings left,
            TextureImporterPlatformSettings right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return left.overridden == right.overridden
                   && left.format == right.format
                   && left.textureCompression == right.textureCompression
                   && left.compressionQuality == right.compressionQuality
                   && left.maxTextureSize == right.maxTextureSize;
        }

        private static AtlasImportRule ResolveAtlasRule(string atlasKey)
        {
            if (!AtlasToAssets.TryGetValue(atlasKey, out HashSet<string> atlasSet))
            {
                return null;
            }

            // Determinism: HashSet iteration order is not stable. Sort first, then return the first
            // resolvable rule so the same config resolves to the same rule on any machine and in any
            // run order. (The AtlasGroup uniqueness check already blocks "same key hits different
            // rules" at the root, but this keeps cross-run consistency defensively.)
            var sortedPaths = new string[atlasSet.Count];
            atlasSet.CopyTo(sortedPaths);
            Array.Sort(sortedPaths, StringComparer.Ordinal);

            foreach (string path in sortedPaths)
            {
                AtlasImportRule rule = ResolveRule(path);
                if (rule != null)
                {
                    return rule;
                }
            }

            return null;
        }

        private static AtlasImportRule ResolveRule(string assetPath)
        {
            string path = NormalizeAssetPath(assetPath);
            for (int i = 0; i < RuleCache.Count; i++)
            {
                if (RuleCache[i].MatchesPath(path) && !RuleCache[i].IsPathExcluded(path))
                {
                    return RuleCache[i];
                }
            }

            return null;
        }

        private static void RefreshRuleOrder()
        {
            RuleCache.Clear();
            if (_settingsCache == null)
            {
                return;
            }

            // Sync the naming policy with settings: once AsciiOnlyNames is tightened, non-ASCII
            // file names (CJK, etc.) flow through the existing invalid-name prompt → review window
            // → build validation path.
            AtlasNaming.AsciiOnlyNames = _settingsCache.AsciiOnlyNames;

            IReadOnlyList<AtlasImportRule> importRules = _settingsCache.ImportRules;
            if (importRules == null)
            {
                return;
            }

            var configurationOrder =
                new Dictionary<AtlasImportRule, int>(importRules.Count);
            bool healedAnyReference = false;
            for (int i = 0; i < importRules.Count; i++)
            {
                AtlasImportRule rule = importRules[i];
                configurationOrder[rule] = i;
                RuleCache.Add(rule);

                if (rule == null)
                {
                    continue;
                }

                // Refresh the resolved-folder cache so a renamed folder (same GUID) resolves to the
                // new path.
                rule.RefreshResolvedFolder();

                // One-time migration: backfill the GUID for legacy rules that only have a path.
                if (rule.HealSourceFolderGuid())
                {
                    healedAnyReference = true;
                }
            }

            if (healedAnyReference)
            {
                EditorUtility.SetDirty(_settingsCache);
            }

            RuleCache.Sort((left, right) =>
            {
                // 1) Longer paths are more specific and win.
                int folderComparison = right.NormalizedSourceFolder.Length
                    .CompareTo(left.NormalizedSourceFolder.Length);
                if (folderComparison != 0)
                {
                    return folderComparison;
                }

                // 2) Keyword dimension. MatchesPath means "0 keywords matches everything, N keywords
                //    match if any hits", so 0 is the broadest fallback and more keywords means a
                //    wider match. The fallback must sort last and the rest ascending (fewer = more
                //    specific). A plain ascending sort would be wrong: it would push the 0-keyword
                //    fallback to the front and swallow every other rule in the same folder.
                int leftKeywordRank = left.PathKeywords.Count == 0
                    ? int.MaxValue
                    : left.PathKeywords.Count;
                int rightKeywordRank = right.PathKeywords.Count == 0
                    ? int.MaxValue
                    : right.PathKeywords.Count;
                int keywordComparison = leftKeywordRank.CompareTo(rightKeywordRank);
                if (keywordComparison != 0)
                {
                    return keywordComparison;
                }

                // 3) List.Sort is introsort (unstable); equal keys are not guaranteed to keep their
                //    relative order across runs. Use the original config index as a deterministic
                //    tiebreaker.
                return configurationOrder[left].CompareTo(configurationOrder[right]);
            });
        }

        private static void EnsureAssetFolderExists(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder)
                || !assetFolder.StartsWith("Assets/", StringComparison.Ordinal)
                || AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            string absolute = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                assetFolder.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar)));
            Directory.CreateDirectory(absolute);
            AssetDatabase.Refresh();
        }

        private static bool IsSupportedImagePath(string assetPath)
        {
            // The single implementation lives in AtlasNaming (no UnityEngine dependency, unit-testable);
            // this delegates to avoid two copies drifting apart — extending .tga/.webp later touches
            // one place only.
            return AtlasNaming.IsSupportedImagePath(assetPath);
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : assetPath.Replace('\\', '/');
        }
    }

    public readonly struct AtlasPipelineSnapshot
    {
        public AtlasPipelineSnapshot(
            int ruleCount,
            int indexedSpriteCount,
            int atlasCount,
            int dirtyAtlasCount)
        {
            RuleCount = ruleCount;
            IndexedSpriteCount = indexedSpriteCount;
            AtlasCount = atlasCount;
            DirtyAtlasCount = dirtyAtlasCount;
        }

        public int RuleCount { get; }
        public int IndexedSpriteCount { get; }
        public int AtlasCount { get; }
        public int DirtyAtlasCount { get; }
    }
}
