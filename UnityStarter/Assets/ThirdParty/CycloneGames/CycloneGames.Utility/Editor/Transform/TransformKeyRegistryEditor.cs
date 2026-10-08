using System;
using System.Collections.Generic;
using System.Globalization;

using CycloneGames.Logging;
using CycloneGames.Utility.Runtime;

using UnityEditor;
using UnityEngine;

namespace CycloneGames.Utility.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(TransformKeyRegistry))]
    public sealed class TransformKeyRegistryEditor : UnityEditor.Editor
    {
        private static readonly LogChannel Log = UtilityEditorLog.Channel;
        private const float SmallButtonWidth = 24f;
        private const float Spacing = 4f;

        /// <summary>
        /// Label column reserved for the policy toggles. Sized to the longest caption plus the toggle, so
        /// "Include Nested Registries" and "Transform.Find Fallback" stay readable when the panel is narrow.
        /// </summary>
        private const float PolicyLabelWidth = 172f;

        /// <summary>Floor for the "Add Entry" button so it keeps a comfortable hit area.</summary>
        private const float AddButtonMinWidth = 96f;

        /// <summary>
        /// Horizontal chrome subtracted from the inspector view width before full-width buttons divide it: window
        /// padding and scrollbar. The in-panel variant adds the help box borders and padding around the Entries
        /// panel, where the collect buttons live.
        /// </summary>
        private const float ActionRowChromeWidth = 36f;
        private const float PanelChromeWidth = 16f;

        /// <summary>Ancestor walk bound for validation, matching the runtime's hierarchy depth ceiling.</summary>
        private const int MaxValidationDepth = 1024;

        private static readonly Color IndexColor = new Color(0.17f, 0.49f, 0.67f, 1f);
        private static readonly Color EntriesColor = new Color(0.45f, 0.32f, 0.68f, 1f);
        private static readonly Color RowHighlightColor = new Color(0.86f, 0.32f, 0.28f, 0.16f);
        private static readonly GUIContent RemoveContent = new GUIContent("-", "Remove this entry.");
        private static readonly GUIContent AddContent = new GUIContent("Add Entry");
        private static readonly GUIContent BuildOnAwakeContent = new GUIContent("Build On Awake");
        private static readonly GUIContent IncludeNestedContent = new GUIContent("Include Nested Registries");
        private static readonly GUIContent KeyPathModeContent = new GUIContent(
            "Key Path Mode",
            "Leaf keeps authored keys verbatim. Leaf Then Path also accepts the path relative to this registry. Path Only indexes full paths and is unambiguous at depth.");
        private static readonly GUIContent FindFallbackContent = new GUIContent("Transform.Find Fallback");
        private static readonly GUIContent CollectContent = new GUIContent(
            "Collect Direct Children",
            "Add every direct child that is not already referenced. Existing and removed references are left untouched; use Sync after deleting children.");
        private static readonly GUIContent SyncContent = new GUIContent(
            "Sync Direct Children",
            "Replace the entry list with the current direct children, keyed by their GameObject name.");
        private static readonly GUIContent CollectDeepContent = new GUIContent(
            "Collect Deep Children",
            "Add every descendant below this registry. Roles that repeat across a rig or a widget tree (Head, Hand, Panel) are keyed by their path so they stay addressable at depth. Registries nested below are linked by reference and keep their own subtree.");
        private static readonly GUIContent SyncDeepContent = new GUIContent(
            "Sync Deep Children",
            "Replace the entry list with every descendant below this registry, keyed by path.");
        private static readonly GUIContent RebuildContent = new GUIContent(
            "Rebuild Runtime Index",
            "Build the runtime index now. Play mode lookups never build implicitly.");
        private static readonly GUIContent BuildHierarchyContent = new GUIContent(
            "Build Hierarchy",
            "Build this registry and every nested registry below it.");

        private const int RowFlagEmptyKey = 1;
        private const int RowFlagMissing = 2;
        private const int RowFlagDuplicate = 4;
        private const int RowFlagExternal = 8;

        /// <summary>Both the short key and the qualified path are taken, so the row resolves to nothing.</summary>
        private const int RowFlagUnindexed = 16;

        private readonly HashSet<string> _validationKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<Transform> _collectTransforms = new HashSet<Transform>();
        private readonly List<int> _rowFlags = new List<int>(32);
        private readonly List<Transform> _deepCollectBuffer = new List<Transform>(32);

        private SerializedProperty _script;
        private SerializedProperty _entries;
        private SerializedProperty _autoBuild;
        private SerializedProperty _includeNested;
        private SerializedProperty _keyPathMode;
        private SerializedProperty _findFallback;

        private bool _indexExpanded = true;
        private bool _entriesExpanded = true;
        private bool _issuesExpanded;

        /// <summary>
        /// Whether the collect buttons inside the Entries panel stack full-width. Decided once per Layout pass;
        /// see <see cref="RefreshActionLayout"/>.
        /// </summary>
        private bool _stackCollectButtons;

        /// <summary>
        /// Whether the build buttons at the bottom of the inspector stack full-width. Decided once per Layout
        /// pass so the Layout and Repaint events of one frame always walk the same branch; see
        /// <see cref="RefreshActionLayout"/>.
        /// </summary>
        private bool _stackActionButtons;
        private bool _validationDirty = true;
        private ValidationSummary _validation;
        private string _emptyKeyMessage;
        private string _missingTransformMessage;
        private string _duplicateKeyMessage;
        private string _unindexedKeyMessage;
        private string _externalTransformMessage;

        private void OnEnable()
        {
            _script = serializedObject.FindProperty("m_Script");
            _entries = serializedObject.FindProperty("Entries");
            _autoBuild = serializedObject.FindProperty("AutoBuildOnAwake");
            _includeNested = serializedObject.FindProperty("IncludeNestedRegistries");
            _keyPathMode = serializedObject.FindProperty("KeyPathMode");
            _findFallback = serializedObject.FindProperty("UseTransformFindFallback");
            Undo.undoRedoPerformed += InvalidateValidation;
            EditorApplication.hierarchyChanged += InvalidateValidation;
            TransformKeyRegistry.IndexNotBuilt += OnIndexNotBuilt;
            _validationDirty = true;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= InvalidateValidation;
            EditorApplication.hierarchyChanged -= InvalidateValidation;
            TransformKeyRegistry.IndexNotBuilt -= OnIndexNotBuilt;
        }

        public override void OnInspectorGUI()
        {
            if (Event.current.type == EventType.Layout)
            {
                RefreshActionLayout();
            }

            serializedObject.Update();
            InspectorUiUtility.DrawScriptProperty(_script);
            InspectorUiUtility.DrawModuleHeader(
                "Transform Key Registry",
                "Deterministic key index. Empty entries are ignored and the first valid duplicate key wins.");

            EditorGUI.BeginChangeCheck();

            DrawIndexPolicy();
            DrawEntries();

            bool controlsChanged = EditorGUI.EndChangeCheck();
            bool applied = serializedObject.ApplyModifiedProperties();
            if (controlsChanged || applied)
            {
                InvalidateValidation();
            }

            DrawValidation();
            DrawActions();
        }

        private void DrawIndexPolicy()
        {
            _indexExpanded = InspectorUiUtility.DrawFoldoutHeader("Index Policy", _indexExpanded, IndexColor);
            if (!_indexExpanded)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // The label column is widened for the policy captions and restored afterwards so the rest of the
                // inspector keeps Unity's default label width.
                float previousLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = PolicyLabelWidth;
                try
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.PropertyField(_autoBuild, BuildOnAwakeContent);
                        EditorGUILayout.PropertyField(_includeNested, IncludeNestedContent);
                        if (IsAnyTargetEnabled(_includeNested))
                        {
                            EditorGUILayout.PropertyField(_keyPathMode, KeyPathModeContent);
                        }

                        EditorGUILayout.PropertyField(_findFallback, FindFallbackContent);
                    }
                }
                finally
                {
                    EditorGUIUtility.labelWidth = previousLabelWidth;
                }

                if (IsAnyTargetEnabled(_findFallback))
                {
                    EditorGUILayout.HelpBox(
                        "Transform.Find walks the whole subtree and splits the path string. Do not call it from a frame hot path.",
                        MessageType.Warning);
                }

                if (!IsAnyTargetEnabled(_autoBuild))
                {
                    EditorGUILayout.HelpBox(
                        "Automatic build is off. Lookups fail until Rebuild Runtime Index runs, so warm up explicitly during load.",
                        MessageType.Info);
                }
            }
        }

        private void DrawEntries()
        {
            _entriesExpanded = InspectorUiUtility.DrawFoldoutHeader("Entries", _entriesExpanded, EntriesColor);
            if (!_entriesExpanded)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (serializedObject.isEditingMultipleObjects)
                {
                    EditorGUILayout.HelpBox(
                        "Multi-object editing uses Unity's standard array editor so different array sizes remain explicit.",
                        MessageType.Info);
                    EditorGUILayout.PropertyField(_entries, true);
                }
                else
                {
                    DrawSingleTargetEntries();
                }

                // The collect buttons populate the entry list, so they live inside the Entries panel right under
                // the grid instead of at the bottom of the inspector next to the build actions.
                EditorGUILayout.Space(2f);
                DrawButtonRow(
                    _stackCollectButtons,
                    CollectContent,
                    () => CollectChildrenForTargets(false, false),
                    SyncContent,
                    () => CollectChildrenForTargets(true, false));
                EditorGUILayout.Space(2f);
                DrawButtonRow(
                    _stackCollectButtons,
                    CollectDeepContent,
                    () => CollectChildrenForTargets(false, true),
                    SyncDeepContent,
                    () => CollectChildrenForTargets(true, true));
            }
        }

        /// <summary>
        /// Every row lays out an identical control set, and removal is deferred until after the loop, so the
        /// control count cannot change between the Layout and Repaint passes.
        /// </summary>
        private void DrawSingleTargetEntries()
        {
            // The "Size" row keeps the policy label width so the entry grid below aligns with it.
            Rect sizeRect = EditorGUILayout.GetControlRect();
            float sizeLabelWidth = Mathf.Min(PolicyLabelWidth, sizeRect.width * 0.5f);
            Rect sizeFieldRect = new Rect(sizeRect.x + sizeLabelWidth, sizeRect.y, Mathf.Max(0f, sizeRect.width - sizeLabelWidth), sizeRect.height);
            EditorGUI.LabelField(new Rect(sizeRect.x, sizeRect.y, sizeLabelWidth, sizeRect.height), "Size", EditorStyles.label);
            EditorGUI.BeginChangeCheck();
            int requestedSize = EditorGUI.IntField(sizeFieldRect, _entries.arraySize);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Resize Transform Key Entries");
                _entries.arraySize = Mathf.Max(0, requestedSize);
            }

            // Column grid: key | transform | remove. The key column is clamped so the transform column always
            // keeps a usable width, and the remove button never falls off the right edge.
            // Width is derived from the upcoming row rect rather than a throwaway GetControlRect(): reserving a
            // rect purely to read its width still consumes a full row of vertical layout, which pushed the
            // "Add Entry" button past the panel border and clipped it whenever the entry list was empty.
            float gridWidth = Mathf.Max(0f, sizeRect.width - SmallButtonWidth - Spacing * 2f);
            float keyWidth = Mathf.Clamp(gridWidth * 0.38f, 60f, Mathf.Max(60f, gridWidth - 80f));
            float transformWidth = Mathf.Max(0f, gridWidth - keyWidth);

            Rect headerRect = EditorGUILayout.GetControlRect();
            Rect keyHeader = new Rect(headerRect.x, headerRect.y, keyWidth, headerRect.height);
            Rect transformHeader = new Rect(keyHeader.xMax + Spacing, headerRect.y, transformWidth, headerRect.height);
            EditorGUI.LabelField(keyHeader, "Key", EditorStyles.miniBoldLabel);
            EditorGUI.LabelField(transformHeader, "Transform", EditorStyles.miniBoldLabel);

            // Row flags feed both the highlight rect and the issue list, and the list persists between events,
            // so rebuilding on Layout only is enough. Path-mode validation composes strings per row; running it
            // on Repaint too would double that allocation for no visible change.
            if (Event.current.type == EventType.Layout)
            {
                RebuildRowFlags();
            }

            int removeIndex = -1;

            for (int i = 0; i < _entries.arraySize; i++)
            {
                SerializedProperty entry = _entries.GetArrayElementAtIndex(i);
                SerializedProperty key = entry.FindPropertyRelative("Key");
                SerializedProperty transformProperty = entry.FindPropertyRelative("Transform");
                Rect row = EditorGUILayout.GetControlRect();
                Rect keyRect = new Rect(row.x, row.y, keyWidth, row.height);
                Rect transformRect = new Rect(keyRect.xMax + Spacing, row.y, transformWidth, row.height);
                Rect removeRect = new Rect(transformRect.xMax + Spacing, row.y, SmallButtonWidth, row.height);

                if (i < _rowFlags.Count && _rowFlags[i] != 0)
                {
                    EditorGUI.DrawRect(row, RowHighlightColor);
                }

                EditorGUI.PropertyField(keyRect, key, GUIContent.none);
                EditorGUI.PropertyField(transformRect, transformProperty, GUIContent.none);

                if (GUI.Button(removeRect, RemoveContent))
                {
                    removeIndex = i;
                }
            }

            if (removeIndex >= 0)
            {
                // Deferred so the row loop above never changes its control count mid-pass.
                Undo.RecordObject(target, "Remove Transform Key Entry");
                _entries.DeleteArrayElementAtIndex(removeIndex);
                InvalidateValidation();
            }

            // Sized to the caption instead of a fixed guess, so "Add Entry" is never clipped.
            Rect addRect = EditorGUILayout.GetControlRect();
            addRect.width = Mathf.Min(addRect.width, Mathf.Max(AddButtonMinWidth, MeasureButtonWidth(AddContent.text)));
            if (GUI.Button(addRect, AddContent))
            {
                Undo.RecordObject(target, "Add Transform Key Entry");
                int index = _entries.arraySize;
                _entries.arraySize++;
                SerializedProperty entry = _entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("Key").stringValue = string.Empty;
                entry.FindPropertyRelative("Transform").objectReferenceValue = null;
                InvalidateValidation();
            }
        }

        private void DrawValidation()
        {
            if (_validationDirty && Event.current.type == EventType.Layout)
            {
                RebuildValidation();
            }

            if (_validation.IsClean)
            {
                return;
            }

            if (_validation.EmptyKeyCount > 0)
            {
                EditorGUILayout.HelpBox(_emptyKeyMessage, MessageType.Warning);
            }

            if (_validation.MissingTransformCount > 0)
            {
                EditorGUILayout.HelpBox(_missingTransformMessage, MessageType.Warning);
            }

            if (_validation.DuplicateKeyCount > 0)
            {
                EditorGUILayout.HelpBox(_duplicateKeyMessage, MessageType.Warning);
            }

            if (_validation.UnindexedCount > 0)
            {
                EditorGUILayout.HelpBox(_unindexedKeyMessage, MessageType.Error);
            }

            if (_validation.ExternalTransformCount > 0)
            {
                EditorGUILayout.HelpBox(_externalTransformMessage, MessageType.Info);
            }

            if (serializedObject.isEditingMultipleObjects)
            {
                return;
            }

            EditorGUILayout.Space(2f);
            _issuesExpanded = EditorGUILayout.Foldout(
                _issuesExpanded,
                string.Concat("Authoring issues by row (", CountFlaggedRows().ToString(CultureInfo.InvariantCulture), ")"),
                true);
            if (!_issuesExpanded)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < _rowFlags.Count; i++)
                {
                    if (_rowFlags[i] == 0)
                    {
                        continue;
                    }

                    // One full-width, word-wrapped line per flagged row: a two-column LabelField clips the
                    // description at the panel edge, and the row number names the flagged row itself rather
                    // than a misleading range back to the previous issue.
                    EditorGUILayout.LabelField(
                        string.Concat("Row ", i.ToString(CultureInfo.InvariantCulture), ": ", DescribeFlags(_rowFlags[i])),
                        EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private int CountFlaggedRows()
        {
            int count = 0;
            for (int i = 0; i < _rowFlags.Count; i++)
            {
                if (_rowFlags[i] != 0)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Draws the build actions. The collect buttons live in the Entries panel; this block only owns the
        /// runtime index operations.
        /// </summary>
        private void DrawActions()
        {
            EditorGUILayout.Space(4f);
            DrawButtonRow(
                _stackActionButtons,
                RebuildContent,
                () => RebuildTargets(false),
                BuildHierarchyContent,
                () => RebuildTargets(true));

            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Play mode lookups never build implicitly. Use Build Hierarchy after changing entries at runtime.",
                    MessageType.Info);
            }
        }

        /// <summary>
        /// Recomputes the stacking decisions for the button blocks. Runs on the Layout event only and the results
        /// are reused by every other event of the frame: IMGUI requires an identical control count across all
        /// events that share a layout, and a per-event width comparison can flip the branch between Layout and
        /// Repaint, desyncing the layout cursor and collapsing buttons into slivers a few pixels tall.
        /// </summary>
        private void RefreshActionLayout()
        {
            float viewWidth = EditorGUIUtility.currentViewWidth;

            // The collect buttons sit inside the Entries help box, so their usable width is narrower than the
            // full-width build buttons below it.
            float panelHalfWidth = (viewWidth - ActionRowChromeWidth - PanelChromeWidth - Spacing) * 0.5f;
            _stackCollectButtons =
                MeasureButtonWidth(CollectContent.text) > panelHalfWidth ||
                MeasureButtonWidth(SyncContent.text) > panelHalfWidth ||
                MeasureButtonWidth(CollectDeepContent.text) > panelHalfWidth ||
                MeasureButtonWidth(SyncDeepContent.text) > panelHalfWidth;

            float actionHalfWidth = (viewWidth - ActionRowChromeWidth - Spacing) * 0.5f;
            _stackActionButtons =
                MeasureButtonWidth(RebuildContent.text) > actionHalfWidth ||
                MeasureButtonWidth(BuildHierarchyContent.text) > actionHalfWidth;
        }

        /// <summary>
        /// Draws one row of two equal buttons, or two stacked full-width buttons when the captions cannot share a
        /// line at the current panel width. The side-by-side rects are split by hand because a horizontal
        /// GUILayout group divides leftover space around each button's content width, which leaves the two
        /// halves visibly unequal; the single GetControlRect keeps a constant control count, so the cached
        /// <paramref name="stack"/> branch stays layout-safe.
        /// </summary>
        private void DrawButtonRow(bool stack, GUIContent left, Action leftAction, GUIContent right, Action rightAction)
        {
            if (stack)
            {
                // Two captions cannot share one line at this width; stack them full-width instead of truncating.
                if (GUILayout.Button(left))
                {
                    leftAction();
                }

                if (GUILayout.Button(right))
                {
                    rightAction();
                }

                return;
            }

            Rect row = EditorGUILayout.GetControlRect();
            float halfWidth = (row.width - Spacing) * 0.5f;
            Rect leftRect = new Rect(row.x, row.y, halfWidth, row.height);
            Rect rightRect = new Rect(row.xMax - halfWidth, row.y, halfWidth, row.height);
            if (GUI.Button(leftRect, left))
            {
                leftAction();
            }

            if (GUI.Button(rightRect, right))
            {
                rightAction();
            }
        }

        /// <summary>
        /// Pixel width a button needs for its caption, including the skin's horizontal padding, so a caption is
        /// never clipped by a rect sized from <see cref="GUIStyle.CalcSize"/> alone.
        /// </summary>
        private static float MeasureButtonWidth(string text)
        {
            GUIStyle style = GUI.skin.button;
            return style.CalcSize(new GUIContent(text)).x + style.padding.horizontal;
        }

        private void RebuildValidation()
        {
            ValidationSummary summary = default;
            for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                TransformKeyRegistry registry = targets[targetIndex] as TransformKeyRegistry;
                if (registry == null)
                {
                    continue;
                }

                using (var registryObject = new SerializedObject(registry))
                {
                    registryObject.UpdateIfRequiredOrScript();
                    SerializedProperty entries = registryObject.FindProperty("Entries");
                    _validationKeys.Clear();
                    Transform root = registry.transform;
                    bool pathKeys = IsPathKeyMode(registry.KeyPath);

                    for (int i = 0; i < entries.arraySize; i++)
                    {
                        SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                        string key = entry.FindPropertyRelative("Key").stringValue;
                        Transform value = entry.FindPropertyRelative("Transform").objectReferenceValue as Transform;
                        if (string.IsNullOrEmpty(key))
                        {
                            summary.EmptyKeyCount++;
                        }

                        if (value == null)
                        {
                            summary.MissingTransformCount++;
                        }
                        else
                        {
                            // Validation mirrors the runtime's per-form admission: the short key and the qualified
                            // path are claimed independently, so a repeated leaf name is only a hard conflict when
                            // its path form is taken too.
                            if (!string.IsNullOrEmpty(key))
                            {
                                string effective = pathKeys ? registry.ComposeKey(key, value) : key;
                                bool primaryFree = _validationKeys.Add(effective);
                                bool aliasFree = !pathKeys || _validationKeys.Add(BuildQualifiedKey(registry, key, value));

                                if (!primaryFree)
                                {
                                    summary.DuplicateKeyCount++;
                                }

                                if (!primaryFree && !aliasFree)
                                {
                                    summary.UnindexedCount++;
                                }
                            }

                            if (value != root && !value.IsChildOf(root))
                            {
                                summary.ExternalTransformCount++;
                            }
                        }
                    }
                }
            }

            _validation = summary;
            _emptyKeyMessage = summary.EmptyKeyCount > 0
                ? string.Concat(summary.EmptyKeyCount, " entries have empty keys and are ignored.")
                : null;
            _missingTransformMessage = summary.MissingTransformCount > 0
                ? string.Concat(summary.MissingTransformCount, " entries have no Transform and are ignored.")
                : null;
            _duplicateKeyMessage = summary.DuplicateKeyCount > 0
                ? string.Concat(
                    summary.DuplicateKeyCount,
                    " duplicate keys were skipped; the first authored entry keeps the short name.")
                : null;
            _unindexedKeyMessage = summary.UnindexedCount > 0
                ? string.Concat(
                    summary.UnindexedCount,
                    " entries are addressable by neither their key nor their path. Rename the node or the key.")
                : null;
            _externalTransformMessage = summary.ExternalTransformCount > 0
                ? string.Concat(
                    summary.ExternalTransformCount,
                    " entries reference Transforms outside their registry root. Verify ownership and lifetime.")
                : null;
            _validationDirty = false;
        }

        /// <summary>
        /// Recomputes the per-row issue bitmap from the live serialized state so a flagged row can be highlighted in
        /// place instead of only being counted in a summary line.
        /// </summary>
        private void RebuildRowFlags()
        {
            _rowFlags.Clear();
            if (serializedObject.isEditingMultipleObjects || !(target is TransformKeyRegistry registry))
            {
                return;
            }

            _validationKeys.Clear();
            Transform root = registry.transform;
            bool pathKeys = IsPathKeyMode(registry.KeyPath);
            int count = _entries.arraySize;
            for (int i = 0; i < count; i++)
            {
                SerializedProperty entry = _entries.GetArrayElementAtIndex(i);
                string key = entry.FindPropertyRelative("Key").stringValue;
                Transform value = entry.FindPropertyRelative("Transform").objectReferenceValue as Transform;
                int flags = 0;
                if (string.IsNullOrEmpty(key))
                {
                    flags |= RowFlagEmptyKey;
                }

                if (value == null)
                {
                    flags |= RowFlagMissing;
                }
                else
                {
                    if (!string.IsNullOrEmpty(key))
                    {
                        string effective = pathKeys ? registry.ComposeKey(key, value) : key;
                        bool primaryFree = _validationKeys.Add(effective);
                        bool aliasFree = !pathKeys || _validationKeys.Add(BuildQualifiedKey(registry, key, value));
                        if (!primaryFree)
                        {
                            flags |= RowFlagDuplicate;
                        }

                        // Fully shadowed: neither the short key nor the path is free, so the row is unreachable.
                        if (!primaryFree && !aliasFree)
                        {
                            flags |= RowFlagUnindexed;
                        }
                    }

                    if (value != root && !value.IsChildOf(root))
                    {
                        flags |= RowFlagExternal;
                    }
                }

                _rowFlags.Add(flags);
            }
        }

        /// <summary>
        /// Collects children into each target. When <paramref name="replaceExisting"/> is true the entry list is
        /// replaced with the current children; otherwise only children not already referenced are added.
        /// </summary>
        /// <param name="includeDescendants">
        /// When true the walk covers the whole subtree and keys become hierarchy paths. A registry met below the
        /// root is linked by reference and its own subtree is skipped, so nesting stays a link rather than a flat
        /// copy of itself.
        /// </param>
        private void CollectChildrenForTargets(bool replaceExisting, bool includeDescendants)
        {
            serializedObject.ApplyModifiedProperties();
            for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                TransformKeyRegistry registry = targets[targetIndex] as TransformKeyRegistry;
                if (registry == null)
                {
                    continue;
                }

                Undo.RecordObject(registry, replaceExisting ? "Sync Transform Keys" : "Collect Transform Keys");
                using (var registryObject = new SerializedObject(registry))
                {
                    registryObject.UpdateIfRequiredOrScript();
                    SerializedProperty entries = registryObject.FindProperty("Entries");
                    _collectTransforms.Clear();
                    if (replaceExisting)
                    {
                        entries.arraySize = 0;
                    }
                    else
                    {
                        for (int i = 0; i < entries.arraySize; i++)
                        {
                            Transform existing = entries
                                .GetArrayElementAtIndex(i)
                                .FindPropertyRelative("Transform")
                                .objectReferenceValue as Transform;
                            if (existing != null)
                            {
                                _collectTransforms.Add(existing);
                            }
                        }
                    }

                    Transform root = registry.transform;
                    if (includeDescendants)
                    {
                        CollectDescendants(root, entries);
                    }
                    else
                    {
                        CollectDirectChildren(root, entries);
                    }

                    registryObject.ApplyModifiedProperties();
                }

                registry.Invalidate();
            }

            serializedObject.Update();
            InvalidateValidation();
        }

        private void CollectDirectChildren(Transform root, SerializedProperty entries)
        {
            int childCount = root.childCount;
            for (int childIndex = 0; childIndex < childCount; childIndex++)
            {
                AppendEntry(root.GetChild(childIndex), entries);
            }
        }

        /// <summary>
        /// Depth-first walk of the whole subtree reusing a preallocated list, so collecting a large rig allocates
        /// nothing per node. The authored key written for every node is its plain name; the hierarchy path used to
        /// disambiguate repeated names is derived at build time by the registry's key path mode, so the entry list
        /// stays a single source of truth that survives a mode change or a hierarchy move.
        /// </summary>
        private void CollectDescendants(Transform root, SerializedProperty entries)
        {
            List<Transform> buffer = _deepCollectBuffer;
            buffer.Clear();

            int childCount = root.childCount;
            for (int i = childCount - 1; i >= 0; i--)
            {
                buffer.Add(root.GetChild(i));
            }

            while (buffer.Count > 0)
            {
                int lastIndex = buffer.Count - 1;
                Transform current = buffer[lastIndex];
                buffer.RemoveAt(lastIndex);

                // A nested registry is linked by reference and owns its own subtree, so it is not flattened into
                // this list; stopping here is what keeps deep nesting a link instead of a duplicated copy.
                if (!IsNestedRegistry(current))
                {
                    AppendEntry(current, entries);

                    int descendants = current.childCount;
                    for (int i = descendants - 1; i >= 0; i--)
                    {
                        buffer.Add(current.GetChild(i));
                    }
                }
            }

            buffer.Clear();
        }

        private static bool IsNestedRegistry(Transform candidate)
        {
            // The walk only ever visits strict descendants of the root, so no ancestry check is needed: any
            // registry below the root is a nested registry.
            return candidate.GetComponent<TransformKeyRegistry>() != null;
        }

        private void AppendEntry(Transform value, SerializedProperty entries)
        {
            if (!_collectTransforms.Add(value))
            {
                return;
            }

            int entryIndex = entries.arraySize;
            entries.arraySize++;
            SerializedProperty entry = entries.GetArrayElementAtIndex(entryIndex);
            entry.FindPropertyRelative("Key").stringValue = value.name;
            entry.FindPropertyRelative("Transform").objectReferenceValue = value;
        }

        private static bool IsPathKeyMode(RegistryKeyPathMode mode)
        {
            return mode != RegistryKeyPathMode.Leaf;
        }

        /// <summary>
        /// The qualified form a path-mode registry indexes alongside the authored key. Leaf mode has no alias, and
        /// PathOnly uses <see cref="TransformKeyRegistry.ComposeKey"/> as its primary form instead.
        /// </summary>
        private static string BuildQualifiedKey(TransformKeyRegistry registry, string key, Transform value)
        {
            if (!IsPathKeyMode(registry.KeyPath))
            {
                return key;
            }

            string prefix = BuildAncestorPrefix(registry.transform, value);
            if (prefix.Length == 0)
            {
                return key;
            }

            return string.Concat(prefix, TransformKeyRegistry.KeyPathSeparator, key);
        }

        /// <summary>
        /// Slash-joined names from the registry root's child level down to the target's parent. Mirrors the
        /// composition the runtime performs, so inspector validation cannot drift from the built index.
        /// </summary>
        private static string BuildAncestorPrefix(Transform root, Transform target)
        {
            if (target == null || target == root || target.parent == root)
            {
                return string.Empty;
            }

            int depth = 0;
            Transform cursor = target.parent;
            while (cursor != null && cursor != root && depth < MaxValidationDepth)
            {
                depth++;
                cursor = cursor.parent;
            }

            if (depth == 0)
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(depth * 8);
            cursor = target.parent;
            var segments = new string[depth];
            for (int i = depth - 1; i >= 0; i--)
            {
                segments[i] = cursor.name;
                cursor = cursor.parent;
            }

            for (int i = 0; i < depth; i++)
            {
                if (i > 0)
                {
                    builder.Append(TransformKeyRegistry.KeyPathSeparator);
                }

                builder.Append(segments[i]);
            }

            return builder.ToString();
        }

        private void RebuildTargets(bool includeHierarchy)
        {
            serializedObject.ApplyModifiedProperties();
            for (int i = 0; i < targets.Length; i++)
            {
                if (!(targets[i] is TransformKeyRegistry registry))
                {
                    continue;
                }

                if (includeHierarchy)
                {
                    registry.BuildHierarchy();
                }
                else
                {
                    registry.BuildIndex();
                }
            }

            Repaint();
        }

        private void InvalidateValidation()
        {
            _validationDirty = true;
            Repaint();
        }

        private void OnIndexNotBuilt(TransformKeyRegistry registry)
        {
            if (registry == null)
            {
                return;
            }

            Log.Warning(
                string.Concat(
                    "TransformKeyRegistry on '",
                    registry.gameObject.name,
                    "' was queried before its index was built. Build it during load or enable Build On Awake."));
        }

        private static bool IsAnyTargetEnabled(SerializedProperty property)
        {
            if (property == null || property.hasMultipleDifferentValues)
            {
                return true;
            }

            return property.boolValue;
        }

        private static string DescribeFlags(int flags)
        {
            if (flags == RowFlagEmptyKey)
            {
                return "Empty key (ignored)";
            }

            if (flags == RowFlagMissing)
            {
                return "Missing Transform (ignored)";
            }

            if (flags == RowFlagUnindexed)
            {
                return "Neither the key nor the path is free, so the row resolves to nothing";
            }

            if ((flags & RowFlagUnindexed) != 0 && (flags & RowFlagExternal) != 0)
            {
                return "Unreachable row and Transform outside the registry root";
            }

            if ((flags & RowFlagUnindexed) != 0)
            {
                return "Key and path are both taken by earlier rows";
            }

            if ((flags & RowFlagDuplicate) != 0 && (flags & RowFlagExternal) != 0)
            {
                return "Duplicate key and Transform outside the registry root";
            }

            if ((flags & RowFlagDuplicate) != 0)
            {
                return "Short key is taken; the qualified path still resolves";
            }

            return "Transform lives outside the registry root, verify ownership and lifetime";
        }

        private struct ValidationSummary
        {
            public int EmptyKeyCount;
            public int MissingTransformCount;
            public int DuplicateKeyCount;

            /// <summary>Entries addressable by neither their short key nor their qualified path.</summary>
            public int UnindexedCount;

            public int ExternalTransformCount;

            public bool IsClean =>
                EmptyKeyCount == 0 &&
                MissingTransformCount == 0 &&
                DuplicateKeyCount == 0 &&
                UnindexedCount == 0 &&
                ExternalTransformCount == 0;
        }
    }
}
