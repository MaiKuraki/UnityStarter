using UnityEngine;
using UnityEditor;
using CycloneGames.RPGFoundation.Runtime.Interaction;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    /// <summary>
    /// Scene view overlay for visualizing interaction system state in real-time.
    /// Provides 0GC debug visualization with minimal editor overhead.
    /// </summary>
    [InitializeOnLoad]
    public static class InteractionSceneDebugger
    {
        private static bool s_enabled;
        private static bool s_showLabels = true;
        private static bool s_showConnections;
        private static bool s_showStateColors = true;
        private static float s_labelScale = 1f;

        // Pre-allocated for 0GC
        private static readonly GUIContent s_titleContent = new("Interaction Debug");
        private static readonly Color ColorIdle = new(0.5f, 0.5f, 0.5f, 0.6f);
        private static readonly Color ColorStarting = new(1f, 0.8f, 0.2f, 0.8f);
        private static readonly Color ColorInProgress = new(0.2f, 0.8f, 1f, 0.8f);
        private static readonly Color ColorCompleting = new(0.3f, 1f, 0.5f, 0.8f);
        private static readonly Color ColorCancelled = new(1f, 0.3f, 0.3f, 0.8f);
        private static readonly Color ColorDetector = new(0.2f, 1f, 0.4f, 0.5f);
        private static readonly Color ColorCandidate = new(1f, 0.6f, 0.2f, 0.7f);
        private static readonly Color ColorConnection = new(0.8f, 0.8f, 0.2f, 0.4f);

        private static GUIStyle s_labelStyle;
        private static GUIStyle s_boxStyle;
        private static Rect s_windowRect = new(10, 10, 200, 150);

        static InteractionSceneDebugger()
        {
            s_enabled = EditorPrefs.GetBool("InteractionSceneDebugger_Enabled", false);
            s_showLabels = EditorPrefs.GetBool("InteractionSceneDebugger_ShowLabels", true);
            s_showConnections = EditorPrefs.GetBool("InteractionSceneDebugger_ShowConnections", false);
            s_showStateColors = EditorPrefs.GetBool("InteractionSceneDebugger_ShowStateColors", true);
            s_labelScale = EditorPrefs.GetFloat("InteractionSceneDebugger_LabelScale", 1f);

            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        [MenuItem("Tools/CycloneGames/Interaction/Toggle Scene Interaction Debug")]
        public static void ToggleDebug()
        {
            s_enabled = !s_enabled;
            EditorPrefs.SetBool("InteractionSceneDebugger_Enabled", s_enabled);
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/CycloneGames/Interaction/Toggle Scene Interaction Debug", true)]
        public static bool ToggleDebugValidate()
        {
            Menu.SetChecked("Tools/CycloneGames/Interaction/Toggle Scene Interaction Debug", s_enabled);
            return true;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!s_enabled) return;

            InitStyles();

            Handles.BeginGUI();
            DrawControlPanel();
            Handles.EndGUI();

            if (!Application.isPlaying)
            {
                DrawEditModeVisualization();
            }
            else
            {
                DrawPlayModeVisualization();
            }
        }

        private static void InitStyles()
        {
            if (s_labelStyle == null)
            {
                s_labelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.RoundToInt(10 * s_labelScale),
                    normal = { textColor = Color.white }
                };
            }

            if (s_boxStyle == null)
            {
                s_boxStyle = new GUIStyle(GUI.skin.box)
                {
                    padding = new RectOffset(8, 8, 8, 8)
                };
            }
        }

        private static void DrawControlPanel()
        {
            s_windowRect = GUILayout.Window(
                54322, // Unique ID
                s_windowRect,
                DrawControlPanelContent,
                s_titleContent,
                GUILayout.Width(200)
            );
        }

        private static void DrawControlPanelContent(int windowId)
        {
            EditorGUI.BeginChangeCheck();

            s_showLabels = GUILayout.Toggle(s_showLabels, "Show Labels");
            s_showStateColors = GUILayout.Toggle(s_showStateColors, "State Colors");
            s_showConnections = GUILayout.Toggle(s_showConnections, "Show Connections");

            GUILayout.Space(4);
            GUILayout.Label("Label Scale:");
            s_labelScale = GUILayout.HorizontalSlider(s_labelScale, 0.5f, 2f);

            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool("InteractionSceneDebugger_ShowLabels", s_showLabels);
                EditorPrefs.SetBool("InteractionSceneDebugger_ShowConnections", s_showConnections);
                EditorPrefs.SetBool("InteractionSceneDebugger_ShowStateColors", s_showStateColors);
                EditorPrefs.SetFloat("InteractionSceneDebugger_LabelScale", s_labelScale);
                s_labelStyle = null; // Force recreate with new scale
            }

            GUILayout.Space(4);
            if (Application.isPlaying)
            {
                GUI.color = Color.green;
                GUILayout.Label("▶ PLAY MODE", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                GUI.color = Color.yellow;
                GUILayout.Label("● EDIT MODE", EditorStyles.centeredGreyMiniLabel);
            }
            GUI.color = Color.white;

            GUI.DragWindow();
        }

        private static void DrawEditModeVisualization()
        {
            var interactables = Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None);
            var detectors = Object.FindObjectsByType<InteractionDetector>(FindObjectsSortMode.None);

            // Draw interactable zones
            foreach (var interactable in interactables)
            {
                if (interactable == null) continue;

                Vector3 pos = interactable.transform.position;
                Collider col = interactable.GetComponent<Collider>();

                Handles.color = ColorIdle;
                if (col != null)
                {
                    Handles.DrawWireDisc(pos, Vector3.up, col.bounds.extents.magnitude);
                }
                else
                {
                    Handles.DrawWireDisc(pos, Vector3.up, 0.5f);
                }

                if (s_showLabels)
                {
                    Handles.Label(pos + Vector3.up * 1.5f, interactable.name, s_labelStyle);
                }
            }

            // Draw detector ranges
            foreach (var detector in detectors)
            {
                if (detector == null) continue;

                Vector3 pos = detector.transform.position;
                var so = new SerializedObject(detector);
                float radius = so.FindProperty("detectionRadius")?.floatValue ?? 3f;

                Handles.color = ColorDetector;
                Handles.DrawWireDisc(pos, Vector3.up, radius);

                if (s_showLabels)
                {
                    Handles.Label(pos + Vector3.up * 0.5f, "Detector", s_labelStyle);
                }
            }
        }

        private static void DrawPlayModeVisualization()
        {
            var interactables = Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None);
            var detectors = Object.FindObjectsByType<InteractionDetector>(FindObjectsSortMode.None);

            // Draw interactables with state colors
            foreach (var interactable in interactables)
            {
                if (interactable == null) continue;

                Vector3 pos = interactable.transform.position;
                Color stateColor = GetStateColor(interactable.CurrentState);

                if (s_showStateColors)
                {
                    // Draw state indicator
                    Handles.color = stateColor;
                    Handles.DrawSolidDisc(pos + Vector3.up * 0.1f, Vector3.up, 0.3f);
                    Handles.DrawWireDisc(pos + Vector3.up * 0.1f, Vector3.up, 0.4f);

                    // Pulsing effect for active states
                    if (interactable.IsInteracting)
                    {
                        float pulse = Mathf.Sin(Time.realtimeSinceStartup * 4f) * 0.5f + 0.5f;
                        Color pulseColor = stateColor;
                        pulseColor.a = pulse * 0.5f;
                        Handles.color = pulseColor;
                        Handles.DrawSolidDisc(pos + Vector3.up * 0.1f, Vector3.up, 0.6f);
                    }
                }

                if (s_showLabels)
                {
                    string stateLabel = interactable.CurrentState.ToString();
                    s_labelStyle.normal.textColor = stateColor;
                    Handles.Label(pos + Vector3.up * 1.5f, $"{interactable.name}\n[{stateLabel}]", s_labelStyle);
                    s_labelStyle.normal.textColor = Color.white;
                }
            }

            // Draw detector connections to candidates
            foreach (var detector in detectors)
            {
                if (detector == null) continue;

                Vector3 detectorPos = detector.transform.position;
                var so = new SerializedObject(detector);
                float radius = so.FindProperty("detectionRadius")?.floatValue ?? 3f;

                // Detection radius
                Handles.color = ColorDetector;
                Handles.DrawWireDisc(detectorPos, Vector3.up, radius);

                // Current target connection
                var targetProp = so.FindProperty("currentTarget");
                if (targetProp != null && targetProp.propertyType == SerializedPropertyType.Generic)
                {
                    // ReactiveProperty - try to get value at runtime
                    var rp = detector.GetType().GetProperty("CurrentTarget");
                    if (rp != null)
                    {
                        var rpValue = rp.GetValue(detector);
                        var valueProp = rpValue?.GetType().GetProperty("Value");
                        var currentTarget = valueProp?.GetValue(rpValue) as IInteractable;

                        if (currentTarget != null && currentTarget is MonoBehaviour mb)
                        {
                            Handles.color = ColorCandidate;
                            Handles.DrawLine(detectorPos, mb.transform.position);
                            Handles.DrawSolidDisc(mb.transform.position + Vector3.up * 0.5f, Vector3.up, 0.15f);
                        }
                    }
                }

                if (s_showConnections)
                {
                    // Draw lines to all interactables in range
                    foreach (var interactable in interactables)
                    {
                        if (interactable == null) continue;

                        float dist = Vector3.Distance(detectorPos, interactable.transform.position);
                        if (dist <= radius)
                        {
                            Color lineColor = ColorConnection;
                            lineColor.a = 1f - (dist / radius) * 0.7f;
                            Handles.color = lineColor;
                            Handles.DrawDottedLine(detectorPos, interactable.transform.position, 4f);
                        }
                    }
                }
            }

            // Force repaint for animations
            SceneView.RepaintAll();
        }

        private static Color GetStateColor(InteractionStateType state)
        {
            return state switch
            {
                InteractionStateType.Idle => ColorIdle,
                InteractionStateType.Starting => ColorStarting,
                InteractionStateType.InProgress => ColorInProgress,
                InteractionStateType.Completing => ColorCompleting,
                InteractionStateType.Completed => ColorCompleting,
                InteractionStateType.Cancelled => ColorCancelled,
                _ => Color.white
            };
        }
    }
}