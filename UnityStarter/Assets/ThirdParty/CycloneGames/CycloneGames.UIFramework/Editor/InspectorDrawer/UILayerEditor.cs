#if UNITY_EDITOR
// Copyright (c) CycloneGames
// Licensed under the MIT License.

using UnityEditor;
using UnityEngine;
using CycloneGames.UIFramework.Runtime;

namespace CycloneGames.UIFramework.Editor
{
    [CustomEditor(typeof(UILayer))]
    public class UILayerEditor : UnityEditor.Editor
    {
        private const string InvalidWindowName = "InvalidWindowName";
        private const float WindowRowHeight = 20f;
        private const float WindowControlHeight = 18f;
        private const float WindowStatusBadgeWidth = 72f;
        private const float WindowSelectButtonWidth = 50f;

        // Colors
        private static readonly Color statsColor = new Color(0.4f, 0.5f, 0.7f);
        private static readonly Color windowListColor = new Color(0.5f, 0.6f, 0.5f);
        private static readonly Color successColor = new Color(0.3f, 0.7f, 0.4f);
        private static readonly Color warningColor = new Color(0.9f, 0.6f, 0.2f);
        private static readonly Color errorColor = new Color(0.8f, 0.3f, 0.3f);

        // Foldout states
        private bool showStats = true;
        private bool showWindowList = true;

        // Scroll position for window list
        private Vector2 windowListScrollPos;

        // Cached GUIStyles to avoid per-frame allocations
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _whiteNameStyle;
        private GUIStyle _errorNameStyle;
        private GUIStyle _priorityStyle;
        private bool _stylesInitialized;

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };

            _subtitleStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 10 };

            _valueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13
            };

            _whiteNameStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = Color.white } };
            _errorNameStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = errorColor } };

            _priorityStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter
            };

        }

        public override void OnInspectorGUI()
        {
            UILayer uiLayer = (UILayer)target;

            // Initialize cached styles
            InitializeStyles();
            // Title
            DrawTitle(uiLayer);

            EditorGUILayout.Space(5);

            // Not initialized warning
            if (!uiLayer.IsFinishedLayerInit)
            {
                EditorGUILayout.HelpBox("Layer not initialized yet. Enter Play Mode to see runtime data.", MessageType.Info);
                
                EditorGUILayout.Space(5);
                
                // Show default inspector for serialized fields
                DrawDefaultInspector();
                return;
            }

            // Stats Section
            showStats = InspectorUiUtility.DrawFoldoutHeader("Layer Statistics", showStats, statsColor);
            if (showStats)
            {
                DrawStatsSection(uiLayer);
            }

            EditorGUILayout.Space(3);

            // Window List Section
            int windowCount = uiLayer.WindowCount;
            string windowListTitle = $"Active Windows ({windowCount})";
            showWindowList = InspectorUiUtility.DrawFoldoutHeader(windowListTitle, showWindowList, windowListColor);
            if (showWindowList)
            {
                DrawWindowListSection(uiLayer);
            }

            // Auto-repaint during play mode
            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void DrawTitle(UILayer uiLayer)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("UI Layer", _titleStyle, GUILayout.Height(24));

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // Subtitle
            EditorGUILayout.LabelField(uiLayer.LayerName, _subtitleStyle);
        }

        private void DrawStatsSection(UILayer uiLayer)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            int childCount = uiLayer.transform.childCount;
            int windowCount = uiLayer.WindowCount;
            bool isMatch = childCount == windowCount;

            // Stats grid
            EditorGUILayout.BeginHorizontal();
            
            // Child Count
            DrawStatBox("Children", childCount.ToString(), Color.white);
            
            // Window Count
            DrawStatBox("Windows", windowCount.ToString(), Color.white);
            
            // Status
            Color statusColor = isMatch ? successColor : errorColor;
            string statusText = isMatch ? "Synced" : "Mismatch";
            DrawStatusStatBox("Status", statusText, statusColor);

            EditorGUILayout.EndHorizontal();

            // Warning if mismatch
            if (!isMatch)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "Child count does not match Window count. Possible causes:\n" +
                    "- Non-UIWindow GameObjects in hierarchy\n" +
                    "- UIWindows not registered in UILayer\n" +
                    "- Windows destroyed but not unregistered",
                    MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawStatBox(string label, string value, Color valueColor)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(80));
            
            EditorGUILayout.LabelField(label, EditorStyles.centeredGreyMiniLabel);
            
            // Use cached style and update color
            _valueStyle.normal.textColor = valueColor;
            EditorGUILayout.LabelField(value, _valueStyle);
            
            EditorGUILayout.EndVertical();
        }

        private static void DrawStatusStatBox(string label, string value, Color valueColor)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(80));

            EditorGUILayout.LabelField(label, EditorStyles.centeredGreyMiniLabel);

            Rect badgeRect = GUILayoutUtility.GetRect(80f, 18f, GUILayout.Height(18f));
            InspectorUiUtility.DrawStatusBadge(badgeRect, value, valueColor);

            EditorGUILayout.EndVertical();
        }

        private void DrawWindowListSection(UILayer uiLayer)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (uiLayer.UIWindowArray == null || uiLayer.WindowCount == 0)
            {
                EditorGUILayout.LabelField("No active windows", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            // Header row
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("#", EditorStyles.miniLabel, GUILayout.Width(25));
            EditorGUILayout.LabelField("Window Name", EditorStyles.miniLabel, GUILayout.MinWidth(120));
            EditorGUILayout.LabelField("Priority", EditorStyles.miniLabel, GUILayout.Width(55));
            EditorGUILayout.LabelField("Status", EditorStyles.miniLabel, GUILayout.Width(WindowStatusBadgeWidth));
            EditorGUILayout.LabelField("Action", EditorStyles.miniLabel, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            // Separator
            Rect separatorRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(separatorRect, Color.gray * 0.5f);

            // Scrollable window list (max 8 items visible)
            float itemHeight = 22f;
            int visibleItems = Mathf.Min(8, uiLayer.WindowCount);
            float scrollHeight = visibleItems * itemHeight;

            windowListScrollPos = EditorGUILayout.BeginScrollView(windowListScrollPos, 
                GUILayout.Height(scrollHeight + 5));

            for (int i = 0; i < uiLayer.WindowCount; i++)
            {
                var window = uiLayer.UIWindowArray[i];
                DrawWindowRow(i, window, uiLayer);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawWindowRow(int index, UIWindow window, UILayer uiLayer)
        {
            bool isValid = window != null;
            bool isChild = isValid && window.transform.parent == uiLayer.transform;
            bool isActive = isValid && window.gameObject.activeInHierarchy;

            // Alternating row background
            Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(WindowRowHeight));
            if (index % 2 == 0)
            {
                EditorGUI.DrawRect(rowRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));
            }

            // Index
            EditorGUILayout.LabelField(index.ToString(), GUILayout.Width(25));

            // Window Name
            string windowName = isValid ? window.WindowName : InvalidWindowName;
            EditorGUILayout.LabelField(windowName, isValid ? _whiteNameStyle : _errorNameStyle, GUILayout.MinWidth(120));

            // Priority
            string priorityText = isValid ? window.Priority.ToString() : "N/A";
            Color priorityColor = GetPriorityColor(isValid ? window.Priority : 0);
            _priorityStyle.normal.textColor = priorityColor;
            EditorGUILayout.LabelField(priorityText, _priorityStyle, GUILayout.Width(55));

            string statusLabel;
            Color statusColor;
            if (!isValid)
            {
                statusLabel = "Null";
                statusColor = errorColor;
            }
            else if (!isChild)
            {
                statusLabel = "Orphan";
                statusColor = warningColor;
            }
            else if (!isActive)
            {
                statusLabel = "Hidden";
                statusColor = Color.gray;
            }
            else
            {
                statusLabel = "Active";
                statusColor = successColor;
            }

            Rect statusRect = GUILayoutUtility.GetRect(
                WindowStatusBadgeWidth,
                WindowControlHeight,
                GUILayout.Width(WindowStatusBadgeWidth),
                GUILayout.Height(WindowControlHeight));
            InspectorUiUtility.DrawStatusBadge(statusRect, statusLabel, statusColor);

            var selectContent = new GUIContent("Select", "Click to select and ping this window in Hierarchy");
            Rect selectRect = GUILayoutUtility.GetRect(
                WindowSelectButtonWidth,
                WindowControlHeight,
                GUILayout.Width(WindowSelectButtonWidth),
                GUILayout.Height(WindowControlHeight));
            using (new EditorGUI.DisabledScope(!isValid))
            {
                if (GUI.Button(selectRect, selectContent, EditorStyles.miniButton))
                {
                    EditorGUIUtility.PingObject(window.gameObject);
                    Selection.activeGameObject = window.gameObject;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private Color GetPriorityColor(int priority)
        {
            if (priority < 0) return new Color(0.6f, 0.6f, 0.9f);
            if (priority < 100) return Color.white;
            if (priority < 200) return new Color(0.9f, 0.8f, 0.3f);
            return new Color(0.9f, 0.5f, 0.3f);
        }

    }
}
#endif
