using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

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
        private SerializedProperty _blockOffsetProperty;
        private SerializedProperty _includeInBuildProperty;
        private SerializedProperty _asciiOnlyNamesProperty;
        private SerializedProperty _rulesProperty;

        private ReorderableList _rulesList;
        private Vector2 _scrollPosition;
        private bool _showGeneral = true;
        private bool _showRules = true;
        private bool _showPacking = true;
        private bool _showValidation = true;
        private string _feedbackTitle = string.Empty;
        private string _feedbackMessage = string.Empty;
        private bool _settingsSaveScheduled;
        private readonly HashSet<int> _expandedRules = new HashSet<int>();
        private IReadOnlyList<string> _cachedValidationErrors = new List<string>();
        private bool _validationCacheDirty = true;
        private static readonly int[] MaxTextureSizeOptions =
        {
            256,
            512,
            1024,
            2048,
            4096,
        };

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

        private void OnProjectChanged()
        {
            // Route through HandleProjectChanged: projectChanged raised by this tool's own batch
            // operations is skipped (the source of the full-rescan feedback loop), while external
            // changes are coalesced through delayCall into a single rescan.
            AtlasPipeline.HandleProjectChanged();
            _validationCacheDirty = true;

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

            var settings = (AtlasPipelineSettings)_settingsObject.targetObject;
            for (int i = 0; i < settings.ImportRules.Count; i++)
            {
                settings.ImportRules[i]?.RefreshResolvedFolder();
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
            for (int i = 0; i < settings.ImportRules.Count; i++)
            {
                AtlasImportRule rule = settings.ImportRules[i];
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
            DrawRules();
            DrawPacking();
            DrawValidation();
            DrawActions();
            DrawFeedback();

            EditorGUILayout.EndScrollView();

            if (_settingsObject.ApplyModifiedProperties())
            {
                HealStaleSourceFolderPaths();
                EditorUtility.SetDirty(_settingsObject.targetObject);
                ScheduleSettingsSave();
                AtlasPipeline.HandleSettingsChanged();
                _validationCacheDirty = true;
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
                new GUIContent("Auto Import Sprites"));
            EditorGUILayout.PropertyField(
                _autoGenerateAtlasesProperty,
                new GUIContent("Auto Generate Atlases"));
            EditorGUILayout.PropertyField(
                _asciiOnlyNamesProperty,
                new GUIContent(
                    "ASCII-Only Names",
                    "When enabled, atlas source file names may only contain ASCII letters, "
                    + "digits, underscores and dashes. Non-ASCII names (Chinese, full-width "
                    + "characters, emoji) enter the rename review flow and block the build "
                    + "validation. Recommended for multi-platform projects."));
            DrawFolderObjectField(
                _outputAtlasFolderProperty,
                new GUIContent("Output Atlas Folder"));
            AtlasInspectorUiUtility.EndPanel();
            EditorGUILayout.Space(4f);
        }

        private void DrawRules()
        {
            _showRules = AtlasInspectorUiUtility.DrawFoldoutHeader(
                "Import Rules",
                _showRules,
                AtlasInspectorUiUtility.ImportColor,
                _rulesProperty != null ? _rulesProperty.arraySize.ToString() + " RULES" : "0 RULES",
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
            EditorGUILayout.PropertyField(
                _atlasPaddingProperty,
                new GUIContent("Padding"));
            EditorGUILayout.PropertyField(
                _blockOffsetProperty,
                new GUIContent("Block Offset"));
            EditorGUILayout.PropertyField(
                _enableRotationProperty,
                new GUIContent(
                    "Default Enable Rotation",
                    "Used when an import rule's Atlas Rotation is set to Inherit. "
                    + "Pixel Art rules always disable rotation regardless of this setting."));
            EditorGUILayout.PropertyField(
                _enableTightPackingProperty,
                new GUIContent("Tight Packing"));
            EditorGUILayout.PropertyField(
                _includeInBuildProperty,
                new GUIContent("Include In Build"));
            AtlasInspectorUiUtility.EndPanel();
            EditorGUILayout.Space(4f);
        }

        private void DrawValidation()
        {
            // Cache the validation result: ValidateForBuild runs RefreshRuleOrder (List.Sort) and a
            // full file-name scan, and calling it every OnGUI frame would drop frames in the window
            // itself. The criteria are unified with includeNameScan: true to match the build step,
            // fixing the previous inconsistency where the window showed READY while the build failed.
            if (_validationCacheDirty)
            {
                _cachedValidationErrors =
                    AtlasPipeline.ValidateForBuild(includeNameScan: true);
                _validationCacheDirty = false;
            }

            IReadOnlyList<string> errors = _cachedValidationErrors;
            bool valid = errors.Count == 0;
            _showValidation = AtlasInspectorUiUtility.DrawFoldoutHeader(
                "Build Validation",
                _showValidation,
                valid ? AtlasInspectorUiUtility.SuccessColor : AtlasInspectorUiUtility.WarningColor,
                valid ? "READY" : errors.Count + (errors.Count == 1 ? " ISSUE" : " ISSUES"),
                valid ? AtlasInspectorUiUtility.SuccessColor : AtlasInspectorUiUtility.WarningColor);
            if (!_showValidation)
            {
                return;
            }

            AtlasInspectorUiUtility.BeginPanel();
            AtlasInspectorUiUtility.DrawStatusRow(
                "Validation",
                valid ? "Passed" : errors.Count + (errors.Count == 1 ? " issue" : " issues"),
                valid ? AtlasInspectorUiUtility.SuccessColor : AtlasInspectorUiUtility.WarningColor);

            if (!valid)
            {
                for (int i = 0; i < errors.Count; i++)
                {
                    EditorGUILayout.HelpBox(errors[i], MessageType.Warning);
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
            _blockOffsetProperty = _settingsObject.FindProperty("blockOffset");
            _includeInBuildProperty = _settingsObject.FindProperty("includeInBuild");
            _asciiOnlyNamesProperty = _settingsObject.FindProperty("asciiOnlyNames");
            _rulesProperty = _settingsObject.FindProperty("importRules");
        }

        private void BuildRulesList()
        {
            if (_settingsObject == null || _rulesProperty == null)
            {
                return;
            }

            _rulesList = new ReorderableList(
                _settingsObject,
                _rulesProperty,
                true,
                true,
                true,
                true);
            _rulesList.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(
                    rect,
                    $"Sprite Import Rules ({_rulesProperty.arraySize})",
                    EditorStyles.boldLabel);
            };
            _rulesList.elementHeightCallback = ComputeRuleElementHeight;
            _rulesList.drawElementCallback = DrawRuleElement;
            _rulesList.onAddCallback = list =>
            {
                int index = list.serializedProperty.arraySize;
                list.serializedProperty.InsertArrayElementAtIndex(index);

                SerializedProperty element = list.serializedProperty.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("name").stringValue = $"Rule {index + 1}";
                element.FindPropertyRelative("sourceFolder").stringValue = string.Empty;
                element.FindPropertyRelative("spriteMode").enumValueIndex = 0;
                element.FindPropertyRelative("pixelsPerUnit").floatValue = 100f;
                element.FindPropertyRelative("androidFormat").enumValueIndex =
                    (int)AtlasPlatformFormats.GetDefaultFormat(AtlasPlatform.Android);
                element.FindPropertyRelative("iphoneFormat").enumValueIndex =
                    (int)AtlasPlatformFormats.GetDefaultFormat(AtlasPlatform.Iphone);
                element.FindPropertyRelative("webglFormat").enumValueIndex =
                    (int)AtlasPlatformFormats.GetDefaultFormat(AtlasPlatform.Webgl);
                element.FindPropertyRelative("standaloneFormat").enumValueIndex =
                    (int)AtlasPlatformFormats.GetDefaultFormat(AtlasPlatform.Standalone);
                element.FindPropertyRelative("pixelArt").boolValue = false;
                element.FindPropertyRelative("mipmaps").boolValue = false;
                element.FindPropertyRelative("readable").boolValue = false;
                element.FindPropertyRelative("filterMode").intValue = (int)FilterMode.Bilinear;
                element.FindPropertyRelative("wrapMode").intValue = (int)TextureWrapMode.Clamp;
                element.FindPropertyRelative("compressionQuality").intValue =
                    AtlasPlatformFormats.DefaultCompressionQuality;
                element.FindPropertyRelative("atlasGranularity").enumValueIndex = (int)AtlasGranularity.PerSourceFolder;
                element.FindPropertyRelative("recommendedMaxTextureSize").intValue = 2048;
                element.FindPropertyRelative("atlasMaxTextureSize").intValue = 2048;
                element.FindPropertyRelative("warnTextureSize").boolValue = true;
                element.FindPropertyRelative("atlasRotationMode").enumValueIndex =
                    (int)AtlasRotationMode.Inherit;
                element.FindPropertyRelative("atlasGroup").stringValue = "General";
                element.FindPropertyRelative("pathKeywords").arraySize = 0;
                element.FindPropertyRelative("excludedFolderPaths").arraySize = 0;
                element.FindPropertyRelative("excludedNameKeywords").arraySize = 0;
            };
        }

        private void DrawRuleElement(
            Rect rect,
            int index,
            bool isActive,
            bool isFocused)
        {
            if (_rulesProperty == null)
            {
                return;
            }

            SerializedProperty element = _rulesProperty.GetArrayElementAtIndex(index);
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            rect.x += 2f;
            rect.width -= 4f;
            rect.y += spacing;

            DrawPropertyPair(
                ref rect,
                line,
                spacing,
                element.FindPropertyRelative("name"),
                new GUIContent("Rule Name"),
                element.FindPropertyRelative("atlasGroup"),
                new GUIContent("Atlas Group"));

            SerializedProperty sourceFolderProperty =
                element.FindPropertyRelative("sourceFolder");
            SerializedProperty sourceFolderGuidProperty =
                element.FindPropertyRelative("sourceFolderGuid");
            DrawFolderObjectField(
                new Rect(rect.x, rect.y, rect.width, line),
                sourceFolderProperty,
                new GUIContent(
                    "Source Folder",
                    "The folder reference is stored by GUID, so renaming the folder "
                    + "in the Project window keeps the rule pointing at it."),
                sourceFolderGuidProperty,
                ResolveRuleSourceFolderPath(index));
            NextLine(ref rect, line, spacing);

            DrawSourceFolderPathLabel(
                new Rect(rect.x + 16f, rect.y, rect.width - 16f, line),
                ResolveRuleSourceFolderPath(index));
            NextLine(ref rect, line, spacing);

            bool expanded = _expandedRules.Contains(index);
            DrawAdvancedFoldout(
                new Rect(rect.x, rect.y, rect.width, line),
                index,
                expanded);
            NextLine(ref rect, line, spacing);

            if (!expanded)
            {
                return;
            }

            DrawPropertyPair(
                ref rect,
                line,
                spacing,
                element.FindPropertyRelative("spriteMode"),
                new GUIContent("Sprite Mode"),
                element.FindPropertyRelative("pixelsPerUnit"),
                new GUIContent("Pixels Per Unit"));

            DrawSinglePropertyField(
                ref rect,
                line,
                spacing,
                element.FindPropertyRelative("pixelArt"),
                new GUIContent(
                    "Pixel Art (Uncompressed)",
                    "Forces both the source texture and generated atlas to RGBA32 "
                    + "(uncompressed) on all platforms, avoiding compressed-source "
                    + "packing artifacts for pixel art."));

            DrawPlatformFormatRow(
                ref rect,
                line,
                spacing,
                element);

            DrawSinglePropertyField(
                ref rect,
                line,
                spacing,
                element.FindPropertyRelative("atlasRotationMode"),
                new GUIContent(
                    "Atlas Rotation",
                    "Inherit uses the global default; Enabled forces rotation; "
                    + "Disabled disables rotation for this rule. Pixel Art rules "
                    + "always disable rotation to avoid non-integer texel sampling."));

            DrawPathKeywordList(
                ref rect,
                line,
                spacing,
                element.FindPropertyRelative("pathKeywords"));

            DrawPropertyPair(
                ref rect,
                line,
                spacing,
                element.FindPropertyRelative("mipmaps"),
                new GUIContent("Mipmaps"),
                element.FindPropertyRelative("readable"),
                new GUIContent("Readable"));

            DrawPropertyPair(
                ref rect,
                line,
                spacing,
                element.FindPropertyRelative("filterMode"),
                new GUIContent("Filter Mode"),
                element.FindPropertyRelative("wrapMode"),
                new GUIContent("Wrap Mode"));

            DrawPropertyPair(
                ref rect,
                line,
                spacing,
                element.FindPropertyRelative("compressionQuality"),
                new GUIContent("Compression Quality"),
                element.FindPropertyRelative("atlasGranularity"),
                new GUIContent("Atlas Granularity"));

            DrawMaxTextureSizePair(
                ref rect,
                line,
                spacing,
                element);

            DrawSinglePropertyField(
                ref rect,
                line,
                spacing,
                element.FindPropertyRelative("warnTextureSize"),
                new GUIContent(
                    "Warn Texture Size",
                    "When enabled, oversized source textures are reported in a single dialog."));

            DrawExcludedFolderList(
                ref rect,
                line,
                spacing,
                element.FindPropertyRelative("excludedFolderPaths"));

            DrawExcludedKeywordList(
                ref rect,
                line,
                spacing,
                element.FindPropertyRelative("excludedNameKeywords"));
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

        private static void DrawMaxTextureSizePair(
            ref Rect rect,
            float line,
            float spacing,
            SerializedProperty element)
        {
            float columnGap = 8f;
            float columnWidth = (rect.width - columnGap) * 0.5f;
            Rect leftRect = new Rect(rect.x, rect.y, columnWidth, line);
            Rect rightRect = new Rect(
                rect.x + columnWidth + columnGap,
                rect.y,
                rect.width - columnWidth - columnGap,
                line);

            DrawMaxTextureSizePopup(
                leftRect,
                element.FindPropertyRelative("recommendedMaxTextureSize"),
                new GUIContent(
                    "Recommended Max",
                    "Maximum source texture size before the importer warns the developer."));
            DrawMaxTextureSizePopup(
                rightRect,
                element.FindPropertyRelative("atlasMaxTextureSize"),
                new GUIContent(
                    "Atlas Max",
                    "Maximum generated SpriteAtlas texture size."));

            NextLine(ref rect, line, spacing);
        }

        private static void DrawMaxTextureSizePopup(
            Rect rect,
            SerializedProperty property,
            GUIContent label)
        {
            int currentValue = property.intValue;
            int selectedIndex = Array.IndexOf(MaxTextureSizeOptions, currentValue);
            if (selectedIndex < 0)
            {
                selectedIndex = 3;
                property.intValue = MaxTextureSizeOptions[selectedIndex];
            }

            GUIContent[] options = new GUIContent[MaxTextureSizeOptions.Length];
            for (int i = 0; i < MaxTextureSizeOptions.Length; i++)
            {
                options[i] = new GUIContent(MaxTextureSizeOptions[i].ToString());
            }

            int newIndex = EditorGUI.Popup(rect, label, selectedIndex, options);
            if (newIndex >= 0
                && newIndex < MaxTextureSizeOptions.Length
                && newIndex != selectedIndex)
            {
                property.intValue = MaxTextureSizeOptions[newIndex];
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
            SerializedProperty element)
        {
            if (element.FindPropertyRelative("pixelArt").boolValue)
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
                element.FindPropertyRelative("androidFormat"),
                "Android",
                AtlasPlatform.Android,
                "Texture format used by the Android player.");

            DrawPlatformFormatPopup(
                new Rect(rect.x + columnWidth + columnGap, rect.y, columnWidth, line),
                element.FindPropertyRelative("iphoneFormat"),
                "iPhone",
                AtlasPlatform.Iphone,
                "Texture format used by the iOS player.");

            DrawPlatformFormatPopup(
                new Rect(
                    rect.x + (columnWidth + columnGap) * 2f,
                    rect.y,
                    columnWidth,
                    line),
                element.FindPropertyRelative("webglFormat"),
                "WebGL",
                AtlasPlatform.Webgl,
                "Texture format used by WebGL. ASTC is recommended for Android/iOS browsers; "
                + "DXT5/DXT1 is available for desktop browsers.");

            NextLine(ref rect, line, spacing);
        }

        private static void DrawPlatformFormatPopup(
            Rect rect,
            SerializedProperty property,
            string label,
            AtlasPlatform platform,
            string tooltip)
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

            GUIContent[] displayNames = new GUIContent[formats.Count];
            for (int i = 0; i < formats.Count; i++)
            {
                displayNames[i] = new GUIContent(
                    AtlasPlatformFormats.GetDisplayName(formats[i]));
            }

            int newIndex = EditorGUI.Popup(
                rect,
                new GUIContent(label, tooltip),
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

        private float ComputeRuleElementHeight(int index)
        {
            const int baseRowCount = 4;
            int pathKeywordCount = 0;
            int excludedFolderCount = 0;
            int excludedKeywordCount = 0;
            if (_rulesProperty != null && index >= 0 && index < _rulesProperty.arraySize)
            {
                SerializedProperty element = _rulesProperty.GetArrayElementAtIndex(index);
                SerializedProperty pathKeywords =
                    element.FindPropertyRelative("pathKeywords");
                if (pathKeywords != null)
                {
                    pathKeywordCount = pathKeywords.arraySize;
                }

                SerializedProperty paths = element.FindPropertyRelative("excludedFolderPaths");
                if (paths != null)
                {
                    excludedFolderCount = paths.arraySize;
                }

                SerializedProperty keywords =
                    element.FindPropertyRelative("excludedNameKeywords");
                if (keywords != null)
                {
                    excludedKeywordCount = keywords.arraySize;
                }
            }

            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            int rowCount = _expandedRules.Contains(index)
                ? baseRowCount + 15 + pathKeywordCount
                                  + excludedFolderCount + excludedKeywordCount
                : baseRowCount;
            return rowCount * (line + spacing) + spacing * 2f + 8f;
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
            if (index < 0 || index >= settings.ImportRules.Count)
            {
                return string.Empty;
            }

            // Show the current GUID-resolved path, so a renamed folder displays its new path here
            // rather than the stale serialized string.
            return settings.ImportRules[index]?.NormalizedSourceFolder ?? string.Empty;
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
