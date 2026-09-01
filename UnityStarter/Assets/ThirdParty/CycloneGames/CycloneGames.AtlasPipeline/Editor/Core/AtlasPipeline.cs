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
        /// Atlas count above which a PerSprite rule is reported. PerSprite is correct for a handful
        /// of large images, so the threshold sits well above that and well below the point where a
        /// folder of icons looks like a mistake — which at ten thousand sprites it is: one asset and
        /// one texture per sprite.
        /// </summary>
        private const int PerSpriteAtlasCountWarningThreshold = 64;

        /// <summary>
        /// Platforms capacity is evaluated for, in reporting order. Each can cap the atlas at a
        /// different size, so each has to be checked separately.
        /// </summary>
        private static readonly AtlasPlatform[] CapacityPlatforms =
        {
            AtlasPlatform.Android,
            AtlasPlatform.Iphone,
            AtlasPlatform.Webgl,
            AtlasPlatform.Standalone,
        };

        /// <summary>
        /// Per-platform max sizes for the atlas currently being generated. Reused so the page planner
        /// does not allocate an array per atlas.
        /// </summary>
        private static readonly int[] PlatformMaxSizeBuffer = new int[CapacityPlatforms.Length];

        /// <summary>
        /// Page count each atlas is believed to have on disk, keyed by atlas key. The manifest is
        /// built from the index alone, which cannot know page counts without loading sprites, so
        /// generation records them here and everything that reasons about pages — manifest writing,
        /// the existence check, the orphan sweep — reads them back.
        /// </summary>
        /// <remarks>
        /// This used to be a per-pass dictionary that was cleared on every batch, which silently
        /// corrupted the manifest: an incremental pass regenerates only the atlases that changed, so
        /// every paged atlas it did not touch fell back to one page in the manifest — pointing at a
        /// base file that does not exist. Time slicing made it worse, because a later batch cleared
        /// the counts an earlier batch had just recorded. The map is now persistent for the session
        /// and seeded from the committed manifest, so an atlas nobody touched keeps its true page
        /// count.
        /// </remarks>
        private static readonly Dictionary<string, int> KnownPageCounts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Atlases written during the current generation pass. Page-count warnings are scoped to
        /// this set: warning about every paged atlas in the project on every pass would be noise,
        /// while warning only about the ones just regenerated says "this one grew" when it grew.
        /// </summary>
        private static readonly HashSet<string> RegeneratedThisPass =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Atlases whose fingerprint matched and were left untouched during the current pass. On a
        /// build agent this is the observable proof that the incremental pass actually worked: a
        /// cold build with an up-to-date manifest should report everything skipped and nothing
        /// regenerated, and anything else points at the fingerprints or the manifest.
        /// </summary>
        private static int _skippedThisPass;

        /// <summary>
        /// Whether <see cref="KnownPageCounts"/> has been seeded from the committed manifest in this
        /// domain. Once per domain, not per pass: the seed fills only keys generation has not
        /// already set, so re-seeding could never add anything and would only cost a file read.
        /// </summary>
        private static bool _knownPageCountsSeeded;

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
        /// Portable source fingerprints for the current generation pass, keyed by asset path, so a
        /// source that feeds several atlases is read from disk once. Cleared at the start of every
        /// pass: an edit made between two passes must never be served from a previous reading.
        /// </summary>
        private static readonly Dictionary<string, long> SourceFileHashes =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The committed manifest, read at most once per session, and only when a cold start actually
        /// needs it. Null until read — including when the file simply does not exist.
        /// </summary>
        private static AtlasManifest _recordedManifest;
        private static bool _recordedManifestRead;

        /// <summary>
        /// Rule fingerprints the atlases currently on disk were generated with, keyed by AtlasGroup
        /// (globally unique across rules, enforced by build validation). Compared against the live
        /// rules when settings change, so editing one rule dirties only the atlases that rule owns.
        /// </summary>
        private static readonly Dictionary<string, int> GeneratedRuleFingerprints =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private static int _generatedGlobalFingerprint;

        /// <summary>
        /// Source assets each rule claimed during indexing, keyed by rule. A rule that ends at zero
        /// is dead weight: it validates, it shows in the window, and it manages nothing — because its
        /// folder is empty, everything in it is excluded, or an earlier rule (same folder, or the
        /// whole subtree carved up by deeper rules) claims every sprite. Surfaced by validation,
        /// because nothing else in the pipeline says so.
        /// </summary>
        private static readonly Dictionary<AtlasImportRule, int> MatchedAssetsPerRule =
            new Dictionary<AtlasImportRule, int>();

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
                MigrateLegacyInlineRules();
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

        /// <summary>
        /// Audits the rule asset lifecycle: every settings slot resolved against every rule asset on
        /// disk. Findings are classified by severity at the call site — a missing or duplicate
        /// reference blocks the build because the rule's folder silently stops being managed, while
        /// an orphaned asset only warns because unregistering is supposed to leave the file alone.
        /// </summary>
        public static IReadOnlyList<AtlasRuleAuditEntry> AuditRules()
        {
            EnsureSettingsAsset();
            AtlasPipelineSettings settings = _settingsCache;

            // One guid per slot, in list order: this is the resolution order, so a duplicate's first
            // slot wins and every later one is the problem.
            var registered = new List<string>();
            if (settings != null)
            {
                IReadOnlyList<AtlasRuleAsset> assets = settings.RuleAssets;
                for (int i = 0; i < assets.Count; i++)
                {
                    AtlasRuleAsset asset = assets[i];
                    if (asset == null)
                    {
                        registered.Add(string.Empty);
                        continue;
                    }

                    string path = AssetDatabase.GetAssetPath(asset);
                    registered.Add(string.IsNullOrEmpty(path)
                        ? string.Empty
                        : AssetDatabase.AssetPathToGUID(path));
                }
            }

            // Rules may live anywhere under Assets, so the disk side scans the whole tree rather
            // than the default rules folder. An orphan outside the folder is exactly the one nobody
            // is looking for.
            var onDisk = new List<KeyValuePair<string, string>>();
            string[] guids = AssetDatabase.FindAssets("t:AtlasRuleAsset", new[] { "Assets" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(path))
                {
                    onDisk.Add(new KeyValuePair<string, string>(guids[i], path));
                }
            }

            return AtlasRuleAuditor.Audit(registered, onDisk);
        }

        /// <summary>
        /// Deletes every rule asset the audit classifies as orphaned — on disk, referenced by no
        /// settings slot. Asks for explicit confirmation with the full path list, because "the list
        /// no longer mentions this file" is a decision for a human: the file may be work in
        /// progress, and unregistering was designed to keep it.
        /// </summary>
        /// <remarks>
        /// Re-audits immediately before deleting rather than trusting the caller's findings: the
        /// gap between an audit and this call is exactly where someone registers the asset back.
        /// Deleting a rule asset that has become registered again would destroy live configuration.
        /// </remarks>
        /// <returns>The number of assets deleted.</returns>
        public static int DeleteOrphanRuleAssets()
        {
            EnsureSettingsAsset();

            IReadOnlyList<AtlasRuleAuditEntry> findings = AuditRules();
            var orphans = new List<AtlasRuleAuditEntry>();
            for (int i = 0; i < findings.Count; i++)
            {
                if (findings[i].Kind == AtlasRuleAuditKind.OrphanAsset)
                {
                    orphans.Add(findings[i]);
                }
            }

            if (orphans.Count == 0)
            {
                AtlasPipelineLog.Channel.Info(
                    "[CycloneGames Atlas Pipeline] No unregistered rule assets to delete.");
                return 0;
            }

            var listing = new StringBuilder();
            for (int i = 0; i < orphans.Count; i++)
            {
                listing.Append('\n').Append(orphans[i].AssetPath);
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Delete Unregistered Rule Assets",
                orphans.Count + " rule asset(s) exist on disk but are not registered in the "
                + "atlas settings. They have no effect on generation:"
                + listing
                + "\n\nDelete them? Unregistering a rule never deletes its file, so this "
                + "confirmation is the only thing between a removed list entry and a lost rule.",
                "Delete",
                "Cancel");

            if (!confirmed)
            {
                return 0;
            }

            int deleted = 0;
            for (int i = 0; i < orphans.Count; i++)
            {
                string path = orphans[i].AssetPath;

                // Re-checked against the live audit rather than the confirmed list: registration
                // state may have changed between the dialog and here.
                IReadOnlyList<AtlasRuleAuditEntry> current = AuditRules();
                bool stillOrphaned = false;
                for (int j = 0; j < current.Count; j++)
                {
                    if (current[j].Kind == AtlasRuleAuditKind.OrphanAsset
                        && string.Equals(
                            current[j].AssetPath,
                            path,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        stillOrphaned = true;
                        break;
                    }
                }

                if (!stillOrphaned
                    || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
                {
                    continue;
                }

                if (AssetDatabase.DeleteAsset(path))
                {
                    deleted++;
                    AtlasPipelineLog.Channel.Info(
                        "[CycloneGames Atlas Pipeline] Deleted unregistered rule asset '" + path + "'.");
                }
            }

            AtlasPipelineLog.Channel.Info(
                "[CycloneGames Atlas Pipeline] Deleted " + deleted + " of " + orphans.Count
                + " unregistered rule asset(s).");

            // No cache invalidation needed: orphaned assets are not in the index, the rule cache or
            // the manifest, and the deletions raise projectChanged, which the domain-level
            // subscription turns into a rescan anyway.
            return deleted;
        }

        [MenuItem("Assets/CycloneGames Atlas Pipeline/Delete Unregistered Rule Assets")]
        private static void DeleteOrphanRuleAssetsMenu()
        {
            DeleteOrphanRuleAssets();
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

            // Domain level, not window level. This used to be subscribed by the pipeline window,
            // which meant every external change — a git pull adding art, a rule edited in another
            // editor — went unnoticed while the window was closed: no rescan, no dirty marking, no
            // generation, and a manifest that silently described the previous project. The window
            // still subscribes for its own UI caches, which is all it ever needed to own.
            EditorApplication.projectChanged -= HandleProjectChanged;
            EditorApplication.projectChanged += HandleProjectChanged;
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

            // An empty dirty set here means the whole queue was processed and nothing failed — a
            // failed atlas is re-marked dirty by the pass, and a truncated one re-queues the rest.
            // Record that, because the manifest is the only committed description of the atlases.
            // Left at its previous state, it silently goes stale after every automatic incremental
            // pass, and the CI drift gate then fails builds for developers who did regenerate: it
            // cannot tell "never regenerated" from "regenerated, manifest never refreshed".
            if (Index.DirtyCount == 0)
            {
                WriteManifest();
            }
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

        /// <summary>
        /// Default home for rule assets. Rules can live anywhere under Assets/ and be referenced from
        /// the settings asset; this is only where the migration and the "+" button put them.
        /// </summary>
        public const string DefaultRuleFolder = "Assets/Settings/AtlasRules";

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
                MigrateLegacyInlineRules();
                RefreshRuleOrder();
                return;
            }

            EnsureAssetFolderExists("Assets/Settings");
            _settingsCache = AtlasPipelineSettings.CreateDefault();
            AssetDatabase.CreateAsset(_settingsCache, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            MigrateLegacyInlineRules();
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

            // Page counts describe the atlases on disk, which this call no longer vouches for:
            // keeping them could protect stale pages from the sweep. They re-seed from the
            // committed manifest on the next access.
            KnownPageCounts.Clear();
            RegeneratedThisPass.Clear();
            _knownPageCountsSeeded = false;
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
            AppendFnv(ref hash, rule.GetAtlasMaxTextureSize(AtlasPlatform.Android));
            AppendFnv(ref hash, rule.GetAtlasMaxTextureSize(AtlasPlatform.Iphone));
            AppendFnv(ref hash, rule.GetAtlasMaxTextureSize(AtlasPlatform.Webgl));
            AppendFnv(ref hash, rule.GetAtlasMaxTextureSize(AtlasPlatform.Standalone));
            AppendFnv(ref hash, (int)rule.IncludeInBuildOverride);
            AppendFnv(ref hash, (int)rule.AlphaDilationOverride);
            AppendFnv(ref hash, (int)rule.FilterMode);
            AppendFnv(ref hash, (int)rule.WrapMode);
            AppendFnv(ref hash, (int)rule.AtlasRotationMode);
            AppendFnv(ref hash, (int)rule.AtlasGranularity);
            AppendFnv(ref hash, (int)rule.SpriteMode);
            AtlasHash.AppendFnv1a(ref hash, rule.AtlasGroup);

            // Part of the identity because it decides the output path. Left out, renaming a rule's
            // subfolder would leave the atlas in the old folder, never write the new one, and fail
            // the post-generation existence check with a message about a path nobody asked for.
            AtlasHash.AppendFnv1a(ref hash, rule.OutputSubfolder);
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
            AppendFnv(ref hash, settings.EnableAlphaDilation ? 1 : 0);
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
        /// manifests. Source pixel content is deliberately excluded from the CONTENT hash: repainting
        /// a texture does not change which packables an atlas holds, so it does not make the atlas
        /// structurally stale. It is covered separately by the source hashes below, which answer a
        /// different question — "may a cold start skip regenerating this" — and therefore must
        /// include it.
        /// </summary>
        /// <param name="includeSourceHashes">
        /// Whether to read source files and record their fingerprints. Drift checking answers a
        /// purely structural question and must stay cheap on CI, so it passes false.
        /// </param>
        private static AtlasManifest BuildManifest(bool includeSourceHashes = true)
        {
            string outputFolder = _settingsCache != null
                ? _settingsCache.NormalizedOutputAtlasFolder
                : string.Empty;

            var entries = new List<AtlasManifestEntry>(Index.BucketCount);
            var sourceHashes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<AtlasBucket> buckets = Index.GetBuckets();
            for (int i = 0; i < buckets.Count; i++)
            {
                AtlasBucket bucket = buckets[i];
                if (bucket.Count == 0)
                {
                    continue;
                }

                AtlasImportRule rule = ResolveAtlasRule(bucket.Key);
                long contentHash = bucket.ComputeContentHash(
                    ComputeRuleFingerprint(rule), ComputeGlobalFingerprint());

                // Keyed by atlas key, not page key: the generator needs this before it can know the
                // page count, and the page count is only knowable by loading the sprite rects this
                // fingerprint exists to avoid loading.
                if (includeSourceHashes
                    && TryComputeSourceHash(bucket, out long sourceHash))
                {
                    sourceHashes[bucket.Key] = sourceHash;
                }

                // The index alone cannot know a page count — that needs sprite rects — so generation
                // records what it produced and the manifest reads it back. Atlases this pass did not
                // touch keep the count they are known to have on disk: falling back to one page here
                // would record an atlas that spans several files as a single file at a path that
                // does not exist.
                int pageCount = GetKnownPageCount(bucket.Key);

                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    string pageKey = AtlasCapacityPlanner.BuildPageKey(
                        bucket.Key,
                        pageIndex,
                        pageCount);
                    entries.Add(new AtlasManifestEntry(
                        pageKey,
                        BuildAtlasAssetPath(outputFolder, rule.OutputSubfolder, pageKey),
                        bucket.Count,
                        contentHash,
                        pageCount,
                        bucket.RuleId));
                }
            }

            return new AtlasManifest(
                AtlasManifest.CurrentSchemaVersion,
                ManifestGeneratorVersion,
                AtlasHash.Combine64(
                    ComputeGlobalFingerprint(),
                    ComputeRuleSetFingerprint()),
                entries,
                sourceHashes);
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

            // Batched because this is the tool editing its own file. Writing a text asset under
            // Assets/ raises projectChanged, and an unbatched write would pay a full rescan for
            // every manifest update — including the automatic ones after an incremental pass.
            BeginBatchedAssetEditing();
            try
            {
                // LF only and no BOM. The file is committed, so a CRLF or BOM difference between a
                // Windows developer machine and a Linux CI agent would show up as a whole-file diff
                // on every change and turn every merge into a conflict.
                File.WriteAllText(
                    absolute,
                    AtlasManifestSerializer.Write(BuildManifest()),
                    new UTF8Encoding(false));
                AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                EndBatchedAssetEditing();
            }
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

            // Only a NEWER manifest is rejected: it may use fields this version cannot interpret, so
            // it must not be trusted. An older one is still readable — the deserializer is
            // field-count tolerant, and all that is missing is the source fingerprint, which just
            // disables the cold-start skip until that atlas is next generated. Rejecting older
            // manifests here would force a full regeneration on every project that upgrades, which
            // is the exact cost this fingerprint exists to remove.
            if (recorded.SchemaVersion > AtlasManifest.CurrentSchemaVersion)
            {
                drift.Add(
                    $"Atlas manifest schema {recorded.SchemaVersion} is newer than the supported "
                    + $"version {AtlasManifest.CurrentSchemaVersion}. Update the CycloneGames atlas "
                    + "pipeline, or regenerate the atlases and commit the new manifest.");
                return drift;
            }

            // Structural comparison only: drift asks "are the committed atlases stale", which the
            // content hash answers without touching a single source file. Paying for source reads
            // here would make the cheap CI check as expensive as the generation it replaces.
            AtlasManifestDelta delta =
                AtlasManifestComparer.Compare(recorded, BuildManifest(includeSourceHashes: false));
            if (delta.IsUpToDate)
            {
                return drift;
            }

            // Each category names what actually happened, not just that something is out of date.
            // A CI log is often the only thing a developer sees, and "atlas manifest is stale" sends
            // them looking in the wrong place.
            if (delta.Added.Count > 0)
            {
                drift.Add(
                    $"{delta.Added.Count} atlas(es) the project needs are missing from the "
                    + $"manifest: {SummarizeKeys(delta.Added)}. "
                    + "Caused by a new rule, or a new folder under an existing one. Until they are "
                    + "generated these sprites ship as individual textures, unbatched.");
            }

            if (delta.Removed.Count > 0)
            {
                drift.Add(
                    $"{delta.Removed.Count} atlas(es) the manifest records no longer exist: "
                    + $"{SummarizeKeys(delta.Removed)}. Their rule or source folder was removed "
                    + "or renamed, and the files they produced are now orphans.");
            }

            if (delta.Changed.Count > 0)
            {
                drift.Add(
                    $"{delta.Changed.Count} atlas(es) changed since the manifest was written: "
                    + $"{SummarizeKeys(delta.Changed)}. Members were added or removed, or the "
                    + "governing rule was reconfigured — so the atlases on disk hold the previous "
                    + "member list and any new sprite is missing from them.");
            }

            drift.Add(
                "Regenerate the atlases (Regenerate Atlases in the pipeline window, or the "
                + "cyclonegames-atlas-pipeline build step) and commit the updated manifest.");
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

        /// <summary>
        /// Validates everything the pipeline can check without generating anything.
        /// </summary>
        /// <param name="includeNameScan">Also scan for source file names that need renaming.</param>
        /// <param name="warnings">
        /// Receives costly-but-legitimate choices, such as an uncompressed mobile atlas. These are
        /// reported to the user and never block the build: a pixel-art rule must be allowed to ship
        /// RGBA 32 even though it is expensive.
        /// </param>
        public static IReadOnlyList<string> ValidateForBuild(
            bool includeNameScan = false,
            ICollection<string> warnings = null)
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

                AtlasPlatformFormats.ValidateRule(rule, errors, warnings);
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

            // Folder relationship matrix. Every rule has a source folder and an effective output
            // folder (the shared root, or the root plus its subfolder); the settings also carry
            // global excludes, and the output root is always excluded on top of those. The checks
            // below are ordered most-specific first so each conflict produces exactly one error —
            // the precise one, not a generic fallback.
            //
            // A rule sourcing from ANOTHER rule's output folder is the nastiest case: the generated
            // atlases are re-read as sources, and the sweep — which treats everything the index does
            // not expect as garbage — will delete them on the next full pass. It is also the case a
            // check scoped to "each rule against its own output" cannot see, which is why the check
            // is cross-rule.
            string outputRoot = _settingsCache.NormalizedOutputAtlasFolder;
            var effectiveOutputs = new string[RuleCache.Count];
            for (int i = 0; i < RuleCache.Count; i++)
            {
                effectiveOutputs[i] = RuleCache[i].OutputSubfolder.Length == 0
                    ? outputRoot
                    : outputRoot + "/" + RuleCache[i].OutputSubfolder;
            }

            for (int i = 0; i < RuleCache.Count; i++)
            {
                string sourceFolder = RuleCache[i].NormalizedSourceFolder;
                if (string.IsNullOrEmpty(sourceFolder))
                {
                    continue;
                }

                // Most specific culprit wins: when several output folders overlap this source, the
                // deepest one is where the loop actually closes.
                int culprit = -1;
                for (int j = 0; j < RuleCache.Count; j++)
                {
                    if (!AtlasPathUtility.PathsOverlap(effectiveOutputs[j], sourceFolder))
                    {
                        continue;
                    }

                    if (culprit < 0
                        || effectiveOutputs[j].Length > effectiveOutputs[culprit].Length)
                    {
                        culprit = j;
                    }
                }

                if (culprit >= 0)
                {
                    if (culprit == i)
                    {
                        errors.Add(
                            $"Import rule '{RuleCache[i].Name}' writes its atlases into "
                            + $"'{effectiveOutputs[culprit]}', which is also its own source folder "
                            + $"('{sourceFolder}'). Every source image inside the output folder is "
                            + "treated as an intrusion and one confirmation would move the entire "
                            + "art folder into quarantine. Choose a disjoint output folder, or give "
                            + "this rule a different output subfolder.");
                    }
                    else
                    {
                        errors.Add(
                            $"Import rule '{RuleCache[culprit].Name}' writes its atlases into "
                            + $"'{effectiveOutputs[culprit]}', which is also the source folder of "
                            + $"rule '{RuleCache[i].Name}'. The generated atlases would be read back "
                            + "as sources, and the sweep would delete them as unexpected output — a "
                            + "feedback loop between the two rules. Give one of them a different "
                            + "folder.");
                    }

                    continue;
                }

                // No rule writes where this rule reads, but the folder can still sit inside the
                // output tree, which the pipeline ignores entirely — so the rule would manage
                // nothing, silently.
                if (AtlasPathUtility.PathsOverlap(outputRoot, sourceFolder))
                {
                    errors.Add(
                        $"Import rule '{RuleCache[i].Name}' source folder '{sourceFolder}' is "
                        + $"inside the output tree '{outputRoot}'. Everything under the output tree "
                        + "is ignored by the pipeline, so this rule matches no assets at all. Move "
                        + "the source folder outside the output tree.");
                }
            }

            // Nested output folders. Equality is deliberately allowed — two rules writing into the
            // same folder is the intended "two rules, one package" case — but strict nesting means
            // a collector targeting the outer folder ships the inner rule's atlases, so the two
            // rules cannot be updated as separate packages. Warned, not blocked: the nesting may be
            // intentional.
            for (int i = 0; i < RuleCache.Count; i++)
            {
                for (int j = i + 1; j < RuleCache.Count; j++)
                {
                    if (AtlasPathUtility.IsProperlyUnderFolder(
                            effectiveOutputs[i], effectiveOutputs[j]))
                    {
                        warnings?.Add(
                            $"Rule '{RuleCache[i].Name}' writes into '{effectiveOutputs[i]}', "
                            + $"which is inside the output folder of rule '{RuleCache[j].Name}' "
                            + $"('{effectiveOutputs[j]}'). A collector targeting the outer folder "
                            + "ships the inner rule's atlases too, so the two cannot be updated as "
                            + "separate packages. Keep output folders disjoint unless the nesting "
                            + "is intentional.");
                    }
                    else if (AtlasPathUtility.IsProperlyUnderFolder(
                                 effectiveOutputs[j], effectiveOutputs[i]))
                    {
                        warnings?.Add(
                            $"Rule '{RuleCache[j].Name}' writes into '{effectiveOutputs[j]}', "
                            + $"which is inside the output folder of rule '{RuleCache[i].Name}' "
                            + $"('{effectiveOutputs[i]}'). A collector targeting the outer folder "
                            + "ships the inner rule's atlases too, so the two cannot be updated as "
                            + "separate packages. Keep output folders disjoint unless the nesting "
                            + "is intentional.");
                    }
                }
            }

            // A rule that matched nothing is dead weight: it validates, it shows in the window, and
            // it manages no assets. Same-folder rules are legal — keyword partitions are the point —
            // but a partition that leaves one side with nothing is a configuration mistake, and so
            // is a folder whose entire subtree is carved up by deeper rules. Warned rather than
            // blocked, because an empty folder is a normal state while art is being organised.
            for (int i = 0; i < RuleCache.Count; i++)
            {
                AtlasImportRule rule = RuleCache[i];
                if (rule == null
                    || string.IsNullOrEmpty(rule.NormalizedSourceFolder)
                    || (MatchedAssetsPerRule.TryGetValue(rule, out int matched) && matched > 0))
                {
                    continue;
                }

                var claimants = new List<string>();
                for (int j = 0; j < RuleCache.Count; j++)
                {
                    if (j == i || RuleCache[j] == null)
                    {
                        continue;
                    }

                    if (AtlasPathUtility.PathsOverlap(
                            RuleCache[j].NormalizedSourceFolder, rule.NormalizedSourceFolder))
                    {
                        claimants.Add(RuleCache[j].Name);
                    }
                }

                claimants.Sort(StringComparer.Ordinal);
                string claimantsText = claimants.Count > 0
                    ? " Rules whose source folders overlap it: " + string.Join(", ", claimants) + "."
                    : string.Empty;

                warnings?.Add(
                    $"Import rule '{rule.Name}' matched no source assets in "
                    + $"'{rule.NormalizedSourceFolder}'. Either the folder is empty, its assets are "
                    + "excluded, or another rule claims them first — resolution order is longest "
                    + "source folder first, then fewest keywords, then list order."
                    + claimantsText
                    + " The rule has no effect until one of those changes.");
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

            // PerSprite makes one atlas per sprite, so the atlas count equals the sprite count. That
            // is the right shape for a handful of large images — a splash screen, a set of full-page
            // backgrounds — and the wrong shape for a folder of icons, where it produces one asset
            // to import, index and ship per sprite and no two of them ever batch.
            // Reported rather than blocked, because the small case is legitimate. But at this scale
            // the rule is misconfigured, and nothing else in the pipeline says so.
            var perSpriteAtlases = new Dictionary<AtlasImportRule, int>();
            for (int i = 0; i < RuleCache.Count; i++)
            {
                if (RuleCache[i].AtlasGranularity == AtlasGranularity.PerSprite)
                {
                    perSpriteAtlases[RuleCache[i]] = 0;
                }
            }

            if (perSpriteAtlases.Count > 0)
            {
                IReadOnlyList<AtlasBucket> buckets = Index.GetBuckets();
                for (int i = 0; i < buckets.Count; i++)
                {
                    AtlasImportRule owner = ResolveAtlasRule(buckets[i].Key);
                    if (owner != null
                        && perSpriteAtlases.TryGetValue(owner, out int atlasCount))
                    {
                        perSpriteAtlases[owner] = atlasCount + 1;
                    }
                }

                // Sorted: dictionary order would make the same project log a different warning order
                // on every machine, which is noise in a CI diff.
                var noisy = new List<string>(perSpriteAtlases.Count);
                foreach (KeyValuePair<AtlasImportRule, int> pair in perSpriteAtlases)
                {
                    if (pair.Value > PerSpriteAtlasCountWarningThreshold)
                    {
                        noisy.Add(pair.Key.Name + " (" + pair.Value + " atlas(es))");
                    }
                }

                noisy.Sort(StringComparer.Ordinal);
                for (int i = 0; i < noisy.Count; i++)
                {
                    warnings?.Add(
                        $"Import rule '{noisy[i]}' uses PerSprite granularity, which builds one "
                        + "atlas per sprite. Each atlas is its own asset and its own texture, so "
                        + "sprites in different atlases never batch. PerSprite suits a few large "
                        + "images; for a set this size switch the rule to PerChildFolder or "
                        + "PerSourceFolder so the sprites share an atlas.");
                }
            }

            // Rule asset lifecycle. A missing reference is the dangerous one: the rule's folder
            // silently stops being managed — no atlases, no import settings — and the atlases it
            // used to own become orphans the sweep deletes. Blocking the build turns a silent
            // config problem into a loud one before it can eat art.
            IReadOnlyList<AtlasRuleAuditEntry> ruleAudit = AuditRules();
            for (int i = 0; i < ruleAudit.Count; i++)
            {
                AtlasRuleAuditEntry entry = ruleAudit[i];
                if (entry.Kind == AtlasRuleAuditKind.OrphanAsset)
                {
                    warnings?.Add(AtlasRuleAuditor.Describe(
                        new[] { entry }));
                }
                else
                {
                    errors.Add(AtlasRuleAuditor.Describe(new[] { entry }));
                }
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

                // Which rules actually need "AOT" treatment is otherwise guesswork: it depends on
                // what the installer-baked scenes and Resources content reference. That question is
                // answerable mechanically, so answer it: report rules whose sprites are referenced
                // by baked content while their Include In Build resolves off.
                if (warnings != null)
                {
                    ClassifyBakedSpriteConflicts(
                        CollectInstallerBakedSpriteSourcePaths(),
                        RuleCache,
                        _settingsCache != null && _settingsCache.IncludeInBuild,
                        warnings);
                }
            }

            return errors;
        }

        /// <summary>
        /// Source-asset paths of every sprite referenced by installer-baked content: scenes in the
        /// build settings plus every prefab and scene inside a Resources folder (Resources content is
        /// always fully baked into the player, whatever the bundle setup). This is the mechanical
        /// definition of "AOT-referenced" for atlas purposes.
        /// Loads each root once via CollectDependencies, so the cost is bounded by the number of
        /// build scenes and Resources prefabs — comparable to the rename scan that runs beside it.
        /// </summary>
        internal static List<string> CollectInstallerBakedSpriteSourcePaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void CollectFromRoots(IEnumerable<string> rootPaths)
            {
                foreach (string rootPath in rootPaths)
                {
                    if (string.IsNullOrEmpty(rootPath))
                    {
                        continue;
                    }

                    UnityEngine.Object root = AssetDatabase.LoadMainAssetAtPath(rootPath);
                    if (root == null)
                    {
                        continue;
                    }

                    UnityEngine.Object[] dependencies =
                        EditorUtility.CollectDependencies(new[] { root });
                    if (dependencies == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < dependencies.Length; i++)
                    {
                        if (dependencies[i] is Sprite sprite && sprite != null)
                        {
                            paths.Add(AssetDatabase.GetAssetPath(sprite));
                        }
                    }
                }
            }

            var scenePaths = new List<string>();
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes != null)
            {
                for (int i = 0; i < scenes.Length; i++)
                {
                    if (scenes[i] != null && scenes[i].enabled)
                    {
                        scenePaths.Add(scenes[i].path);
                    }
                }
            }

            CollectFromRoots(scenePaths);

            // Resources folders can be nested anywhere ("Assets/X/Resources"). Every prefab and
            // scene inside one is baked and can hold sprite references.
            var resourceRoots = new List<string>();
            string[] allPaths = AssetDatabase.GetAllAssetPaths();
            for (int i = 0; i < allPaths.Length; i++)
            {
                string path = allPaths[i];
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    int marker = path.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
                    if (marker >= 0)
                    {
                        resourceRoots.Add(path);
                    }
                }
            }

            CollectFromRoots(resourceRoots);
            return new List<string>(paths);
        }

        /// <summary>
        /// Pure classification: which rules have sprites referenced by installer-baked content while
        /// their Include In Build resolves off. Split from the AssetDatabase scan so the policy —
        /// what counts as a conflict and what the message says — is unit-testable.
        /// </summary>
        /// <param name="bakedSpriteSourcePaths">
        /// Source-asset paths of sprites referenced by baked content, from
        /// <see cref="CollectInstallerBakedSpriteSourcePaths"/>.
        /// </param>
        /// <param name="orderedRules">Rules in resolution order (RuleCache).</param>
        /// <param name="globalIncludeInBuild">The global Include In Build setting.</param>
        /// <param name="warnings">Receives one entry per affected rule.</param>
        internal static void ClassifyBakedSpriteConflicts(
            IReadOnlyList<string> bakedSpriteSourcePaths,
            IReadOnlyList<AtlasImportRule> orderedRules,
            bool globalIncludeInBuild,
            ICollection<string> warnings)
        {
            if (bakedSpriteSourcePaths == null
                || bakedSpriteSourcePaths.Count == 0
                || orderedRules == null
                || orderedRules.Count == 0
                || warnings == null)
            {
                return;
            }

            var countsByRule = new Dictionary<AtlasImportRule, int>();
            for (int i = 0; i < bakedSpriteSourcePaths.Count; i++)
            {
                string path = AtlasPathUtility.Normalize(bakedSpriteSourcePaths[i]);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                // Same resolution order as ResolveRule: first rule in the ordered cache that
                // matches and does not exclude the path owns it.
                for (int r = 0; r < orderedRules.Count; r++)
                {
                    AtlasImportRule rule = orderedRules[r];
                    if (rule == null || !rule.OwnsPath(path))
                    {
                        continue;
                    }

                    if (rule.AtlasGranularity != AtlasGranularity.None)
                    {
                        countsByRule.TryGetValue(rule, out int count);
                        countsByRule[rule] = count + 1;
                    }

                    break;
                }
            }

            foreach (KeyValuePair<AtlasImportRule, int> pair in countsByRule)
            {
                AtlasImportRule rule = pair.Key;
                if (rule.ResolveIncludeInBuild(globalIncludeInBuild))
                {
                    // The rule bakes its atlas: baked references resolve through the atlas, which
                    // is exactly what Force On is for. Not a conflict.
                    continue;
                }

                warnings.Add(
                    $"Import rule '{rule.Name}' resolves Include In Build to Off, but "
                    + $"{pair.Value} of its sprite source asset(s) are referenced by installer-"
                    + "baked content (build scenes or Resources). Those sprites ship as individual "
                    + "textures in the installer — nothing goes missing, but they lose the atlas's "
                    + "draw-call savings there. If that art is part of the bootstrap (loading "
                    + "screen, first scene), set the rule's Include In Build override to On.");
            }
        }

        public static List<AtlasRenameRequest> CollectInvalidAtlasNames()
        {
            // ResolveRule is passed in as a delegate: the rename scan lives in AtlasNaming, and a
            // direct call back from there to this class would form a circular static dependency
            // (CG0048). The delegate is the pipeline's own resolution entry point, self-heal
            // included, so the scan and the pipeline can never disagree about rule ownership.
            return AtlasNaming.CollectInvalidAtlasNames(TryGetSettings(), ResolveRule);
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

            // Advisory findings ride along with the blocking ones. They do not fail the build, but
            // an orphaned rule asset or a rule that matched nothing is exactly the kind of thing a
            // CI log should surface — dropping the warnings collection here made the build path
            // blind to everything that was not fatal.
            var validationWarnings = new List<string>();
            IReadOnlyList<string> errors =
                ValidateForBuild(includeNameScan: true, warnings: validationWarnings);
            for (int i = 0; i < validationWarnings.Count; i++)
            {
                AtlasPipelineLog.Channel.Warning(validationWarnings[i]);
            }

            if (errors.Count > 0)
            {
                string message = string.Join(Environment.NewLine, errors);
                if (throwOnError)
                {
                    throw new UnityEditor.Build.BuildFailedException(
                        $"CycloneGames atlas pipeline validation failed:{Environment.NewLine}{message}");
                }

                AtlasPipelineLog.Channel.Error(
                    "[CycloneGames Atlas Pipeline] " + message);
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

            // Give the memory back before the player build starts. A generation pass that touched
            // most of the project has pulled every one of those source textures into memory, and the
            // build is about to need that memory for its own work.
            ReleaseGenerationMemory();

            // Only record the manifest once everything succeeded. A partial pass would commit
            // fingerprints for atlases that were never written, which is the one thing the manifest
            // must never claim.
            bool manifestWritten = failures.Count == 0;
            if (manifestWritten)
            {
                WriteManifest();
            }

            // One greppable line for the CI log. The skipped count is the observable proof that the
            // incremental pass worked: on a build agent whose manifest is up to date it should read
            // "everything skipped, nothing regenerated" — any regeneration there means the manifest
            // was behind, and zero skips with a fresh manifest would point at the fingerprints.
            AtlasPipelineLog.Channel.Info(
                "[CycloneGames Atlas Pipeline] Build summary: "
                + $"{RegeneratedThisPass.Count} regenerated, "
                + $"{_skippedThisPass} skipped (unchanged), "
                + $"{DeletedAtlasPaths.Count} file(s) deleted, "
                + $"{validationWarnings.Count} warning(s), "
                + $"{failures.Count} failure(s), manifest "
                + (manifestWritten ? "written." : "NOT written because generation failed."));

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

                AtlasPipelineLog.Channel.Error(
                    "[CycloneGames Atlas Pipeline] " + message);
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
            ReleaseGenerationMemory();

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
                // Every remedy is named, including the one-word one. The in-editor warning already
                // mentioned paging; this message did not, so a build log — often the only thing a
                // developer sees on CI — sent them to the two slowest fixes and omitted the toggle
                // that resolves it in one click.
                failures?.Add(
                    $"Atlas '{CapacityOverflowAtlases[i]}' does not fit its configured max texture "
                    + "size: Unity packs one texture per atlas and silently drops the sprites that "
                    + "do not fit, so they would ship as missing. Either raise 'Atlas Max' on the "
                    + "owning rule, split the source folder with a finer atlas granularity, or turn "
                    + "on 'Auto Page Overflowing Atlas' (currently off) to let it span several page "
                    + "files.");
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

                // Every page is checked, not just the base or the first one. A missing middle page
                // is exactly as fatal at runtime as a missing atlas — the sprites on it render as
                // white quads — and it is the one failure a "base or first page" check is blind to.
                int pageCount = GetKnownPageCount(bucket.Key);
                for (int page = 0; page < pageCount; page++)
                {
                    string pageKey = AtlasCapacityPlanner.BuildPageKey(
                        bucket.Key,
                        page,
                        pageCount);
                    string pagePath = BuildAtlasAssetPath(folder, pageKey);
                    if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(pagePath) != null)
                    {
                        continue;
                    }

                    failures?.Add(
                        $"Expected atlas page '{pagePath}' (atlas '{bucket.Key}', page {page} of "
                        + $"{pageCount}, {bucket.Count} sprite(s)) was not generated. Anything "
                        + "loading that page at runtime will show missing sprites. If the atlas on "
                        + "disk is a different shape than the manifest describes, regenerate with "
                        + "force (the Regenerate Atlases action bypasses fingerprints).");
                    break;
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

            // Compared as paths relative to the output root, so the subfolder is part of the
            // identity rather than something reconstructed per file. Two atlases with the same key
            // in different packages are different files, and a file whose rule moved to a new
            // subfolder is correctly an orphan.
            // The expected set is the FULL page set, not just the base name: an atlas that shrank
            // from five pages to two leaves three files behind, and every one of them strips back to
            // the base key of an atlas that still exists. Without the page count those leftovers are
            // indistinguishable from required pages, which is why they survived every earlier sweep.
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<AtlasBucket> buckets = Index.GetBuckets();
            for (int i = 0; i < buckets.Count; i++)
            {
                string atlasKey = buckets[i].Key;
                int pageCount = GetKnownPageCount(atlasKey);
                for (int page = 0; page < pageCount; page++)
                {
                    expected.Add(RelativeAtlasPath(
                        AtlasCapacityPlanner.BuildPageKey(atlasKey, page, pageCount)));
                }
            }

            // Enumerate the filesystem directly instead of using FindAssets: it avoids search-type
            // differences between atlas V1/V2 and saves an asset-database index query.
            // Recursive because rules may write into subfolders.
            string fullPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", folder))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(fullPath))
            {
                return;
            }

            string[] files = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories);
            int removedOrphans = 0;
            int removedStalePages = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                if (!file.EndsWith(".spriteatlasv2", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relative = ToOutputRelativePath(fullPath, file);
                if (expected.Contains(relative))
                {
                    continue;
                }

                string assetPath = folder + "/" + relative;
                AssetDatabase.DeleteAsset(assetPath);

                // Distinguished only for the log: a stale page says "this atlas shrank", an orphan
                // says "this atlas is gone". Same action, different follow-up for the reader.
                string stem = AtlasPathUtility.GetFileNameWithoutExtension(relative);
                bool isStalePage = AtlasCapacityPlanner.TryGetPageIndex(stem, out string baseKey, out _)
                                   && Index.TryGetBucket(baseKey, out _);
                if (isStalePage)
                {
                    removedStalePages++;
                    AtlasPipelineLog.Channel.Info(
                        $"[CycloneGames Atlas Pipeline] Removed stale page '{assetPath}' — the "
                        + "atlas it belongs to now produces fewer pages.");
                }
                else
                {
                    removedOrphans++;
                    AtlasPipelineLog.Channel.Info(
                        $"[CycloneGames Atlas Pipeline] Removed orphan atlas '{assetPath}'.");
                }
            }

            if (removedOrphans > 0 || removedStalePages > 0)
            {
                AtlasPipelineLog.Channel.Info(
                    $"[CycloneGames Atlas Pipeline] Output sweep removed {removedOrphans} orphan "
                    + $"atlas(es) and {removedStalePages} stale page(s).");
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

            // Counts describe the membership this rebuild produces; the rebuild re-derives all of
            // it, so last rebuild's numbers are stale by definition.
            MatchedAssetsPerRule.Clear();

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

            MatchedAssetsPerRule.TryGetValue(rule, out int matched);
            MatchedAssetsPerRule[rule] = matched + 1;

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
        /// Every overload funnels through the subfolder-aware form, which is the only one that
        /// actually assembles a path.
        /// </summary>
        private static string BuildAtlasAssetPath(string outputFolder, string atlasKey)
        {
            return BuildAtlasAssetPath(outputFolder, ResolveOutputSubfolder(atlasKey), atlasKey);
        }

        /// <param name="fileKey">
        /// Atlas key for a single-page atlas, page key for a page. The subfolder always comes from
        /// the rule that owns the atlas, never from the page key — a page key resolves no bucket.
        /// </param>
        private static string BuildAtlasAssetPath(
            string outputFolder,
            string subfolder,
            string fileKey)
        {
            string directory = string.IsNullOrEmpty(subfolder)
                ? outputFolder
                : outputFolder + "/" + subfolder;
            return directory + "/" + AtlasPathUtility.SanitizePart(fileKey)
                   + ".spriteatlasv2";
        }

        /// <summary>
        /// The output subfolder an atlas is written into, resolved from the rule that owns it.
        /// Empty when the rule is unknown, which places the atlas in the output root — the safe
        /// default, and the pre-existing behaviour.
        /// </summary>
        private static string ResolveOutputSubfolder(string atlasKey)
        {
            return ResolveAtlasRule(atlasKey)?.OutputSubfolder ?? string.Empty;
        }

        /// <summary>
        /// An atlas's file path expressed relative to the output root, for example
        /// "Battle/ui.spriteatlasv2". Subfolder included, so two packages holding atlases of the
        /// same key are distinguishable.
        /// </summary>
        private static string RelativeAtlasPath(string atlasKey)
        {
            string subfolder = ResolveOutputSubfolder(atlasKey);
            string fileName = AtlasPathUtility.SanitizePart(atlasKey) + ".spriteatlasv2";
            return subfolder.Length == 0 ? fileName : subfolder + "/" + fileName;
        }

        /// <summary>
        /// Path of a file inside the output tree, relative to the output root, with separators
        /// normalized to '/'.
        /// </summary>
        private static string ToOutputRelativePath(string outputRootFullPath, string fileFullPath)
        {
            string relative = fileFullPath.Length > outputRootFullPath.Length
                ? fileFullPath.Substring(outputRootFullPath.Length)
                : Path.GetFileName(fileFullPath);
            return relative
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
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

            // A pass is the unit of trust for anything read off disk: source bytes may have changed
            // since the previous one, and a fingerprint that is merely old looks exactly like one
            // that is current.
            ResetSourceCaches();

            // Before any atlas is generated: shrinking a paged atlas needs to know how many pages it
            // had, and that comes from the committed manifest when this session did not produce it.
            EnsureKnownPageCountsSeeded();

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
            RegeneratedThisPass.Clear();
            _skippedThisPass = 0;

            int processCount = keys.Count;
            if (maxCount.HasValue && processCount > maxCount.Value)
            {
                processCount = maxCount.Value;
            }

            // After the key list is known and before the first write: every package folder this pass
            // needs, created with a single refresh.
            EnsureOutputDirectories(keys, processCount);

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
                KnownPageCounts.Remove(RemovedKeyBuffer[i]);
            }

            // The atlases on disk now correspond to the current configuration; the next settings
            // change diffs against this.
            CommitConfigurationSnapshot();

            LogAtlasChangesSummary();
            WarnOnExcessivePaging();
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

            // Every member is gone: the atlas must not exist. Without this, deleting the last sprite
            // in a folder regenerated the atlas as an empty asset — zero packables — that a
            // path-based collector then ships as a texture nobody uses. The bucket itself is dropped
            // at the end of the pass, so queueing the files here keeps disk and index in step.
            if (bucket.Count == 0)
            {
                ScheduleAtlasRemoval(atlasKey);
                KnownPageCounts.Remove(atlasKey);
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
            // A paged atlas has no base file, so the first page's existence stands in for it; the
            // page count is derived from the same inputs the fingerprint covers, so a count change
            // always comes with a fingerprint change.
            string firstPagePath = BuildAtlasAssetPath(
                settings.NormalizedOutputAtlasFolder,
                AtlasCapacityPlanner.BuildPageKey(atlasKey, 0, 2));
            long fingerprint = ComputeAtlasFingerprint(bucket, rule);
            bool outputExists = File.Exists(ToAbsolutePath(outputPath))
                                || File.Exists(ToAbsolutePath(firstPagePath));

            if (!force
                && outputExists
                && GeneratedFingerprints.TryGetValue(atlasKey, out long recorded)
                && recorded == fingerprint)
            {
                _skippedThisPass++;
                return;
            }

            // Cold start: the session table is empty, which is every CI build and every freshly
            // opened editor. Without this branch the pass falls through and decodes every sprite in
            // the project purely to discover that nothing changed — the single most expensive thing
            // this pipeline does at scale.
            // The manifest is the fallback authority. Its fingerprints are portable, so comparing
            // against it is as valid on a build agent as on the machine that wrote it. This costs
            // reading the source bytes, which is roughly two orders of magnitude cheaper than
            // importing them as textures, and it is the only check that can tell a repainted source
            // from an untouched one without decoding it.
            if (!force
                && outputExists
                && TryGetRecordedSourceHash(atlasKey, out long recordedSource)
                && TryComputeSourceHash(bucket, out long currentSource))
            {
                long persisted = ComputePortableFingerprint(bucket, rule, recordedSource);
                long current = ComputePortableFingerprint(bucket, rule, currentSource);
                if (persisted == current)
                {
                    // Promote to the session table so the next pass hits the cheaper branch.
                    GeneratedFingerprints[atlasKey] = fingerprint;
                    _skippedThisPass++;
                    return;
                }
            }

            // The ordered member list is cached on the bucket and only rebuilt when membership
            // changes, so the common incremental pass no longer pays for a copy plus a sort here.
            IReadOnlyList<string> orderedAssetPaths = bucket.GetOrdered();
            SpriteBuffer.Clear();
            long requiredArea = 0L;
            int maxSpriteWidth = 0;
            int maxSpriteHeight = 0;
            OversizedSpriteBuffer.Clear();

            // The most permissive platform decides whether a sprite can ever ship. Anything larger
            // than this cannot be packed anywhere, so it is an authoring error worth naming.
            int largestPlatformMaxSize = DefaultAtlasMaxTextureSize;
            for (int p = 0; p < CapacityPlatforms.Length; p++)
            {
                int platformMax = LargestPlatformMaxSize(rule, CapacityPlatforms[p]);
                if (platformMax > largestPlatformMaxSize)
                {
                    largestPlatformMaxSize = platformMax;
                }
            }
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

                    if (width > maxSpriteWidth)
                    {
                        maxSpriteWidth = width;
                    }

                    if (height > maxSpriteHeight)
                    {
                        maxSpriteHeight = height;
                    }

                    if (AtlasCapacityPlanner.IsSpriteTooLarge(
                            width,
                            height,
                            largestPlatformMaxSize,
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

            // Capacity is checked per platform, because each can cap the atlas at a different size.
            // An atlas that fits on iOS at 2048px can overflow an Android build capped at 1024px, and
            // Unity drops the overflow on the smaller platform only — a failure that is nearly
            // impossible to pin down from device reports alone.
            //
            // A sprite too large for even an empty atlas cannot be paged around, so it stays a
            // failure whatever the paging setting. The aggregate case is what paging solves.
            bool paging = settings.AutoPageOverflowingAtlases;
            int pageCount = 1;
            for (int p = 0; p < CapacityPlatforms.Length; p++)
            {
                AtlasPlatform platform = CapacityPlatforms[p];
                int platformMaxSize = LargestPlatformMaxSize(rule, platform);
                PlatformMaxSizeBuffer[p] = platformMaxSize;
                string platformName = AtlasPlatformFormats.GetPlatformName(platform);

                if (AtlasCapacityPlanner.IsSpriteTooLarge(
                        maxSpriteWidth,
                        maxSpriteHeight,
                        platformMaxSize,
                        padding))
                {
                    ReportCapacityOverflow(atlasKey);
                    AtlasPipelineLog.Channel.Warning(
                        $"[CycloneGames Atlas Pipeline] Atlas '{atlasKey}' cannot be packed for "
                        + $"{platformName}: its largest sprite is {maxSpriteWidth}x{maxSpriteHeight} "
                        + $"and {platformMaxSize}px minus {padding}px padding per side is not "
                        + $"enough. Raise the {platformName} atlas size on the owning rule, or "
                        + "shrink the source image. Paging cannot help: the sprite does not fit even "
                        + "an empty atlas.");
                    continue;
                }

                AtlasCapacityReport capacity = AtlasCapacityPlanner.Evaluate(
                    new AtlasCapacityRequest(
                        SpriteBuffer.Count,
                        requiredArea,
                        platformMaxSize,
                        padding));
                if (!capacity.RequiresSplitting)
                {
                    continue;
                }

                if (paging)
                {
                    // Pages are shared across platforms — the same packable list has to produce the
                    // same page files everywhere, or the output would not be reproducible — so the
                    // page count is the worst case over platforms.
                    if (capacity.PageCount > pageCount)
                    {
                        pageCount = capacity.PageCount;
                    }

                    continue;
                }

                ReportCapacityOverflow(atlasKey);
                AtlasPipelineLog.Channel.Warning(
                    $"[CycloneGames Atlas Pipeline] Atlas '{atlasKey}' needs about "
                    + $"{capacity.PageCount} pages at {platformMaxSize}px on {platformName} "
                    + $"({capacity.RequiredArea}px of padded content against "
                    + $"{capacity.UsableAreaPerPage}px usable per page). A SpriteAtlas is packed "
                    + "into one texture, so Unity silently drops the overflow. Split the source "
                    + "folder with a finer granularity, raise the atlas size, or enable automatic "
                    + "paging in the atlas pipeline settings.");
            }

            if (pageCount > 1)
            {
                AtlasPipelineLog.Channel.Info(
                    $"[CycloneGames Atlas Pipeline] Atlas '{atlasKey}' does not fit a single page "
                    + $"and is split into {pageCount} pages. Members are sliced from the sorted "
                    + "member list, so alphabetical neighbours stay together and adding one sprite "
                    + "moves roughly one member per page boundary.");
            }

            // Recorded before the writes: the count is what the pages just produced describe, and
            // everything downstream — the manifest, the existence check, the orphan sweep — reads it
            // from here. Recording only multi-page atlases was the bug: an atlas that shrank back to
            // one page never updated its count, so it stayed paged in every consumer's view.
            KnownPageCounts.TryGetValue(atlasKey, out int previousPageCount);
            KnownPageCounts[atlasKey] = pageCount;
            RegeneratedThisPass.Add(atlasKey);

            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                string pageKey = AtlasCapacityPlanner.BuildPageKey(
                    atlasKey,
                    pageIndex,
                    pageCount);
                string pageOutputPath = BuildAtlasAssetPath(
                    settings.NormalizedOutputAtlasFolder,
                    rule?.OutputSubfolder ?? string.Empty,
                    pageKey);
                long pageFingerprint = pageCount > 1
                    ? AtlasHash.Combine64(fingerprint, pageIndex)
                    : fingerprint;

                AtlasCapacityPlanner.AssignPageRange(
                    SpriteBuffer.Count,
                    pageCount,
                    pageIndex,
                    out int pageStart,
                    out int pageSpriteCount);

                WriteAtlasPage(
                    atlasKey,
                    pageOutputPath,
                    pageKey,
                    pageFingerprint,
                    pageStart,
                    pageSpriteCount);
            }

            // Pages the new count no longer produces. The orphan sweep cannot catch these on its
            // own: a page file strips back to the base key of an atlas that still exists, so the
            // sweep needs the page count to tell a required page from a leftover — which is exactly
            // the number this atlas had before this regeneration.
            ScheduleStalePageDeletes(atlasKey, previousPageCount, pageCount);

            // An atlas that just started paging leaves a single-page file behind; without removing it
            // the stale file would ship alongside the pages and the orphan sweep would only catch it
            // at the next full pass.
            if (pageCount > 1
                && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(outputPath) != null)
            {
                DeletedAtlasPaths.Add(outputPath);
            }
        }

        /// <summary>
        /// Writes one page (or the whole atlas, when it is not paged). The packables are the slice
        /// <paramref name="spriteStart"/> .. <paramref name="spriteStart"/>+<paramref name="spriteCount"/>
        /// of <see cref="SpriteBuffer"/>, which the caller filled with the atlas's sprites in
        /// machine-independent order.
        /// </summary>
        private static void WriteAtlasPage(
            string atlasKey,
            string outputPath,
            string fingerprintKey,
            long pageFingerprint,
            int spriteStart,
            int spriteCount)
        {
            SpriteAtlasAsset v2Asset = SpriteAtlasAsset.Load(outputPath);
            SpriteAtlas masterAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(outputPath);
            if (v2Asset != null
                && masterAtlas != null
                && AtlasPackablesMatch(masterAtlas, spriteStart, spriteCount)
                && AtlasConfigurationMatches(outputPath, atlasKey))
            {
                GeneratedFingerprints[fingerprintKey] = pageFingerprint;
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
            if (_spriteArrayBuffer.Length != spriteCount)
            {
                _spriteArrayBuffer = new Sprite[spriteCount];
            }

            for (int i = 0; i < spriteCount; i++)
            {
                _spriteArrayBuffer[i] = SpriteBuffer[spriteStart + i];
            }

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
            GeneratedFingerprints[fingerprintKey] = pageFingerprint;
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
            long content = bucket.ComputeContentHash(
                ComputeRuleFingerprint(rule), ComputeGlobalFingerprint());

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

        /// <summary>
        /// The portable half of an atlas's identity: the structural hash the manifest already stores,
        /// combined with a hash of the source bytes behind it. Every input is reproducible on any
        /// machine from a clean checkout, which is what makes it safe to persist and compare — unlike
        /// <see cref="ComputeAtlasFingerprint"/>, whose dependency component reflects the local
        /// import cache and therefore dies with the session.
        /// This is the value the manifest records and the value a cold start compares against.
        /// </summary>
        private static long ComputePortableFingerprint(
            AtlasBucket bucket,
            AtlasImportRule rule,
            long sourceHash)
        {
            return AtlasHash.Combine64(
                bucket.ComputeContentHash(
                    ComputeRuleFingerprint(rule), ComputeGlobalFingerprint()),
                sourceHash);
        }

        /// <summary>
        /// Hash of the source bytes behind an atlas: one fingerprint per member, folded with XOR.
        /// Returns false when any member could not be fingerprinted, in which case
        /// <paramref name="sourceHash"/> must not be used to license a skip.
        /// </summary>
        /// <remarks>
        /// XOR because the member set is unordered here — order is already carried by the content
        /// hash, and folding two order-sensitive hashes into one value would make the result depend
        /// on it twice. A single unresolved member makes the whole atlas unresolvable: an unknown
        /// fingerprint folded in with XOR would simply vanish, silently vouching for an atlas that
        /// contains a file nobody could read.
        /// </remarks>
        private static bool TryComputeSourceHash(AtlasBucket bucket, out long sourceHash)
        {
            sourceHash = AtlasHash.NullHash;
            IReadOnlyList<string> members = bucket.GetOrdered();
            if (members.Count == 0)
            {
                return false;
            }

            long folded = AtlasHash.NullHash;
            for (int i = 0; i < members.Count; i++)
            {
                string member = members[i];
                if (!SourceFileHashes.TryGetValue(member, out long fileHash))
                {
                    fileHash = AtlasSourceHash.Compute(ToAbsolutePath(member));
                    SourceFileHashes[member] = fileHash;
                }

                if (fileHash == AtlasHash.NullHash)
                {
                    sourceHash = AtlasHash.NullHash;
                    return false;
                }

                folded ^= fileHash;
            }

            sourceHash = folded;
            return true;
        }

        /// <summary>
        /// The source fingerprint recorded for an atlas in the committed manifest, or false when the
        /// manifest cannot vouch for it — absent, written by a different generator, or predating
        /// source fingerprints entirely. Every one of those cases must regenerate rather than skip.
        /// </summary>
        private static bool TryGetRecordedSourceHash(string atlasKey, out long sourceHash)
        {
            sourceHash = AtlasHash.NullHash;
            AtlasManifest recorded = GetRecordedManifest();
            if (recorded == null)
            {
                return false;
            }

            // A manifest produced by different generator code describes output this version would
            // not reproduce, so its fingerprints are meaningless here even if the numbers match.
            if (!string.Equals(
                    recorded.GeneratorVersion,
                    ManifestGeneratorVersion,
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (!recorded.SourceHashes.TryGetValue(atlasKey, out long recordedHash)
                || recordedHash == AtlasHash.NullHash)
            {
                return false;
            }

            sourceHash = recordedHash;
            return true;
        }

        /// <summary>
        /// Drops everything derived from files on disk, so the next pass re-reads them. Called at the
        /// start of each generation pass and whenever the index is rebuilt: a cached fingerprint from
        /// an earlier pass is indistinguishable from one that is simply stale.
        /// </summary>
        private static void ResetSourceCaches()
        {
            SourceFileHashes.Clear();
            _recordedManifest = null;
            _recordedManifestRead = false;
        }

        /// <summary>
        /// The committed manifest, read at most once between resets. Shared by the cold-start skip
        /// and the page-count seed, which need the same file for the same reason: both describe what
        /// the last complete generation produced.
        /// </summary>
        private static AtlasManifest GetRecordedManifest()
        {
            if (!_recordedManifestRead)
            {
                _recordedManifestRead = true;
                _recordedManifest = ReadManifest();
            }

            return _recordedManifest;
        }

        /// <summary>
        /// Loads page counts for atlases this session has not generated, from the committed
        /// manifest. Runs once per domain and fills only keys generation has not already set, so it
        /// can never override what this session actually produced.
        /// </summary>
        /// <remarks>
        /// Skipped when the manifest was written by a different generator: its page counts describe
        /// output this version would not reproduce, and trusting them could protect pages that
        /// should have been deleted.
        /// </remarks>
        private static void EnsureKnownPageCountsSeeded()
        {
            if (_knownPageCountsSeeded)
            {
                return;
            }

            _knownPageCountsSeeded = true;

            AtlasManifest recorded = GetRecordedManifest();
            if (recorded == null
                || !string.Equals(
                    recorded.GeneratorVersion,
                    ManifestGeneratorVersion,
                    StringComparison.Ordinal))
            {
                return;
            }

            IList<AtlasManifestEntry> entries = recorded.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                AtlasManifestEntry entry = entries[i];
                if (entry.PageCount <= 1)
                {
                    continue;
                }

                // Entries are keyed by page key; the atlas key is what is left over. A base entry
                // (single page) strips to itself and is skipped above, so only real pages land here.
                string atlasKey = AtlasCapacityPlanner.StripPageSuffix(entry.AtlasKey);
                if (!KnownPageCounts.TryGetValue(atlasKey, out int known)
                    || entry.PageCount > known)
                {
                    KnownPageCounts[atlasKey] = entry.PageCount;
                }
            }
        }

        /// <summary>
        /// Page count an atlas is believed to have on disk. One page unless generation or the
        /// committed manifest says otherwise — one is the safe default, since it matches the
        /// pre-paging output an untracked atlas would have.
        /// </summary>
        private static int GetKnownPageCount(string atlasKey)
        {
            EnsureKnownPageCountsSeeded();
            return KnownPageCounts.TryGetValue(atlasKey, out int pageCount) && pageCount > 1
                ? pageCount
                : 1;
        }

        /// <summary>
        /// Queues deletion of pages a regeneration made surplus. Going from five pages to two leaves
        /// two, three and four behind; going back to a single page leaves every page behind, including
        /// page zero, which is why the single-page case deletes the whole range rather than everything
        /// at or above the new count.
        /// </summary>
        /// <remarks>
        /// Deleting rather than leaving them to the orphan sweep is deliberate: the sweep strips a
        /// page file back to the base key of an atlas that still exists, so without a page count it
        /// cannot tell a leftover from a page that is still required. Generation is the one place
        /// that knows both numbers.
        /// </remarks>
        private static void ScheduleStalePageDeletes(
            string atlasKey,
            int previousPageCount,
            int newPageCount)
        {
            if (previousPageCount <= newPageCount)
            {
                return;
            }

            for (int pageIndex = 0; pageIndex < previousPageCount; pageIndex++)
            {
                if (newPageCount > 1 && pageIndex < newPageCount)
                {
                    continue;
                }

                string stalePageKey = AtlasCapacityPlanner.BuildPageKey(
                    atlasKey,
                    pageIndex,
                    previousPageCount);
                DeletedAtlasPaths.Add(BuildAtlasAssetPath(
                    _settingsCache.NormalizedOutputAtlasFolder,
                    ResolveOutputSubfolder(atlasKey),
                    stalePageKey));
            }
        }

        /// <summary>
        /// Queues deletion of every file an atlas owns: the base file and, when it was paged, each
        /// page. Used when the atlas's last member is removed, so an emptied folder stops producing
        /// an empty asset.
        /// </summary>
        /// <remarks>
        /// Queueing the base file unconditionally is safe even for a paged atlas — the deletion pass
        /// checks existence first, and a paged atlas must not have a base file anyway. The page range
        /// comes from the known page count; when that is unknown the base file is still covered, and
        /// the sweep is the backstop for anything unaccounted for.
        /// </remarks>
        private static void ScheduleAtlasRemoval(string atlasKey)
        {
            string subfolder = ResolveOutputSubfolder(atlasKey);
            DeletedAtlasPaths.Add(BuildAtlasAssetPath(
                _settingsCache.NormalizedOutputAtlasFolder,
                subfolder,
                atlasKey));

            if (KnownPageCounts.TryGetValue(atlasKey, out int pageCount) && pageCount > 1)
            {
                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    DeletedAtlasPaths.Add(BuildAtlasAssetPath(
                        _settingsCache.NormalizedOutputAtlasFolder,
                        subfolder,
                        AtlasCapacityPlanner.BuildPageKey(atlasKey, pageIndex, pageCount)));
                }
            }
        }

        /// <summary>
        /// Gives back the memory a generation pass borrowed. Loading a sprite to read its rect leaves
        /// its texture resident, and nothing in this pipeline ever released them, so one pass over a
        /// large project held every source texture until the domain reloaded — gigabytes at ten
        /// thousand sprites, and on a build agent it is memory the player build needs for itself.
        /// Deliberately not called from the editor's time-sliced pass: that runs several times a
        /// second, and a synchronous unload that often would cost far more than it saves. Full,
        /// unbounded passes only.
        /// </summary>
        private static void ReleaseGenerationMemory()
        {
            EditorUtility.UnloadUnusedAssetsImmediate();
        }

        private static int LargestPlatformMaxSize(AtlasImportRule rule, AtlasPlatform platform)
        {
            return rule != null
                ? rule.GetAtlasMaxTextureSize(platform)
                : DefaultAtlasMaxTextureSize;
        }

        /// <summary>
        /// Records a capacity overflow once per atlas, even when several platforms overflow.
        /// </summary>
        private static void ReportCapacityOverflow(string atlasKey)
        {
            if (!CapacityOverflowAtlases.Contains(atlasKey))
            {
                CapacityOverflowAtlases.Add(atlasKey);
            }
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

        /// <summary>
        /// Page counts above this are a symptom, not a solution. Paging keeps an oversized folder
        /// from failing the build, which is the point — but an atlas spanning many pages means the
        /// bucket really should have been split by rule: every page is its own texture, so a sprite
        /// that moves between pages changes which file has to be loaded.
        /// </summary>
        private const int ExcessivePageCountThreshold = 4;

        private static void WarnOnExcessivePaging()
        {
            if (RegeneratedThisPass.Count == 0)
            {
                return;
            }

            var excessive = new List<string>(RegeneratedThisPass.Count);
            foreach (string atlasKey in RegeneratedThisPass)
            {
                int pageCount = KnownPageCounts.TryGetValue(atlasKey, out int pages) ? pages : 1;
                if (pageCount > ExcessivePageCountThreshold)
                {
                    excessive.Add(atlasKey + " (" + pageCount + " pages)");
                }
            }

            if (excessive.Count == 0)
            {
                return;
            }

            excessive.Sort(StringComparer.Ordinal);
            AtlasPipelineLog.Channel.Warning(
                "[CycloneGames Atlas Pipeline] " + excessive.Count
                + " atlas(es) needed more than " + ExcessivePageCountThreshold
                + " pages: " + string.Join(", ", excessive)
                + ". Paging keeps these from failing the build, but each page is a separate "
                + "texture, so an atlas this large should be split by folder structure instead — "
                + "add a rule with a finer granularity, or raise the atlas size.");
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
            FilterMode filterMode = rule?.FilterMode ?? FilterMode.Bilinear;

            importer.includeInBuild = ResolveIncludeInBuild(settings, rule);
            importer.packingSettings = CreatePackingSettings(settings, rule);
            importer.textureSettings = CreateTextureSettings(filterMode);

            // Per platform, not one shared value: a low-end Android build that caps its atlas at
            // 1024px costs nothing in package size, which is exactly why this is the first lever to
            // reach for before considering a second resolution.
            importer.SetPlatformSettings(
                ResolvePlatformSettings(rule, AtlasPlatform.Android));
            importer.SetPlatformSettings(
                ResolvePlatformSettings(rule, AtlasPlatform.Iphone));
            importer.SetPlatformSettings(
                ResolvePlatformSettings(rule, AtlasPlatform.Webgl));
            importer.SetPlatformSettings(
                ResolvePlatformSettings(rule, AtlasPlatform.Standalone));
        }

        /// <summary>
        /// Compares the atlas's current packables against the sprite slice
        /// <paramref name="spriteStart"/>..<paramref name="spriteStart"/>+<paramref name="spriteCount"/>
        /// of <see cref="SpriteBuffer"/>.
        /// Both sides are reduced to <see cref="AtlasSpriteIdentity"/> — source asset path plus sprite
        /// name — and compared as sorted lists.
        /// Identifying a packable by path plus name rather than by name alone is what keeps two
        /// identically named sub-sprites from different sheets ("idle_0" in two character sheets)
        /// from being mistaken for each other, which used to leave the atlas silently stale.
        /// The identity struct carries its own hashes and falls back to exact string comparison, so
        /// after the buffers warm up the whole comparison allocates nothing.
        /// </summary>
        /// <remarks>
        /// Reads <see cref="SpriteBuffer"/>: only valid while called from the generation path.
        /// </remarks>
        private static bool AtlasPackablesMatch(
            SpriteAtlas atlas,
            int spriteStart,
            int spriteCount)
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

            if (CurrentIdentityBuffer.Count != spriteCount)
            {
                return false;
            }

            ExpectedIdentityBuffer.Clear();
            for (int i = 0; i < spriteCount; i++)
            {
                ExpectedIdentityBuffer.Add(BuildSpriteIdentity(SpriteBuffer[spriteStart + i]));
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
            FilterMode filterMode = rule?.FilterMode ?? FilterMode.Bilinear;

            // Every value here must come from the same helper the writer uses. If the two drifted,
            // the comparison would report "configuration changed" forever and every atlas would be
            // rewritten on every single pass.
            if (importer.includeInBuild != ResolveIncludeInBuild(settings, rule)
                || !PackingSettingsEqual(
                    importer.packingSettings,
                    CreatePackingSettings(settings, rule))
                || !TextureSettingsEqual(
                    importer.textureSettings,
                    CreateTextureSettings(filterMode))
                || !PlatformSettingsEqual(
                    importer.GetPlatformSettings(AtlasPlatformFormats.AndroidPlatformName),
                    ResolvePlatformSettings(rule, AtlasPlatform.Android))
                || !PlatformSettingsEqual(
                    importer.GetPlatformSettings(AtlasPlatformFormats.IphonePlatformName),
                    ResolvePlatformSettings(rule, AtlasPlatform.Iphone))
                || !PlatformSettingsEqual(
                    importer.GetPlatformSettings(AtlasPlatformFormats.WebglPlatformName),
                    ResolvePlatformSettings(rule, AtlasPlatform.Webgl))
                || !PlatformSettingsEqual(
                    importer.GetPlatformSettings(AtlasPlatformFormats.StandalonePlatformName),
                    ResolvePlatformSettings(rule, AtlasPlatform.Standalone)))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// The complete platform override for a rule: format, quality and atlas size. Used by both the
        /// writer and the "has the configuration changed" comparison, so they cannot disagree.
        /// </summary>
        private static TextureImporterPlatformSettings ResolvePlatformSettings(
            AtlasImportRule rule,
            AtlasPlatform platform)
        {
            return CreatePlatformSettings(
                platform,
                GetEffectiveFormat(rule, platform),
                Mathf.Clamp(
                    rule?.CompressionQuality ?? AtlasPlatformFormats.DefaultCompressionQuality,
                    0,
                    100),
                rule != null
                    ? rule.GetAtlasMaxTextureSize(platform)
                    : DefaultAtlasMaxTextureSize);
        }

        private static SpriteAtlasPackingSettings CreatePackingSettings(
            AtlasPipelineSettings settings,
            AtlasImportRule rule = null)
        {
            bool enableRotation = rule != null
                ? rule.ResolveAtlasRotation(settings.EnableRotation)
                : settings.EnableRotation;
            bool enableAlphaDilation = rule != null
                ? rule.ResolveAlphaDilation(settings.EnableAlphaDilation)
                : settings.EnableAlphaDilation;

            return new SpriteAtlasPackingSettings
            {
                padding = settings.AtlasPadding,
                blockOffset = settings.BlockOffset,
                enableRotation = enableRotation,
                enableTightPacking = settings.EnableTightPacking,
                enableAlphaDilation = enableAlphaDilation,
            };
        }

        /// <summary>
        /// Resolves include-in-build for one atlas. Shared by the writer and the configuration
        /// comparison, so the two can never disagree about it.
        /// </summary>
        private static bool ResolveIncludeInBuild(
            AtlasPipelineSettings settings,
            AtlasImportRule rule)
        {
            return rule != null
                ? rule.ResolveIncludeInBuild(settings.IncludeInBuild)
                : settings.IncludeInBuild;
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
                if (RuleCache[i].OwnsPath(path))
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

        /// <summary>
        /// One-time migration from the legacy inline rule array to individual rule assets.
        /// Runs only when the settings asset carries inline rules and references no rule assets.
        /// Every failure path — exception, a rule that will not save — leaves the inline list
        /// untouched, and the pipeline keeps reading it, so the worst outcome is "still on the old
        /// format", never "rules lost".
        /// </summary>
        private static void MigrateLegacyInlineRules()
        {
            AtlasPipelineSettings settings = _settingsCache;
            if (settings == null || !settings.HasLegacyInlineRules)
            {
                return;
            }

            IReadOnlyList<AtlasImportRule> legacyRules = settings.ImportRules;
            try
            {
                EnsureAssetFolderExists(DefaultRuleFolder);
                var assets = new List<AtlasRuleAsset>(legacyRules.Count);
                for (int i = 0; i < legacyRules.Count; i++)
                {
                    AtlasImportRule rule = legacyRules[i];
                    if (rule == null)
                    {
                        continue;
                    }

                    string assetName = AtlasPathUtility.SanitizePart(rule.Name);
                    string assetPath = DefaultRuleFolder + "/" + assetName + ".asset";
                    if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                    {
                        assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
                    }

                    var ruleAsset = ScriptableObject.CreateInstance<AtlasRuleAsset>();
                    ruleAsset.Initialize(rule);
                    AssetDatabase.CreateAsset(ruleAsset, assetPath);
                    assets.Add(ruleAsset);
                }

                if (assets.Count == 0)
                {
                    // Nothing usable to migrate; clearing the inline list would lose the (broken)
                    // entries, so leave everything as it was.
                    return;
                }

                settings.AdoptRuleAssets(assets);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                AtlasPipelineLog.Channel.Info(
                    "[CycloneGames Atlas Pipeline] Migrated " + assets.Count
                    + " inline import rule(s) to individual rule assets under '"
                    + DefaultRuleFolder + "'. Each rule is now its own file, so two contributors "
                    + "editing two rules no longer conflict.");
            }
            catch (Exception exception)
            {
                AtlasPipelineLog.Channel.Warning(
                    "[CycloneGames Atlas Pipeline] Could not migrate inline import rules to rule "
                    + "assets: " + exception.Message
                    + ". The inline rules are kept and still active; the migration will be retried "
                    + "on the next editor session.");
            }
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

        /// <summary>
        /// Creates an Assets/-relative folder (and its parents) if it does not exist. Public for the
        /// authoring window, which creates rule assets.
        /// </summary>
        public static void EnsureAssetFolderExists(string assetFolder)
        {
            if (TryCreateAssetFolder(assetFolder))
            {
                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// Creates the folder if it is missing, and reports whether anything was created. Split out
        /// from <see cref="EnsureAssetFolderExists"/> so a caller creating several folders can
        /// refresh once at the end instead of once per folder — a refresh in the middle of atlas
        /// generation schedules a full rescan through projectChanged.
        /// </summary>
        private static bool TryCreateAssetFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder)
                || !assetFolder.StartsWith("Assets/", StringComparison.Ordinal)
                || AssetDatabase.IsValidFolder(assetFolder))
            {
                return false;
            }

            string absolute = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                assetFolder.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar)));
            Directory.CreateDirectory(absolute);
            return true;
        }

        /// <summary>
        /// Creates every output directory the pending atlases will need, before the first one is
        /// written. SpriteAtlasAsset.Save does not create missing directories, so a rule pointed at
        /// a new subfolder would otherwise fail to write and then be reported missing by the
        /// post-generation existence check.
        /// Batched into one refresh on purpose: the generation loop runs outside batched asset
        /// editing, so a refresh per folder would fire projectChanged mid-pass and queue a full
        /// rescan of the very assets being written.
        /// </summary>
        private static void EnsureOutputDirectories(List<string> keys, int count)
        {
            string root = _settingsCache.NormalizedOutputAtlasFolder;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool created = false;

            for (int i = 0; i < count; i++)
            {
                string subfolder = ResolveOutputSubfolder(keys[i]);
                if (subfolder.Length == 0 || !seen.Add(subfolder))
                {
                    continue;
                }

                created |= TryCreateAssetFolder(root + "/" + subfolder);
            }

            if (created)
            {
                AssetDatabase.Refresh();
            }
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
