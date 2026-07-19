using System;
using System.Collections.Generic;
using System.Globalization;
using CycloneGames.Localization.Runtime;
using CycloneGames.UIFramework.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Runtime.Integrations.Localization.Editor
{
    [CustomEditor(typeof(UILocaleLayout))]
    [CanEditMultipleObjects]
    public sealed class UILocaleLayoutEditor : UnityEditor.Editor
    {
        private const int MaxDisplayedIssues = 24;
        private const double DiffRefreshIntervalSeconds = 0.5d;

        private static readonly GUIContent MoveUpContent = new GUIContent("Up", "Move this element up.");
        private static readonly GUIContent MoveDownContent = new GUIContent("Down", "Move this element down.");
        private static readonly GUIContent RemoveContent = new GUIContent("Remove", "Remove this element and its parallel snapshot values.");
        private static readonly GUIContent RefreshContent = new GUIContent("Refresh", "Refresh validation and hierarchy differences.");
        private static UILocaleLayoutEditor s_previewOwner;

        private SerializedProperty _baseLocale;
        private SerializedProperty _elements;
        private SerializedProperty _snapshots;

        private bool _showSetup = true;
        private bool _showElements = true;
        private bool _showLocales = true;
        private bool _showValidation = true;
        private int _selectedSnapshotIndex;
        private int _dirtyElementCount;
        private int _errorCount;
        private int _warningCount;
        private int _totalIssueCount;
        private bool _hasUnsupportedFutureSchema;
        private double _nextDiffRefreshTime;
        private string _newLocaleCode = string.Empty;

        private readonly List<string> _issues = new List<string>(MaxDisplayedIssues);
        private GUIContent[] _elementLabels = Array.Empty<GUIContent>();
        private string[] _snapshotLabels = Array.Empty<string>();
        private bool _snapshotLabelsDirty = true;
        private bool _validationDirty = true;

        private LocalizationSettings _localizationSettings;
        private int _localizationSettingsCount;
        private bool _settingsCacheDirty = true;
        private string[] _settingsLocaleCodes = Array.Empty<string>();
        private string[] _settingsLocaleLabels = Array.Empty<string>();

        private bool _isPreviewing;

        private void OnEnable()
        {
            _baseLocale = serializedObject.FindProperty("_baseLocale");
            _elements = serializedObject.FindProperty("_elements");
            _snapshots = serializedObject.FindProperty("_snapshots");

            Undo.undoRedoPerformed += HandleExternalChange;
            EditorApplication.hierarchyChanged += HandleExternalChange;
            EditorApplication.projectChanged += HandleProjectChange;
            EditorApplication.playModeStateChanged += HandlePlayModeChange;
            AssemblyReloadEvents.beforeAssemblyReload += ExitPreviewBeforeReload;
            PrefabStage.prefabSaving += HandlePrefabSaving;

            serializedObject.Update();
            RefreshSettingsCache();
            RefreshValidation();
            RefreshDifferences();
        }

        private void OnDisable()
        {
            ExitPreview();
            Undo.undoRedoPerformed -= HandleExternalChange;
            EditorApplication.hierarchyChanged -= HandleExternalChange;
            EditorApplication.projectChanged -= HandleProjectChange;
            EditorApplication.playModeStateChanged -= HandlePlayModeChange;
            AssemblyReloadEvents.beforeAssemblyReload -= ExitPreviewBeforeReload;
            PrefabStage.prefabSaving -= HandlePrefabSaving;
        }

        public override void OnInspectorGUI()
        {
            SynchronizePreviewState();
            serializedObject.Update();
            EnsureSettingsCache();

            if (_validationDirty)
            {
                RefreshValidation();
            }

            InspectorUiUtility.DrawInspectorTitle(
                "UI Locale Layout",
                "Event-driven per-locale layout snapshots",
                InspectorUiUtility.AssetColor);

            if (serializedObject.isEditingMultipleObjects)
            {
                DrawMultiObjectInspector();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            DrawStatusPanel();

            if (_hasUnsupportedFutureSchema)
            {
                EditorGUILayout.HelpBox(
                    "This layout contains a snapshot schema newer than this editor understands. " +
                    "Authoring actions are disabled so the unknown data cannot be rewritten or downgraded.",
                    MessageType.Error);
            }

            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(_isPreviewing || _hasUnsupportedFutureSchema))
            {
                DrawSetupSection();
                DrawTrackedElementsSection();
            }
            DrawLocaleSnapshotsSection();
            bool changed = EditorGUI.EndChangeCheck();

            serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                InvalidateInspection();
                serializedObject.Update();
                RefreshValidation();
            }

            if (!_isPreviewing &&
                Event.current.type == EventType.Layout &&
                EditorApplication.timeSinceStartup >= _nextDiffRefreshTime)
            {
                RefreshDifferences();
                _nextDiffRefreshTime = EditorApplication.timeSinceStartup + DiffRefreshIntervalSeconds;
            }

            DrawValidationSection();
        }

        private void DrawMultiObjectInspector()
        {
            bool containsFutureSchema = AnyTargetHasUnsupportedFutureSchema();
            EditorGUILayout.HelpBox(
                "Multiple UILocaleLayout components are selected. Base locale editing is supported, " +
                "while element and snapshot actions are disabled to protect parallel snapshot indices.",
                MessageType.Info);

            if (containsFutureSchema)
            {
                EditorGUILayout.HelpBox(
                    "At least one selected layout contains an unsupported future schema. Base locale editing " +
                    "is disabled so multi-object serialization cannot rewrite unknown data.",
                    MessageType.Error);
            }

            using (new EditorGUI.DisabledScope(containsFutureSchema))
            {
                EditorGUILayout.PropertyField(_baseLocale, new GUIContent("Base Locale"));
            }
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Selected Layouts", targets.Length);
            }
        }

        private void DrawStatusPanel()
        {
            InspectorUiUtility.BeginPanel();
            string validationLabel;
            Color validationColor;
            if (_errorCount > 0)
            {
                validationLabel = $"{_errorCount} error(s)";
                validationColor = Color.red;
            }
            else if (_warningCount > 0)
            {
                validationLabel = $"{_warningCount} warning(s)";
                validationColor = InspectorUiUtility.WarningColor;
            }
            else
            {
                validationLabel = "Ready";
                validationColor = InspectorUiUtility.SuccessColor;
            }

            InspectorUiUtility.DrawStatusRow(
                "Base locale",
                string.IsNullOrEmpty(_baseLocale.stringValue) ? "Not configured" : _baseLocale.stringValue,
                string.IsNullOrEmpty(_baseLocale.stringValue)
                    ? InspectorUiUtility.WarningColor
                    : InspectorUiUtility.SuccessColor);
            InspectorUiUtility.DrawStatusRow(
                "Tracked elements",
                _elements.arraySize.ToString(),
                _elements.arraySize > 0 ? InspectorUiUtility.AssetColor : InspectorUiUtility.NeutralColor);
            InspectorUiUtility.DrawStatusRow(
                "Locale overrides",
                _snapshots.arraySize.ToString(),
                _snapshots.arraySize > 0 ? InspectorUiUtility.RuntimeColor : InspectorUiUtility.NeutralColor);
            if (_hasUnsupportedFutureSchema)
            {
                InspectorUiUtility.DrawStatusRow("Schema", "Unsupported", Color.red);
            }
            InspectorUiUtility.DrawStatusRow("Validation", validationLabel, validationColor);
            InspectorUiUtility.EndPanel();
        }

        private void DrawSetupSection()
        {
            _showSetup = InspectorUiUtility.DrawFoldoutHeader(
                "Locale Source",
                _showSetup,
                InspectorUiUtility.SetupColor,
                _localizationSettings != null ? "Settings linked" : "Manual");
            if (!_showSetup)
            {
                return;
            }

            InspectorUiUtility.BeginPanel();
            if (_localizationSettings != null && _settingsLocaleCodes.Length > 0)
            {
                int currentIndex = FindSettingsLocaleIndex(_baseLocale.stringValue);
                int selected = EditorGUILayout.Popup(
                    "Base Locale",
                    currentIndex,
                    _settingsLocaleLabels);
                if (selected > 0 && selected != currentIndex)
                {
                    _baseLocale.stringValue = _settingsLocaleCodes[selected];
                    _snapshotLabelsDirty = true;
                }

                if (selected == 0)
                {
                    EditorGUILayout.PropertyField(_baseLocale, new GUIContent("Custom Locale Code"));
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(
                        "Localization Settings",
                        _localizationSettings,
                        typeof(LocalizationSettings),
                        false);
                }

                if (_localizationSettingsCount > 1)
                {
                    EditorGUILayout.HelpBox(
                        "Multiple LocalizationSettings assets were found. The lexicographically first asset path is used for authoring suggestions.",
                        MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.PropertyField(_baseLocale, new GUIContent("Base Locale"));
                EditorGUILayout.HelpBox(
                    "No LocalizationSettings asset was found. Locale codes can still be authored manually.",
                    MessageType.Info);
            }

            EditorGUILayout.HelpBox(
                "The prefab hierarchy is the base-locale layout. Overrides are stored only for locales that differ from it.",
                MessageType.None);
            InspectorUiUtility.EndPanel();
        }

        private void DrawTrackedElementsSection()
        {
            _showElements = InspectorUiUtility.DrawFoldoutHeader(
                "Tracked Elements",
                _showElements,
                InspectorUiUtility.AssetColor,
                _elements.arraySize.ToString());
            if (!_showElements)
            {
                return;
            }

            InspectorUiUtility.BeginPanel();
            EnsureElementLabels();

            bool canReorder = CanReorderTrackedElements();
            for (int i = 0; i < _elements.arraySize; i++)
            {
                SerializedProperty element = _elements.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(_elementLabels[i], EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(!canReorder || i == 0))
                {
                    if (GUILayout.Button(MoveUpContent, EditorStyles.miniButton, GUILayout.Width(44f)))
                    {
                        MoveTrackedElement(i, i - 1);
                        GUIUtility.ExitGUI();
                    }
                }

                using (new EditorGUI.DisabledScope(!canReorder || i >= _elements.arraySize - 1))
                {
                    if (GUILayout.Button(MoveDownContent, EditorStyles.miniButton, GUILayout.Width(50f)))
                    {
                        MoveTrackedElement(i, i + 1);
                        GUIUtility.ExitGUI();
                    }
                }

                if (GUILayout.Button(RemoveContent, EditorStyles.miniButton, GUILayout.Width(62f)))
                {
                    RemoveTrackedElement(i);
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.PropertyField(
                    element.FindPropertyRelative(nameof(TrackedElement.Target)),
                    new GUIContent("Rect Transform"));
                EditorGUILayout.PropertyField(
                    element.FindPropertyRelative(nameof(TrackedElement.Text)),
                    new GUIContent("TMP Text", "Optional text metrics, alignment, and RTL direction."));
                EditorGUILayout.PropertyField(
                    element.FindPropertyRelative(nameof(TrackedElement.LayoutGroup)),
                    new GUIContent("Layout Group", "Optional child alignment source."));
                EditorGUILayout.EndVertical();
            }

            if (_elements.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "Track an element from its component context menu or use discovery below.",
                    MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Discover Text and Layout Groups"))
            {
                DiscoverElements();
                GUIUtility.ExitGUI();
            }

            if (GUILayout.Button("Add Empty"))
            {
                AddEmptyTrackedElement();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Remove Missing and Duplicate"))
            {
                CleanTrackedElements();
                GUIUtility.ExitGUI();
            }

            if (GUILayout.Button("Migrate and Normalize Snapshots"))
            {
                NormalizeSnapshotsWithConfirmation();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            if (!canReorder && _elements.arraySize > 1)
            {
                EditorGUILayout.HelpBox(
                    "Reordering is disabled while a snapshot length differs from the tracked-element count. Normalize snapshots first.",
                    MessageType.Warning);
            }
            InspectorUiUtility.EndPanel();
        }

        private void DrawLocaleSnapshotsSection()
        {
            _showLocales = InspectorUiUtility.DrawFoldoutHeader(
                "Locale Overrides",
                _showLocales,
                InspectorUiUtility.RuntimeColor,
                _snapshots.arraySize.ToString());
            if (!_showLocales)
            {
                return;
            }

            InspectorUiUtility.BeginPanel();
            if (_snapshots.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "Add an override locale, apply it for editing, adjust the hierarchy, and capture the result.",
                    MessageType.Info);
                using (new EditorGUI.DisabledScope(_hasUnsupportedFutureSchema))
                {
                    DrawAddLocaleControls();
                }
                InspectorUiUtility.EndPanel();
                return;
            }

            EnsureSnapshotLabels();
            _selectedSnapshotIndex = Mathf.Clamp(
                _selectedSnapshotIndex,
                0,
                _snapshots.arraySize - 1);

            int selected = EditorGUILayout.Popup(
                "Editing Locale",
                _selectedSnapshotIndex,
                _snapshotLabels);
            if (selected != _selectedSnapshotIndex)
            {
                ExitPreview();
                _selectedSnapshotIndex = selected;
                RefreshDifferences();
            }

            SerializedProperty selectedSnapshot = _snapshots.GetArrayElementAtIndex(_selectedSnapshotIndex);
            int schemaVersion = selectedSnapshot
                .FindPropertyRelative(nameof(LocaleSnapshot.SchemaVersion))
                .intValue;
            string schemaLabel;
            Color schemaColor;
            if (schemaVersion > LocaleSnapshot.CurrentSchemaVersion)
            {
                schemaLabel = $"Unsupported {schemaVersion}";
                schemaColor = Color.red;
            }
            else if (schemaVersion == LocaleSnapshot.CurrentSchemaVersion)
            {
                schemaLabel = $"Schema {schemaVersion}";
                schemaColor = InspectorUiUtility.SuccessColor;
            }
            else
            {
                schemaLabel = "Legacy schema";
                schemaColor = InspectorUiUtility.WarningColor;
            }

            EditorGUILayout.BeginHorizontal();
            InspectorUiUtility.DrawStatusBadge(schemaLabel, schemaColor, 112f);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(RefreshContent, EditorStyles.miniButton, GUILayout.Width(70f)))
            {
                RefreshValidation();
                RefreshDifferences();
            }
            EditorGUILayout.EndHorizontal();

            if (!_isPreviewing && _dirtyElementCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{_dirtyElementCount} tracked element(s) differ from the selected locale snapshot.",
                    MessageType.Warning);
            }

            if (_isPreviewing)
            {
                EditorGUILayout.HelpBox(
                    "Preview mode is temporary. It is restored before prefab save, play mode, domain reload, or inspector close.",
                    MessageType.Info);
            }

            bool canApply = CanApplySelectedSnapshot(out string applyBlockedReason);
            if (!canApply)
            {
                EditorGUILayout.HelpBox(applyBlockedReason, MessageType.Warning);
            }

            bool externalAnimationMode = AnimationMode.InAnimationMode() &&
                                         s_previewOwner != this;
            if (externalAnimationMode)
            {
                EditorGUILayout.HelpBox(
                    "Another Unity animation preview is active. Stop it before previewing this locale layout.",
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(_isPreviewing || _hasUnsupportedFutureSchema))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Capture Current Hierarchy"))
                {
                    CaptureSelectedSnapshot();
                    GUIUtility.ExitGUI();
                }

                using (new EditorGUI.DisabledScope(!canApply))
                {
                    if (GUILayout.Button("Apply for Editing"))
                    {
                        ApplySelectedSnapshotForEditing();
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(
                       !_isPreviewing &&
                       (_hasUnsupportedFutureSchema || !canApply || externalAnimationMode)))
            {
                if (GUILayout.Button(_isPreviewing ? "Exit Preview" : "Preview Snapshot"))
                {
                    if (_isPreviewing)
                    {
                        ExitPreview();
                    }
                    else
                    {
                        EnterPreview();
                    }
                    GUIUtility.ExitGUI();
                }
            }

            using (new EditorGUI.DisabledScope(_isPreviewing || _hasUnsupportedFutureSchema))
            {
                if (GUILayout.Button("Remove Locale Override"))
                {
                    RemoveSelectedSnapshot();
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(_isPreviewing || _hasUnsupportedFutureSchema))
            {
                DrawAddLocaleControls();
            }
            InspectorUiUtility.EndPanel();
        }

        private void DrawAddLocaleControls()
        {
            EditorGUILayout.BeginHorizontal();
            _newLocaleCode = EditorGUILayout.TextField("New Locale", _newLocaleCode);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newLocaleCode)))
            {
                if (GUILayout.Button("Add", GUILayout.Width(52f)))
                {
                    AddLocale(_newLocaleCode.Trim());
                    _newLocaleCode = string.Empty;
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_localizationSettings != null && GUILayout.Button("Add from Localization Settings"))
            {
                ShowAddLocaleMenu();
            }
        }

        private void DrawValidationSection()
        {
            _showValidation = InspectorUiUtility.DrawFoldoutHeader(
                "Validation",
                _showValidation,
                _errorCount > 0
                    ? new Color(0.72f, 0.2f, 0.2f)
                    : InspectorUiUtility.WarningColor,
                _totalIssueCount == 0 ? "Ready" : _totalIssueCount.ToString());
            if (!_showValidation)
            {
                return;
            }

            InspectorUiUtility.BeginPanel();
            if (_totalIssueCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "No authoring errors or warnings were detected.",
                    MessageType.Info);
            }
            else
            {
                for (int i = 0; i < _issues.Count; i++)
                {
                    string issue = _issues[i];
                    MessageType type = issue.StartsWith("Error:", StringComparison.Ordinal)
                        ? MessageType.Error
                        : MessageType.Warning;
                    EditorGUILayout.HelpBox(issue, type);
                }

                if (_totalIssueCount > _issues.Count)
                {
                    EditorGUILayout.HelpBox(
                        $"{_totalIssueCount - _issues.Count} additional issue(s) are not displayed.",
                        MessageType.Warning);
                }
            }

            if (GUILayout.Button("Run Validation"))
            {
                RefreshValidation();
                RefreshDifferences();
            }
            InspectorUiUtility.EndPanel();
        }

        private void DiscoverElements()
        {
            if (!CanModifyAuthoring("discover tracked elements"))
            {
                return;
            }

            ExitPreview();
            UILocaleLayout layout = (UILocaleLayout)target;
            Undo.RecordObject(layout, "Discover Locale Layout Elements");

            List<TMP_Text> texts = new List<TMP_Text>(16);
            List<LayoutGroup> layoutGroups = new List<LayoutGroup>(8);
            layout.GetComponentsInChildren(true, texts);
            layout.GetComponentsInChildren(true, layoutGroups);

            for (int i = 0; i < texts.Count; i++)
            {
                TMP_Text text = texts[i];
                AppendTrackedElement(
                    text.rectTransform,
                    text,
                    text.GetComponent<LayoutGroup>());
            }

            for (int i = 0; i < layoutGroups.Count; i++)
            {
                LayoutGroup group = layoutGroups[i];
                AppendTrackedElement(
                    group.transform as RectTransform,
                    group.GetComponent<TMP_Text>(),
                    group);
            }

            serializedObject.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(layout);
            EditorUtility.SetDirty(layout);
            MarkSceneDirty(layout.gameObject);
            InvalidateInspection();
        }

        private void AppendTrackedElement(
            RectTransform rect,
            TMP_Text text,
            LayoutGroup layoutGroup)
        {
            if (rect == null || ContainsTrackedTarget(rect))
            {
                return;
            }

            int index = _elements.arraySize;
            _elements.InsertArrayElementAtIndex(index);
            SerializedProperty element = _elements.GetArrayElementAtIndex(index);
            ClearTrackedElement(element);
            element.FindPropertyRelative(nameof(TrackedElement.Target)).objectReferenceValue = rect;
            element.FindPropertyRelative(nameof(TrackedElement.Text)).objectReferenceValue = text;
            element.FindPropertyRelative(nameof(TrackedElement.LayoutGroup)).objectReferenceValue = layoutGroup;
        }

        private void AddEmptyTrackedElement()
        {
            if (!CanModifyAuthoring("add a tracked element"))
            {
                return;
            }

            ExitPreview();
            UILocaleLayout layout = (UILocaleLayout)target;
            Undo.RecordObject(layout, "Add Locale Layout Element");
            int index = _elements.arraySize;
            _elements.InsertArrayElementAtIndex(index);
            ClearTrackedElement(_elements.GetArrayElementAtIndex(index));
            serializedObject.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(layout);
            EditorUtility.SetDirty(layout);
            MarkSceneDirty(layout.gameObject);
            InvalidateInspection();
        }

        private void RemoveTrackedElement(int index)
        {
            if (!CanModifyAuthoring("remove a tracked element"))
            {
                return;
            }

            ExitPreview();
            UILocaleLayout layout = (UILocaleLayout)target;
            Undo.RecordObject(layout, "Remove Locale Layout Element");

            for (int snapshotIndex = 0; snapshotIndex < _snapshots.arraySize; snapshotIndex++)
            {
                SerializedProperty snapshotElements = _snapshots
                    .GetArrayElementAtIndex(snapshotIndex)
                    .FindPropertyRelative(nameof(LocaleSnapshot.Elements));
                if (index < snapshotElements.arraySize)
                {
                    snapshotElements.DeleteArrayElementAtIndex(index);
                }
            }

            _elements.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(layout);
            EditorUtility.SetDirty(layout);
            MarkSceneDirty(layout.gameObject);
            InvalidateInspection();
        }

        private void MoveTrackedElement(int sourceIndex, int destinationIndex)
        {
            if (!CanModifyAuthoring("reorder tracked elements"))
            {
                return;
            }

            ExitPreview();
            UILocaleLayout layout = (UILocaleLayout)target;
            Undo.RecordObject(layout, "Reorder Locale Layout Element");
            _elements.MoveArrayElement(sourceIndex, destinationIndex);
            for (int snapshotIndex = 0; snapshotIndex < _snapshots.arraySize; snapshotIndex++)
            {
                SerializedProperty snapshotElements = _snapshots
                    .GetArrayElementAtIndex(snapshotIndex)
                    .FindPropertyRelative(nameof(LocaleSnapshot.Elements));
                snapshotElements.MoveArrayElement(sourceIndex, destinationIndex);
            }

            serializedObject.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(layout);
            EditorUtility.SetDirty(layout);
            MarkSceneDirty(layout.gameObject);
            InvalidateInspection();
        }

        private void CleanTrackedElements()
        {
            if (!CanModifyAuthoring("clean tracked elements"))
            {
                return;
            }

            ExitPreview();
            UILocaleLayout layout = (UILocaleLayout)target;
            Undo.RecordObject(layout, "Clean Locale Layout Elements");
            HashSet<RectTransform> seen = new HashSet<RectTransform>();
            for (int i = _elements.arraySize - 1; i >= 0; i--)
            {
                RectTransform rect = _elements
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative(nameof(TrackedElement.Target))
                    .objectReferenceValue as RectTransform;
                if (rect == null || !seen.Add(rect))
                {
                    for (int snapshotIndex = 0; snapshotIndex < _snapshots.arraySize; snapshotIndex++)
                    {
                        SerializedProperty snapshotElements = _snapshots
                            .GetArrayElementAtIndex(snapshotIndex)
                            .FindPropertyRelative(nameof(LocaleSnapshot.Elements));
                        if (i < snapshotElements.arraySize)
                        {
                            snapshotElements.DeleteArrayElementAtIndex(i);
                        }
                    }

                    _elements.DeleteArrayElementAtIndex(i);
                }
            }

            serializedObject.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(layout);
            EditorUtility.SetDirty(layout);
            MarkSceneDirty(layout.gameObject);
            InvalidateInspection();
        }

        private void NormalizeSnapshotsWithConfirmation()
        {
            if (!CanModifyAuthoring("migrate snapshots"))
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Migrate Locale Layout Snapshots",
                "Every snapshot will be resized to the tracked-element count. Legacy snapshots will " +
                "preserve their original fields and use the current hierarchy for newly supported anchor, " +
                "pivot, scale, alignment, and RTL values. Review each locale after migration.",
                "Migrate and Normalize",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            ExitPreview();
            UILocaleLayout layout = (UILocaleLayout)target;
            Undo.RecordObject(layout, "Migrate Locale Layout Snapshots");

            for (int snapshotIndex = 0; snapshotIndex < _snapshots.arraySize; snapshotIndex++)
            {
                SerializedProperty localeSnapshot = _snapshots.GetArrayElementAtIndex(snapshotIndex);
                SerializedProperty snapshotElements = localeSnapshot.FindPropertyRelative(nameof(LocaleSnapshot.Elements));
                int oldCount = snapshotElements.arraySize;
                int oldSchema = localeSnapshot.FindPropertyRelative(nameof(LocaleSnapshot.SchemaVersion)).intValue;
                ElementSnapshot[] oldValues = new ElementSnapshot[oldCount];
                for (int i = 0; i < oldCount; i++)
                {
                    oldValues[i] = ReadSnapshot(snapshotElements.GetArrayElementAtIndex(i));
                }

                snapshotElements.arraySize = _elements.arraySize;
                for (int i = 0; i < _elements.arraySize; i++)
                {
                    ElementSnapshot value = ElementSnapshot.Capture(ReadTrackedElement(i));
                    if (i < oldValues.Length)
                    {
                        if (oldSchema < LocaleSnapshot.CurrentSchemaVersion)
                        {
                            CopyLegacyFields(ref value, oldValues[i]);
                        }
                        else
                        {
                            value = oldValues[i];
                        }
                    }
                    else if (oldSchema >= LocaleSnapshot.CurrentSchemaVersion)
                    {
                        value = default;
                    }

                    WriteSnapshot(snapshotElements.GetArrayElementAtIndex(i), value);
                }

                localeSnapshot.FindPropertyRelative(nameof(LocaleSnapshot.SchemaVersion)).intValue =
                    LocaleSnapshot.CurrentSchemaVersion;
            }

            serializedObject.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(layout);
            EditorUtility.SetDirty(layout);
            MarkSceneDirty(layout.gameObject);
            InvalidateInspection();
        }

        private void CaptureSelectedSnapshot()
        {
            if (_snapshots.arraySize == 0 || !CanModifyAuthoring("capture a snapshot"))
            {
                return;
            }

            ExitPreview();
            UILocaleLayout layout = (UILocaleLayout)target;
            Undo.RecordObject(layout, "Capture Locale Layout Snapshot");
            SerializedProperty localeSnapshot = _snapshots.GetArrayElementAtIndex(_selectedSnapshotIndex);
            SerializedProperty snapshotElements = localeSnapshot.FindPropertyRelative(nameof(LocaleSnapshot.Elements));
            snapshotElements.arraySize = _elements.arraySize;

            for (int i = 0; i < _elements.arraySize; i++)
            {
                WriteSnapshot(
                    snapshotElements.GetArrayElementAtIndex(i),
                    ElementSnapshot.Capture(ReadTrackedElement(i)));
            }

            localeSnapshot.FindPropertyRelative(nameof(LocaleSnapshot.SchemaVersion)).intValue =
                LocaleSnapshot.CurrentSchemaVersion;
            serializedObject.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(layout);
            EditorUtility.SetDirty(layout);
            MarkSceneDirty(layout.gameObject);
            InvalidateInspection();
            RefreshValidation();
            RefreshDifferences();
        }

        private void ApplySelectedSnapshotForEditing()
        {
            if (_snapshots.arraySize == 0 || !CanModifyAuthoring("apply a snapshot"))
            {
                return;
            }

            if (!CanApplySelectedSnapshot(out string reason))
            {
                Debug.LogWarning($"[UI Locale Layout] {reason}", target);
                return;
            }

            ExitPreview();
            RecordTrackedObjects("Apply Locale Layout Snapshot");
            ApplySelectedSnapshotToHierarchy();
            RecordTrackedPrefabOverrides();
            MarkSceneDirty(((UILocaleLayout)target).gameObject);
            RefreshDifferences();
        }

        private void EnterPreview()
        {
            if (_isPreviewing ||
                _snapshots.arraySize == 0 ||
                !CanModifyAuthoring("preview a snapshot"))
            {
                return;
            }

            if (!CanApplySelectedSnapshot(out string reason))
            {
                Debug.LogWarning($"[UI Locale Layout] {reason}", target);
                return;
            }

            if (AnimationMode.InAnimationMode() || s_previewOwner != null)
            {
                Debug.LogWarning(
                    "[UI Locale Layout] Another Unity animation preview is active. Stop it before previewing this locale layout.",
                    target);
                return;
            }

            try
            {
                AnimationMode.StartAnimationMode();
                s_previewOwner = this;
                RegisterPreviewModifications();
                _isPreviewing = true;
                ApplySelectedSnapshotToHierarchy();
                SceneView.RepaintAll();
            }
            catch (Exception exception)
            {
                if (s_previewOwner == this)
                {
                    s_previewOwner = null;
                    if (AnimationMode.InAnimationMode())
                    {
                        AnimationMode.StopAnimationMode();
                    }
                }

                _isPreviewing = false;
                Debug.LogException(exception, target);
            }
        }

        private void ExitPreview()
        {
            bool ownsAnimationMode = s_previewOwner == this;
            if (!_isPreviewing && !ownsAnimationMode)
            {
                return;
            }

            _isPreviewing = false;
            if (ownsAnimationMode)
            {
                s_previewOwner = null;
                if (AnimationMode.InAnimationMode())
                {
                    AnimationMode.StopAnimationMode();
                }
            }

            SceneView.RepaintAll();
            RefreshDifferences();
        }

        private void ApplySelectedSnapshotToHierarchy()
        {
            SerializedProperty localeSnapshot = _snapshots.GetArrayElementAtIndex(_selectedSnapshotIndex);
            SerializedProperty snapshotElements = localeSnapshot.FindPropertyRelative(nameof(LocaleSnapshot.Elements));
            int schemaVersion = localeSnapshot
                .FindPropertyRelative(nameof(LocaleSnapshot.SchemaVersion))
                .intValue;
            if (schemaVersion != LocaleSnapshot.CurrentSchemaVersion ||
                snapshotElements.arraySize != _elements.arraySize)
            {
                return;
            }

            for (int i = 0; i < _elements.arraySize; i++)
            {
                TrackedElement tracked = ReadTrackedElement(i);
                ElementSnapshot snapshot = ReadSnapshot(snapshotElements.GetArrayElementAtIndex(i));
                if (snapshot.HasValue && IsCurrentSnapshotValueValid(in snapshot, in tracked))
                {
                    snapshot.ApplyTo(in tracked);
                }
            }
        }

        private void RegisterPreviewModifications()
        {
            UILocaleLayout layout = (UILocaleLayout)target;
            Transform root = layout.transform;
            for (int i = 0; i < _elements.arraySize; i++)
            {
                TrackedElement tracked = ReadTrackedElement(i);
                RectTransform rect = tracked.Target;
                RegisterPreviewModification(root, rect, "m_AnchorMin.x", rect.anchorMin.x);
                RegisterPreviewModification(root, rect, "m_AnchorMin.y", rect.anchorMin.y);
                RegisterPreviewModification(root, rect, "m_AnchorMax.x", rect.anchorMax.x);
                RegisterPreviewModification(root, rect, "m_AnchorMax.y", rect.anchorMax.y);
                RegisterPreviewModification(root, rect, "m_Pivot.x", rect.pivot.x);
                RegisterPreviewModification(root, rect, "m_Pivot.y", rect.pivot.y);
                RegisterPreviewModification(root, rect, "m_AnchoredPosition.x", rect.anchoredPosition.x);
                RegisterPreviewModification(root, rect, "m_AnchoredPosition.y", rect.anchoredPosition.y);
                RegisterPreviewModification(root, rect, "m_SizeDelta.x", rect.sizeDelta.x);
                RegisterPreviewModification(root, rect, "m_SizeDelta.y", rect.sizeDelta.y);
                RegisterPreviewModification(root, rect, "m_LocalScale.x", rect.localScale.x);
                RegisterPreviewModification(root, rect, "m_LocalScale.y", rect.localScale.y);
                RegisterPreviewModification(root, rect, "m_LocalScale.z", rect.localScale.z);

                TMP_Text text = tracked.Text;
                if (text != null)
                {
                    RegisterPreviewModification(root, text, "m_fontSize", text.fontSize);
                    RegisterPreviewModification(root, text, "m_lineSpacing", text.lineSpacing);
                    RegisterPreviewModification(root, text, "m_characterSpacing", text.characterSpacing);
                    RegisterPreviewModification(root, text, "m_textAlignment", (int)text.alignment);
                    RegisterPreviewModification(root, text, "m_isRightToLeft", text.isRightToLeftText);
                }

                LayoutGroup layoutGroup = tracked.LayoutGroup;
                if (layoutGroup != null)
                {
                    RegisterPreviewModification(root, layoutGroup, "m_ChildAlignment", (int)layoutGroup.childAlignment);
                }
            }
        }

        private static void RegisterPreviewModification(
            Transform root,
            Component component,
            string propertyPath,
            float value)
        {
            RegisterPreviewModification(
                root,
                component,
                propertyPath,
                value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void RegisterPreviewModification(
            Transform root,
            Component component,
            string propertyPath,
            int value)
        {
            RegisterPreviewModification(
                root,
                component,
                propertyPath,
                value.ToString(CultureInfo.InvariantCulture));
        }

        private static void RegisterPreviewModification(
            Transform root,
            Component component,
            string propertyPath,
            bool value)
        {
            RegisterPreviewModification(root, component, propertyPath, value ? "1" : "0");
        }

        private static void RegisterPreviewModification(
            Transform root,
            Component component,
            string propertyPath,
            string value)
        {
            string transformPath = AnimationUtility.CalculateTransformPath(component.transform, root);
            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                transformPath,
                component.GetType(),
                propertyPath);
            PropertyModification modification = new PropertyModification
            {
                target = component,
                propertyPath = propertyPath,
                value = value
            };
            AnimationMode.AddPropertyModification(binding, modification, false);
        }

        private void SynchronizePreviewState()
        {
            if (!_isPreviewing)
            {
                if (s_previewOwner == this && !AnimationMode.InAnimationMode())
                {
                    s_previewOwner = null;
                }
                return;
            }

            if (s_previewOwner == this && AnimationMode.InAnimationMode())
            {
                return;
            }

            if (s_previewOwner == this)
            {
                s_previewOwner = null;
            }
            _isPreviewing = false;
            RefreshDifferences();
        }

        private bool CanApplySelectedSnapshot(out string reason)
        {
            reason = string.Empty;
            if (_snapshots.arraySize == 0)
            {
                reason = "No locale snapshot is selected.";
                return false;
            }

            if (HasUnsupportedFutureSchema(_snapshots))
            {
                reason = "A future snapshot schema is present; apply and preview are disabled.";
                return false;
            }

            int selectedIndex = Mathf.Clamp(_selectedSnapshotIndex, 0, _snapshots.arraySize - 1);
            _selectedSnapshotIndex = selectedIndex;
            SerializedProperty localeSnapshot = _snapshots.GetArrayElementAtIndex(selectedIndex);
            int schemaVersion = localeSnapshot
                .FindPropertyRelative(nameof(LocaleSnapshot.SchemaVersion))
                .intValue;
            if (schemaVersion != LocaleSnapshot.CurrentSchemaVersion)
            {
                reason = "Apply and preview require the current snapshot schema. Run migration and review the locale first.";
                return false;
            }

            SerializedProperty snapshotElements = localeSnapshot.FindPropertyRelative(nameof(LocaleSnapshot.Elements));
            if (snapshotElements.arraySize != _elements.arraySize)
            {
                reason = "Apply and preview require one snapshot value for every tracked element.";
                return false;
            }

            UILocaleLayout layout = target as UILocaleLayout;
            for (int i = 0; i < _elements.arraySize; i++)
            {
                TrackedElement tracked = ReadTrackedElement(i);
                if (tracked.Target == null ||
                    layout == null ||
                    (tracked.Target != layout.transform && !tracked.Target.IsChildOf(layout.transform)))
                {
                    reason = $"Tracked element {i} must reference a RectTransform inside this layout.";
                    return false;
                }

                if ((tracked.Text != null && tracked.Text.rectTransform != tracked.Target) ||
                    (tracked.LayoutGroup != null && tracked.LayoutGroup.transform != tracked.Target))
                {
                    reason = $"Tracked element {i} contains a component reference from a different RectTransform.";
                    return false;
                }

                ElementSnapshot value = ReadSnapshot(snapshotElements.GetArrayElementAtIndex(i));
                if (!value.HasValue || !IsCurrentSnapshotValueValid(in value, in tracked))
                {
                    reason = $"Tracked element {i} has an incomplete, non-finite, or invalid snapshot value.";
                    return false;
                }
            }

            return true;
        }

        private bool CanModifyAuthoring(string action)
        {
            if (!HasUnsupportedFutureSchema(_snapshots))
            {
                return true;
            }

            _hasUnsupportedFutureSchema = true;
            _validationDirty = true;
            Debug.LogWarning(
                $"[UI Locale Layout] Cannot {action}: the layout contains an unsupported future snapshot schema.",
                target);
            return false;
        }

        internal static bool HasUnsupportedFutureSchema(SerializedProperty snapshots)
        {
            if (snapshots == null || !snapshots.isArray)
            {
                return false;
            }

            for (int i = 0; i < snapshots.arraySize; i++)
            {
                int schemaVersion = snapshots
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative(nameof(LocaleSnapshot.SchemaVersion))
                    .intValue;
                if (schemaVersion > LocaleSnapshot.CurrentSchemaVersion)
                {
                    return true;
                }
            }

            return false;
        }

        private bool AnyTargetHasUnsupportedFutureSchema()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                UnityEngine.Object selectedTarget = targets[i];
                if (selectedTarget == null)
                {
                    continue;
                }

                SerializedObject selectedObject = new SerializedObject(selectedTarget);
                if (HasUnsupportedFutureSchema(selectedObject.FindProperty("_snapshots")))
                {
                    return true;
                }
            }

            return false;
        }

        private void AddLocale(string localeCode)
        {
            if (!CanModifyAuthoring("add a locale override") ||
                string.IsNullOrWhiteSpace(localeCode) ||
                string.Equals(localeCode, _baseLocale.stringValue, StringComparison.OrdinalIgnoreCase) ||
                HasLocaleSnapshot(localeCode))
            {
                return;
            }

            ExitPreview();
            UILocaleLayout layout = (UILocaleLayout)target;
            Undo.RecordObject(layout, "Add Locale Layout Override");
            int index = _snapshots.arraySize;
            _snapshots.InsertArrayElementAtIndex(index);
            SerializedProperty localeSnapshot = _snapshots.GetArrayElementAtIndex(index);
            localeSnapshot.FindPropertyRelative(nameof(LocaleSnapshot.LocaleCode)).stringValue = localeCode;
            localeSnapshot.FindPropertyRelative(nameof(LocaleSnapshot.SchemaVersion)).intValue =
                LocaleSnapshot.CurrentSchemaVersion;

            SerializedProperty snapshotElements = localeSnapshot.FindPropertyRelative(nameof(LocaleSnapshot.Elements));
            snapshotElements.arraySize = _elements.arraySize;
            for (int i = 0; i < _elements.arraySize; i++)
            {
                WriteSnapshot(
                    snapshotElements.GetArrayElementAtIndex(i),
                    ElementSnapshot.Capture(ReadTrackedElement(i)));
            }

            _selectedSnapshotIndex = index;
            serializedObject.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(layout);
            EditorUtility.SetDirty(layout);
            MarkSceneDirty(layout.gameObject);
            InvalidateInspection();
        }

        private void RemoveSelectedSnapshot()
        {
            if (_snapshots.arraySize == 0 || !CanModifyAuthoring("remove a locale override"))
            {
                return;
            }

            string localeCode = _snapshots
                .GetArrayElementAtIndex(_selectedSnapshotIndex)
                .FindPropertyRelative(nameof(LocaleSnapshot.LocaleCode))
                .stringValue;
            if (!EditorUtility.DisplayDialog(
                    "Remove Locale Override",
                    $"Remove the '{localeCode}' layout override?",
                    "Remove",
                    "Cancel"))
            {
                return;
            }

            ExitPreview();
            UILocaleLayout layout = (UILocaleLayout)target;
            Undo.RecordObject(layout, "Remove Locale Layout Override");
            _snapshots.DeleteArrayElementAtIndex(_selectedSnapshotIndex);
            _selectedSnapshotIndex = Mathf.Clamp(
                _selectedSnapshotIndex,
                0,
                Mathf.Max(0, _snapshots.arraySize - 1));
            serializedObject.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(layout);
            EditorUtility.SetDirty(layout);
            MarkSceneDirty(layout.gameObject);
            InvalidateInspection();
        }

        private void ShowAddLocaleMenu()
        {
            GenericMenu menu = new GenericMenu();
            bool hasItem = false;
            IReadOnlyList<Locale> locales = _localizationSettings.AvailableLocales;
            for (int i = 0; i < locales.Count; i++)
            {
                Locale locale = locales[i];
                if (locale == null)
                {
                    continue;
                }

                string code = locale.Id.Code;
                if (string.IsNullOrEmpty(code) ||
                    string.Equals(code, _baseLocale.stringValue, StringComparison.OrdinalIgnoreCase) ||
                    HasLocaleSnapshot(code))
                {
                    continue;
                }

                string capturedCode = code;
                menu.AddItem(
                    new GUIContent(FormatLocaleLabel(locale)),
                    false,
                    () => AddLocale(capturedCode));
                hasItem = true;
            }

            if (!hasItem)
            {
                menu.AddDisabledItem(new GUIContent("No available locale"));
            }

            menu.ShowAsContext();
        }

        private void RefreshDifferences()
        {
            _dirtyElementCount = 0;
            if (_isPreviewing || _snapshots.arraySize == 0 || target == null)
            {
                return;
            }

            _selectedSnapshotIndex = Mathf.Clamp(
                _selectedSnapshotIndex,
                0,
                _snapshots.arraySize - 1);
            SerializedProperty localeSnapshot = _snapshots.GetArrayElementAtIndex(_selectedSnapshotIndex);
            SerializedProperty snapshotElements = localeSnapshot.FindPropertyRelative(nameof(LocaleSnapshot.Elements));
            bool legacy = localeSnapshot
                .FindPropertyRelative(nameof(LocaleSnapshot.SchemaVersion))
                .intValue < LocaleSnapshot.CurrentSchemaVersion;

            for (int i = 0; i < _elements.arraySize; i++)
            {
                if (i >= snapshotElements.arraySize)
                {
                    _dirtyElementCount++;
                    continue;
                }

                ElementSnapshot saved = ReadSnapshot(snapshotElements.GetArrayElementAtIndex(i));
                ElementSnapshot current = ElementSnapshot.Capture(ReadTrackedElement(i));
                if (!saved.ApproximatelyEquals(current, legacy))
                {
                    _dirtyElementCount++;
                }
            }
        }

        private void RefreshValidation()
        {
            _issues.Clear();
            _errorCount = 0;
            _warningCount = 0;
            _totalIssueCount = 0;
            _hasUnsupportedFutureSchema = false;
            _validationDirty = false;

            UILocaleLayout layout = target as UILocaleLayout;
            if (layout == null)
            {
                AddIssue(true, "The UILocaleLayout target is missing.");
                return;
            }

            string baseCode = _baseLocale.stringValue;
            if (string.IsNullOrWhiteSpace(baseCode))
            {
                AddIssue(true, "Base Locale is required.");
            }
            else if (!LooksLikeLocaleCode(baseCode))
            {
                AddIssue(false, $"Base locale '{baseCode}' is not a well-formed BCP 47-style code.");
            }

            for (int i = 0; i < _elements.arraySize; i++)
            {
                SerializedProperty element = _elements.GetArrayElementAtIndex(i);
                RectTransform rect = element
                    .FindPropertyRelative(nameof(TrackedElement.Target))
                    .objectReferenceValue as RectTransform;
                TMP_Text text = element
                    .FindPropertyRelative(nameof(TrackedElement.Text))
                    .objectReferenceValue as TMP_Text;
                LayoutGroup layoutGroup = element
                    .FindPropertyRelative(nameof(TrackedElement.LayoutGroup))
                    .objectReferenceValue as LayoutGroup;

                if (rect == null)
                {
                    AddIssue(true, $"Tracked element {i} has no RectTransform.");
                    continue;
                }

                if (rect != layout.transform && !rect.IsChildOf(layout.transform))
                {
                    AddIssue(true, $"Tracked element {i} is outside the UILocaleLayout hierarchy.");
                }

                for (int previous = 0; previous < i; previous++)
                {
                    RectTransform previousRect = _elements
                        .GetArrayElementAtIndex(previous)
                        .FindPropertyRelative(nameof(TrackedElement.Target))
                        .objectReferenceValue as RectTransform;
                    if (previousRect == rect)
                    {
                        AddIssue(true, $"Tracked element {i} duplicates element {previous}.");
                        break;
                    }
                }

                if (text != null && text.rectTransform != rect)
                {
                    AddIssue(true, $"Tracked element {i} references TMP text on a different RectTransform.");
                }

                if (layoutGroup != null && layoutGroup.transform != rect)
                {
                    AddIssue(true, $"Tracked element {i} references a LayoutGroup on a different RectTransform.");
                }

                Transform parent = rect.parent;
                LayoutGroup parentLayoutGroup = parent != null
                    ? parent.GetComponentInParent<LayoutGroup>(true)
                    : null;
                if (parentLayoutGroup != null)
                {
                    AddIssue(
                        false,
                        $"Tracked element {i} is controlled by parent LayoutGroup '{parentLayoutGroup.name}'. " +
                        "That layout pass can overwrite locale geometry.");
                }

                ContentSizeFitter contentSizeFitter = rect.GetComponent<ContentSizeFitter>();
                if (contentSizeFitter != null &&
                    contentSizeFitter.isActiveAndEnabled &&
                    (contentSizeFitter.horizontalFit != ContentSizeFitter.FitMode.Unconstrained ||
                     contentSizeFitter.verticalFit != ContentSizeFitter.FitMode.Unconstrained))
                {
                    AddIssue(
                        false,
                        $"Tracked element {i} has an active ContentSizeFitter. Assign size authority to either " +
                        "the fitter or the locale snapshot.");
                }

                AspectRatioFitter aspectRatioFitter = rect.GetComponent<AspectRatioFitter>();
                if (aspectRatioFitter != null &&
                    aspectRatioFitter.isActiveAndEnabled &&
                    aspectRatioFitter.aspectMode != AspectRatioFitter.AspectMode.None)
                {
                    AddIssue(
                        false,
                        $"Tracked element {i} has an active AspectRatioFitter that can overwrite locale geometry.");
                }

                Animator animator = rect.GetComponentInParent<Animator>(true);
                if (animator != null && animator.isActiveAndEnabled)
                {
                    AddIssue(
                        false,
                        $"Tracked element {i} is under active Animator '{animator.name}'. Confirm that no animation " +
                        "curve writes the captured layout properties.");
                }

                if (text != null && text.enableAutoSizing)
                {
                    AddIssue(
                        false,
                        $"Tracked element {i} uses TMP auto-size. TMP owns the final font size after layout application.");
                }
            }

            for (int i = 0; i < _snapshots.arraySize; i++)
            {
                SerializedProperty localeSnapshot = _snapshots.GetArrayElementAtIndex(i);
                string localeCode = localeSnapshot
                    .FindPropertyRelative(nameof(LocaleSnapshot.LocaleCode))
                    .stringValue;
                int schemaVersion = localeSnapshot
                    .FindPropertyRelative(nameof(LocaleSnapshot.SchemaVersion))
                    .intValue;
                SerializedProperty snapshotElements = localeSnapshot.FindPropertyRelative(nameof(LocaleSnapshot.Elements));

                if (string.IsNullOrWhiteSpace(localeCode))
                {
                    AddIssue(true, $"Locale override {i} has no locale code.");
                }
                else
                {
                    if (string.Equals(localeCode, baseCode, StringComparison.OrdinalIgnoreCase))
                    {
                        AddIssue(true, $"Locale override '{localeCode}' duplicates the base locale.");
                    }

                    if (!LooksLikeLocaleCode(localeCode))
                    {
                        AddIssue(false, $"Locale override '{localeCode}' is not a well-formed BCP 47-style code.");
                    }

                    for (int previous = 0; previous < i; previous++)
                    {
                        string previousCode = _snapshots
                            .GetArrayElementAtIndex(previous)
                            .FindPropertyRelative(nameof(LocaleSnapshot.LocaleCode))
                            .stringValue;
                        if (string.Equals(previousCode, localeCode, StringComparison.OrdinalIgnoreCase))
                        {
                            AddIssue(true, $"Locale override '{localeCode}' is duplicated.");
                            break;
                        }
                    }

                    if (_localizationSettings != null && !SettingsContainLocaleOrLanguage(localeCode))
                    {
                        AddIssue(false, $"Locale override '{localeCode}' is not present in LocalizationSettings.");
                    }
                }

                if (schemaVersion > LocaleSnapshot.CurrentSchemaVersion)
                {
                    _hasUnsupportedFutureSchema = true;
                    AddIssue(true, $"Locale '{localeCode}' uses unsupported future schema {schemaVersion}.");
                }
                else if (schemaVersion < LocaleSnapshot.CurrentSchemaVersion)
                {
                    AddIssue(false, $"Locale '{localeCode}' uses legacy schema {schemaVersion}; migrate and review it.");
                }

                if (snapshotElements.arraySize != _elements.arraySize)
                {
                    AddIssue(false, $"Locale '{localeCode}' has {snapshotElements.arraySize} values for {_elements.arraySize} tracked elements.");
                }

                int valueCount = Mathf.Min(snapshotElements.arraySize, _elements.arraySize);
                for (int elementIndex = 0; elementIndex < valueCount; elementIndex++)
                {
                    TrackedElement tracked = ReadTrackedElement(elementIndex);
                    ElementSnapshot value = ReadSnapshot(snapshotElements.GetArrayElementAtIndex(elementIndex));
                    if (schemaVersion >= LocaleSnapshot.CurrentSchemaVersion && !value.HasValue)
                    {
                        AddIssue(false, $"Locale '{localeCode}' has no override value for tracked element {elementIndex}; base layout will be used.");
                        continue;
                    }

                    if (!IsFinite(
                            in value,
                            in tracked,
                            schemaVersion < LocaleSnapshot.CurrentSchemaVersion))
                    {
                        AddIssue(true, $"Locale '{localeCode}' contains NaN or Infinity at tracked element {elementIndex}.");
                    }
                    else if (schemaVersion == LocaleSnapshot.CurrentSchemaVersion &&
                              !IsCurrentSnapshotValueValid(in value, in tracked))
                    {
                        AddIssue(true, $"Locale '{localeCode}' contains an invalid alignment value at tracked element {elementIndex}.");
                    }
                }
            }
        }

        private void RefreshSettingsCache()
        {
            _settingsCacheDirty = false;
            _localizationSettings = null;
            _localizationSettingsCount = 0;
            _settingsLocaleCodes = new[] { string.Empty };
            _settingsLocaleLabels = new[] { "Custom locale code" };

            string[] guids = AssetDatabase.FindAssets("t:LocalizationSettings");
            _localizationSettingsCount = guids.Length;
            string selectedPath = null;
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(selectedPath) ||
                    string.Compare(path, selectedPath, StringComparison.Ordinal) < 0)
                {
                    selectedPath = path;
                }
            }

            if (!string.IsNullOrEmpty(selectedPath))
            {
                _localizationSettings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(selectedPath);
            }

            if (_localizationSettings == null)
            {
                return;
            }

            IReadOnlyList<Locale> locales = _localizationSettings.AvailableLocales;
            _settingsLocaleCodes = new string[locales.Count + 1];
            _settingsLocaleLabels = new string[locales.Count + 1];
            _settingsLocaleCodes[0] = string.Empty;
            _settingsLocaleLabels[0] = "Custom locale code";
            for (int i = 0; i < locales.Count; i++)
            {
                Locale locale = locales[i];
                _settingsLocaleCodes[i + 1] = locale != null ? locale.Id.Code : string.Empty;
                _settingsLocaleLabels[i + 1] = locale != null
                    ? FormatLocaleLabel(locale)
                    : "(Missing locale asset)";
            }
        }

        private void EnsureSettingsCache()
        {
            if (_settingsCacheDirty)
            {
                RefreshSettingsCache();
                _validationDirty = true;
            }
        }

        private void EnsureElementLabels()
        {
            if (_elementLabels.Length == _elements.arraySize)
            {
                return;
            }

            _elementLabels = new GUIContent[_elements.arraySize];
            for (int i = 0; i < _elementLabels.Length; i++)
            {
                _elementLabels[i] = new GUIContent($"Element {i}");
            }
        }

        private void EnsureSnapshotLabels()
        {
            if (!_snapshotLabelsDirty && _snapshotLabels.Length == _snapshots.arraySize)
            {
                return;
            }

            _snapshotLabelsDirty = false;
            _snapshotLabels = new string[_snapshots.arraySize];
            for (int i = 0; i < _snapshotLabels.Length; i++)
            {
                string code = _snapshots
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative(nameof(LocaleSnapshot.LocaleCode))
                    .stringValue;
                _snapshotLabels[i] = string.IsNullOrEmpty(code) ? $"Override {i} (missing code)" : code;
            }
        }

        private int FindSettingsLocaleIndex(string code)
        {
            for (int i = 1; i < _settingsLocaleCodes.Length; i++)
            {
                if (string.Equals(_settingsLocaleCodes[i], code, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 0;
        }

        private bool SettingsContainLocaleOrLanguage(string code)
        {
            for (int i = 1; i < _settingsLocaleCodes.Length; i++)
            {
                string configured = _settingsLocaleCodes[i];
                if (string.Equals(configured, code, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                int separator = configured != null ? configured.IndexOf('-') : -1;
                if (separator > 0 &&
                    code.Length == separator &&
                    string.Compare(configured, 0, code, 0, separator, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ContainsTrackedTarget(RectTransform rect)
        {
            for (int i = 0; i < _elements.arraySize; i++)
            {
                if (_elements
                        .GetArrayElementAtIndex(i)
                        .FindPropertyRelative(nameof(TrackedElement.Target))
                        .objectReferenceValue == rect)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasLocaleSnapshot(string localeCode)
        {
            for (int i = 0; i < _snapshots.arraySize; i++)
            {
                string existing = _snapshots
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative(nameof(LocaleSnapshot.LocaleCode))
                    .stringValue;
                if (string.Equals(existing, localeCode, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanReorderTrackedElements()
        {
            for (int i = 0; i < _snapshots.arraySize; i++)
            {
                SerializedProperty snapshotElements = _snapshots
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative(nameof(LocaleSnapshot.Elements));
                if (snapshotElements.arraySize != _elements.arraySize)
                {
                    return false;
                }
            }

            return true;
        }

        private TrackedElement ReadTrackedElement(int index)
        {
            SerializedProperty element = _elements.GetArrayElementAtIndex(index);
            return new TrackedElement
            {
                Target = element
                    .FindPropertyRelative(nameof(TrackedElement.Target))
                    .objectReferenceValue as RectTransform,
                Text = element
                    .FindPropertyRelative(nameof(TrackedElement.Text))
                    .objectReferenceValue as TMP_Text,
                LayoutGroup = element
                    .FindPropertyRelative(nameof(TrackedElement.LayoutGroup))
                    .objectReferenceValue as LayoutGroup
            };
        }

        private void RecordTrackedObjects(string undoName)
        {
            List<UnityEngine.Object> objects = new List<UnityEngine.Object>(_elements.arraySize * 3);
            for (int i = 0; i < _elements.arraySize; i++)
            {
                TrackedElement element = ReadTrackedElement(i);
                AddUnique(objects, element.Target);
                AddUnique(objects, element.Text);
                AddUnique(objects, element.LayoutGroup);
            }

            if (objects.Count > 0)
            {
                Undo.RecordObjects(objects.ToArray(), undoName);
            }
        }

        private void RecordTrackedPrefabOverrides()
        {
            for (int i = 0; i < _elements.arraySize; i++)
            {
                TrackedElement element = ReadTrackedElement(i);
                RecordPrefabOverride(element.Target);
                RecordPrefabOverride(element.Text);
                RecordPrefabOverride(element.LayoutGroup);
            }
        }

        private static void AddUnique(List<UnityEngine.Object> objects, UnityEngine.Object value)
        {
            if (value != null && !objects.Contains(value))
            {
                objects.Add(value);
            }
        }

        private static void RecordPrefabOverride(UnityEngine.Object value)
        {
            if (value != null)
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(value);
                EditorUtility.SetDirty(value);
            }
        }

        private static void ClearTrackedElement(SerializedProperty element)
        {
            element.FindPropertyRelative(nameof(TrackedElement.Target)).objectReferenceValue = null;
            element.FindPropertyRelative(nameof(TrackedElement.Text)).objectReferenceValue = null;
            element.FindPropertyRelative(nameof(TrackedElement.LayoutGroup)).objectReferenceValue = null;
        }

        private static void CopyLegacyFields(ref ElementSnapshot destination, in ElementSnapshot source)
        {
            destination.FontSize = source.FontSize;
            destination.LineSpacing = source.LineSpacing;
            destination.CharacterSpacing = source.CharacterSpacing;
            destination.AnchoredPosition = source.AnchoredPosition;
            destination.SizeDelta = source.SizeDelta;
            destination.HasValue = true;
        }

        internal static void WriteSnapshot(SerializedProperty property, in ElementSnapshot snapshot)
        {
            property.FindPropertyRelative(nameof(ElementSnapshot.FontSize)).floatValue = snapshot.FontSize;
            property.FindPropertyRelative(nameof(ElementSnapshot.LineSpacing)).floatValue = snapshot.LineSpacing;
            property.FindPropertyRelative(nameof(ElementSnapshot.CharacterSpacing)).floatValue = snapshot.CharacterSpacing;
            property.FindPropertyRelative(nameof(ElementSnapshot.AnchoredPosition)).vector2Value = snapshot.AnchoredPosition;
            property.FindPropertyRelative(nameof(ElementSnapshot.SizeDelta)).vector2Value = snapshot.SizeDelta;
            property.FindPropertyRelative(nameof(ElementSnapshot.AnchorMin)).vector2Value = snapshot.AnchorMin;
            property.FindPropertyRelative(nameof(ElementSnapshot.AnchorMax)).vector2Value = snapshot.AnchorMax;
            property.FindPropertyRelative(nameof(ElementSnapshot.Pivot)).vector2Value = snapshot.Pivot;
            property.FindPropertyRelative(nameof(ElementSnapshot.LocalScale)).vector3Value = snapshot.LocalScale;
            property.FindPropertyRelative(nameof(ElementSnapshot.TextAlignment)).intValue = (int)snapshot.TextAlignment;
            property.FindPropertyRelative(nameof(ElementSnapshot.IsRightToLeftText)).boolValue = snapshot.IsRightToLeftText;
            property.FindPropertyRelative(nameof(ElementSnapshot.ChildAlignment)).intValue = (int)snapshot.ChildAlignment;
            property.FindPropertyRelative(nameof(ElementSnapshot.HasValue)).boolValue = snapshot.HasValue;
        }

        private static ElementSnapshot ReadSnapshot(SerializedProperty property)
        {
            return new ElementSnapshot
            {
                FontSize = property.FindPropertyRelative(nameof(ElementSnapshot.FontSize)).floatValue,
                LineSpacing = property.FindPropertyRelative(nameof(ElementSnapshot.LineSpacing)).floatValue,
                CharacterSpacing = property.FindPropertyRelative(nameof(ElementSnapshot.CharacterSpacing)).floatValue,
                AnchoredPosition = property.FindPropertyRelative(nameof(ElementSnapshot.AnchoredPosition)).vector2Value,
                SizeDelta = property.FindPropertyRelative(nameof(ElementSnapshot.SizeDelta)).vector2Value,
                AnchorMin = property.FindPropertyRelative(nameof(ElementSnapshot.AnchorMin)).vector2Value,
                AnchorMax = property.FindPropertyRelative(nameof(ElementSnapshot.AnchorMax)).vector2Value,
                Pivot = property.FindPropertyRelative(nameof(ElementSnapshot.Pivot)).vector2Value,
                LocalScale = property.FindPropertyRelative(nameof(ElementSnapshot.LocalScale)).vector3Value,
                TextAlignment = (TextAlignmentOptions)property.FindPropertyRelative(nameof(ElementSnapshot.TextAlignment)).intValue,
                IsRightToLeftText = property.FindPropertyRelative(nameof(ElementSnapshot.IsRightToLeftText)).boolValue,
                ChildAlignment = (TextAnchor)property.FindPropertyRelative(nameof(ElementSnapshot.ChildAlignment)).intValue,
                HasValue = property.FindPropertyRelative(nameof(ElementSnapshot.HasValue)).boolValue
            };
        }

        private void AddIssue(bool error, string message)
        {
            _totalIssueCount++;
            if (error)
            {
                _errorCount++;
            }
            else
            {
                _warningCount++;
            }

            if (_issues.Count < MaxDisplayedIssues)
            {
                _issues.Add((error ? "Error: " : "Warning: ") + message);
            }
        }

        private void InvalidateInspection()
        {
            _elementLabels = Array.Empty<GUIContent>();
            _snapshotLabelsDirty = true;
            _validationDirty = true;
            _nextDiffRefreshTime = 0d;
        }

        private void HandleExternalChange()
        {
            ExitPreview();
            InvalidateInspection();
            Repaint();
        }

        private void HandleProjectChange()
        {
            _settingsCacheDirty = true;
            InvalidateInspection();
            Repaint();
        }

        private void HandlePlayModeChange(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                ExitPreview();
            }
        }

        private void ExitPreviewBeforeReload()
        {
            ExitPreview();
        }

        private void HandlePrefabSaving(GameObject prefabRoot)
        {
            if (!_isPreviewing || target == null)
            {
                return;
            }

            UILocaleLayout layout = (UILocaleLayout)target;
            if (layout.gameObject == prefabRoot || layout.transform.IsChildOf(prefabRoot.transform))
            {
                ExitPreview();
            }
        }

        private static string FormatLocaleLabel(Locale locale)
        {
            string code = locale.Id.Code;
            if (!string.IsNullOrEmpty(locale.NativeName) &&
                !string.Equals(locale.NativeName, code, StringComparison.OrdinalIgnoreCase))
            {
                return $"{code} ({locale.NativeName})";
            }

            if (!string.IsNullOrEmpty(locale.DisplayName) &&
                !string.Equals(locale.DisplayName, code, StringComparison.OrdinalIgnoreCase))
            {
                return $"{code} ({locale.DisplayName})";
            }

            return code;
        }

        private static bool LooksLikeLocaleCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length > 35 ||
                code[0] == '-' || code[code.Length - 1] == '-')
            {
                return false;
            }

            bool previousWasSeparator = false;
            for (int i = 0; i < code.Length; i++)
            {
                char character = code[i];
                if (character == '-')
                {
                    if (previousWasSeparator)
                    {
                        return false;
                    }

                    previousWasSeparator = true;
                    continue;
                }

                previousWasSeparator = false;
                bool asciiLetter = (character >= 'A' && character <= 'Z') ||
                                   (character >= 'a' && character <= 'z');
                bool digit = character >= '0' && character <= '9';
                if (!asciiLetter && !digit)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFinite(
            in ElementSnapshot snapshot,
            in TrackedElement tracked,
            bool legacyOnly)
        {
            bool legacyFinite =
                (tracked.Target == null ||
                 (IsFinite(snapshot.AnchoredPosition) && IsFinite(snapshot.SizeDelta))) &&
                (tracked.Text == null ||
                 (IsFinite(snapshot.FontSize) &&
                  IsFinite(snapshot.LineSpacing) &&
                  IsFinite(snapshot.CharacterSpacing)));
            if (!legacyFinite || legacyOnly)
            {
                return legacyFinite;
            }

            return tracked.Target == null ||
                   (IsFinite(snapshot.AnchorMin) &&
                    IsFinite(snapshot.AnchorMax) &&
                    IsFinite(snapshot.Pivot) &&
                    IsFinite(snapshot.LocalScale));
        }

        private static bool IsCurrentSnapshotValueValid(
            in ElementSnapshot snapshot,
            in TrackedElement tracked)
        {
            return IsFinite(in snapshot, in tracked, false) &&
                   (tracked.Text == null || IsValidTextAlignment(snapshot.TextAlignment)) &&
                   (tracked.LayoutGroup == null ||
                    (snapshot.ChildAlignment >= TextAnchor.UpperLeft &&
                     snapshot.ChildAlignment <= TextAnchor.LowerRight));
        }

        private static bool IsValidTextAlignment(TextAlignmentOptions alignment)
        {
            int value = (int)alignment;
            if (value == (int)TextAlignmentOptions.Converted)
            {
                return true;
            }

            int horizontal = value & 0xFF;
            int vertical = value & 0xFF00;
            bool validHorizontal = horizontal == 0x1 ||
                                   horizontal == 0x2 ||
                                   horizontal == 0x4 ||
                                   horizontal == 0x8 ||
                                   horizontal == 0x10 ||
                                   horizontal == 0x20;
            bool validVertical = vertical == 0x100 ||
                                 vertical == 0x200 ||
                                 vertical == 0x400 ||
                                 vertical == 0x800 ||
                                 vertical == 0x1000 ||
                                 vertical == 0x2000;
            return validHorizontal && validVertical && (value & ~0xFFFF) == 0;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static void MarkSceneDirty(GameObject gameObject)
        {
            if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            {
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
        }
    }
}
