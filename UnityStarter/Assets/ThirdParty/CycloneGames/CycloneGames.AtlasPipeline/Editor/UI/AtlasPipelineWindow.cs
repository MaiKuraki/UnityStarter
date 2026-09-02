using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using CycloneGames.AtlasPipeline.Pure;

namespace CycloneGames.AtlasPipeline
{
    /// <summary>
    /// Authoring surface for the CycloneGames atlas pipeline. It edits the project-owned settings asset
    /// through SerializedObject/Undo and presents live import/index/atlas metrics.
    /// </summary>
    public sealed class AtlasPipelineWindow : EditorWindow
    {
        private SerializedObject _settingsObject;
        private SerializedProperty _autoImportProperty;
        private SerializedProperty _autoGenerateAtlasesProperty;
        private SerializedProperty _outputAtlasFolderProperty;
        private SerializedProperty _atlasPaddingProperty;
        private SerializedProperty _enableRotationProperty;
        private SerializedProperty _enableTightPackingProperty;
        private SerializedProperty _enableAlphaDilationProperty;
        private SerializedProperty _blockOffsetProperty;
        private SerializedProperty _includeInBuildProperty;
        private SerializedProperty _asciiOnlyNamesProperty;
        private SerializedProperty _atlasKeyCasingProperty;
        private SerializedProperty _collisionSafeAtlasKeysProperty;
        private SerializedProperty _autoPageOverflowingAtlasesProperty;
        private SerializedProperty _globalExcludedFolderPathsProperty;
        private SerializedProperty _ruleAssetsProperty;

        private ReorderableList _rulesList;
        private Vector2 _scrollPosition;
        private bool _showGeneral = true;
        private bool _showRules = true;
        private bool _showPacking = true;
        private bool _showValidation = true;
        private bool _showManifest = true;
        private bool _showExclusions = false;
        private string _feedbackTitle = string.Empty;
        private string _feedbackMessage = string.Empty;
        private bool _settingsSaveScheduled;
        private bool _rulesChanged;
        private readonly HashSet<int> _expandedRules = new HashSet<int>();

        // The overrides intro text lives on AtlasPipelineUi as a cached GUIContent, so the string
        // that gets measured and the string that gets drawn are literally the same object.

        // Row accounting for ComputeRuleElementHeight. Each value is the number of NextLine
        // advances the matching section performs in DrawRuleAssetElement. The ReorderableList has
        // no auto-layout for its elements, so these must be kept in step with the draw code by
        // hand — the section comments there name the same groups.
        private const int IdentityRows = 5;         // name/group, folder, path label, subfolder, foldout
        private const int SpriteImportRows = 5;     // mode/ppu, pixel art, filter/wrap, mip/read, compression/granularity
        private const int AtlasCompositionRows = 3; // atlas max, recommended/warn, platform format row
        private const int OverridesRows = 5;        // section label, size header, size row, toggles, rotation
        private const int ListCount = 3;            // path keywords, excluded folders, excluded keywords

        /// <summary>Each list draws a section header plus the row its Add button sits on.</summary>
        private const int PerListChromeRows = 2;

        /// <summary>Bottom breathing room inside the rule card, after the last list.</summary>
        private const float RuleElementBottomPadding = 8f;

        /// <summary>
        /// Memoised width and height of the overrides intro label. The height callback runs before
        /// the draw pass and therefore has no element rect to measure against, so both sides read
        /// the same cached value instead of each guessing a width — see
        /// <see cref="MeasureIntroHeight"/>.
        /// </summary>
        private float _introWidth = -1f;
        private float _introHeight;

        private IReadOnlyList<string> _cachedValidationErrors = new List<string>();

        /// <summary>
        /// Non-blocking findings from the last validation pass: rule audit orphans, rules that
        /// matched no assets, nested output folders, capacity advice. They do not fail the build, but
        /// they were invisible in this window for exactly that reason — a warning nobody sees is a
        /// warning nobody acts on.
        /// </summary>
        private IReadOnlyList<string> _cachedValidationWarnings = new List<string>();

        /// <summary>
        /// Findings from comparing the committed manifest against the project, cached alongside the
        /// validation pass. CI fails on this; the window is where it gets fixed before the push.
        /// </summary>
        private IReadOnlyList<string> _cachedManifestDrift = new List<string>();

        /// <summary>
        /// Whether a committed manifest exists, cached with the drift pass. A missing manifest is the
        /// normal state of a fresh clone and is reported differently from real drift.
        /// </summary>
        private bool _cachedManifestMissing;

        private bool _validationCacheDirty = true;
        [MenuItem("Tools/CycloneGames/Atlas Pipeline/Open Atlas Pipeline")]
        public static void ShowWindow()
        {
            AtlasPipelineWindow window = GetWindow<AtlasPipelineWindow>(
                "CycloneGames Atlas Pipeline");
            window.minSize = new Vector2(560f, 700f);
            window.Show();
        }

        private void OnEnable()
        {
            AtlasPipeline.EnsureSettingsAsset();
            AtlasPipeline.ScheduleSpritePackerModePrompt();
            _settingsObject = new SerializedObject(AtlasPipeline.Settings);
            CacheProperties();
            BuildRulesList();

            // The collector probe is cached per (window lifetime, output folder); a project change
            // re-probes so editing the YooAsset collector config is picked up without reopening.
            _collectorProbeDone = false;

            // UI-only reaction. The pipeline's own refresh lives in AtlasPipeline at domain level,
            // so it no longer depends on this window being open.
            EditorApplication.projectChanged += OnProjectChanged;

            // Force index initialization in OnEnable so the first OnGUI frame does not stall on a
            // full scan.
            AtlasPipeline.GetSnapshot();
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
            SaveSettingsAsset();
        }

        /// <summary>
        /// Window-local reaction to a project change. The pipeline's own refresh (rescan, dirty
        /// marking, incremental generation) is subscribed at domain level in
        /// <see cref="AtlasPipeline"/>, so it keeps working while this window is closed — it used to
        /// live here, which meant art pulled while the window was shut was never indexed at all.
        /// </summary>
        private void OnProjectChanged()
        {
            _validationCacheDirty = true;
            _collectorProbeDone = false;

            // Clear the folder-resolution cache of the rule instances this window holds: a folder
            // rename is an external event, and the window's instances are not replaced when the
            // pipeline reloads assets, so without an explicit invalidation the label and ObjectField
            // keep showing the stale path.
            RefreshWindowRuleFolderCache();
            Repaint();
        }

        private void RefreshWindowRuleFolderCache()
        {
            if (_settingsObject == null || _settingsObject.targetObject == null)
            {
                return;
            }

            // Iterate the rule assets, not the resolved rule list: the two can have different
            // indices when a rule asset reference is missing, and this window is the only place
            // that has to care.
            var settings = (AtlasPipelineSettings)_settingsObject.targetObject;
            IReadOnlyList<AtlasRuleAsset> assets = settings.RuleAssets;
            for (int i = 0; i < assets.Count; i++)
            {
                assets[i]?.Rule?.RefreshResolvedFolder();
            }
        }

        /// <summary>
        /// Heals the path string back to the current GUID-resolved path when saving: after a folder
        /// rename the raw string is stale and the GUID-resolved path is the current one. Called only
        /// after ApplyModifiedProperties (inside the edit + save flow).
        /// </summary>
        private void HealStaleSourceFolderPaths()
        {
            if (_settingsObject == null || _settingsObject.targetObject == null)
            {
                return;
            }

            var settings = (AtlasPipelineSettings)_settingsObject.targetObject;
            IReadOnlyList<AtlasRuleAsset> assets = settings.RuleAssets;
            for (int i = 0; i < assets.Count; i++)
            {
                AtlasImportRule rule = assets[i]?.Rule;
                if (rule == null)
                {
                    continue;
                }

                string resolved = rule.NormalizedSourceFolder;
                if (!string.IsNullOrEmpty(resolved)
                    && !string.Equals(
                        resolved,
                        rule.SourceFolder,
                        StringComparison.Ordinal))
                {
                    rule.UpdateSourceFolderPath(resolved);
                    EditorUtility.SetDirty(assets[i]);
                }
            }
        }

        private void OnGUI()
        {
            if (_settingsObject == null || _settingsObject.targetObject == null)
            {
                EditorGUILayout.HelpBox(
                    "CycloneGames atlas settings asset is unavailable. Reopen the window to create it.",
                    MessageType.Warning);
                return;
            }

            _settingsObject.Update();

            AtlasInspectorUiUtility.DrawInspectorTitle(
                "CycloneGames Atlas Pipeline",
                "Data-driven sprite import rules, incremental atlas generation, and build-ready validation.",
                AtlasInspectorUiUtility.ArtColor);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawSummary();
            DrawGeneral();
            DrawExclusions();
            DrawRules();
            DrawPacking();
            DrawValidation();
            DrawActions();
            DrawFeedback();

            EditorGUILayout.EndScrollView();

            bool settingsChanged = _settingsObject.ApplyModifiedProperties();
            if (settingsChanged || _rulesChanged)
            {
                HealStaleSourceFolderPaths();
                if (settingsChanged)
                {
                    EditorUtility.SetDirty(_settingsObject.targetObject);
                }

                ScheduleSettingsSave();
                AtlasPipeline.HandleSettingsChanged();
                _validationCacheDirty = true;
                _rulesChanged = false;
                Repaint();
            }
        }

        private void DrawSummary()
        {
            AtlasPipelineSnapshot snapshot = AtlasPipeline.GetSnapshot();
            AtlasInspectorUiUtility.BeginPanel();

            EditorGUILayout.BeginHorizontal();
            AtlasInspectorUiUtility.DrawMetric(
                "Rules",
                snapshot.RuleCount.ToString(),
                AtlasInspectorUiUtility.ImportColor);
            AtlasInspectorUiUtility.DrawMetric(
                "Sprites",
                snapshot.IndexedSpriteCount.ToString(),
                AtlasInspectorUiUtility.AtlasColor);
            AtlasInspectorUiUtility.DrawMetric(
                "Atlases",
                snapshot.AtlasCount.ToString(),
                AtlasInspectorUiUtility.SuccessColor);
            AtlasInspectorUiUtility.DrawMetric(
                "Dirty",
                snapshot.DirtyAtlasCount.ToString(),
                snapshot.DirtyAtlasCount == 0
                    ? AtlasInspectorUiUtility.NeutralColor
                    : AtlasInspectorUiUtility.WarningColor);
            EditorGUILayout.EndHorizontal();

            AtlasInspectorUiUtility.DrawStatusRow(
                "Settings asset",
                AtlasPipeline.SettingsAssetPath,
                AtlasInspectorUiUtility.SuccessColor);

            AtlasInspectorUiUtility.EndPanel();
            EditorGUILayout.Space(6f);
        }

        private void DrawGeneral()
        {
            _showGeneral = AtlasInspectorUiUtility.DrawFoldoutHeader(
                "General",
                _showGeneral,
                AtlasInspectorUiUtility.ArtColor,
                "PIPELINE",
                AtlasInspectorUiUtility.ArtColor);
            if (!_showGeneral)
            {
                return;
            }

            AtlasInspectorUiUtility.BeginPanel();
            EditorGUILayout.PropertyField(
                _autoImportProperty,
                AtlasPipelineUi.AutoImport);
            EditorGUILayout.PropertyField(
                _autoGenerateAtlasesProperty,
                AtlasPipelineUi.AutoGenerateAtlases);
            EditorGUILayout.PropertyField(
                _asciiOnlyNamesProperty,
                AtlasPipelineUi.AsciiOnlyNames);
            EditorGUILayout.PropertyField(
                _atlasKeyCasingProperty,
                AtlasPipelineUi.AtlasKeyCasing);
            EditorGUILayout.PropertyField(
                _collisionSafeAtlasKeysProperty,
                AtlasPipelineUi.CollisionSafeKeys);
            EditorGUILayout.PropertyField(
                _autoPageOverflowingAtlasesProperty,
                AtlasPipelineUi.AutoPageOverflowing);
            DrawFolderObjectField(
                _outputAtlasFolderProperty,
                AtlasPipelineUi.OutputAtlasFolder);
            AtlasInspectorUiUtility.EndPanel();
            EditorGUILayout.Space(4f);
        }

        /// <summary>
        /// Folders the pipeline ignores entirely, whatever the rules say. The atlas output folder is
        /// always excluded on top of this list and is deliberately not shown here, because it is not
        /// configurable.
        /// </summary>
        private void DrawExclusions()
        {
            int excludeCount = _globalExcludedFolderPathsProperty?.arraySize ?? 0;
            _showExclusions = AtlasInspectorUiUtility.DrawFoldoutHeader(
                "Global Exclusions",
                _showExclusions,
                AtlasInspectorUiUtility.WarningColor,
                _folderBadge.Get(excludeCount),
                AtlasInspectorUiUtility.WarningColor);
            if (!_showExclusions)
            {
                return;
            }

            AtlasInspectorUiUtility.BeginPanel();
            EditorGUILayout.HelpBox(
                "Assets under these folders are ignored completely: no atlas membership, no "
                + "import settings, no rename prompts. The atlas output folder is always excluded "
                + "and cannot be removed from that guarantee.",
                MessageType.Info);
            DrawStringFolderList(_globalExcludedFolderPathsProperty);
            AtlasInspectorUiUtility.EndPanel();
            EditorGUILayout.Space(4f);
        }

        private void DrawRules()
        {
            int count = _ruleAssetsProperty?.arraySize ?? 0;
            _showRules = AtlasInspectorUiUtility.DrawFoldoutHeader(
                "Import Rules",
                _showRules,
                AtlasInspectorUiUtility.ImportColor,
                _ruleBadge.Get(count),
                AtlasInspectorUiUtility.ImportColor);
            if (!_showRules)
            {
                return;
            }

            AtlasInspectorUiUtility.BeginPanel();
            if (_rulesList == null)
            {
                BuildRulesList();
            }

            _rulesList.DoLayoutList();
            AtlasInspectorUiUtility.EndPanel();
            EditorGUILayout.Space(4f);
        }

        private void DrawPacking()
        {
            _showPacking = AtlasInspectorUiUtility.DrawFoldoutHeader(
                "Atlas Packing",
                _showPacking,
                AtlasInspectorUiUtility.AtlasColor,
                "SPRITEATLAS",
                AtlasInspectorUiUtility.AtlasColor);
            if (!_showPacking)
            {
                return;
            }

            AtlasInspectorUiUtility.BeginPanel();

            // Layout density: these four together decide how much of each page is sprite pixels
            // versus empty space.
            EditorGUILayout.LabelField("Layout Density", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(
                _atlasPaddingProperty,
                AtlasPipelineUi.Padding);
            EditorGUILayout.PropertyField(
                _blockOffsetProperty,
                AtlasPipelineUi.BlockOffset);
            EditorGUILayout.PropertyField(
                _enableRotationProperty,
                AtlasPipelineUi.RotationDefault);

            DrawToggleGuidance(AtlasPipelineUi.RotationGuidance);

            EditorGUILayout.PropertyField(
                _enableTightPackingProperty,
                AtlasPipelineUi.TightPacking);

            DrawToggleGuidance(AtlasPipelineUi.TightPackingGuidance);

            EditorGUILayout.Space(4f);

            // Edge quality: dilation is the anti-seam measure. It writes only into padding,
            // never into the sprite's own pixels.
            EditorGUILayout.LabelField("Edge Quality", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(
                _enableAlphaDilationProperty,
                AtlasPipelineUi.AlphaDilationDefault);

            DrawToggleGuidance(AtlasPipelineUi.AlphaDilationGuidance);

            EditorGUILayout.Space(4f);

            // Distribution: the one setting with a real project-level consequence, so it gets the
            // full explanation plus detection of an asset-management system.
            EditorGUILayout.LabelField("Distribution", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(
                _includeInBuildProperty,
                AtlasPipelineUi.IncludeInBuildDefault);

            DrawToggleGuidance(AtlasPipelineUi.IncludeInBuildGuidance);

            DrawAssetManagementHint();

            AtlasInspectorUiUtility.EndPanel();
            EditorGUILayout.Space(4f);
        }

        /// <summary>
        /// Structured "On / When On / When Off / Tip" guidance for an atlas packing toggle. One
        /// HelpBox per toggle so the whole panel reads as a stack of related blocks rather than a
        /// pile of chevrons; bold prefixes give a scannable hierarchy; single line breaks, no blank
        /// lines between sections. The Tip paragraph uses its own bold label so it is unambiguously
        /// separate from the On/When sections above it.
        /// The content and the style both come from <see cref="AtlasPipelineUi"/>: the text is
        /// pre-built rich text and the style is the cached HelpBox derivative with rich text on,
        /// so a repaint allocates nothing.
        /// </summary>
        private static void DrawToggleGuidance(GUIContent guidance)
        {
            GUILayout.Box(
                guidance,
                AtlasPipelineUi.RichHelpBoxStyle,
                AtlasPipelineUi.ExpandWidth);
        }

        /// <summary>
        /// Warns when the atlas output folder is actually collected by YooAsset while atlases are
        /// also baked into the installer — the combination that ships the same textures twice.
        /// The probe deliberately does NOT warn on "YooAsset is installed" alone: plenty of projects
        /// use it for scenes and audio while their atlases stay baked, and a warning there would be
        /// noise. A collector path covering the output folder is the precise signal that generated
        /// atlases really flow through YooAsset.
        /// Addressables is deliberately not probed: its entries reference assets by GUID, generated
        /// atlases are uncommitted and therefore per-machine, so the interaction there is a design
        /// question documented in the README rather than something a presence check can settle.
        /// </summary>
        private void DrawAssetManagementHint()
        {
            if (!_includeInBuildProperty.boolValue)
            {
                return;
            }

            string outputFolder = _outputAtlasFolderProperty.stringValue ?? string.Empty;
            if (!_collectorProbeDone
                || !string.Equals(_collectorProbeFolder, outputFolder, StringComparison.Ordinal))
            {
                _collectorProbeDone = true;
                _collectorProbeFolder = outputFolder;
                _detectedCollectorPath = FindYooCollectorCoveringOutput(outputFolder);
            }

            if (string.IsNullOrEmpty(_detectedCollectorPath))
            {
                return;
            }

            EditorGUILayout.HelpBox(
                $"The atlas output folder is collected by YooAsset (collector path "
                + $"'{_detectedCollectorPath}'), and Include In Build is on: the same atlas "
                + "textures are baked into the installer and shipped again in the bundle. Turn "
                + "Include In Build off, and force it on only for the rules whose atlases must "
                + "ship with the installer.",
                MessageType.Warning);
        }

        private bool _collectorProbeDone;
        private string _collectorProbeFolder;
        private string _detectedCollectorPath;

        /// <summary>
        /// The YooAsset collector path that covers the atlas output folder, or null when YooAsset is
        /// absent or nothing it collects overlaps the output folder.
        /// Zero coupling by construction: the collector setting is located by name and read as
        /// serialized text, so this assembly never loads a YooAsset type and the asmdef stays clean.
        /// </summary>
        private static string FindYooCollectorCoveringOutput(string outputFolder)
        {
            if (string.IsNullOrEmpty(outputFolder))
            {
                return null;
            }

            string[] guids = AssetDatabase.FindAssets("AssetBundleCollectorSetting");
            if (guids == null || guids.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < guids.Length; i++)
            {
                string settingPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(settingPath) || !File.Exists(settingPath))
                {
                    continue;
                }

                foreach (string collectedPath in EnumerateYooCollectPaths(settingPath))
                {
                    if (AtlasPathUtility.PathsOverlap(outputFolder, collectedPath))
                    {
                        return collectedPath;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Reads CollectPath values straight out of the collector setting's serialized text. A line
        /// parse rather than a deserialization: the field name has been stable across YooAsset
        /// versions, and a format change degrades to "no hint" rather than an error.
        /// </summary>
        private static IEnumerable<string> EnumerateYooCollectPaths(string settingPath)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(settingPath);
            }
            catch (Exception)
            {
                yield break;
            }

            const string marker = "CollectPath:";
            for (int i = 0; i < lines.Length; i++)
            {
                int index = lines[i].IndexOf(marker, StringComparison.Ordinal);
                if (index < 0)
                {
                    continue;
                }

                string value = lines[i].Substring(index + marker.Length).Trim();
                if (value.Length > 0)
                {
                    yield return value;
                }
            }
        }

        private void DrawValidation()
        {
            // Cache the validation result: ValidateForBuild runs RefreshRuleOrder (List.Sort) and a
            // full file-name scan, and calling it every OnGUI frame would drop frames in the window
            // itself. The criteria are unified with includeNameScan: true to match the build step,
            // fixing the previous inconsistency where the window showed READY while the build failed.
            // The manifest drift check rides the same cache cycle: it answers a structural question
            // (no source bytes are read), and CI fails the build on it, so the window has to show it.
            if (_validationCacheDirty)
            {
                var advisory = new List<string>();
                _cachedValidationErrors = AtlasPipeline.ValidateForBuild(
                    includeNameScan: true,
                    warnings: advisory);
                _cachedValidationWarnings = advisory;
                _cachedManifestDrift = AtlasPipeline.ValidateManifestDrift();
                _cachedManifestMissing = AtlasPipeline.ReadManifest() == null;
                _validationCacheDirty = false;
            }

            IReadOnlyList<string> errors = _cachedValidationErrors;
            IReadOnlyList<string> warnings = _cachedValidationWarnings;
            bool valid = errors.Count == 0;

            _showValidation = AtlasInspectorUiUtility.DrawFoldoutHeader(
                "Build Validation",
                _showValidation,
                errors.Count > 0
                    ? AtlasInspectorUiUtility.WarningColor
                    : AtlasInspectorUiUtility.SuccessColor,
                errors.Count > 0
                    ? _issueBadge.Get(errors.Count)
                    : warnings.Count > 0
                        ? _warningBadge.Get(warnings.Count)
                        : "READY",
                errors.Count > 0
                    ? AtlasInspectorUiUtility.WarningColor
                    : warnings.Count > 0
                        ? AtlasInspectorUiUtility.WarningColor
                        : AtlasInspectorUiUtility.SuccessColor);

            if (!_showValidation)
            {
                return;
            }

            AtlasInspectorUiUtility.BeginPanel();
            AtlasInspectorUiUtility.DrawStatusRow(
                "Blocking",
                errors.Count == 0
                    ? "None"
                    : errors.Count + (errors.Count == 1 ? " issue" : " issues"),
                errors.Count == 0
                    ? AtlasInspectorUiUtility.SuccessColor
                    : AtlasInspectorUiUtility.WarningColor);
            AtlasInspectorUiUtility.DrawStatusRow(
                "Advisory",
                warnings.Count == 0
                    ? "None"
                    : warnings.Count + (warnings.Count == 1 ? " note" : " notes"),
                warnings.Count == 0
                    ? AtlasInspectorUiUtility.SuccessColor
                    : AtlasInspectorUiUtility.WarningColor);

            // Severity split: blocking findings fail the build, advisory ones do not. They used to
            // share one flat list rendered as warnings, which made an advisory note look exactly
            // like the thing that was about to fail the build.
            for (int i = 0; i < errors.Count; i++)
            {
                EditorGUILayout.HelpBox(errors[i], MessageType.Error);
            }

            for (int i = 0; i < warnings.Count; i++)
            {
                EditorGUILayout.HelpBox(warnings[i], MessageType.Warning);
            }

            AtlasInspectorUiUtility.EndPanel();
            EditorGUILayout.Space(4f);

            DrawManifestPanel();
        }

        /// <summary>
        /// The committed manifest is the only record CI has of what the atlases should contain, so
        /// drift between it and the project is what makes the validate-only build step fail. Showing
        /// it here — with a one-click fix — is what closes the "art changed, nobody regenerated"
        /// loop at the moment the work happens instead of on the build agent.
        /// </summary>
        private void DrawManifestPanel()
        {
            IReadOnlyList<string> drift = _cachedManifestDrift;

            // Cached with the drift pass, not read here: this method runs every OnGUI frame, and a
            // file read per frame is exactly the kind of thing the validation cache exists to avoid.
            bool manifestMissing = _cachedManifestMissing;

            _showManifest = AtlasInspectorUiUtility.DrawFoldoutHeader(
                "Atlas Manifest",
                _showManifest,
                drift.Count == 0
                    ? AtlasInspectorUiUtility.SuccessColor
                    : AtlasInspectorUiUtility.WarningColor,
                drift.Count == 0
                    ? "UP TO DATE"
                    : _driftBadge.Get(drift.Count),
                AtlasInspectorUiUtility.WarningColor);

            if (!_showManifest)
            {
                return;
            }

            AtlasInspectorUiUtility.BeginPanel();

            if (drift.Count == 0)
            {
                AtlasInspectorUiUtility.DrawStatusRow(
                    "Manifest",
                    "Matches the current project",
                    AtlasInspectorUiUtility.SuccessColor);
            }
            else
            {
                AtlasInspectorUiUtility.DrawStatusRow(
                    "Drift",
                    manifestMissing
                        ? "No manifest yet"
                        : drift.Count + (drift.Count == 1 ? " finding" : " findings"),
                    AtlasInspectorUiUtility.WarningColor);

                for (int i = 0; i < drift.Count; i++)
                {
                    EditorGUILayout.HelpBox(drift[i], MessageType.Warning);
                }

                // Missing is the normal state of a fresh clone — regeneration creates it — while
                // real drift means the committed baseline no longer describes the project.
                EditorGUILayout.HelpBox(
                    manifestMissing
                        ? "No manifest has been written yet. It is created by a complete, "
                          + "error-free generation pass and committed so CI can verify staleness."
                        : "Regenerate the atlases and the manifest is rewritten to match.",
                    MessageType.Info);

                if (GUILayout.Button("Regenerate & Update Manifest"))
                {
                    AtlasPipeline.ProcessAllDirtyAtlases();
                    _validationCacheDirty = true;
                    SetFeedback(
                        "Manifest",
                        "All atlases were regenerated (unchanged ones skipped) and the manifest "
                        + "was rewritten to match the project.");
                }
            }

            AtlasInspectorUiUtility.EndPanel();
            EditorGUILayout.Space(4f);
        }

        private void DrawActions()
        {
            AtlasInspectorUiUtility.BeginPanel();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Importers"))
            {
                bool changed = AtlasPipeline.ApplyImportSettingsToAll();
                SetFeedback(
                    "Importers",
                    changed
                        ? "Sprite import settings were applied."
                        : "No sprite importer changes were required.");
            }

            if (GUILayout.Button("Rebuild Index"))
            {
                AtlasPipeline.RebuildIndex(markDirty: false);
                SetFeedback("Index", "Sprite atlas index was rebuilt without marking all atlases dirty.");
            }

            if (GUILayout.Button("Regenerate Atlases"))
            {
                AtlasPipeline.ProcessAllDirtyAtlases();
                SetFeedback("Atlases", "All configured atlases were regenerated.");
            }

            if (GUILayout.Button("Review Atlas Names"))
            {
                AtlasRenameWindow.ShowWindow();
            }

            EditorGUILayout.EndHorizontal();
            AtlasInspectorUiUtility.EndPanel();
        }

        private void DrawFeedback()
        {
            if (string.IsNullOrEmpty(_feedbackTitle))
            {
                return;
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox(
                $"{_feedbackTitle}: {_feedbackMessage}",
                MessageType.None);
        }

        private void SetFeedback(string title, string message)
        {
            _feedbackTitle = title;
            _feedbackMessage = message;
            Repaint();
        }

        private void CacheProperties()
        {
            _autoImportProperty = _settingsObject.FindProperty("autoImport");
            _autoGenerateAtlasesProperty = _settingsObject.FindProperty("autoGenerateAtlases");
            _outputAtlasFolderProperty = _settingsObject.FindProperty("outputAtlasFolder");
            _atlasPaddingProperty = _settingsObject.FindProperty("atlasPadding");
            _enableRotationProperty = _settingsObject.FindProperty("enableRotation");
            _enableTightPackingProperty = _settingsObject.FindProperty("enableTightPacking");
            _enableAlphaDilationProperty = _settingsObject.FindProperty("enableAlphaDilation");
            _blockOffsetProperty = _settingsObject.FindProperty("blockOffset");
            _includeInBuildProperty = _settingsObject.FindProperty("includeInBuild");
            _asciiOnlyNamesProperty = _settingsObject.FindProperty("asciiOnlyNames");
            _atlasKeyCasingProperty = _settingsObject.FindProperty("atlasKeyCasing");
            _collisionSafeAtlasKeysProperty = _settingsObject.FindProperty("collisionSafeAtlasKeys");
            _autoPageOverflowingAtlasesProperty =
                _settingsObject.FindProperty("autoPageOverflowingAtlases");
            _globalExcludedFolderPathsProperty =
                _settingsObject.FindProperty("globalExcludedFolderPaths");
            _ruleAssetsProperty = _settingsObject.FindProperty("ruleAssets");
        }

        private void BuildRulesList()
        {
            if (_settingsObject == null || _ruleAssetsProperty == null)
            {
                return;
            }

            // Each rule is its own asset, edited through its own SerializedObject. The settings
            // object only owns the ordered list of references.
            _rulesList = new ReorderableList(
                _settingsObject,
                _ruleAssetsProperty,
                true,
                true,
                true,
                true);
            _rulesList.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(
                    rect,
                    $"Import Rules ({_ruleAssetsProperty.arraySize})",
                    EditorStyles.boldLabel);
            };
            _rulesList.elementHeightCallback = ComputeRuleElementHeight;
            _rulesList.drawElementCallback = DrawRuleAssetElement;
            _rulesList.onAddCallback = list =>
            {
                AtlasRuleAsset asset = AtlasPipeline.CreateAndRegisterRuleAsset(
                    BuildDefaultRule(list.serializedProperty.arraySize + 1));
                if (asset == null)
                {
                    return;
                }

                // The helper mutated the settings asset directly (with a full undo group), so this
                // window's SerializedObject view is stale until it re-reads.
                _settingsObject.Update();
                _rulesChanged = true;
                _validationCacheDirty = true;
            };

            // Explicit so the semantics are not an accident of ReorderableList's default remove:
            // this UNREGISTERS the reference and nothing else. The asset file stays on disk as an
            // orphan for the audit and the "Delete Unregistered Rule Assets" command to handle —
            // no AssetDatabase.DeleteAsset here, ever. The delete is recorded by the settings
            // SerializedObject's ApplyModifiedProperties, so undo restores the same reference.
            // The double delete is the object-reference-array quirk: the first call clears the
            // reference, only the second removes the slot — without it, every removal would leave
            // a blank row that grows the list.
            _rulesList.onRemoveCallback = list =>
            {
                if (list.index < 0 || list.index >= list.serializedProperty.arraySize)
                {
                    return;
                }

                int sizeBefore = list.serializedProperty.arraySize;
                list.serializedProperty.DeleteArrayElementAtIndex(list.index);
                if (list.serializedProperty.arraySize == sizeBefore)
                {
                    list.serializedProperty.DeleteArrayElementAtIndex(list.index);
                }

                _rulesChanged = true;
                _validationCacheDirty = true;
            };
        }

        /// <summary>
        /// The defaults a freshly added rule starts with — the same values the old inline "+"
        /// used to write, so a new rule behaves identically whichever way it was added.
        /// </summary>
        private static AtlasImportRule BuildDefaultRule(int ruleNumber)
        {
            return AtlasImportRule.Create(
                "Rule " + ruleNumber,
                string.Empty,
                AtlasPlatformFormats.GetDefaultFormat(AtlasPlatform.Android),
                AtlasPlatformFormats.GetDefaultFormat(AtlasPlatform.Iphone),
                AtlasGranularity.PerSourceFolder,
                "General",
                webglFormat: AtlasPlatformFormats.GetDefaultFormat(AtlasPlatform.Webgl),
                standaloneFormat: AtlasPlatformFormats.GetDefaultFormat(AtlasPlatform.Standalone),
                atlasMaxTextureSize: 2048);
        }

        /// <summary>
        /// Cached SerializedObject plus pre-resolved child properties for one rule asset.
        /// SerializedProperty lookups by name allocate, and the draw path touches ~30 child
        /// properties per rule per pass — so they are resolved exactly once here and reused.
        /// Rebuilt automatically if the target asset is destroyed or reimported.
        /// </summary>
        private sealed class RuleView
        {
            public SerializedObject Object;
            public SerializedProperty Name;
            public SerializedProperty AtlasGroup;
            public SerializedProperty SourceFolder;
            public SerializedProperty SourceFolderGuid;
            public SerializedProperty OutputSubfolder;
            public SerializedProperty SpriteMode;
            public SerializedProperty PixelsPerUnit;
            public SerializedProperty PixelArt;
            public SerializedProperty FilterMode;
            public SerializedProperty WrapMode;
            public SerializedProperty Mipmaps;
            public SerializedProperty Readable;
            public SerializedProperty CompressionQuality;
            public SerializedProperty AtlasGranularity;
            public SerializedProperty AtlasMax;
            public SerializedProperty RecommendedMax;
            public SerializedProperty WarnTextureSize;
            public SerializedProperty AndroidFormat;
            public SerializedProperty IphoneFormat;
            public SerializedProperty WebglFormat;
            public SerializedProperty AndroidSize;
            public SerializedProperty IphoneSize;
            public SerializedProperty WebglSize;
            public SerializedProperty StandaloneSize;
            public SerializedProperty IncludeInBuild;
            public SerializedProperty AlphaDilation;
            public SerializedProperty AtlasRotation;
            public SerializedProperty PathKeywords;
            public SerializedProperty ExcludedFolders;
            public SerializedProperty ExcludedKeywords;
        }

        private readonly UiCountText _folderBadge = new UiCountText("FOLDER", "FOLDERS");
        private readonly UiCountText _ruleBadge = new UiCountText("RULE", "RULES");
        private readonly UiCountText _issueBadge = new UiCountText("ISSUE", "ISSUES");
        private readonly UiCountText _warningBadge = new UiCountText("WARNING", "WARNINGS");
        private readonly UiCountText _driftBadge = new UiCountText("FINDING", "FINDINGS");

        private readonly Dictionary<AtlasRuleAsset, RuleView> _ruleViews =
            new Dictionary<AtlasRuleAsset, RuleView>();

        private RuleView GetRuleView(AtlasRuleAsset asset)
        {
            if (_ruleViews.TryGetValue(asset, out RuleView existing)
                && existing.Object != null
                && existing.Object.targetObject != null)
            {
                return existing;
            }

            var view = new RuleView
            {
                Object = new SerializedObject(asset),
            };
            SerializedProperty root = view.Object.FindProperty("rule");
            view.Name = root.FindPropertyRelative("name");
            view.AtlasGroup = root.FindPropertyRelative("atlasGroup");
            view.SourceFolder = root.FindPropertyRelative("sourceFolder");
            view.SourceFolderGuid = root.FindPropertyRelative("sourceFolderGuid");
            view.OutputSubfolder = root.FindPropertyRelative("outputSubfolder");
            view.SpriteMode = root.FindPropertyRelative("spriteMode");
            view.PixelsPerUnit = root.FindPropertyRelative("pixelsPerUnit");
            view.PixelArt = root.FindPropertyRelative("pixelArt");
            view.FilterMode = root.FindPropertyRelative("filterMode");
            view.WrapMode = root.FindPropertyRelative("wrapMode");
            view.Mipmaps = root.FindPropertyRelative("mipmaps");
            view.Readable = root.FindPropertyRelative("readable");
            view.CompressionQuality = root.FindPropertyRelative("compressionQuality");
            view.AtlasGranularity = root.FindPropertyRelative("atlasGranularity");
            view.AtlasMax = root.FindPropertyRelative("atlasMaxTextureSize");
            view.RecommendedMax = root.FindPropertyRelative("recommendedMaxTextureSize");
            view.WarnTextureSize = root.FindPropertyRelative("warnTextureSize");
            view.AndroidFormat = root.FindPropertyRelative("androidFormat");
            view.IphoneFormat = root.FindPropertyRelative("iphoneFormat");
            view.WebglFormat = root.FindPropertyRelative("webglFormat");
            view.AndroidSize = root.FindPropertyRelative("androidAtlasMaxSize");
            view.IphoneSize = root.FindPropertyRelative("iphoneAtlasMaxSize");
            view.WebglSize = root.FindPropertyRelative("webglAtlasMaxSize");
            view.StandaloneSize = root.FindPropertyRelative("standaloneAtlasMaxSize");
            view.IncludeInBuild = root.FindPropertyRelative("includeInBuildOverride");
            view.AlphaDilation = root.FindPropertyRelative("alphaDilationOverride");
            view.AtlasRotation = root.FindPropertyRelative("atlasRotationMode");
            view.PathKeywords = root.FindPropertyRelative("pathKeywords");
            view.ExcludedFolders = root.FindPropertyRelative("excludedFolderPaths");
            view.ExcludedKeywords = root.FindPropertyRelative("excludedNameKeywords");
            _ruleViews[asset] = view;
            return view;
        }

        private void DrawRuleAssetElement(
            Rect rect,
            int index,
            bool isActive,
            bool isFocused)
        {
            if (_ruleAssetsProperty == null)
            {
                return;
            }

            SerializedProperty assetReference =
                _ruleAssetsProperty.GetArrayElementAtIndex(index);
            AtlasRuleAsset asset = assetReference.objectReferenceValue as AtlasRuleAsset;
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            rect.x += 2f;
            rect.width -= 4f;
            rect.y += spacing;

            if (asset == null)
            {
                // A missing reference (deleted rule asset, or a merge that dropped the file) must be
                // visible and fixable, not silently skipped: the pipeline ignores it, but the empty
                // row is the only hint the list is shorter than it looks.
                UnityEngine.Object dropped = EditorGUI.ObjectField(
                    new Rect(rect.x, rect.y, rect.width, line),
                    "Missing rule asset",
                    null,
                    typeof(AtlasRuleAsset),
                    false);
                if (dropped is AtlasRuleAsset restored)
                {
                    assetReference.objectReferenceValue = restored;
                    _rulesChanged = true;
                }

                return;
            }

            RuleView view = GetRuleView(asset);
            view.Object.Update();

            DrawRuleElement(rect, index, view, line, spacing);

            if (view.Object.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(asset);
                ScheduleSettingsSave();
                _rulesChanged = true;
                _validationCacheDirty = true;
            }
        }

        /// <summary>
        /// Draws one rule. All child properties come pre-resolved from the rule's
        /// <see cref="RuleView"/> — no per-frame name lookups or allocations.
        /// </summary>
        /// <remarks>
        /// Layout is grouped into four sections, each with a clear purpose:
        ///   Identity — Rule Name, Atlas Group, Source Folder (always visible).
        ///   Sprite Import — direct sprite-import settings.
        ///   Atlas Composition — granularity, shared Atlas Max, recommended-max warning,
        ///     per-platform format dropdowns (direct, not overrides).
        ///   Overrides (per-rule) — every field that says "Inherit" lives here. Two kinds of
        ///     Inherit share the section: the project-wide settings (Include In Build, Alpha
        ///     Dilation, Atlas Rotation) and the per-rule refinements (Per-Platform Atlas Size).
        ///     New override fields added in the future go in this same group.
        ///   Keywords & Excludes — path keywords, excluded folders, excluded keywords.
        /// </remarks>
        private void DrawRuleElement(
            Rect rect,
            int index,
            RuleView view,
            float line,
            float spacing)
        {
            DrawPropertyPair(
                ref rect,
                line,
                spacing,
                view.Name,
                AtlasPipelineUi.RuleName,
                view.AtlasGroup,
                AtlasPipelineUi.AtlasGroup);

            SerializedProperty sourceFolderProperty =
                view.SourceFolder;
            SerializedProperty sourceFolderGuidProperty =
                view.SourceFolderGuid;
            DrawFolderObjectField(
                new Rect(rect.x, rect.y, rect.width, line),
                sourceFolderProperty,
                AtlasPipelineUi.SourceFolder,
                sourceFolderGuidProperty,
                ResolveRuleSourceFolderPath(index));
            NextLine(ref rect, line, spacing);

            DrawSourceFolderPathLabel(
                new Rect(rect.x + 16f, rect.y, rect.width - 16f, line),
                ResolveRuleSourceFolderPath(index));
            NextLine(ref rect, line, spacing);

            // Which package this rule ships in. Kept above the foldout rather than among the
            // advanced settings: it is a distribution decision, and a project splitting atlases
            // across asset packages needs it visible on every rule at a glance.
            DrawOutputSubfolderField(ref rect, line, spacing, view.OutputSubfolder);

            bool expanded = _expandedRules.Contains(index);
            DrawAdvancedFoldout(
                new Rect(rect.x, rect.y, rect.width, line),
                index,
                expanded);
            NextLine(ref rect, line, spacing);

            // Reading the local `expanded` — not the set — is what makes the click frame safe.
            // It still holds the value the ReorderableList sized this element from, so:
            //   collapsing: still true, the body is drawn into a rect that is still tall enough,
            //               and the next pass draws the element short.
            //   expanding:  still false, so we stop here instead of pushing the body into the
            //               shrunken rect, and the next pass draws the element tall.
            // Either way this pass only ever draws into the height it was actually given.
            if (!expanded)
            {
                return;
            }

            // ── Direct: sprite import ──────────────────────────────────────────────
            DrawPropertyPair(
                ref rect,
                line,
                spacing,
                view.SpriteMode,
                AtlasPipelineUi.SpriteMode,
                view.PixelsPerUnit,
                AtlasPipelineUi.PixelsPerUnit);

            DrawSinglePropertyField(
                ref rect,
                line,
                spacing,
                view.PixelArt,
                AtlasPipelineUi.PixelArt);

            DrawPropertyPair(
                ref rect,
                line,
                spacing,
                view.FilterMode,
                AtlasPipelineUi.FilterMode,
                view.WrapMode,
                AtlasPipelineUi.WrapMode);

            DrawPropertyPair(
                ref rect,
                line,
                spacing,
                view.Mipmaps,
                AtlasPipelineUi.Mipmaps,
                view.Readable,
                AtlasPipelineUi.Readable);

            DrawPropertyPair(
                ref rect,
                line,
                spacing,
                view.CompressionQuality,
                AtlasPipelineUi.CompressionQuality,
                view.AtlasGranularity,
                AtlasPipelineUi.AtlasGranularity);

            // ── Direct: atlas composition ──────────────────────────────────────────
            DrawSinglePropertyField(
                ref rect,
                line,
                spacing,
                view.AtlasMax,
                AtlasPipelineUi.AtlasMax);

            DrawPropertyPair(
                ref rect,
                line,
                spacing,
                view.RecommendedMax,
                AtlasPipelineUi.RecommendedMax,
                view.WarnTextureSize,
                AtlasPipelineUi.WarnTextureSize);

            DrawPlatformFormatRow(
                ref rect,
                line,
                spacing,
                view);

            // ── Overrides (per-rule) ────────────────────────────────────────────────
            // All "Inherit" fields live here, and the intro text sits inside the section so the
            // explanation is adjacent to what it explains. Two kinds of Inherit share this
            // section: the project-wide settings (Include In Build, Alpha Dilation, Atlas
            // Rotation) and the per-rule refinements (Per-Platform Atlas Size). New override
            // fields added in the future go in this same group.
            DrawSectionLabel(
                ref rect,
                line,
                spacing,
                "Overrides (per-rule)");

            // The intro's height is measured with CalcHeight so wide windows get one line and
            // narrow windows get two — no fixed two-row reservation, which is where the extra
            // blank space under the Inherit text came from. Measuring against rect.width also
            // seeds the cache the height callback reads, so both agree on the line count.
            float introHeight = MeasureIntroHeight(rect.width);
            EditorGUI.LabelField(
                new Rect(rect.x, rect.y, rect.width, introHeight),
                AtlasPipelineUi.RuleOverridesIntro,
                EditorStyles.wordWrappedMiniLabel);
            rect.y += introHeight + spacing;

            DrawPerPlatformAtlasSizeRow(
                ref rect,
                line,
                spacing,
                view);

            DrawRuleToggleOverrideRow(
                ref rect,
                line,
                spacing,
                view);

            DrawSinglePropertyField(
                ref rect,
                line,
                spacing,
                view.AtlasRotation,
                AtlasPipelineUi.AtlasRotationRule);

            // ── Keywords & Excludes ─────────────────────────────────────────────────
            DrawPathKeywordList(
                ref rect,
                line,
                spacing,
                view.PathKeywords);

            DrawExcludedFolderList(
                ref rect,
                line,
                spacing,
                view.ExcludedFolders);

            DrawExcludedKeywordList(
                ref rect,
                line,
                spacing,
                view.ExcludedKeywords);
        }

        private static void DrawSectionLabel(
            ref Rect rect,
            float line,
            float spacing,
            string label)
        {
            EditorGUI.LabelField(
                new Rect(rect.x, rect.y, rect.width, line),
                label,
                EditorStyles.miniBoldLabel);
            NextLine(ref rect, line, spacing);
        }

        /// <summary>
        /// Per-platform atlas size overrides. Zero means "inherit the rule's Atlas Max", shown as
        /// "Inherit" so the sentinel value never appears as a bare number.
        /// </summary>
        private static void DrawPerPlatformAtlasSizeRow(
            ref Rect rect,
            float line,
            float spacing,
            RuleView view)
        {
            EditorGUI.LabelField(
                new Rect(rect.x, rect.y, rect.width, line),
                "Atlas Size Per Platform",
                EditorStyles.miniBoldLabel);
            NextLine(ref rect, line, spacing);

            SerializedProperty[] sizeProperties =
            {
                view.AndroidSize,
                view.IphoneSize,
                view.WebglSize,
                view.StandaloneSize,
            };
            GUIContent[] labels = AtlasPipelineUi.PlatformLabels;

            float columnGap = 6f;
            float columnWidth = (rect.width - columnGap * 3f) / 4f;
            for (int i = 0; i < sizeProperties.Length; i++)
            {
                DrawAtlasSizePopup(
                    new Rect(rect.x + (columnWidth + columnGap) * i, rect.y, columnWidth, line),
                    sizeProperties[i],
                    labels[i]);
            }

            NextLine(ref rect, line, spacing);
        }

        /// <summary>
        /// The two tri-state toggles that override global defaults: include-in-build and alpha
        /// dilation. Drawn as one row because neither needs a full line of its own.
        /// </summary>
        private static void DrawRuleToggleOverrideRow(
            ref Rect rect,
            float line,
            float spacing,
            RuleView view)
        {
            float columnGap = 6f;
            float columnWidth = (rect.width - columnGap) / 2f;

            DrawToggleOverridePopup(
                new Rect(rect.x, rect.y, columnWidth, line),
                view.IncludeInBuild,
                AtlasPipelineUi.IncludeInBuildOverride);
            DrawToggleOverridePopup(
                new Rect(rect.x + columnWidth + columnGap, rect.y, columnWidth, line),
                view.AlphaDilation,
                AtlasPipelineUi.AlphaDilationOverride);

            NextLine(ref rect, line, spacing);
        }

        private static void DrawToggleOverridePopup(
            Rect rect,
            SerializedProperty property,
            GUIContent label)
        {
            var options = AtlasPipelineUi.ToggleOptions;
            int index = Mathf.Clamp(property.enumValueIndex, 0, 2);
            int newIndex = EditorGUI.Popup(rect, label, index, options);
            if (newIndex != index)
            {
                property.enumValueIndex = newIndex;
            }
        }

        private static void DrawAtlasSizePopup(
            Rect rect,
            SerializedProperty property,
            GUIContent label)
        {
            int currentValue = property.intValue;
            int selectedIndex = currentValue > 0
                ? Array.IndexOf(AtlasPipelineUi.SizeValues, currentValue)
                : 0;
            if (selectedIndex < 0)
            {
                // A non-power-of-two value set by hand: show it as-is is impossible in a popup, so
                // fall back to the nearest option and let validation complain about the original.
                selectedIndex = 3;
            }

            var options = AtlasPipelineUi.SizePopupOptions;

            int newIndex = EditorGUI.Popup(rect, label,
                selectedIndex,
                options);
            if (newIndex == selectedIndex)
            {
                return;
            }

            property.intValue = newIndex == 0 ? 0 : AtlasPipelineUi.SizeValues[newIndex - 1];
        }

        private static void DrawPropertyPair(
            ref Rect rect,
            float line,
            float spacing,
            SerializedProperty leftProperty,
            GUIContent leftLabel,
            SerializedProperty rightProperty,
            GUIContent rightLabel)
        {
            float columnGap = 8f;
            float columnWidth = (rect.width - columnGap) * 0.5f;
            Rect leftRect = new Rect(rect.x, rect.y, columnWidth, line);
            Rect rightRect = new Rect(
                rect.x + columnWidth + columnGap,
                rect.y,
                rect.width - columnWidth - columnGap,
                line);

            EditorGUI.PropertyField(leftRect, leftProperty, leftLabel);
            EditorGUI.PropertyField(rightRect, rightProperty, rightLabel);
            NextLine(ref rect, line, spacing);
        }

        private static void DrawSinglePropertyField(
            ref Rect rect,
            float line,
            float spacing,
            SerializedProperty property,
            GUIContent label)
        {
            EditorGUI.PropertyField(
                new Rect(rect.x, rect.y, rect.width, line),
                property,
                label);
            NextLine(ref rect, line, spacing);
        }

        /// <summary>
        /// Draws the per-rule output subfolder as a folder drag target plus the resolved relative
        /// path. The stored value stays a path relative to the shared output root: an absolute folder
        /// reference would let a rule point outside the one output root the global exclusion test and
        /// the orphan sweep are built on, so the picker converts rather than stores.
        /// </summary>
        /// <remarks>
        /// The trailing label is what makes a not-yet-created folder survivable: generation creates
        /// output folders on demand, so a freshly typed subfolder has no asset to show in the object
        /// field, and without the label the value would look empty while it is not.
        /// </remarks>
        private void DrawOutputSubfolderField(
            ref Rect rect,
            float line,
            float spacing,
            SerializedProperty property)
        {
            string outputRoot = AtlasPipeline.Settings != null
                ? AtlasPipeline.Settings.NormalizedOutputAtlasFolder
                : string.Empty;
            string subfolder = property.stringValue ?? string.Empty;

            float fieldWidth = Mathf.Max(120f, rect.width * 0.6f);
            var fieldRect = new Rect(rect.x, rect.y, fieldWidth, line);
            var labelRect = new Rect(
                rect.x + fieldWidth + 4f,
                rect.y,
                Mathf.Max(0f, rect.width - fieldWidth - 4f),
                line);

            var current = string.IsNullOrEmpty(outputRoot)
                ? null
                : AssetDatabase.LoadAssetAtPath<DefaultAsset>(
                    string.IsNullOrEmpty(subfolder)
                        ? outputRoot
                        : outputRoot + "/" + subfolder);

            EditorGUI.BeginChangeCheck();
            UnityEngine.Object picked = EditorGUI.ObjectField(
                fieldRect,
                AtlasPipelineUi.OutputSubfolder,
                current,
                typeof(DefaultAsset),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                ApplyPickedOutputFolder(property, picked, outputRoot);
            }

            EditorGUI.LabelField(
                labelRect,
                new GUIContent(
                    string.IsNullOrEmpty(subfolder) ? "(output root)" : subfolder,
                    AtlasPipelineUi.OutputSubfolder.tooltip));

            NextLine(ref rect, line, spacing);
        }

        /// <summary>
        /// Converts a dropped folder into a subfolder path relative to the output root. Rejections
        /// keep the previous value and explain themselves on the window, because a silent revert
        /// looks exactly like a bug.
        /// </summary>
        private void ApplyPickedOutputFolder(
            SerializedProperty property,
            UnityEngine.Object picked,
            string outputRoot)
        {
            if (picked == null)
            {
                // Cleared: back to writing into the shared root.
                property.stringValue = string.Empty;
                return;
            }

            string path = AtlasPathUtility.Normalize(AssetDatabase.GetAssetPath(picked));

            if (!AssetDatabase.IsValidFolder(path))
            {
                ShowNotification(new GUIContent("Drop a folder, not a file."), 3f);
                return;
            }

            if (string.IsNullOrEmpty(outputRoot))
            {
                ShowNotification(new GUIContent("Set the default output folder first."), 3f);
                return;
            }

            bool isRootItself = string.Equals(path, outputRoot, StringComparison.OrdinalIgnoreCase);
            if (!isRootItself && !AtlasPathUtility.IsUnderFolder(path, outputRoot))
            {
                // The default two-second toast is easy to miss mid-drag, and this rejection is the
                // one place a developer learns the output-root invariant — so state the constraint
                // AND the way out, and hold it on screen longer.
                ShowNotification(new GUIContent(
                    "Output subfolders must be inside '" + outputRoot
                    + "'. Create or pick a subfolder of it, or drop the root itself to write "
                    + "to the root."), 4f);
                return;
            }

            property.stringValue = isRootItself
                ? string.Empty
                : AtlasPathUtility.SanitizeSubfolder(path.Substring(outputRoot.Length + 1));
        }

        private static void DrawMaxTextureSizePopup(
            Rect rect,
            SerializedProperty property,
            GUIContent label)
        {
            int currentValue = property.intValue;
            int selectedIndex = Array.IndexOf(AtlasPipelineUi.SizeValues, currentValue);
            if (selectedIndex < 0)
            {
                selectedIndex = 3;
                property.intValue = AtlasPipelineUi.SizeValues[selectedIndex];
            }

            GUIContent[] options = AtlasPipelineUi.SizeOptions;

            int newIndex = EditorGUI.Popup(rect, label, selectedIndex, options);
            if (newIndex >= 0
                && newIndex < AtlasPipelineUi.SizeValues.Length
                && newIndex != selectedIndex)
            {
                property.intValue = AtlasPipelineUi.SizeValues[newIndex];
            }
        }

        private void DrawPathKeywordList(
            ref Rect rect,
            float line,
            float spacing,
            SerializedProperty keywordsProperty)
        {
            EditorGUI.LabelField(
                new Rect(rect.x, rect.y, rect.width, line),
                "Path Keywords",
                EditorStyles.boldLabel);
            NextLine(ref rect, line, spacing);

            for (int i = 0; i < keywordsProperty.arraySize; i++)
            {
                SerializedProperty keywordProperty =
                    keywordsProperty.GetArrayElementAtIndex(i);
                Rect fieldRect = new Rect(rect.x, rect.y, rect.width - 22f, line);
                string currentValue = keywordProperty.stringValue ?? string.Empty;
                keywordProperty.stringValue = EditorGUI.TextField(
                    fieldRect,
                    currentValue);

                if (GUI.Button(new Rect(rect.xMax - 20f, rect.y, 20f, line), "X"))
                {
                    keywordsProperty.DeleteArrayElementAtIndex(i);
                    break;
                }

                NextLine(ref rect, line, spacing);
            }

            if (GUI.Button(new Rect(rect.x, rect.y, rect.width, line), "Add Path Keyword"))
            {
                keywordsProperty.InsertArrayElementAtIndex(keywordsProperty.arraySize);
            }

            NextLine(ref rect, line, spacing);
        }

        private static void DrawPlatformFormatRow(
            ref Rect rect,
            float line,
            float spacing,
            RuleView view)
        {
            if (view.PixelArt.boolValue)
            {
                EditorGUI.LabelField(
                    new Rect(rect.x, rect.y, rect.width, line),
                    "Platform Formats",
                    "RGBA32 (uncompressed, forced by Pixel Art)");
                NextLine(ref rect, line, spacing);
                return;
            }

            float columnGap = 6f;
            float columnWidth = (rect.width - columnGap * 2f) / 3f;

            DrawPlatformFormatPopup(
                new Rect(rect.x, rect.y, columnWidth, line),
                view.AndroidFormat,
                AtlasPipelineUi.FormatAndroid,
                AtlasPlatform.Android);

            DrawPlatformFormatPopup(
                new Rect(rect.x + columnWidth + columnGap, rect.y, columnWidth, line),
                view.IphoneFormat,
                AtlasPipelineUi.FormatIphone,
                AtlasPlatform.Iphone);

            DrawPlatformFormatPopup(
                new Rect(
                    rect.x + (columnWidth + columnGap) * 2f,
                    rect.y,
                    columnWidth,
                    line),
                view.WebglFormat,
                AtlasPipelineUi.FormatWebgl,
                AtlasPlatform.Webgl);

            NextLine(ref rect, line, spacing);
        }

        private static void DrawPlatformFormatPopup(
            Rect rect,
            SerializedProperty property,
            GUIContent label,
            AtlasPlatform platform)
        {
            IReadOnlyList<AtlasTextureFormat> formats =
                AtlasPlatformFormats.GetSupportedFormats(platform);
            if (formats == null || formats.Count == 0)
            {
                return;
            }

            int currentFormatIndex = property.enumValueIndex;
            AtlasTextureFormat currentFormat =
                (AtlasTextureFormat)currentFormatIndex;
            int selectedIndex = AtlasPlatformFormats.GetSupportedFormatIndex(
                platform,
                currentFormat);
            if (selectedIndex < 0)
            {
                AtlasTextureFormat defaultFormat =
                    AtlasPlatformFormats.GetDefaultFormat(platform);
                selectedIndex = AtlasPlatformFormats.GetSupportedFormatIndex(
                    platform,
                    defaultFormat);
                if (selectedIndex < 0)
                {
                    selectedIndex = 0;
                    defaultFormat = formats[0];
                }

                property.enumValueIndex = (int)defaultFormat;
                EditorUtility.SetDirty(property.serializedObject.targetObject);
            }

            GUIContent[] displayNames = AtlasPipelineUi.GetFormatOptions(platform);

            int newIndex = EditorGUI.Popup(
                rect,
                label,
                selectedIndex,
                displayNames);
            if (newIndex < 0 || newIndex >= formats.Count || newIndex == selectedIndex)
            {
                return;
            }

            property.enumValueIndex = (int)formats[newIndex];
        }

        private static void DrawSourceFolderPathLabel(
            Rect rect,
            string sourceFolderPath)
        {
            string displayPath = string.IsNullOrWhiteSpace(sourceFolderPath)
                ? "No folder selected"
                : sourceFolderPath;
            Color previousColor = GUI.color;
            GUI.color = EditorGUIUtility.isProSkin
                ? new Color(0.66f, 0.70f, 0.74f)
                : new Color(0.32f, 0.36f, 0.40f);
            EditorGUI.LabelField(rect, displayPath, EditorStyles.miniLabel);
            GUI.color = previousColor;
        }

        /// <summary>
        /// Draws the Advanced Settings foldout and records a state change. It deliberately does
        /// NOT abort the pass: the caller decides what to draw from the pre-change value, see the
        /// comment at the call site.
        /// <para>
        /// The obvious remedy here was <c>GUIUtility.ExitGUI()</c>, and it does stop the pass —
        /// but it works by throwing, and unwinding an exception through the enclosing scroll view
        /// leaves the GUILayout clip stack unbalanced. That is what produced the stall and the
        /// misplaced layout on toggle. Changing the state and asking for a repaint costs one
        /// frame of the old height and leaves the GUI in a consistent state.
        /// </para>
        /// </summary>
        private void DrawAdvancedFoldout(
            Rect rect,
            int index,
            bool expanded)
        {
            bool newExpanded = EditorGUI.Foldout(
                rect,
                expanded,
                "Advanced Settings",
                true);
            if (newExpanded == expanded)
            {
                return;
            }

            if (newExpanded)
            {
                _expandedRules.Add(index);
            }
            else
            {
                _expandedRules.Remove(index);
            }

            Repaint();
        }

        private void DrawExcludedFolderList(
            ref Rect rect,
            float line,
            float spacing,
            SerializedProperty pathsProperty)
        {
            EditorGUI.LabelField(
                new Rect(rect.x, rect.y, rect.width, line),
                "Excluded Folders",
                EditorStyles.boldLabel);
            NextLine(ref rect, line, spacing);

            for (int i = 0; i < pathsProperty.arraySize; i++)
            {
                SerializedProperty pathProperty = pathsProperty.GetArrayElementAtIndex(i);
                Rect fieldRect = new Rect(rect.x, rect.y, rect.width - 22f, line);
                DrawFolderObjectField(fieldRect, pathProperty, GUIContent.none);

                if (GUI.Button(new Rect(rect.xMax - 20f, rect.y, 20f, line), "X"))
                {
                    pathsProperty.DeleteArrayElementAtIndex(i);
                    break;
                }

                NextLine(ref rect, line, spacing);
            }

            if (GUI.Button(new Rect(rect.x, rect.y, rect.width, line), "Add Excluded Folder"))
            {
                string absolute = EditorUtility.OpenFolderPanel(
                    "Select Excluded Folder",
                    Application.dataPath,
                    string.Empty);
                if (!string.IsNullOrEmpty(absolute))
                {
                    string assetsRoot = Path.GetFullPath(Application.dataPath)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string fullPath = Path.GetFullPath(absolute);
                    string rootWithSeparator = assetsRoot + Path.DirectorySeparatorChar;
                    if (string.Equals(fullPath, assetsRoot, StringComparison.OrdinalIgnoreCase)
                        || fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                    {
                        string assetPath = "Assets"
                                           + fullPath.Substring(assetsRoot.Length)
                                               .Replace('\\', '/');
                        int index = pathsProperty.arraySize;
                        pathsProperty.InsertArrayElementAtIndex(index);
                        pathsProperty.GetArrayElementAtIndex(index).stringValue = assetPath;
                        Repaint();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog(
                            "CycloneGames Atlas Pipeline",
                            "The selected folder must be inside the Unity project Assets folder.",
                            "OK");
                    }
                }
            }

            NextLine(ref rect, line, spacing);
        }

        private void DrawExcludedKeywordList(
            ref Rect rect,
            float line,
            float spacing,
            SerializedProperty keywordsProperty)
        {
            if (keywordsProperty == null)
            {
                return;
            }

            EditorGUI.LabelField(
                new Rect(rect.x, rect.y, rect.width, line),
                "Excluded Keywords",
                EditorStyles.boldLabel);
            NextLine(ref rect, line, spacing);

            for (int i = 0; i < keywordsProperty.arraySize; i++)
            {
                SerializedProperty keywordProperty =
                    keywordsProperty.GetArrayElementAtIndex(i);
                Rect fieldRect = new Rect(rect.x, rect.y, rect.width - 22f, line);
                string currentValue = keywordProperty.stringValue ?? string.Empty;
                keywordProperty.stringValue = EditorGUI.TextField(
                    fieldRect,
                    currentValue);

                if (GUI.Button(new Rect(rect.xMax - 20f, rect.y, 20f, line), "X"))
                {
                    keywordsProperty.DeleteArrayElementAtIndex(i);
                    break;
                }

                NextLine(ref rect, line, spacing);
            }

            if (GUI.Button(new Rect(rect.x, rect.y, rect.width, line), "Add Excluded Keyword"))
            {
                keywordsProperty.InsertArrayElementAtIndex(keywordsProperty.arraySize);
            }

            NextLine(ref rect, line, spacing);
        }

        /// <summary>
        /// Top-level string list of folder paths, with a folder picker that only accepts paths under
        /// Assets/. Used for the global exclusion list.
        /// </summary>
        private void DrawStringFolderList(SerializedProperty pathsProperty)
        {
            for (int i = 0; i < pathsProperty.arraySize; i++)
            {
                EditorGUILayout.BeginHorizontal();
                SerializedProperty pathProperty = pathsProperty.GetArrayElementAtIndex(i);
                string current = pathProperty.stringValue ?? string.Empty;

                DefaultAsset folder = string.IsNullOrEmpty(current)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<DefaultAsset>(current);
                DefaultAsset picked = (DefaultAsset)EditorGUILayout.ObjectField(
                    GUIContent.none,
                    folder,
                    typeof(DefaultAsset),
                    false);
                if (picked != folder && picked != null)
                {
                    string pickedPath = AssetDatabase.GetAssetPath(picked);
                    if (AssetDatabase.IsValidFolder(pickedPath))
                    {
                        pathProperty.stringValue = pickedPath;
                    }
                }

                if (GUILayout.Button("X", GUILayout.Width(22f)))
                {
                    pathsProperty.DeleteArrayElementAtIndex(i);
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add Excluded Folder"))
            {
                string absolute = EditorUtility.OpenFolderPanel(
                    "Select Excluded Folder",
                    Application.dataPath,
                    string.Empty);
                if (!string.IsNullOrEmpty(absolute))
                {
                    string assetsRoot = Path.GetFullPath(Application.dataPath)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string fullPath = Path.GetFullPath(absolute);
                    string rootWithSeparator = assetsRoot + Path.DirectorySeparatorChar;
                    if (string.Equals(fullPath, assetsRoot, StringComparison.OrdinalIgnoreCase)
                        || fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                    {
                        int index = pathsProperty.arraySize;
                        pathsProperty.InsertArrayElementAtIndex(index);
                        pathsProperty.GetArrayElementAtIndex(index).stringValue =
                            "Assets" + fullPath.Substring(assetsRoot.Length).Replace('\\', '/');
                        Repaint();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog(
                            "CycloneGames Atlas Pipeline",
                            "The selected folder must be inside the Unity project Assets folder.",
                            "OK");
                    }
                }
            }
        }

        /// <summary>
        /// Analytic element height, derived from the same section structure
        /// <see cref="DrawRuleAssetElement"/> draws. Analytic rather than measured because the
        /// ReorderableList asks for heights BEFORE the draw pass: a measured height is always one
        /// draw pass behind, which is exactly why expanding a rule used to overlap the next one —
        /// the click frame drew expanded content into a collapsed-height rect.
        /// Every row here is one NextLine advance in the draw code, counted per section so the two
        /// can be diffed side by side when a section changes.
        /// </summary>
        private float ComputeRuleElementHeight(int index)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            // One spacing for the top inset the draw applies before the first row, one for the
            // gap above the card's bottom edge.
            float padding = spacing * 2f + RuleElementBottomPadding;

            if (!_expandedRules.Contains(index))
            {
                return IdentityRows * (line + spacing) + padding;
            }

            AtlasRuleAsset asset = GetRuleAssetAtIndex(index);
            int listEntries = 0;
            if (asset != null)
            {
                RuleView view = GetRuleView(asset);
                listEntries += view.PathKeywords?.arraySize ?? 0;
                listEntries += view.ExcludedFolders?.arraySize ?? 0;
                listEntries += view.ExcludedKeywords?.arraySize ?? 0;
            }

            int rows = IdentityRows
                       + SpriteImportRows
                       + AtlasCompositionRows
                       + OverridesRows
                       + ListCount * PerListChromeRows
                       + listEntries;

            // Before the first draw pass there is no element rect to measure, so seed from the
            // window width; from then on the draw pass keeps _introWidth in sync with the real
            // rect and both sides measure the same width.
            float introWidth = _introWidth > 0f
                ? _introWidth
                : EditorGUIUtility.currentViewWidth - 60f;

            return rows * (line + spacing)
                   + MeasureIntroHeight(introWidth) + spacing
                   + padding;
        }

        /// <summary>
        /// Wrapped height of the overrides intro label at the given width, memoised on that width.
        /// The height callback runs before the draw pass and therefore cannot know the element
        /// rect, so it used to guess with <c>currentViewWidth - 60</c> while the draw measured the
        /// real rect — two widths that differ by the scrollbar, the list's own padding and the
        /// inset the draw applies. Whenever they straddled a wrap boundary the reserved height was
        /// a line off, and the rule body shifted on the next frame. Sharing one cache keyed by
        /// width makes the two sides agree; the only stale frame is the one right after a resize.
        /// </summary>
        private float MeasureIntroHeight(float width)
        {
            if (width <= 0f)
            {
                return EditorGUIUtility.singleLineHeight * 2f;
            }

            if (_introWidth < 0f || Mathf.Abs(width - _introWidth) > 0.5f)
            {
                _introWidth = width;
                _introHeight = EditorStyles.wordWrappedMiniLabel.CalcHeight(
                    AtlasPipelineUi.RuleOverridesIntro,
                    width);
            }

            return _introHeight;
        }

        private AtlasRuleAsset GetRuleAssetAtIndex(int index)
        {
            if (_ruleAssetsProperty == null
                || index < 0
                || index >= _ruleAssetsProperty.arraySize)
            {
                return null;
            }

            return _ruleAssetsProperty.GetArrayElementAtIndex(index)
                .objectReferenceValue as AtlasRuleAsset;
        }

        private static void NextLine(ref Rect rect, float lineHeight, float spacing)
        {
            rect.y += lineHeight + spacing;
        }

        private static void DrawFolderObjectField(
            SerializedProperty property,
            GUIContent label)
        {
            DrawFolderObjectField(EditorGUILayout.GetControlRect(), property, label);
        }

        private static void DrawFolderObjectField(
            Rect rect,
            SerializedProperty property,
            GUIContent label,
            SerializedProperty guidProperty = null,
            string displayPath = null)
        {
            // displayPath takes precedence: it is the current GUID-resolved path. After a folder
            // rename, the serialized field still holds the stale path, and LoadAssetAtPath on it
            // returns null so the ObjectField renders empty — the root cause of the "reference
            // disappeared" symptom.
            string currentPath = string.IsNullOrEmpty(displayPath)
                ? property.stringValue ?? string.Empty
                : displayPath;
            DefaultAsset currentFolder = string.IsNullOrEmpty(currentPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<DefaultAsset>(currentPath);

            DefaultAsset newFolder = (DefaultAsset)EditorGUI.ObjectField(
                rect,
                label,
                currentFolder,
                typeof(DefaultAsset),
                false);

            if (newFolder == currentFolder)
            {
                return;
            }

            string newPath = newFolder != null
                ? AssetDatabase.GetAssetPath(newFolder)
                : string.Empty;
            if (!string.IsNullOrEmpty(newPath) && !AssetDatabase.IsValidFolder(newPath))
            {
                EditorUtility.DisplayDialog(
                    "CycloneGames Atlas Pipeline",
                    "Only folders under Assets/ can be used by the CycloneGames atlas pipeline.",
                    "OK");
                return;
            }

            property.stringValue = newPath;
            if (guidProperty != null)
            {
                // Write path and GUID together: the GUID is the stable reference, the path is only
                // for display / fallback.
                guidProperty.stringValue = string.IsNullOrEmpty(newPath)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(newPath);
            }
        }

        private string ResolveRuleSourceFolderPath(int index)
        {
            if (_settingsObject == null || _settingsObject.targetObject == null)
            {
                return string.Empty;
            }

            var settings = (AtlasPipelineSettings)_settingsObject.targetObject;
            IReadOnlyList<AtlasRuleAsset> assets = settings.RuleAssets;
            if (index < 0 || index >= assets.Count)
            {
                return string.Empty;
            }

            // Show the current GUID-resolved path, so a renamed folder displays its new path here
            // rather than the stale serialized string.
            return assets[index]?.Rule?.NormalizedSourceFolder ?? string.Empty;
        }

        private void SaveSettingsAsset()
        {
            if (_settingsObject == null || _settingsObject.targetObject == null)
            {
                return;
            }

            _settingsObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(_settingsObject.targetObject);
            AssetDatabase.SaveAssets();
        }

        private void ScheduleSettingsSave()
        {
            if (_settingsSaveScheduled)
            {
                return;
            }

            _settingsSaveScheduled = true;
            EditorApplication.delayCall += () =>
            {
                _settingsSaveScheduled = false;
                SaveSettingsAsset();
            };
        }
    }
}
