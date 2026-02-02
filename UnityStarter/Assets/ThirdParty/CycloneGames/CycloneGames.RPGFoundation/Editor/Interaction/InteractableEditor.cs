using UnityEngine;
using UnityEditor;
using Cysharp.Threading.Tasks;
using CycloneGames.RPGFoundation.Runtime.Interaction;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    [CustomEditor(typeof(Interactable), true)]
    [CanEditMultipleObjects]
    public class InteractableEditor : UnityEditor.Editor
    {
        private Interactable _target;

        private SerializedProperty _interactionPrompt;
        private SerializedProperty _isInteractable;
        private SerializedProperty _autoInteract;
        private SerializedProperty _priority;
        private SerializedProperty _interactionDistance;
        private SerializedProperty _interactionPoint;
        private SerializedProperty _interactionCooldown;
        private SerializedProperty _resetToIdleOnComplete;

        private SerializedProperty _useLocalization;
        private SerializedProperty _promptData;

        private SerializedProperty _onInteract;
        private SerializedProperty _onFocus;
        private SerializedProperty _onDefocus;

        private static bool _coreSettingsFoldout = true;
        private static bool _behaviorFoldout = true;
        private static bool _localizationFoldout = true;
        private static bool _eventsFoldout;
        private static bool _debugFoldout = true;

        private static readonly Color ColorIdle = new(0.3f, 0.8f, 0.3f, 1f);
        private static readonly Color ColorInteracting = new(1f, 0.6f, 0.2f, 1f);
        private static readonly Color ColorDisabled = new(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color ColorAuto = new(0.3f, 0.7f, 1f, 1f);
        private static readonly Color ColorCooldown = new(0.8f, 0.4f, 0.4f, 1f);

        private void OnEnable()
        {
            _target = (Interactable)target;

            _interactionPrompt = serializedObject.FindProperty("interactionPrompt");
            _isInteractable = serializedObject.FindProperty("isInteractable");
            _autoInteract = serializedObject.FindProperty("autoInteract");
            _priority = serializedObject.FindProperty("priority");
            _interactionDistance = serializedObject.FindProperty("interactionDistance");
            _interactionPoint = serializedObject.FindProperty("interactionPoint");
            _interactionCooldown = serializedObject.FindProperty("interactionCooldown");
            _resetToIdleOnComplete = serializedObject.FindProperty("resetToIdleOnComplete");

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
            DrawValidation();
            DrawRuntimeStatus();

            EditorGUILayout.Space(8);

            DrawCoreSettings();
            DrawBehaviorSettings();
            DrawLocalization();
            DrawEvents();
            DrawDebugSettings();
            DrawDerivedProperties();

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
                Repaint();
        }

        private new void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("🎯 Interactable", EditorStyles.boldLabel);

            GUI.enabled = false;
            EditorGUILayout.ObjectField(_target, typeof(Interactable), false, GUILayout.Width(200));
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Interactive object that can be detected and used by the player.\n" +
                "Requires a Collider (Trigger recommended) on the Interactable layer.",
                MessageType.None);
        }

        private void DrawValidation()
        {
            Collider col = _target.GetComponent<Collider>();
            int layer = _target.gameObject.layer;

            if (col == null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    "⚠️ MISSING COLLIDER\nInteractionDetector uses Physics.OverlapSphere to find this object.",
                    MessageType.Error);

                GUI.backgroundColor = new Color(1f, 0.9f, 0.4f);
                if (GUILayout.Button("+ Add Sphere Collider (Trigger)", GUILayout.Height(24)))
                {
                    Undo.RecordObject(_target.gameObject, "Add SphereCollider");
                    var sphere = _target.gameObject.AddComponent<SphereCollider>();
                    sphere.isTrigger = true;
                    sphere.radius = 0.1f;
                }
                GUI.backgroundColor = Color.white;
            }
            else if (!col.isTrigger)
            {
                EditorGUILayout.HelpBox(
                    "ℹ️ Collider is solid. Consider setting 'Is Trigger = true' for walk-through interactions.",
                    MessageType.Info);
            }

            if (LayerMask.LayerToName(layer) == "Default")
            {
                EditorGUILayout.HelpBox(
                    "⚠️ Object is on Default layer. Ensure InteractionDetector's LayerMask includes this layer.",
                    MessageType.Warning);
            }
        }

        private void DrawRuntimeStatus()
        {
            if (!Application.isPlaying) return;

            EditorGUILayout.Space(4);
            _debugFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_debugFoldout, "🔍 Runtime Status");
            if (_debugFoldout)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    Color statusColor = GetStateColor();
                    GUI.color = statusColor;

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("State", GUILayout.Width(100));
                    EditorGUILayout.LabelField(_target.CurrentState.ToString(), EditorStyles.boldLabel);
                    EditorGUILayout.EndHorizontal();

                    GUI.color = Color.white;

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Is Interacting", GUILayout.Width(100));
                    EditorGUILayout.Toggle(_target.IsInteracting);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Can Interact", GUILayout.Width(100));
                    EditorGUILayout.Toggle(_target.IsInteractable);
                    EditorGUILayout.EndHorizontal();

                    if (_interactionCooldown.floatValue > 0)
                    {
                        float cooldownRemaining = GetCooldownRemaining();
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("Cooldown", GUILayout.Width(100));
                        Rect rect = EditorGUILayout.GetControlRect();
                        float progress = 1f - (cooldownRemaining / _interactionCooldown.floatValue);
                        EditorGUI.ProgressBar(rect, Mathf.Clamp01(progress),
                            cooldownRemaining > 0 ? $"{cooldownRemaining:F1}s" : "Ready");
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.Space(4);
                    EditorGUILayout.BeginHorizontal();
                    GUI.enabled = _target.IsInteractable;
                    if (GUILayout.Button("🎮 Trigger Interact", GUILayout.Height(24)))
                    {
                        _target.TryInteractAsync().Forget();
                    }
                    GUI.enabled = _target.IsInteracting;
                    if (GUILayout.Button("⏹ Force End", GUILayout.Height(24)))
                    {
                        _target.ForceEndInteraction();
                    }
                    GUI.enabled = true;
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private Color GetStateColor()
        {
            if (!_isInteractable.boolValue) return ColorDisabled;
            if (_autoInteract.boolValue) return ColorAuto;
            if (_target.IsInteracting) return ColorInteracting;
            if (GetCooldownRemaining() > 0) return ColorCooldown;
            return ColorIdle;
        }

        private float GetCooldownRemaining()
        {
            if (_interactionCooldown.floatValue <= 0) return 0f;
            var lastTimeField = typeof(Interactable).GetField("_lastInteractionTime",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (lastTimeField == null) return 0f;
            float lastTime = (float)lastTimeField.GetValue(_target);
            float elapsed = Time.time - lastTime;
            return Mathf.Max(0, _interactionCooldown.floatValue - elapsed);
        }

        private void DrawCoreSettings()
        {
            _coreSettingsFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_coreSettingsFoldout, "⚙️ Core Settings");
            if (_coreSettingsFoldout)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(_isInteractable, new GUIContent("Enabled", "Can this object be interacted with?"));
                    EditorGUILayout.PropertyField(_autoInteract, new GUIContent("Auto Interact", "Automatically trigger when player enters range"));
                    EditorGUILayout.PropertyField(_priority, new GUIContent("Priority", "Higher priority objects are selected first"));

                    EditorGUILayout.Space(4);

                    EditorGUILayout.PropertyField(_interactionDistance, new GUIContent("Detection Radius", "Maximum distance for interaction"));
                    EditorGUILayout.PropertyField(_interactionPoint, new GUIContent("Interaction Point", "Override position for distance calculations"));
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawBehaviorSettings()
        {
            _behaviorFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_behaviorFoldout, "🔄 Behavior");
            if (_behaviorFoldout)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(_interactionCooldown, new GUIContent("Cooldown", "Minimum time between interactions"));
                    EditorGUILayout.PropertyField(_resetToIdleOnComplete, new GUIContent("Auto Reset", "Return to Idle state after completion"));
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawLocalization()
        {
            _localizationFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_localizationFoldout, "🌐 Prompt & Localization");
            if (_localizationFoldout)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(_useLocalization, new GUIContent("Use Localization"));

                    EditorGUILayout.Space(2);

                    if (_useLocalization.boolValue)
                    {
                        EditorGUILayout.PropertyField(_promptData, new GUIContent("Prompt Data"), true);
                        EditorGUILayout.HelpBox("FallbackText is used if localization lookup fails.", MessageType.None);
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(_interactionPrompt, new GUIContent("Prompt Text"));
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawEvents()
        {
            _eventsFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_eventsFoldout, "📢 Events");
            if (_eventsFoldout)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(_onInteract, new GUIContent("On Interact"));
                    EditorGUILayout.PropertyField(_onFocus, new GUIContent("On Focus"));
                    EditorGUILayout.PropertyField(_onDefocus, new GUIContent("On Defocus"));
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawDebugSettings()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Scene View", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                InteractableGizmoSettings.ShowGizmos = EditorGUILayout.Toggle("Show Gizmos", InteractableGizmoSettings.ShowGizmos);
                InteractableGizmoSettings.ShowLabels = EditorGUILayout.Toggle("Show Labels", InteractableGizmoSettings.ShowLabels);
                EditorGUI.indentLevel--;
            }
        }

        private static readonly string[] ExcludedProperties =
        {
            "m_Script",
            "interactionPrompt", "isInteractable", "autoInteract", "priority",
            "interactionDistance", "interactionPoint", "interactionCooldown",
            "resetToIdleOnComplete", "useLocalization", "promptData",
            "onInteract", "onFocus", "onDefocus"
        };

        private void DrawDerivedProperties()
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool hasAnyDerived = false;

            if (iterator.NextVisible(true))
            {
                do
                {
                    if (System.Array.IndexOf(ExcludedProperties, iterator.name) < 0)
                    {
                        hasAnyDerived = true;
                        break;
                    }
                } while (iterator.NextVisible(false));
            }

            if (!hasAnyDerived) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("🔧 " + target.GetType().Name + " Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawPropertiesExcluding(serializedObject, ExcludedProperties);
            EditorGUILayout.EndVertical();
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

            if (InteractableGizmoSettings.ShowLabels)
            {
                GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = wireColor }
                };
                string label = Application.isPlaying
                    ? $"{interactable.name}\n[{interactable.CurrentState}]"
                    : interactable.name;
                Handles.Label(pos + Vector3.up * (radius + 0.5f), label, style);
            }
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