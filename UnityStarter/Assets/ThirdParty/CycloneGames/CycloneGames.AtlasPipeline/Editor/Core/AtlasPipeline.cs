using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using CycloneGames.Logging;
using CycloneGames.AtlasPipeline.Pure;

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

        private const double MaxEditorFrameBudgetSeconds = 0.008d;

        /// <summary>Fallback atlas size when no rule owns an atlas. Matches Unity's default.</summary>
        private const int DefaultAtlasMaxTextureSize = 2048;

        /// <summary>
        /// Committed record of every atlas the current configuration should produce. It is the
        /// cross-machine baseline: CI compares against it to detect stale atlases without generating
        /// anything, and it gives a fresh editor session the configuration snapshot that
        /// <see cref="HandleSettingsChanged"/> needs to avoid dirtying every atlas.
        /// </summary>
        public const string ManifestPath = "Assets/Settings/AtlasPipelineManifest.txt";

        /// <summary>
        /// Written into the manifest so a manifest produced by an incompatible generator can be
        /// rejected instead of silently misread.
        /// </summary>
        private const string ManifestGeneratorVersion = "CycloneGames.AtlasPipeline/2";

        /// <summary>Cap on how many atlas keys are listed in one drift message.</summary>
        private const int MaxLoggedManifestKeys = 10;

        /// <summary>
        /// Cap on how many oversized sprite names go into one warning. A misconfigured rule can flag
        /// thousands at once, and one log line per sprite would bury everything else.
        /// </summary>
        private const int MaxLoggedOversizedSprites = 8;

        /// <summary>
        /// Cached so sorting sub-sprites does not allocate a delegate per atlas per pass.
        /// </summary>
        private static readonly Comparison<Sprite> SpriteNameComparison =
            (left, right) => string.CompareOrdinal(left?.name, right?.name);

        /// <summary>
        /// Source-asset to atlas mapping and the dirty set. Replaces three parallel static
        /// collections: a single owner means membership, ordering, fingerprints and dirty tracking
        /// can never disagree with each other, and the per-bucket ordered list is cached instead of
        /// being rebuilt and re-sorted for every atlas on every pass.
        /// </summary>
        private static readonly AtlasIndex Index = new AtlasIndex();

        private static readonly List<AtlasImportRule> RuleCache =
            new List<AtlasImportRule>();

        // Reused across passes. EditorApplication.update polls every frame, so anything allocated
        // per pass is allocated hundreds of times a minute while an artist imports art.
        private static readonly List<string> DirtyKeyBuffer = new List<string>();
        private static readonly List<string> RemovedKeyBuffer = new List<string>();
        private static readonly List<Sprite> SpriteBuffer = new List<Sprite>();
        private static readonly List<Sprite> SubSpriteBuffer = new List<Sprite>();
        private static readonly List<AtlasSpriteIdentity> CurrentIdentityBuffer =
            new List<AtlasSpriteIdentity>();
        private static readonly List<AtlasSpriteIdentity> ExpectedIdentityBuffer =
            new List<AtlasSpriteIdentity>();
        private static readonly List<string> OversizedSpriteBuffer = new List<string>();

        /// <summary>
        /// First spelling seen for each sprite name in the atlas being generated, used to flag names
        /// that differ only by letter case. Reused across atlases.
        /// </summary>
        private static readonly Dictionary<string, string> SpriteNameSpelling =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> CaseVariantNameSamples = new List<string>();
        private static int _caseVariantNameCount;

        private static Sprite[] _spriteArrayBuffer = Array.Empty<Sprite>();

        private static readonly List<string> GeneratedAtlasPaths =
            new List<string>();
        private static readonly List<string> DeletedAtlasPaths =
            new List<string>();
        private static readonly List<string> PendingAtlasConfigure =
            new List<string>();

        /// <summary>
        /// Fingerprint of every atlas written (or verified) during this session, keyed by atlas key.
        /// A regeneration pass compares it and skips atlases whose content cannot have changed, which
        /// is the difference between a settings change costing a handful of hash comparisons and
        /// costing a full reload of every sprite in the project.
        /// Session-local by construction: it is rebuilt from scratch after every domain reload and is
        /// never persisted, so a wrong entry can only cause extra work, never a missed regeneration.
        /// </summary>
        private static readonly Dictionary<string, long> GeneratedFingerprints =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Rule fingerprints the atlases currently on disk were generated with, keyed by AtlasGroup
        /// (globally unique across rules, enforced by build validation). Compared against the live
        /// rules when settings change, so editing one rule dirties only the atlases that rule owns.
        /// </summary>
        private static readonly Dictionary<string, int> GeneratedRuleFingerprints =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private static int _generatedGlobalFingerprint;

        /// <summary>
        /// Tracks PerSprite atlas keys back to the first asset that claimed them. PerSprite keys are
        /// the file stem alone, so two "btn.png" in different folders collapse into one atlas and one
        /// set of sprites silently never ships. Recorded during indexing — no extra scan needed.
        /// </summary>
        private static readonly Dictionary<string, string> PerSpriteKeyOwners =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> CollidedAtlasKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Atlas keys whose content exceeds the configured max texture size. Unity drops the
        /// overflow silently and the loss only shows up as white quads at runtime, so it is collected
        /// during generation and promoted to a build failure.
        /// </summary>
        private static readonly List<string> CapacityOverflowAtlases = new List<string>();

        private static AtlasPipelineSettings _settingsCache;
        private static bool _initialized;
        private static double _nextProcessTime;
        private static bool _invalidNamePromptScheduled;
        private static bool _textureSizePromptScheduled;
        private static bool _outputFolderIntrusionPromptScheduled;
        private static bool _spritePackerPromptScheduled;
        private static bool _projectChangedRefreshScheduled;
        private static int _batchedEditingDepth;

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
                Index.AssetCount,
                Index.BucketCount,
                Index.DirtyCount);
        }

        /// <summary>
        /// Atlas keys where two different source assets resolve to one output file, which silently
        /// drops one set of sprites. Empty unless a rule uses PerSprite granularity.
        /// </summary>
        public static IReadOnlyList<string> GetCollidedAtlasKeys()
        {
            var keys = new List<string>(CollidedAtlasKeys.Count);
            keys.AddRange(CollidedAtlasKeys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        [InitializeOnLoadMethod]
        private static void InitializeOnLoad()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            if (Index.DirtyCount == 0
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

        /// <summary>
        /// Drops every cached view of the project, including the fingerprints that let a regeneration
        /// pass skip unchanged atlases. Conservative by design: after this call the next pass
        /// regenerates everything it is asked to, with no chance of a stale skip.
        /// </summary>
        public static void InvalidateCache()
        {
            _settingsCache = null;
            _initialized = false;
            RuleCache.Clear();
            ClearIndex();
            GeneratedFingerprints.Clear();
            GeneratedRuleFingerprints.Clear();
            _generatedGlobalFingerprint = AtlasHash.NullHash;
        }

        /// <summary>
        /// Entry point called after the user edits settings.
        /// Dirtying every atlas on any settings edit was the previous behaviour, and on a large
        /// project it turned a single format change into a full regeneration of every atlas in the
        /// project. The rules are now diffed against the configuration the atlases on disk were
        /// generated with, so only atlases owned by a changed rule are queued. Global settings
        /// (padding, rotation, tight packing, output folder, include-in-build) still dirty
        /// everything, because they feed every atlas.
        /// </summary>
        public static void HandleSettingsChanged()
        {
            // Read the previous configuration before dropping the cache. The settings object has
            // already been mutated by the inspector at this point, so the only reliable record of
            // "what the atlases on disk were built with" is the snapshot committed by the last
            // generation pass.
            var previousRuleFingerprints =
                new Dictionary<string, int>(GeneratedRuleFingerprints, StringComparer.Ordinal);
            int previousGlobalFingerprint = _generatedGlobalFingerprint;

            _settingsCache = null;
            _initialized = false;
            RuleCache.Clear();

            EnsureSettingsAsset();
            BuildIndexFromAssetDatabase(markDirty: false, clearDirtyKeys: true);
            _initialized = true;

            ApplyConfigurationDelta(previousRuleFingerprints, previousGlobalFingerprint);
            ScheduleProcessing();
        }

        /// <summary>
        /// Marks the atlases affected by a settings change. Falls back to dirtying everything when
        /// there is no trustworthy baseline — the first settings edit of a session has nothing to
        /// diff against. A missed regeneration ships a stale atlas; an unnecessary one only costs
        /// time, so the fallback is always "regenerate".
        /// </summary>
        private static void ApplyConfigurationDelta(
            Dictionary<string, int> previousRuleFingerprints,
            int previousGlobalFingerprint)
        {
            if (previousRuleFingerprints.Count == 0)
            {
                Index.MarkAllDirty();
                return;
            }

            bool globalChanged = previousGlobalFingerprint != ComputeGlobalFingerprint();
            IReadOnlyList<AtlasBucket> buckets = Index.GetBuckets();
            for (int i = 0; i < buckets.Count; i++)
            {
                AtlasBucket bucket = buckets[i];
                AtlasImportRule rule = ResolveAtlasRule(bucket.Key);
                if (rule == null || globalChanged)
                {
                    Index.MarkDirty(bucket.Key);
                    continue;
                }

                if (!previousRuleFingerprints.TryGetValue(rule.AtlasGroup, out int previous)
                    || previous != ComputeRuleFingerprint(rule))
                {
                    Index.MarkDirty(bucket.Key);
                }
            }
        }

        /// <summary>
        /// Records the configuration that the atlases on disk now correspond to, so the next settings
        /// change can diff against it instead of dirtying everything.
        /// </summary>
        private static void CommitConfigurationSnapshot()
        {
            GeneratedRuleFingerprints.Clear();
            for (int i = 0; i < RuleCache.Count; i++)
            {
                AtlasImportRule rule = RuleCache[i];
                if (rule == null)
                {
                    continue;
                }

                // First rule wins on a duplicate group. Build validation already rejects that
                // configuration; an arbitrary but stable choice keeps the delta deterministic.
                if (!GeneratedRuleFingerprints.ContainsKey(rule.AtlasGroup))
                {
                    GeneratedRuleFingerprints.Add(
                        rule.AtlasGroup,
                        ComputeRuleFingerprint(rule));
                }
            }

            _generatedGlobalFingerprint = ComputeGlobalFingerprint();
        }

        /// <summary>
        /// Fingerprint of the rule fields that change a generated atlas. Source-import-only fields
        /// (mipmaps, readability) are excluded: those change the asset's dependency hash, which is
        /// folded into the per-atlas fingerprint separately and covers them more precisely.
        /// </summary>
        private static int ComputeRuleFingerprint(AtlasImportRule rule)
        {
            if (rule == null)
            {
                return AtlasHash.NullHash;
            }

            int hash = AtlasHash.BeginFnv1a();
            AppendFnv(ref hash, (int)rule.AndroidFormat);
            AppendFnv(ref hash, (int)rule.IphoneFormat);
            AppendFnv(ref hash, (int)rule.WebglFormat);
            AppendFnv(ref hash, (int)rule.StandaloneFormat);
            AppendFnv(ref hash, rule.PixelArt ? 1 : 0);
            AppendFnv(ref hash, rule.CompressionQuality);
            AppendFnv(ref hash, rule.AtlasMaxTextureSize);
            AppendFnv(ref hash, (int)rule.FilterMode);
            AppendFnv(ref hash, (int)rule.WrapMode);
            AppendFnv(ref hash, (int)rule.AtlasRotationMode);
            AppendFnv(ref hash, (int)rule.AtlasGranularity);
            AppendFnv(ref hash, (int)rule.SpriteMode);
            AtlasHash.AppendFnv1a(ref hash, rule.AtlasGroup);
            return hash;
        }

        /// <summary>Global settings that feed every atlas: a change here dirties all of them.</summary>
        private static int ComputeGlobalFingerprint()
        {
            AtlasPipelineSettings settings = _settingsCache;
            if (settings == null)
            {
                return AtlasHash.NullHash;
            }

            int hash = AtlasHash.BeginFnv1a();
            AppendFnv(ref hash, settings.AtlasPadding);
            AppendFnv(ref hash, settings.EnableRotation ? 1 : 0);
            AppendFnv(ref hash, settings.EnableTightPacking ? 1 : 0);
            AppendFnv(ref hash, settings.BlockOffset);
            AppendFnv(ref hash, settings.IncludeInBuild ? 1 : 0);
            AtlasHash.AppendFnv1a(ref hash, settings.NormalizedOutputAtlasFolder);
            return hash;
        }

        /// <summary>
        /// Fingerprint of the whole resolved rule list. Rule order is deterministic (folder
        /// specificity, then keyword count, then configuration index), so this value is reproducible
        /// across machines and is what lets the manifest detect any rule edit.
        /// </summary>
        private static int ComputeRuleSetFingerprint()
        {
            int hash = AtlasHash.BeginFnv1a();
            for (int i = 0; i < RuleCache.Count; i++)
            {
                AppendFnv(ref hash, ComputeRuleFingerprint(RuleCache[i]));
                AtlasHash.AppendFnv1a(ref hash, '\u001F');
            }

            return hash;
        }

        private static void AppendFnv(ref int hash, int value)
        {
            uint bits = (uint)value;
            for (int shift = 0; shift < 32; shift += 8)
            {
                AtlasHash.AppendFnv1a(ref hash, (char)((bits >> shift) & 0xFFu));
            }
        }

        /// <summary>
        /// Builds the manifest for the current index.
        /// The content hashes are pure functions of the ordered member list and the governing rule
        /// configuration, so two machines with the same sources and settings produce byte-identical
        /// manifests. Source pixel content is deliberately excluded: repainting a texture does not
        /// change which packables an atlas holds, so it does not make the atlas stale.
        /// </summary>
        private static AtlasManifest BuildManifest()
        {
            string outputFolder = _settingsCache != null
                ? _settingsCache.NormalizedOutputAtlasFolder
                : string.Empty;

            var entries = new List<AtlasManifestEntry>(Index.BucketCount);
            IReadOnlyList<AtlasBucket> buckets = Index.GetBuckets();
            for (int i = 0; i < buckets.Count; i++)
            {
                AtlasBucket bucket = buckets[i];
                if (bucket.Count == 0)
                {
                    continue;
                }

                AtlasImportRule rule = ResolveAtlasRule(bucket.Key);
                entries.Add(new AtlasManifestEntry(
                    bucket.Key,
                    BuildAtlasAssetPath(outputFolder, bucket.Key),
                    bucket.Count,
                    bucket.ComputeContentHash(ComputeRuleFingerprint(rule)),

                    // Always one page today: a SpriteAtlas packs into a single texture, and an atlas
                    // that needs more already fails the build through the capacity check. The field
                    // is carried so the format does not have to change if auto-splitting is added.
                    1,
                    bucket.RuleId));
            }

            return new AtlasManifest(
                AtlasManifest.CurrentSchemaVersion,
                ManifestGeneratorVersion,
                AtlasHash.Combine64(
                    ComputeGlobalFingerprint(),
                    ComputeRuleSetFingerprint()),
                entries);
        }

        /// <summary>
        /// Writes the manifest for the current index. Called only after a complete generation pass:
        /// a partial pass would record atlases that were never actually written.
        /// </summary>
        public static void WriteManifest()
        {
            if (_settingsCache == null)
            {
                return;
            }

            string absolute = ToAbsolutePath(ManifestPath);
            if (string.IsNullOrEmpty(absolute))
            {
                return;
            }

            string directory = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // LF only and no BOM. The file is committed, so a CRLF or BOM difference between a
            // Windows developer machine and a Linux CI agent would show up as a whole-file diff on
            // every change and turn every merge into a conflict.
            File.WriteAllText(
                absolute,
                AtlasManifestSerializer.Write(BuildManifest()),
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>
        /// Reads the committed manifest, or null when it does not exist yet. A missing manifest is a
        /// normal state for a fresh clone, not an error.
        /// </summary>
        public static AtlasManifest ReadManifest(ICollection<string> errors = null)
        {
            string absolute = ToAbsolutePath(ManifestPath);
            if (string.IsNullOrEmpty(absolute) || !File.Exists(absolute))
            {
                return null;
            }

            return AtlasManifestSerializer.Read(File.ReadAllText(absolute), errors);
        }

        /// <summary>
        /// Compares the committed manifest against the current index and reports the difference.
        /// Generates and writes nothing, so it is safe to run on a CI agent or in an editor right
        /// after a pull.
        /// Only structural drift is reported — added, removed, or reconfigured atlases. Repainting a
        /// source image is not drift, because the atlas holds the same packables either way.
        /// </summary>
        public static IReadOnlyList<string> ValidateManifestDrift()
        {
            EnsureInitialized();

            var errors = new List<string>();
            AtlasManifest recorded = ReadManifest(errors);
            var drift = new List<string>(errors);
            if (recorded == null)
            {
                if (drift.Count == 0)
                {
                    drift.Add(
                        $"No atlas manifest found at '{ManifestPath}'. Run a full atlas "
                        + "regeneration and commit the manifest so CI can detect stale atlases.");
                }

                return drift;
            }

            if (recorded.SchemaVersion != AtlasManifest.CurrentSchemaVersion)
            {
                drift.Add(
                    $"Atlas manifest schema {recorded.SchemaVersion} predates the supported "
                    + $"version {AtlasManifest.CurrentSchemaVersion}. Regenerate the atlases and "
                    + "commit the new manifest.");
                return drift;
            }

            AtlasManifestDelta delta = AtlasManifestComparer.Compare(recorded, BuildManifest());
            if (delta.IsUpToDate)
            {
                return drift;
            }

            if (delta.Added.Count > 0)
            {
                drift.Add($"{delta.Added.Count} atlas(es) are absent from the manifest: "
                          + SummarizeKeys(delta.Added));
            }

            if (delta.Removed.Count > 0)
            {
                drift.Add($"{delta.Removed.Count} atlas(es) no longer exist: "
                          + SummarizeKeys(delta.Removed));
            }

            if (delta.Changed.Count > 0)
            {
                drift.Add($"{delta.Changed.Count} atlas(es) are stale: "
                          + SummarizeKeys(delta.Changed));
            }

            drift.Add("Regenerate the atlases and commit the updated manifest.");
            return drift;
        }

        private static string SummarizeKeys(IReadOnlyList<string> keys)
        {
            if (keys.Count <= MaxLoggedManifestKeys)
            {
                return string.Join(", ", keys);
            }

            var builder = new StringBuilder();
            for (int i = 0; i < MaxLoggedManifestKeys; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(keys[i]);
            }

            builder.Append(" ... and ").Append(keys.Count - MaxLoggedManifestKeys).Append(" more");
            return builder.ToString();
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

            // Atlas-key collisions are discovered while indexing, so the index has to exist before
            // they can be reported. EnsureInitialized is a no-op once the index is built.
            EnsureInitialized();

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
            // Case-insensitive on purpose: atlas keys are compared case-insensitively throughout the
            // index, so two groups spelled "UI" and "ui" would pass an ordinal check here and then
            // silently merge into one atlas. The check has to use the same notion of equality as the
            // bucket map, or it validates a guarantee the pipeline does not actually provide.
            var atlasGroupOwners =
                new Dictionary<string, AtlasImportRule>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < RuleCache.Count; i++)
            {
                AtlasImportRule rule = RuleCache[i];
                string groupKey = AtlasPathUtility.SanitizePart(rule.AtlasGroup);
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

                if (AtlasPathUtility.PathsOverlap(overlapOutputFolder, sourceFolder))
                {
                    errors.Add(
                        $"Output atlas folder '{overlapOutputFolder}' overlaps with import rule "
                        + $"'{RuleCache[i].Name}' source folder '{sourceFolder}'. Every source "
                        + "image inside the output folder is treated as an intrusion and would "
                        + "be moved to quarantine. Choose a disjoint output folder.");
                }
            }

            // PerSprite atlas keys are the file stem alone, so two identically named files in
            // different folders merge into one atlas. There is no warning at runtime for the sprites
            // that never ship, so block it here. Detection happens during indexing — no extra scan.
            IReadOnlyList<string> collidedKeys = GetCollidedAtlasKeys();
            for (int i = 0; i < collidedKeys.Count; i++)
            {
                errors.Add(
                    $"Atlas key '{collidedKeys[i]}' is claimed by more than one source asset. "
                    + "PerSprite granularity builds the atlas key from the file name alone, so "
                    + "identically named files under different folders collapse into a single atlas "
                    + "and one set of sprites is silently lost. Rename the files, or switch the "
                    + "rule to PerChildFolder granularity.");
            }

            // Case-variant source paths. On Windows and on default macOS volumes these are one file,
            // so such a project cannot be checked out correctly there: one developer sees one asset,
            // a Linux CI agent sees two, and the atlases do not match. This is invisible until
            // something renders wrong on only some machines, so block it.
            if (Index.CaseVariantCount > 0)
            {
                errors.Add(
                    $"{Index.CaseVariantCount} source asset(s) differ from another asset only by "
                    + "letter case (for example " + SummarizeKeys(Index.CaseVariantSamples)
                    + "). Windows and default macOS volumes treat these as one file, so the project "
                    + "checks out differently per machine and generates different atlases. Give the "
                    + "files distinct names.");
            }

            // A global exclude that swallows a rule's source folder disables that rule silently — the
            // rule still validates, still shows up in the window, and simply matches nothing.
            IReadOnlyList<string> globalExcludes = _settingsCache.GlobalExcludedFolderPaths;
            for (int i = 0; i < globalExcludes.Count; i++)
            {
                string exclude = AtlasPathUtility.NormalizeAndTrim(globalExcludes[i]);
                if (string.IsNullOrEmpty(exclude))
                {
                    continue;
                }

                for (int r = 0; r < RuleCache.Count; r++)
                {
                    string sourceFolder = RuleCache[r].NormalizedSourceFolder;
                    if (string.IsNullOrEmpty(sourceFolder))
                    {
                        continue;
                    }

                    if (AtlasPathUtility.IsUnderFolder(sourceFolder, exclude))
                    {
                        errors.Add(
                            $"Global exclude folder '{exclude}' contains import rule "
                            + $"'{RuleCache[r].Name}' source folder '{sourceFolder}'. The rule "
                            + "would silently match nothing. Remove the exclude or move the rule.");
                    }
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

            // Capacity overflow is only knowable once the sprite rects are loaded, so it is collected
            // during generation and promoted to a failure here. Unity packs one texture per atlas and
            // silently drops whatever does not fit; shipping that means shipping missing sprites that
            // only show up as white quads at runtime.
            AppendCapacityFailures(failures);

            // Sweep orphan atlases: stale .spriteatlasv2 files left in the output folder after a
            // rule rename/deletion would otherwise ship in the player forever.
            SweepOrphanAtlases();

            // Only record the manifest once everything succeeded. A partial pass would commit
            // fingerprints for atlases that were never written, which is the one thing the manifest
            // must never claim.
            if (failures.Count == 0)
            {
                WriteManifest();
            }

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

        /// <summary>
        /// Regenerates every atlas the index expects.
        /// </summary>
        /// <param name="force">
        /// Bypasses the per-atlas fingerprint and reloads every sprite. Use it when on-disk state may
        /// have drifted in a way the fingerprint cannot see, for example after manually editing a
        /// .spriteatlasv2 file. Normal passes leave it off.
        /// </param>
        public static void ProcessAllDirtyAtlases(bool force = false)
        {
            RebuildIndex(markDirty: true);

            // The manual entry point collects failures too, so the window cannot report "all
            // rebuilt" while some atlases actually failed.
            var failures = new List<string>();
            ProcessDirtyAtlases(failures: failures, force: force);

            // Same as the build path: post-generation check + capacity + orphan sweep.
            VerifyExpectedAtlases(failures);
            AppendCapacityFailures(failures);
            SweepOrphanAtlases();

            // See the build path: the manifest is only trustworthy after a clean, complete pass.
            if (failures.Count == 0)
            {
                WriteManifest();
            }

            if (failures.Count > 0)
            {
                AtlasPipelineLog.Channel.Error(
                    "[CycloneGames Atlas Pipeline] Atlas regeneration finished with "
                    + $"{failures.Count} failure(s):{Environment.NewLine}"
                    + string.Join(Environment.NewLine, failures));
            }
        }

        private static void AppendCapacityFailures(ICollection<string> failures)
        {
            if (CapacityOverflowAtlases.Count == 0)
            {
                return;
            }

            CapacityOverflowAtlases.Sort(StringComparer.Ordinal);
            for (int i = 0; i < CapacityOverflowAtlases.Count; i++)
            {
                failures?.Add(
                    $"Atlas '{CapacityOverflowAtlases[i]}' does not fit its configured max texture "
                    + "size: Unity packs one texture per atlas and silently drops the sprites that "
                    + "do not fit, so they would ship as missing. Raise 'Atlas Max' on the owning "
                    + "rule, or split the source folder with a finer atlas granularity.");
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

            // GetBuckets returns them in atlas-key order, so a failure list is identical across
            // machines and CI logs stay diffable.
            IReadOnlyList<AtlasBucket> buckets = Index.GetBuckets();
            for (int i = 0; i < buckets.Count; i++)
            {
                AtlasBucket bucket = buckets[i];
                if (bucket.Count == 0)
                {
                    // An empty bucket is the normal path for an atlas that was just cleared and
                    // deleted.
                    continue;
                }

                string expectedPath = BuildAtlasAssetPath(folder, bucket.Key);
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(expectedPath) == null)
                {
                    failures?.Add(
                        $"Expected atlas '{expectedPath}' (key '{bucket.Key}', "
                        + $"{bucket.Count} sprite(s)) was not generated. "
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
            IReadOnlyList<AtlasBucket> buckets = Index.GetBuckets();
            for (int i = 0; i < buckets.Count; i++)
            {
                expected.Add(BuildAtlasAssetPath(folder, buckets[i].Key));
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

                // Allocation-free folder test: StartsWith(outputFolder + "/") built a new string per
                // imported asset, on the path that runs for every image import.
                if (AtlasPathUtility.IsUnderFolder(path, outputFolder))
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
                // Keep the dirty set: a project-changed rescan must not discard regeneration work
                // that is still queued from the asset changes that triggered it.
                Index.ClearMembership();
                ResetCollisionTracking();
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
            Index.Clear();
            ResetCollisionTracking();
        }

        /// <summary>
        /// Drops the per-scan diagnostics that are rebuilt while indexing. Called on every index
        /// rebuild so a deleted or renamed source asset cannot leave a stale collision report behind.
        /// </summary>
        private static void ResetCollisionTracking()
        {
            PerSpriteKeyOwners.Clear();
            CollidedAtlasKeys.Clear();
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

            Index.Add(assetPath, atlasKey, markDirty);

            if (Index.TryGetBucket(atlasKey, out AtlasBucket bucket))
            {
                // Keep the owning-rule shortcut in sync. It turns "which rule configures this atlas"
                // from a sorted scan over every member plus a rule match per member into an index
                // lookup, and the rule is what supplies the atlas size, formats and rotation.
                bucket.RuleId = rule.PipelineIndex;
            }

            // PerSprite keys are the file stem alone, so two "btn.png" under different folders
            // collapse into one atlas and one set of sprites silently never ships. There is no
            // runtime error for it, so it is detected here and blocked by build validation.
            if (rule.AtlasGranularity == AtlasGranularity.PerSprite)
            {
                if (PerSpriteKeyOwners.TryGetValue(atlasKey, out string owner))
                {
                    if (!string.Equals(owner, assetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        CollidedAtlasKeys.Add(atlasKey);
                    }
                }
                else
                {
                    PerSpriteKeyOwners.Add(atlasKey, assetPath);
                }
            }
        }

        private static bool RemoveAsset(string assetPath)
        {
            return Index.Remove(assetPath, markDirty: true, out _);
        }

        private static string ResolveAtlasKey(AtlasImportRule rule, string assetPath)
        {
            // Both switches live on the settings asset rather than the rule: flipping either renames
            // every atlas in the project, which is a project-wide decision.
            bool collisionSafe = _settingsCache != null && _settingsCache.CollisionSafeAtlasKeys;
            AtlasKeyCasing casing = _settingsCache != null
                ? _settingsCache.AtlasKeyCasing
                : AtlasKeyCasing.Preserve;
            return ResolveAtlasKey(rule, assetPath, collisionSafe, casing);
        }

        /// <summary>
        /// Pure atlas-key computation. Split out from the settings-aware overload so the naming rules
        /// — the thing that decides which file an atlas lands in, and therefore every runtime path
        /// built from it — can be unit tested without an asset database.
        /// </summary>
        internal static string ResolveAtlasKey(
            AtlasImportRule rule,
            string assetPath,
            bool collisionSafe)
        {
            return ResolveAtlasKey(rule, assetPath, collisionSafe, AtlasKeyCasing.Preserve);
        }

        internal static string ResolveAtlasKey(
            AtlasImportRule rule,
            string assetPath,
            bool collisionSafe,
            AtlasKeyCasing casing)
        {
            if (rule == null || rule.AtlasGranularity == AtlasGranularity.None)
            {
                return null;
            }

            // Normalize defensively. Every caller already normalizes, but a single raw path slipping
            // through would silently produce a different atlas key, and the key is the output file
            // name. Normalize returns the same instance when there is nothing to replace, so the
            // cost is one scan of a path-sized string.
            assetPath = AtlasPathUtility.Normalize(assetPath);

            string group = AtlasPathUtility.SanitizePart(rule.AtlasGroup);
            if (rule.AtlasGranularity == AtlasGranularity.PerSourceFolder)
            {
                return ApplyKeyCasing(group, casing);
            }

            // Everything below works on the part of the path under the rule folder. Taking ranges
            // instead of building an intermediate relative path avoids one allocation per asset per
            // rule, which is the dominant cost on a full rescan of a large art tree.
            string folder = rule.NormalizedSourceFolder;

            // Callers only ever resolve a rule for a path it matches, but the atlas key is the output
            // file name, so a mis-resolved rule must not be allowed to slice an unrelated path into a
            // garbage key. Fall back to the group when the path is not actually under the folder.
            if (!AtlasPathUtility.IsUnderFolder(assetPath, folder))
            {
                return ApplyKeyCasing(group, casing);
            }

            int relativeStart = folder.Length;
            if (relativeStart >= assetPath.Length)
            {
                return ApplyKeyCasing(group, casing);
            }

            if (assetPath[relativeStart] == '/')
            {
                relativeStart++;
            }

            if (rule.AtlasGranularity == AtlasGranularity.PerSprite)
            {
                AtlasPathUtility.GetStemRange(assetPath, out int stemStart, out int stemLength);
                string spriteName = stemLength > 0
                    ? assetPath.Substring(stemStart, stemLength)
                    : string.Empty;

                if (!collisionSafe)
                {
                    // Historical behaviour: the stem alone. Two "btn.png" under different folders
                    // collapse into one atlas and one set of sprites silently never ships. Detected
                    // during indexing and reported by ValidateForBuild.
                    return ApplyKeyCasing(
                        group + "_" + AtlasPathUtility.SanitizePart(spriteName),
                        casing);
                }

                // Fold the directory below the rule folder into the key (slashes become underscores)
                // so identically named files land in different atlases.
                int directoryEnd = stemStart > 0 ? stemStart - 1 : -1;
                string directoryPart = directoryEnd >= relativeStart
                    ? assetPath.Substring(relativeStart, directoryEnd - relativeStart)
                        .Replace('/', '_')
                    : "Root";

                return ApplyKeyCasing(
                    group
                    + "_" + AtlasPathUtility.SanitizePart(directoryPart)
                    + "_" + AtlasPathUtility.SanitizePart(spriteName),
                    casing);
            }

            // PerChildFolder: the first path segment below the rule folder, or "Root" when the file
            // sits directly inside it.
            int firstSlash = IndexOfSeparator(assetPath, relativeStart);
            string child = firstSlash >= 0
                ? assetPath.Substring(relativeStart, firstSlash - relativeStart)
                : "Root";
            return ApplyKeyCasing(group + "_" + AtlasPathUtility.SanitizePart(child), casing);
        }

        /// <summary>
        /// Applies the project's atlas-key casing policy. The key becomes the generated file name, so
        /// lowercasing it makes the output predictable from the rule configuration alone instead of
        /// depending on which spelling of a group or folder happened to be indexed first.
        /// Sprite names are never touched: they are looked up by name at runtime and are case
        /// sensitive there.
        /// </summary>
        private static string ApplyKeyCasing(string key, AtlasKeyCasing casing)
        {
            if (string.IsNullOrEmpty(key) || casing != AtlasKeyCasing.Lower)
            {
                return key;
            }

            // Invariant, never culture-sensitive: a Turkish locale must not turn "I" into "ı".
            return key.ToLowerInvariant();
        }

        private static int IndexOfSeparator(string path, int startIndex)
        {
            for (int i = startIndex < 0 ? 0 : startIndex; i < path.Length; i++)
            {
                char c = path[i];
                if (c == '/' || c == '\\')
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Deterministic output path for an atlas asset. Shared by generation, existence checks,
        /// and orphan sweeping so the path-assembly logic cannot drift.
        /// </summary>
        private static string BuildAtlasAssetPath(string outputFolder, string atlasKey)
        {
            return outputFolder + "/" + AtlasPathUtility.SanitizePart(atlasKey)
                   + ".spriteatlasv2";
        }

        private static void ProcessDirtyAtlases(
            int? maxCount = null,
            double? timeBudgetSeconds = null,
            ICollection<string> failures = null,
            bool force = false)
        {
            if (Index.DirtyCount == 0)
            {
                return;
            }

            AtlasPipelineSettings settings = _settingsCache;
            if (settings == null || !settings.AutoGenerateAtlases)
            {
                Index.ClearDirty();
                return;
            }

            EnsureAssetFolderExists(settings.NormalizedOutputAtlasFolder);

            // TakeDirtyKeys sorts before handing the keys over, so atlases are always written in
            // atlas-key order. HashSet iteration order is an implementation detail and must never
            // leak into generation order, or the same project would produce different results on
            // different machines.
            List<string> keys = DirtyKeyBuffer;
            Index.TakeDirtyKeys(keys);
            GeneratedAtlasPaths.Clear();
            DeletedAtlasPaths.Clear();
            PendingAtlasConfigure.Clear();
            CapacityOverflowAtlases.Clear();

            int processCount = keys.Count;
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
                    GenerateAtlas(keys[i], force);
                }
                catch (Exception exception)
                {
                    Index.MarkDirty(keys[i]);
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

            if (processed < keys.Count)
            {
                for (int i = processed; i < keys.Count; i++)
                {
                    Index.MarkDirty(keys[i]);
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

            // Buckets that lost every member are dropped so a long editor session does not
            // accumulate thousands of empty entries; their atlas files were deleted above.
            RemovedKeyBuffer.Clear();
            Index.RemoveEmptyBuckets(RemovedKeyBuffer);
            for (int i = 0; i < RemovedKeyBuffer.Count; i++)
            {
                GeneratedFingerprints.Remove(RemovedKeyBuffer[i]);
            }

            // The atlases on disk now correspond to the current configuration; the next settings
            // change diffs against this.
            CommitConfigurationSnapshot();

            LogAtlasChangesSummary();
        }

        /// <summary>
        /// Generates one atlas. The three early-outs are ordered cheapest-first: a content
        /// fingerprint that loads nothing, then a packable comparison that loads sprites but writes
        /// nothing, then the actual write.
        /// </summary>
        private static void GenerateAtlas(string atlasKey, bool force)
        {
            AtlasPipelineSettings settings = _settingsCache;
            if (settings == null
                || !Index.TryGetBucket(atlasKey, out AtlasBucket bucket))
            {
                return;
            }

            AtlasImportRule rule = ResolveAtlasRule(atlasKey);
            int atlasMaxSize = rule != null
                ? rule.AtlasMaxTextureSize
                : DefaultAtlasMaxTextureSize;
            int padding = settings.AtlasPadding < 0 ? 0 : settings.AtlasPadding;
            string outputPath = BuildAtlasAssetPath(
                settings.NormalizedOutputAtlasFolder,
                atlasKey);

            // Cheapest check first: ordered membership plus owning-rule configuration plus the
            // import state of every source asset. When nothing that feeds packing changed, the file
            // on disk is already correct and we return without loading a single sprite. This is what
            // keeps a settings-driven regeneration pass cheap on a project with tens of thousands of
            // images, where loading every sprite just to compare packables dominated the pass.
            long fingerprint = ComputeAtlasFingerprint(bucket, rule);
            if (!force
                && GeneratedFingerprints.TryGetValue(atlasKey, out long recorded)
                && recorded == fingerprint
                && File.Exists(ToAbsolutePath(outputPath)))
            {
                return;
            }

            // The ordered member list is cached on the bucket and only rebuilt when membership
            // changes, so the common incremental pass no longer pays for a copy plus a sort here.
            IReadOnlyList<string> orderedAssetPaths = bucket.GetOrdered();
            SpriteBuffer.Clear();
            long requiredArea = 0L;
            OversizedSpriteBuffer.Clear();
            SpriteNameSpelling.Clear();
            CaseVariantNameSamples.Clear();
            _caseVariantNameCount = 0;

            for (int i = 0; i < orderedAssetPaths.Count; i++)
            {
                string assetPath = orderedAssetPaths[i];
                if (!AtlasPathUtility.IsSupportedImagePath(assetPath))
                {
                    continue;
                }

                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                if (assets == null)
                {
                    continue;
                }

                SubSpriteBuffer.Clear();
                for (int a = 0; a < assets.Length; a++)
                {
                    if (assets[a] is Sprite sprite && sprite != null)
                    {
                        SubSpriteBuffer.Add(sprite);
                    }
                }

                // Sub-sprites of one sheet are ordered by name so a multi-sprite sheet contributes
                // its packables in a machine-independent order.
                SubSpriteBuffer.Sort(SpriteNameComparison);

                for (int s = 0; s < SubSpriteBuffer.Count; s++)
                {
                    Sprite sprite = SubSpriteBuffer[s];
                    SpriteBuffer.Add(sprite);

                    int width = Mathf.RoundToInt(sprite.rect.width);
                    int height = Mathf.RoundToInt(sprite.rect.height);
                    requiredArea += AtlasCapacityPlanner.ComputePaddedArea(
                        width,
                        height,
                        padding);

                    if (AtlasCapacityPlanner.IsSpriteTooLarge(
                            width,
                            height,
                            atlasMaxSize,
                            padding))
                    {
                        OversizedSpriteBuffer.Add(sprite.name);
                    }

                    RecordSpriteNameSpelling(sprite.name);
                }
            }

            // Two sprites whose names differ only by letter case ("Idle_0" and "idle_0") are legal
            // everywhere, and runtime lookup by name is case sensitive, so whichever one
            // GetSprite returns depends on packable order rather than on the name that was asked
            // for. Worth reporting: it looks like a random wrong sprite at runtime.
            if (_caseVariantNameCount > 0)
            {
                AtlasPipelineLog.Channel.Warning(
                    $"[CycloneGames Atlas Pipeline] Atlas '{atlasKey}' contains "
                    + $"{_caseVariantNameCount} sprite name(s) that differ from another name only by "
                    + "letter case (for example " + SummarizeKeys(CaseVariantNameSamples)
                    + "). Give them distinct names.");
            }

            // A sprite that cannot fit even into an empty atlas is dropped by Unity silently and
            // shows up as a white quad at runtime — one of the hardest failures to trace in a large
            // art set, so it is reported explicitly.
            if (OversizedSpriteBuffer.Count > 0)
            {
                LogOversizedSprites(atlasKey, atlasMaxSize, padding);
            }

            if (SpriteBuffer.Count == 0)
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(outputPath) != null)
                {
                    DeletedAtlasPaths.Add(outputPath);
                }

                GeneratedFingerprints[atlasKey] = fingerprint;
                return;
            }

            // Capacity budget: unlike the per-sprite check above, this catches the aggregate case
            // where every sprite fits individually but the atlas as a whole does not. Unity clamps an
            // atlas to a single texture of the configured max size and drops the overflow.
            AtlasCapacityReport capacity = AtlasCapacityPlanner.Evaluate(
                new AtlasCapacityRequest(
                    SpriteBuffer.Count,
                    requiredArea,
                    atlasMaxSize,
                    padding));
            if (capacity.RequiresSplitting)
            {
                CapacityOverflowAtlases.Add(atlasKey);
                AtlasPipelineLog.Channel.Warning(
                    $"[CycloneGames Atlas Pipeline] Atlas '{atlasKey}' needs about "
                    + $"{capacity.PageCount} pages at {atlasMaxSize}px "
                    + $"({capacity.RequiredArea}px of padded content against "
                    + $"{capacity.UsableAreaPerPage}px usable per page). A SpriteAtlas is packed "
                    + "into one texture, so Unity silently drops the overflow. Raise 'Atlas Max' on "
                    + "the owning rule or split the source folder with a finer granularity.");
            }

            SpriteAtlasAsset v2Asset = SpriteAtlasAsset.Load(outputPath);
            SpriteAtlas masterAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(outputPath);
            bool existed = v2Asset != null;
            if (existed
                && masterAtlas != null
                && AtlasPackablesMatch(masterAtlas)
                && AtlasConfigurationMatches(outputPath, atlasKey))
            {
                GeneratedFingerprints[atlasKey] = fingerprint;
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

            // The array is resized only when the count changes, so repeated passes over the same
            // atlas reuse one allocation. It is built here rather than earlier because only the
            // write path needs an array.
            if (_spriteArrayBuffer.Length != SpriteBuffer.Count)
            {
                _spriteArrayBuffer = new Sprite[SpriteBuffer.Count];
            }

            SpriteBuffer.CopyTo(_spriteArrayBuffer);
            v2Asset.Add(_spriteArrayBuffer);
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
            GeneratedFingerprints[atlasKey] = fingerprint;
        }

        /// <summary>
        /// Fingerprint of everything that feeds the packed result of one atlas: the ordered member
        /// list, the owning rule's configuration, and the import state of every source asset.
        /// The first two are pure and reproducible across machines. The dependency hashes come from
        /// Unity's import cache and are only ever compared within one editor session, which is all
        /// they are used for — the worst case for a wrong value is an extra regeneration, never a
        /// missed one.
        /// </summary>
        private static long ComputeAtlasFingerprint(AtlasBucket bucket, AtlasImportRule rule)
        {
            long content = bucket.ComputeContentHash(ComputeRuleFingerprint(rule));

            // XOR rather than a running hash: the set of sources is order-independent, so the value
            // must not change when only the member order changes. Order is already covered by
            // content, and mixing two order-sensitive hashes here would make the fingerprint depend
            // on it twice.
            long dependencies = AtlasHash.NullHash;
            IReadOnlyList<string> members = bucket.GetOrdered();
            for (int i = 0; i < members.Count; i++)
            {
                dependencies ^= AssetDatabase.GetAssetDependencyHash(members[i]).GetHashCode();
            }

            return AtlasHash.Combine64(content, dependencies);
        }

        private static void RecordSpriteNameSpelling(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
            {
                return;
            }

            if (SpriteNameSpelling.TryGetValue(spriteName, out string firstSpelling))
            {
                if (!string.Equals(firstSpelling, spriteName, StringComparison.Ordinal))
                {
                    _caseVariantNameCount++;
                    if (CaseVariantNameSamples.Count < MaxLoggedManifestKeys)
                    {
                        CaseVariantNameSamples.Add(firstSpelling + " / " + spriteName);
                    }
                }

                return;
            }

            SpriteNameSpelling.Add(spriteName, spriteName);
        }

        private static void LogOversizedSprites(string atlasKey, int atlasMaxSize, int padding)
        {
            OversizedSpriteBuffer.Sort(StringComparer.Ordinal);

            var builder = new StringBuilder();
            builder.Append("[CycloneGames Atlas Pipeline] ")
                .Append(OversizedSpriteBuffer.Count)
                .Append(" sprite(s) in atlas '")
                .Append(atlasKey)
                .Append("' exceed the usable ")
                .Append(atlasMaxSize)
                .Append("px limit (padding ")
                .Append(padding)
                .Append("px per side):");

            int logged = Math.Min(OversizedSpriteBuffer.Count, MaxLoggedOversizedSprites);
            for (int i = 0; i < logged; i++)
            {
                builder.AppendLine();
                builder.Append("  ").Append(OversizedSpriteBuffer[i]);
            }

            if (OversizedSpriteBuffer.Count > logged)
            {
                builder.AppendLine();
                builder.Append("  ... and ")
                    .Append(OversizedSpriteBuffer.Count - logged)
                    .Append(" more.");
            }

            builder.AppendLine();
            builder.Append(
                "Unity silently drops them from the packed atlas; shrink the source images or raise "
                + "'Atlas Max' on the owning rule.");

            AtlasPipelineLog.Channel.Warning(builder.ToString());
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

        /// <summary>
        /// Compares the atlas's current packables against the sprites in <see cref="SpriteBuffer"/>.
        /// Both sides are reduced to <see cref="AtlasSpriteIdentity"/> — source asset path plus sprite
        /// name — and compared as sorted lists.
        /// Identifying a packable by path plus name rather than by name alone is what keeps two
        /// identically named sub-sprites from different sheets ("idle_0" in two character sheets)
        /// from being mistaken for each other, which used to leave the atlas silently stale.
        /// The identity struct carries its own hashes and falls back to exact string comparison, so
        /// after the buffers warm up the whole comparison allocates nothing.
        /// </summary>
        /// <remarks>
        /// Reads <see cref="SpriteBuffer"/>: only valid while called from <see cref="GenerateAtlas"/>.
        /// </remarks>
        private static bool AtlasPackablesMatch(SpriteAtlas atlas)
        {
            UnityEngine.Object[] current = atlas.GetPackables();
            if (current == null)
            {
                return false;
            }

            CurrentIdentityBuffer.Clear();
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i] is Sprite sprite && sprite != null)
                {
                    CurrentIdentityBuffer.Add(BuildSpriteIdentity(sprite));
                }
            }

            if (CurrentIdentityBuffer.Count != SpriteBuffer.Count)
            {
                return false;
            }

            ExpectedIdentityBuffer.Clear();
            for (int i = 0; i < SpriteBuffer.Count; i++)
            {
                ExpectedIdentityBuffer.Add(BuildSpriteIdentity(SpriteBuffer[i]));
            }

            CurrentIdentityBuffer.Sort();
            ExpectedIdentityBuffer.Sort();
            for (int i = 0; i < CurrentIdentityBuffer.Count; i++)
            {
                if (CurrentIdentityBuffer[i] != ExpectedIdentityBuffer[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static AtlasSpriteIdentity BuildSpriteIdentity(Sprite sprite)
        {
            // GetAssetPath on a sub-sprite returns its main texture path, the stable identity we want.
            return new AtlasSpriteIdentity(
                AssetDatabase.GetAssetPath(sprite),
                sprite.name);
        }

        /// <summary>
        /// Converts an Assets/-relative path to an absolute path without going through the asset
        /// database, so the existence check in the fingerprint fast path stays cheap enough to run
        /// for every atlas on every pass.
        /// Returns null when the path is not under Assets/.
        /// </summary>
        private static string ToAbsolutePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)
                || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return null;
            }

            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                assetPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar)));
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
            if (!Index.TryGetBucket(atlasKey, out AtlasBucket bucket))
            {
                return null;
            }

            // Fast path: the owning rule was recorded when the members were indexed. Without it,
            // every atlas generation paid for a sorted copy of the whole member list plus a rule
            // match per member, just to answer "which rule configures this atlas".
            int ruleId = bucket.RuleId;
            if (ruleId >= 0 && ruleId < RuleCache.Count)
            {
                return RuleCache[ruleId];
            }

            // Cache miss: the first pass after a settings reload, or an atlas indexed before the
            // rule list was rebuilt. Recompute deterministically — sorted members, first resolvable
            // rule — and store the result. Determinism matters because the rule supplies the atlas
            // size, formats and rotation; two machines disagreeing here would produce different
            // atlases from identical input.
            AtlasImportRule resolved = null;
            IReadOnlyList<string> members = bucket.GetOrdered();
            for (int i = 0; i < members.Count && resolved == null; i++)
            {
                resolved = ResolveRule(members[i]);
            }

            bucket.RuleId = resolved != null ? resolved.PipelineIndex : -1;
            return resolved;
        }

        /// <summary>
        /// Resolves the rule that governs a source asset, or null when none does.
        /// This is the single entry point for "does the pipeline own this asset": everything that asks
        /// that question — indexing, import-setting application, rename scanning, atlas key resolution
        /// — must go through here, so a new exclusion rule only ever has to be added once.
        /// </summary>
        internal static AtlasImportRule ResolveRule(string assetPath)
        {
            string path = NormalizeAssetPath(assetPath);
            if (IsGloballyExcluded(path))
            {
                return null;
            }

            // Self-heal rather than return "no rule": callers such as the rename scan reach this
            // without going through EnsureInitialized, and an empty rule cache would make every asset
            // look unowned — silently, which is the worst way to fail.
            if (RuleCache.Count == 0 && _settingsCache != null)
            {
                RefreshRuleOrder();
            }

            for (int i = 0; i < RuleCache.Count; i++)
            {
                if (RuleCache[i].MatchesPath(path) && !RuleCache[i].IsPathExcluded(path))
                {
                    return RuleCache[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Folders the pipeline ignores outright, whatever the rules say: no atlas membership, no
        /// import settings, no rename prompts.
        /// The atlas output folder is always excluded here and is deliberately not part of the
        /// configurable list — the tool's own output must never be able to feed back into its input,
        /// and a setting someone can clear is not a guarantee.
        /// </summary>
        internal static bool IsGloballyExcluded(string normalizedAssetPath)
        {
            AtlasPipelineSettings settings = _settingsCache;
            if (settings == null)
            {
                return false;
            }

            return IsGloballyExcluded(
                normalizedAssetPath,
                settings.NormalizedOutputAtlasFolder,
                settings.GlobalExcludedFolderPaths);
        }

        /// <summary>
        /// Pure exclusion test. Split out from the settings-aware overload so the rule "what does the
        /// pipeline refuse to touch" can be unit tested without an asset database.
        /// </summary>
        internal static bool IsGloballyExcluded(
            string normalizedAssetPath,
            string outputFolder,
            IReadOnlyList<string> globalExcludes)
        {
            if (string.IsNullOrEmpty(normalizedAssetPath))
            {
                return true;
            }

            // The output folder is checked first and is not configurable: the tool's own output must
            // never be able to become its own input.
            outputFolder = AtlasPathUtility.NormalizeAndTrim(outputFolder);
            if (!string.IsNullOrEmpty(outputFolder)
                && AtlasPathUtility.IsUnderFolder(normalizedAssetPath, outputFolder))
            {
                return true;
            }

            if (globalExcludes == null)
            {
                return false;
            }

            for (int i = 0; i < globalExcludes.Count; i++)
            {
                string exclude = AtlasPathUtility.NormalizeAndTrim(globalExcludes[i]);
                if (!string.IsNullOrEmpty(exclude)
                    && AtlasPathUtility.IsUnderFolder(normalizedAssetPath, exclude))
                {
                    return true;
                }
            }

            return false;
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

            // Publish the new positions and drop the per-atlas rule shortcuts. Rule ids are indices
            // into RuleCache, so after a reorder the same index points at a different rule; resolving
            // the wrong one would write the wrong packing configuration into an atlas.
            for (int i = 0; i < RuleCache.Count; i++)
            {
                if (RuleCache[i] != null)
                {
                    RuleCache[i].PipelineIndex = i;
                }
            }

            Index.ResetRuleIds();
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
            return AtlasPathUtility.Normalize(assetPath);
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
