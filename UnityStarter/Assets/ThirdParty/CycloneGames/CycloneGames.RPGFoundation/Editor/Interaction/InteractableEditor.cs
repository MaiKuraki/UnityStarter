using System;
using UnityEditor;
using UnityEngine;
using Cysharp.Threading.Tasks;
using CycloneGames.RPGFoundation.Runtime.Interaction;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    [CustomEditor(typeof(Interactable), true)]
    [CanEditMultipleObjects]
    public class InteractableEditor : UnityEditor.Editor
    {
        private Interactable _target;

        private SerializedProperty _stableId;
        private SerializedProperty _interactionPrompt;
        private SerializedProperty _isInteractable;
        private SerializedProperty _autoInteract;
        private SerializedProperty _priority;
        private SerializedProperty _interactionDistance;
        private SerializedProperty _interactionPoint;
        private SerializedProperty _interactionCooldown;
        private SerializedProperty _resetToIdleOnComplete;
        private SerializedProperty _channel;
        private SerializedProperty _holdDuration;
        private SerializedProperty _maxInteractionRange;
        private SerializedProperty _positionUpdateThreshold;
        private SerializedProperty _actions;
        private SerializedProperty _useLocalization;
        private SerializedProperty _promptData;
        private SerializedProperty _onInteract;
        private SerializedProperty _onFocus;
        private SerializedProperty _onDefocus;

        private static bool s_validationFoldout = true;
        private static bool s_runtimeFoldout = true;
        private static bool s_coreFoldout = true;
        private static bool s_behaviorFoldout = true;
        private static bool s_actionsFoldout;
        private static bool s_localizationFoldout = true;
        private static bool s_eventsFoldout;
        private static bool s_sceneViewFoldout;

        private static readonly Color ColorIdle = new(0.3f, 0.8f, 0.3f, 1f);
        private static readonly Color ColorInteracting = new(1f, 0.6f, 0.2f, 1f);
        private static readonly Color ColorDisabled = new(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color ColorAuto = new(0.3f, 0.7f, 1f, 1f);
        private static readonly Color ColorCooldown = new(0.8f, 0.4f, 0.4f, 1f);

        private static GUIStyle s_gizmoLabelStyle;

        private static readonly string[] ExcludedProperties =
        {
            "m_Script",
            "stableId",
            "interactionPrompt",
            "isInteractable",
            "autoInteract",
            "priority",
            "interactionDistance",
            "interactionPoint",
            "interactionCooldown",
            "resetToIdleOnComplete",
            "useLocalization",
            "promptData",
            "channel",
            "actions",
            "holdDuration",
            "maxInteractionRange",
            "positionUpdateThreshold",
            "onInteract",
            "onFocus",
            "onDefocus"
        };

        private void OnEnable()
        {
            _target = (Interactable)target;

            _stableId = serializedObject.FindProperty("stableId");
            _interactionPrompt = serializedObject.FindProperty("interactionPrompt");
            _isInteractable = serializedObject.FindProperty("isInteractable");
            _autoInteract = serializedObject.FindProperty("autoInteract");
            _priority = serializedObject.FindProperty("priority");
            _interactionDistance = serializedObject.FindProperty("interactionDistance");
            _interactionPoint = serializedObject.FindProperty("interactionPoint");
            _interactionCooldown = serializedObject.FindProperty("interactionCooldown");
            _resetToIdleOnComplete = serializedObject.FindProperty("resetToIdleOnComplete");
            _channel = serializedObject.FindProperty("channel");
            _holdDuration = serializedObject.FindProperty("holdDuration");
            _maxInteractionRange = serializedObject.FindProperty("maxInteractionRange");
            _positionUpdateThreshold = serializedObject.FindProperty("positionUpdateThreshold");
            _actions = serializedObject.FindProperty("actions");
            _useLocalization = serializedObject.FindProperty("useLocalization");
            _promptData = serializedObject.FindProperty("promptData");
            _onInteract = serializedObject.FindProperty("onInteract");
            _onFocus = serializedObject.FindProperty("onFocus");
            _onDefocus = serializedObject.FindProperty("onDefocus");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawHeader();
            InteractionComponentRules.DrawIssuesFor(targets);
            DrawValidation();
            DrawRuntimeStatus();
            DrawCoreSettings();
            DrawBehaviorSettings();
            DrawActionsSettings();
            DrawLocalizationSettings();
            DrawEventSettings();
            DrawSceneViewSettings();

            InteractionInspectorUiUtility.DrawDerivedProperties(
                serializedObject,
                target.GetType().Name + " Settings",
                ExcludedProperties);

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
                Repaint();
        }

        private new void DrawHeader()
        {
            EditorGUILayout.LabelField("Interactable", EditorStyles.boldLabel);
            InteractionInspectorUiUtility.DrawHelpBox(
                "World object that can be detected and interacted with. Physics modes require a Collider or Collider2D on a layer included by the detector. SpatialHash mode can work without colliders.",
                MessageType.None);
        }

        private void DrawValidation()
        {
            s_validationFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Validation",
                s_validationFoldout,
                InteractionInspectorUiUtility.ColorWarning);
            if (!s_validationFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Collider collider3D = _target.GetComponent<Collider>();
                Collider2D collider2D = _target.GetComponent<Collider2D>();

                if (collider3D == null && collider2D == null)
                {
                    InteractionInspectorUiUtility.DrawHelpBox(
                        "No Collider or Collider2D was found. Physics3D and Physics2D detection require a collider; SpatialHash detection does not.",
                        MessageType.Warning);

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Add SphereCollider"))
                    {
                        SphereCollider sphere = Undo.AddComponent<SphereCollider>(_target.gameObject);
                        sphere.isTrigger = true;
                        sphere.radius = 0.1f;
                    }

                    if (GUILayout.Button("Add CircleCollider2D"))
                    {
                        CircleCollider2D circle = Undo.AddComponent<CircleCollider2D>(_target.gameObject);
                        circle.isTrigger = true;
                        circle.radius = 0.1f;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    if (collider3D != null && !collider3D.isTrigger)
                    {
                        InteractionInspectorUiUtility.DrawHelpBox(
                            "The 3D collider is solid. Trigger colliders are usually safer for walk-through interaction volumes.",
                            MessageType.Info);
                    }

                    if (collider2D != null && !collider2D.isTrigger)
                    {
                        InteractionInspectorUiUtility.DrawHelpBox(
                            "The 2D collider is solid. Trigger colliders are usually safer for walk-through interaction volumes.",
                            MessageType.Info);
                    }
                }

                if (LayerMask.LayerToName(_target.gameObject.layer) == "Default")
                {
                    InteractionInspectorUiUtility.DrawHelpBox(
                        "This object is on the Default layer. Ensure the detector layer mask includes this layer, or move the object to an explicit interactable layer.",
                        MessageType.Info);
                }
            }
        }

        private void DrawRuntimeStatus()
        {
            if (!Application.isPlaying)
                return;

            s_runtimeFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Runtime Status",
                s_runtimeFoldout,
                InteractionInspectorUiUtility.ColorRuntime);
            if (!s_runtimeFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawStatusRow("State", _target.CurrentState.ToString(), GetStateColor());
                DrawStatusRow("Interactable", _target.IsInteractable ? "Available" : "Blocked", _target.IsInteractable ? ColorIdle : ColorCooldown);
                DrawStatusRow("Interacting", _target.IsInteracting ? "Yes" : "No", _target.IsInteracting ? ColorInteracting : ColorDisabled);

                if (_target.InteractionProgress > 0f)
                {
                    Rect rect = EditorGUILayout.GetControlRect(false, 18f);
                    EditorGUI.ProgressBar(rect, _target.InteractionProgress, _target.InteractionProgress.ToString("P0"));
                }

                if (_interactionCooldown.floatValue > 0f)
                {
                    float remaining = _target.CooldownRemaining;
                    float progress = 1f - remaining / _interactionCooldown.floatValue;
                    Rect rect = EditorGUILayout.GetControlRect(false, 18f);
                    EditorGUI.ProgressBar(rect, Mathf.Clamp01(progress), remaining > 0f ? remaining.ToString("F1") + "s" : "Ready");
                }

                InstigatorHandle instigator = _target.CurrentInstigator;
                if (instigator != null)
                {
                    if (instigator is GameObjectInstigator gameObjectInstigator && gameObjectInstigator.GameObject != null)
                    {
                        EditorGUILayout.ObjectField("Instigator", gameObjectInstigator.GameObject, typeof(GameObject), true);
                    }
                    else
                    {
                        EditorGUILayout.LabelField("Instigator", instigator.Id.ToString());
                    }
                }

                EditorGUILayout.BeginHorizontal();
                GUI.enabled = _target.IsInteractable;
                if (GUILayout.Button("Trigger Interact"))
                    _target.TryInteractAsync().Forget();

                GUI.enabled = _target.IsInteracting;
                if (GUILayout.Button("Force End"))
                    _target.ForceEndInteraction();

                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawStatusRow(string label, string value, Color color)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(96f));
            Rect badgeRect = EditorGUILayout.GetControlRect(false, 18f, GUILayout.Width(120f));
            InteractionInspectorUiUtility.DrawStatusBadge(badgeRect, value, color);
            EditorGUILayout.EndHorizontal();
        }

        private Color GetStateColor()
        {
            if (!_isInteractable.boolValue) return ColorDisabled;
            if (_autoInteract.boolValue) return ColorAuto;
            if (_target.IsInteracting) return ColorInteracting;
            if (_target.CooldownRemaining > 0f) return ColorCooldown;
            return ColorIdle;
        }

        private void DrawCoreSettings()
        {
            s_coreFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Core Settings",
                s_coreFoldout,
                InteractionInspectorUiUtility.ColorCore);
            if (!s_coreFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_isInteractable, new GUIContent("Enabled"));
                EditorGUILayout.PropertyField(_stableId, new GUIContent("Stable Id"));
                if (string.IsNullOrWhiteSpace(_stableId.stringValue))
                {
                    InteractionInspectorUiUtility.DrawHelpBox(
                        "Stable Id is required for server-authoritative multiplayer, save data, replay, and analytics. Leave empty only for local-only scene objects.",
                        MessageType.Info);
                }
                EditorGUILayout.PropertyField(_autoInteract, new GUIContent("Auto Interact"));
                EditorGUILayout.PropertyField(_priority, new GUIContent("Priority"));
                EditorGUILayout.PropertyField(_channel, new GUIContent("Channel"));
                EditorGUILayout.Space(2f);
                EditorGUILayout.PropertyField(_interactionDistance, new GUIContent("Interaction Distance"));
                EditorGUILayout.PropertyField(_interactionPoint, new GUIContent("Interaction Point"));
            }
        }

        private void DrawBehaviorSettings()
        {
            s_behaviorFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Behavior",
                s_behaviorFoldout,
                InteractionInspectorUiUtility.ColorBehavior);
            if (!s_behaviorFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_interactionCooldown, new GUIContent("Cooldown"));
                EditorGUILayout.PropertyField(_resetToIdleOnComplete, new GUIContent("Reset To Idle On Complete"));
                EditorGUILayout.PropertyField(_holdDuration, new GUIContent("Hold Duration"));
                EditorGUILayout.PropertyField(_maxInteractionRange, new GUIContent("Max Interaction Range"));
                EditorGUILayout.PropertyField(_positionUpdateThreshold, new GUIContent("Position Update Threshold"));

                if (_holdDuration.floatValue > 0f || _maxInteractionRange.floatValue > 0f)
                {
                    InteractionInspectorUiUtility.DrawHelpBox(
                        "Hold progress and range cancellation are evaluated during the active interaction. Moving interactables should call NotifyPositionChanged from their movement system when using SpatialHash detection.",
                        MessageType.None);
                }
            }
        }

        private void DrawActionsSettings()
        {
            s_actionsFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Actions",
                s_actionsFoldout,
                InteractionInspectorUiUtility.ColorCore);
            if (!s_actionsFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                InteractionInspectorUiUtility.DrawInteractionActions(_actions);
                InteractionInspectorUiUtility.DrawHelpBox(
                    "Leave the array empty for a single default action. Use stable ActionId values when UI, input, save data, or networking needs to reference actions.",
                    MessageType.None);
            }
        }

        private void DrawLocalizationSettings()
        {
            s_localizationFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Prompt And Localization",
                s_localizationFoldout,
                InteractionInspectorUiUtility.ColorCore);
            if (!s_localizationFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_useLocalization, new GUIContent("Use Localization"));
                if (_useLocalization.boolValue)
                {
                    InteractionInspectorUiUtility.DrawIndentedProperty(_promptData, new GUIContent("Prompt Data"), true);
                    InteractionInspectorUiUtility.DrawHelpBox("Fallback text is used if your localization layer cannot resolve the prompt key.", MessageType.None);
                }
                else
                {
                    EditorGUILayout.PropertyField(_interactionPrompt, new GUIContent("Prompt Text"));
                }
            }
        }

        private void DrawEventSettings()
        {
            s_eventsFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Unity Events",
                s_eventsFoldout,
                InteractionInspectorUiUtility.ColorBehavior);
            if (!s_eventsFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                InteractionInspectorUiUtility.DrawIndentedProperty(_onInteract, new GUIContent("On Interact"), true);
                InteractionInspectorUiUtility.DrawIndentedProperty(_onFocus, new GUIContent("On Focus"), true);
                InteractionInspectorUiUtility.DrawIndentedProperty(_onDefocus, new GUIContent("On Defocus"), true);
                InteractionInspectorUiUtility.DrawHelpBox(
                    "UnityEvent callbacks are convenient for scene wiring. Prefer code overrides or injected services for high-frequency gameplay logic.",
                    MessageType.None);
            }
        }

        private void DrawSceneViewSettings()
        {
            s_sceneViewFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Scene View",
                s_sceneViewFoldout,
                InteractionInspectorUiUtility.ColorDebug);
            if (!s_sceneViewFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                InteractableGizmoSettings.ShowGizmos = EditorGUILayout.Toggle("Show Gizmos", InteractableGizmoSettings.ShowGizmos);
                InteractableGizmoSettings.ShowLabels = EditorGUILayout.Toggle("Show Labels", InteractableGizmoSettings.ShowLabels);
            }
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Pickable)]
        private static void DrawInteractableGizmos(Interactable interactable, GizmoType gizmoType)
        {
            if (!InteractableGizmoSettings.ShowGizmos) return;

            bool isSelected = (gizmoType & GizmoType.Selected) != 0;
            Vector3 pos = interactable.Position;
            float radius = interactable.InteractionDistance;

            Color wireColor;
            if (!interactable.isActiveAndEnabled)
                wireColor = ColorDisabled;
            else if (interactable.AutoInteract)
                wireColor = ColorAuto;
            else if (Application.isPlaying && interactable.IsInteracting)
                wireColor = ColorInteracting;
            else
                wireColor = ColorIdle;

            wireColor.a = isSelected ? 0.8f : 0.4f;
            Handles.color = wireColor;
            Handles.DrawWireDisc(pos, Vector3.up, radius);
            Handles.DrawWireDisc(pos, Vector3.forward, radius);
            Handles.DrawWireDisc(pos, Vector3.right, radius);

            if (isSelected)
            {
                Color fillColor = wireColor;
                fillColor.a = 0.1f;
                Handles.color = fillColor;
                Handles.DrawSolidDisc(pos, Vector3.up, radius);
            }

            if (!InteractableGizmoSettings.ShowLabels)
                return;

            if (s_gizmoLabelStyle == null)
            {
                s_gizmoLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter
                };
            }

            s_gizmoLabelStyle.normal.textColor = wireColor;
            string label = Application.isPlaying
                ? interactable.name + "\n[" + interactable.CurrentState + "]"
                : interactable.name;
            Handles.Label(pos + Vector3.up * (radius + 0.5f), label, s_gizmoLabelStyle);
        }
    }

    internal static class InteractionInspectorUiUtility
    {
        private const float HeaderHorizontalPadding = 4f;
        private const float HeaderArrowWidth = 13f;
        private const float BadgeHorizontalPadding = 6f;
        private const float InlineFoldoutIndent = 6f;
        private const float InlineButtonSpacing = 4f;

        private static readonly Vector3[] s_trianglePoints = new Vector3[3];

        private static GUIStyle s_foldoutLabelStyle;
        private static GUIStyle s_badgeStyle;
        private static GUIStyle s_miniButtonStyle;

        private static readonly GUIContent ActionsLabel = new("Actions");
        private static readonly GUIContent ActionIdLabel = new("Action Id");
        private static readonly GUIContent DisplayTextLabel = new("Display Text");
        private static readonly GUIContent LocalizationKeyLabel = new("Localization Key");
        private static readonly GUIContent InputHintLabel = new("Input Hint");
        private static readonly GUIContent DisplayOrderLabel = new("Display Order");
        private static readonly GUIContent IsEnabledLabel = new("Enabled");

        public static readonly Color ColorCore = new(0.22f, 0.45f, 0.74f, 1f);
        public static readonly Color ColorBehavior = new(0.22f, 0.58f, 0.44f, 1f);
        public static readonly Color ColorRuntime = new(0.54f, 0.42f, 0.22f, 1f);
        public static readonly Color ColorDebug = new(0.42f, 0.42f, 0.48f, 1f);
        public static readonly Color ColorWarning = new(0.74f, 0.42f, 0.22f, 1f);
        public static readonly Color ColorDerived = new(0.38f, 0.34f, 0.58f, 1f);

        public static bool DrawFoldoutHeader(string title, bool foldout, Color color)
        {
            EnsureStyles();

            EditorGUILayout.Space(2f);
            Rect rect = EditorGUILayout.GetControlRect(false, 22f);
            Color backgroundColor = foldout ? color : new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f, color.a);
            EditorGUI.DrawRect(rect, backgroundColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), Color.black * 0.2f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Color.black * 0.2f);

            Rect arrowRect = new(
                rect.x + HeaderHorizontalPadding,
                rect.y + 2f,
                HeaderArrowWidth,
                rect.height - 4f);

            Rect labelRect = new(
                arrowRect.xMax + 1f,
                rect.y,
                rect.width - (arrowRect.xMax - rect.x) - HeaderHorizontalPadding - 1f,
                rect.height);

            DrawFoldoutTriangle(arrowRect, foldout);
            EditorGUI.LabelField(labelRect, title, s_foldoutLabelStyle);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                foldout = !foldout;
                Event.current.Use();
            }

            return foldout;
        }

        public static void DrawHelpBox(string message, MessageType messageType = MessageType.None)
        {
            EditorGUILayout.HelpBox(message, messageType);
        }

        public static void DrawStatusBadge(Rect rect, string label, Color color)
        {
            EnsureStyles();

            Color backgroundColor = new(color.r, color.g, color.b, 0.85f);
            EditorGUI.DrawRect(rect, backgroundColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), Color.black * 0.18f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Color.black * 0.18f);

            Rect labelRect = new(
                rect.x + BadgeHorizontalPadding,
                rect.y,
                rect.width - BadgeHorizontalPadding * 2f,
                rect.height);
            EditorGUI.LabelField(labelRect, label, s_badgeStyle);
        }

        public static void DrawIndentedProperty(SerializedProperty property, GUIContent label, bool includeChildren = false)
        {
            if (property == null)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(property, label, includeChildren);
            }
        }

        public static void DrawInteractionActions(SerializedProperty actionsProperty)
        {
            EnsureStyles();

            if (actionsProperty == null)
                return;

            if (!actionsProperty.isArray || actionsProperty.serializedObject.isEditingMultipleObjects)
            {
                DrawIndentedProperty(actionsProperty, ActionsLabel, true);
                return;
            }

            Rect headerRect = EditorGUILayout.GetControlRect(false, 20f);
            Rect clearRect = new(headerRect.xMax - 48f, headerRect.y, 48f, headerRect.height);
            Rect addRect = new(clearRect.x - InlineButtonSpacing - 46f, headerRect.y, 46f, headerRect.height);
            Rect foldoutRect = new(headerRect.x, headerRect.y, addRect.x - headerRect.x - InlineButtonSpacing, headerRect.height);

            actionsProperty.isExpanded = DrawInlineFoldout(foldoutRect, "Actions (" + actionsProperty.arraySize + ")", actionsProperty.isExpanded);

            if (GUI.Button(addRect, "Add", s_miniButtonStyle))
            {
                AddActionElement(actionsProperty);
            }

            EditorGUI.BeginDisabledGroup(actionsProperty.arraySize == 0);
            if (GUI.Button(clearRect, "Clear", s_miniButtonStyle))
            {
                actionsProperty.arraySize = 0;
            }
            EditorGUI.EndDisabledGroup();

            if (!actionsProperty.isExpanded)
                return;

            if (actionsProperty.arraySize == 0)
            {
                EditorGUILayout.HelpBox("List is empty. The interactable will expose one default action.", MessageType.Info);
                return;
            }

            for (int i = 0; i < actionsProperty.arraySize; i++)
            {
                SerializedProperty element = actionsProperty.GetArrayElementAtIndex(i);
                DrawActionElement(actionsProperty, element, i);
            }

        }

        public static void DrawDerivedProperties(SerializedObject serializedObject, string title, string[] excludedProperties)
        {
            if (!HasDerivedProperties(serializedObject, excludedProperties))
                return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                SerializedProperty iterator = serializedObject.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (Array.IndexOf(excludedProperties, iterator.name) >= 0)
                        continue;
                    DrawIndentedProperty(iterator, new GUIContent(iterator.displayName), true);
                }
            }
        }

        private static void DrawActionElement(SerializedProperty actionsProperty, SerializedProperty element, int index)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Rect headerRect = EditorGUILayout.GetControlRect(false, 20f);
                Rect removeRect = new(headerRect.xMax - 44f, headerRect.y, 44f, headerRect.height);
                Rect downRect = new(removeRect.x - InlineButtonSpacing - 28f, headerRect.y, 28f, headerRect.height);
                Rect upRect = new(downRect.x - InlineButtonSpacing - 28f, headerRect.y, 28f, headerRect.height);
                Rect badgeRect = new(upRect.x - InlineButtonSpacing - 66f, headerRect.y + 1f, 66f, headerRect.height - 2f);
                Rect foldoutRect = new(headerRect.x, headerRect.y, upRect.x - headerRect.x - InlineButtonSpacing, headerRect.height);

                element.isExpanded = DrawInlineFoldout(foldoutRect, GetActionTitle(element, index), element.isExpanded);
                DrawActionStateBadge(badgeRect, element);

                EditorGUI.BeginDisabledGroup(index == 0);
                if (GUI.Button(upRect, "Up", s_miniButtonStyle))
                {
                    actionsProperty.MoveArrayElement(index, index - 1);
                    EditorGUI.EndDisabledGroup();
                    return;
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(index >= actionsProperty.arraySize - 1);
                if (GUI.Button(downRect, "Dn", s_miniButtonStyle))
                {
                    actionsProperty.MoveArrayElement(index, index + 1);
                    EditorGUI.EndDisabledGroup();
                    return;
                }
                EditorGUI.EndDisabledGroup();

                if (GUI.Button(removeRect, "Del", s_miniButtonStyle))
                {
                    actionsProperty.DeleteArrayElementAtIndex(index);
                    return;
                }

                if (!element.isExpanded)
                    return;

                using (new EditorGUI.IndentLevelScope())
                {
                    DrawRelativeProperty(element, "ActionId", ActionIdLabel);
                    DrawRelativeProperty(element, "DisplayText", DisplayTextLabel);
                    DrawRelativeProperty(element, "LocalizationKey", LocalizationKeyLabel);
                    DrawRelativeProperty(element, "InputHint", InputHintLabel);
                    DrawRelativeProperty(element, "DisplayOrder", DisplayOrderLabel);
                    DrawRelativeProperty(element, "IsEnabled", IsEnabledLabel);
                    DrawActionElementValidation(actionsProperty, element, index);
                }
            }
        }

        private static void DrawActionStateBadge(Rect rect, SerializedProperty element)
        {
            SerializedProperty enabled = element.FindPropertyRelative("IsEnabled");
            bool isEnabled = enabled == null || enabled.boolValue;
            DrawStatusBadge(rect, isEnabled ? "Enabled" : "Disabled", isEnabled ? ColorBehavior : ColorDebug);
        }

        private static void DrawActionElementValidation(SerializedProperty actionsProperty, SerializedProperty element, int index)
        {
            SerializedProperty actionId = element.FindPropertyRelative("ActionId");
            SerializedProperty displayText = element.FindPropertyRelative("DisplayText");
            SerializedProperty localizationKey = element.FindPropertyRelative("LocalizationKey");

            string actionIdValue = actionId != null ? actionId.stringValue : string.Empty;
            if (string.IsNullOrWhiteSpace(actionIdValue))
            {
                EditorGUILayout.HelpBox("Action Id is required. UI, input, save data, and network adapters should reference stable action ids.", MessageType.Error);
            }
            else
            {
                if (actionIdValue.IndexOf(' ') >= 0)
                {
                    EditorGUILayout.HelpBox("Action Id contains spaces. Prefer stable ids such as pickup, examine, or open_door.", MessageType.Warning);
                }

                if (HasDuplicateActionId(actionsProperty, actionIdValue, index))
                {
                    EditorGUILayout.HelpBox("Action Id is duplicated in this list.", MessageType.Error);
                }
            }

            bool hasDisplayText = displayText != null && !string.IsNullOrWhiteSpace(displayText.stringValue);
            bool hasLocalizationKey = localizationKey != null && !string.IsNullOrWhiteSpace(localizationKey.stringValue);
            if (!hasDisplayText && !hasLocalizationKey)
            {
                EditorGUILayout.HelpBox("Display Text or Localization Key should be set for player-facing prompts.", MessageType.Info);
            }
        }

        private static bool HasDuplicateActionId(SerializedProperty actionsProperty, string actionId, int currentIndex)
        {
            for (int i = 0; i < actionsProperty.arraySize; i++)
            {
                if (i == currentIndex)
                    continue;

                SerializedProperty other = actionsProperty.GetArrayElementAtIndex(i).FindPropertyRelative("ActionId");
                if (other != null && string.Equals(other.stringValue, actionId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool DrawInlineFoldout(Rect rect, string title, bool expanded)
        {
            EnsureStyles();

            Rect arrowRect = new(
                rect.x + InlineFoldoutIndent,
                rect.y + 2f,
                HeaderArrowWidth,
                rect.height - 4f);

            Rect labelRect = new(
                arrowRect.xMax + 3f,
                rect.y,
                Mathf.Max(0f, rect.xMax - arrowRect.xMax - 3f),
                rect.height);

            DrawFoldoutTriangle(arrowRect, expanded);
            EditorGUI.LabelField(labelRect, title, EditorStyles.boldLabel);

            Event current = Event.current;
            if (current.type == EventType.MouseDown && rect.Contains(current.mousePosition))
            {
                expanded = !expanded;
                current.Use();
            }

            return expanded;
        }

        private static void DrawRelativeProperty(SerializedProperty parent, string propertyName, GUIContent label)
        {
            SerializedProperty property = parent.FindPropertyRelative(propertyName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, label);
            }
        }

        private static string GetActionTitle(SerializedProperty element, int index)
        {
            SerializedProperty actionId = element.FindPropertyRelative("ActionId");
            SerializedProperty displayText = element.FindPropertyRelative("DisplayText");

            string id = actionId != null ? actionId.stringValue : string.Empty;
            string text = displayText != null ? displayText.stringValue : string.Empty;

            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(text))
                return id + " | " + text;

            if (!string.IsNullOrEmpty(id))
                return id;

            if (!string.IsNullOrEmpty(text))
                return "Element " + index + " | " + text;

            return "Element " + index;
        }

        private static void AddActionElement(SerializedProperty actionsProperty)
        {
            int index = actionsProperty.arraySize;
            actionsProperty.InsertArrayElementAtIndex(index);
            InitializeActionElement(actionsProperty.GetArrayElementAtIndex(index), index);
            actionsProperty.isExpanded = true;
        }

        private static void InitializeActionElement(SerializedProperty element, int index)
        {
            SetString(element, "ActionId", "action_" + index);
            SetString(element, "DisplayText", string.Empty);
            SetString(element, "LocalizationKey", string.Empty);
            SetString(element, "InputHint", string.Empty);
            SetInt(element, "DisplayOrder", index);
            SetBool(element, "IsEnabled", true);
            element.isExpanded = true;
        }

        private static void SetString(SerializedProperty parent, string propertyName, string value)
        {
            SerializedProperty property = parent.FindPropertyRelative(propertyName);
            if (property != null)
                property.stringValue = value;
        }

        private static void SetInt(SerializedProperty parent, string propertyName, int value)
        {
            SerializedProperty property = parent.FindPropertyRelative(propertyName);
            if (property != null)
                property.intValue = value;
        }

        private static void SetBool(SerializedProperty parent, string propertyName, bool value)
        {
            SerializedProperty property = parent.FindPropertyRelative(propertyName);
            if (property != null)
                property.boolValue = value;
        }

        private static bool HasDerivedProperties(SerializedObject serializedObject, string[] excludedProperties)
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (Array.IndexOf(excludedProperties, iterator.name) < 0)
                    return true;
            }

            return false;
        }

        private static void EnsureStyles()
        {
            if (s_foldoutLabelStyle != null)
                return;

            s_foldoutLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            s_badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            s_miniButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter
            };
        }

        private static void DrawFoldoutTriangle(Rect rect, bool expanded)
        {
            Vector2 center = rect.center;

            if (expanded)
            {
                s_trianglePoints[0] = new Vector3(center.x - 4f, center.y - 2f);
                s_trianglePoints[1] = new Vector3(center.x + 4f, center.y - 2f);
                s_trianglePoints[2] = new Vector3(center.x, center.y + 3f);
            }
            else
            {
                s_trianglePoints[0] = new Vector3(center.x - 2f, center.y - 4f);
                s_trianglePoints[1] = new Vector3(center.x - 2f, center.y + 4f);
                s_trianglePoints[2] = new Vector3(center.x + 3f, center.y);
            }

            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = new Color(0.9f, 0.9f, 0.9f, 0.95f);
            Handles.DrawAAConvexPolygon(s_trianglePoints);
            Handles.color = previousColor;
            Handles.EndGUI();
        }
    }

    [CustomEditor(typeof(PooledEffect), true)]
    [CanEditMultipleObjects]
    public sealed class PooledEffectEditor : UnityEditor.Editor
    {
        private PooledEffect _target;
        private SerializedProperty _defaultDuration;

        private static bool s_lifetimeFoldout = true;
        private static bool s_validationFoldout = true;
        private static bool s_runtimeFoldout = true;
        private static System.Reflection.FieldInfo s_isPooledField;
        private static System.Reflection.FieldInfo s_timerField;

        private static readonly string[] ExcludedProperties =
        {
            "m_Script",
            "defaultDuration"
        };

        private void OnEnable()
        {
            _target = (PooledEffect)target;
            _defaultDuration = serializedObject.FindProperty("defaultDuration");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Pooled Effect", EditorStyles.boldLabel);
            InteractionInspectorUiUtility.DrawHelpBox(
                "Effect prefab component used by EffectPoolSystem. Attach this to prefabs that should avoid Instantiate fallback on spawn.",
                MessageType.None);

            InteractionComponentRules.DrawIssuesFor(targets);
            DrawLifetime();
            DrawValidation();
            DrawRuntimeStatus();

            InteractionInspectorUiUtility.DrawDerivedProperties(
                serializedObject,
                target.GetType().Name + " Settings",
                ExcludedProperties);

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
                Repaint();
        }

        private void DrawLifetime()
        {
            s_lifetimeFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Lifetime",
                s_lifetimeFoldout,
                InteractionInspectorUiUtility.ColorCore);
            if (!s_lifetimeFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_defaultDuration, new GUIContent("Default Duration"));
                if (_defaultDuration.floatValue <= 0f)
                {
                    InteractionInspectorUiUtility.DrawHelpBox(
                        "Default Duration is zero. Effects spawned without an explicit duration will return to the pool on the next Update.",
                        MessageType.Warning);
                }
            }
        }

        private void DrawValidation()
        {
            s_validationFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Validation",
                s_validationFoldout,
                InteractionInspectorUiUtility.ColorWarning);
            if (!s_validationFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                ParticleSystem particleSystem = _target.GetComponent<ParticleSystem>();
                if (particleSystem == null)
                {
                    InteractionInspectorUiUtility.DrawHelpBox(
                        "No ParticleSystem was found. This is valid for custom visual effects, but particle prefabs usually need a ParticleSystem component.",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.ObjectField("Particle System", particleSystem, typeof(ParticleSystem), true);
                }

                if (_target.gameObject.scene.IsValid() && _target.gameObject.scene.isLoaded)
                {
                    InteractionInspectorUiUtility.DrawHelpBox(
                        "Scene instances are valid for testing. Production spawning should usually reference a prefab with PooledEffect and call EffectPoolSystem.Prewarm during loading.",
                        MessageType.None);
                }
            }
        }

        private void DrawRuntimeStatus()
        {
            if (!Application.isPlaying)
                return;

            s_runtimeFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Runtime Status",
                s_runtimeFoldout,
                InteractionInspectorUiUtility.ColorRuntime);
            if (!s_runtimeFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Pool State", GUILayout.Width(100f));
                Rect badgeRect = EditorGUILayout.GetControlRect(false, 18f, GUILayout.Width(140f));
                bool isPooled = GetIsPooled(_target);
                InteractionInspectorUiUtility.DrawStatusBadge(
                    badgeRect,
                    isPooled ? "Spawned" : "Idle",
                    isPooled ? InteractionInspectorUiUtility.ColorRuntime : InteractionInspectorUiUtility.ColorDebug);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField("Remaining", GetRemainingDuration(_target).ToString("F2") + "s");

                using (new EditorGUI.DisabledScope(!isPooled))
                {
                    if (GUILayout.Button("Return To Pool"))
                        _target.ReturnToPool();
                }
            }
        }

        private static bool GetIsPooled(PooledEffect target)
        {
            s_isPooledField ??= typeof(PooledEffect).GetField(
                "_isPooled",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            return s_isPooledField != null && (bool)s_isPooledField.GetValue(target);
        }

        private static float GetRemainingDuration(PooledEffect target)
        {
            s_timerField ??= typeof(PooledEffect).GetField(
                "_timer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (s_timerField == null)
                return 0f;

            float value = (float)s_timerField.GetValue(target);
            return value > 0f ? value : 0f;
        }
    }

    public static class InteractableGizmoSettings
    {
        private const string ShowGizmosKey = "Interactable_ShowGizmos";
        private const string ShowLabelsKey = "Interactable_ShowLabels";

        public static bool ShowGizmos
        {
            get => EditorPrefs.GetBool(ShowGizmosKey, true);
            set => EditorPrefs.SetBool(ShowGizmosKey, value);
        }

        public static bool ShowLabels
        {
            get => EditorPrefs.GetBool(ShowLabelsKey, true);
            set => EditorPrefs.SetBool(ShowLabelsKey, value);
        }
    }
}
