using System.Reflection;
using UnityEditor;
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime.Interaction;
using Object = UnityEngine.Object;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    /// <summary>
    /// Scene view overlay for visualizing interaction system state.
    /// </summary>
    [InitializeOnLoad]
    public static class InteractionSceneDebugger
    {
        private const int WindowId = 54322;
        private const double CacheRefreshInterval = 0.2d;

        private static readonly GUIContent TitleContent = new("Interaction Debug");
        private static readonly Color ColorIdle = new(0.5f, 0.5f, 0.5f, 0.6f);
        private static readonly Color ColorStarting = new(1f, 0.8f, 0.2f, 0.8f);
        private static readonly Color ColorInProgress = new(0.2f, 0.8f, 1f, 0.8f);
        private static readonly Color ColorCompleting = new(0.3f, 1f, 0.5f, 0.8f);
        private static readonly Color ColorCancelled = new(1f, 0.3f, 0.3f, 0.8f);
        private static readonly Color ColorDetector = new(0.2f, 1f, 0.4f, 0.5f);
        private static readonly Color ColorCandidate = new(1f, 0.6f, 0.2f, 0.7f);
        private static readonly Color ColorConnection = new(0.8f, 0.8f, 0.2f, 0.4f);

        private static bool s_enabled;
        private static bool s_showLabels = true;
        private static bool s_showConnections;
        private static bool s_showStateColors = true;
        private static float s_labelScale = 1f;
        private static GUIStyle s_labelStyle;
        private static Rect s_windowRect = new(10f, 10f, 210f, 154f);

        private static Interactable[] s_cachedInteractables;
        private static InteractionDetector[] s_cachedDetectors;
        private static double s_lastCacheTime;

        private static FieldInfo s_radiusField;
        private static FieldInfo s_is2DField;

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
            if (!s_enabled)
            {
                return;
            }

            InitStyles();

            Handles.BeginGUI();
            DrawControlPanel();
            Handles.EndGUI();

            if (Application.isPlaying)
            {
                DrawPlayModeVisualization();
            }
            else
            {
                DrawEditModeVisualization();
            }
        }

        private static void InitStyles()
        {
            if (s_labelStyle != null)
            {
                return;
            }

            s_labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(10f * s_labelScale),
                normal = { textColor = Color.white }
            };
        }

        private static void DrawControlPanel()
        {
            s_windowRect = GUILayout.Window(
                WindowId,
                s_windowRect,
                DrawControlPanelContent,
                TitleContent,
                GUILayout.Width(210f));
        }

        private static void DrawControlPanelContent(int windowId)
        {
            EditorGUI.BeginChangeCheck();

            s_showLabels = GUILayout.Toggle(s_showLabels, "Show Labels");
            s_showStateColors = GUILayout.Toggle(s_showStateColors, "State Colors");
            s_showConnections = GUILayout.Toggle(s_showConnections, "Show Connections");

            GUILayout.Space(4f);
            GUILayout.Label("Label Scale");
            s_labelScale = GUILayout.HorizontalSlider(s_labelScale, 0.5f, 2f);

            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool("InteractionSceneDebugger_ShowLabels", s_showLabels);
                EditorPrefs.SetBool("InteractionSceneDebugger_ShowConnections", s_showConnections);
                EditorPrefs.SetBool("InteractionSceneDebugger_ShowStateColors", s_showStateColors);
                EditorPrefs.SetFloat("InteractionSceneDebugger_LabelScale", s_labelScale);
                s_labelStyle = null;
            }

            GUILayout.Space(4f);
            GUI.color = Application.isPlaying ? Color.green : Color.yellow;
            GUILayout.Label(Application.isPlaying ? "PLAY MODE" : "EDIT MODE", EditorStyles.centeredGreyMiniLabel);
            GUI.color = Color.white;

            GUI.DragWindow();
        }

        private static void RefreshCacheIfNeeded()
        {
            double now = EditorApplication.timeSinceStartup;
            if (s_cachedInteractables != null && now - s_lastCacheTime < CacheRefreshInterval)
            {
                return;
            }

            s_lastCacheTime = now;
            s_cachedInteractables = Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None);
            s_cachedDetectors = Object.FindObjectsByType<InteractionDetector>(FindObjectsSortMode.None);
        }

        private static void DrawEditModeVisualization()
        {
            RefreshCacheIfNeeded();
            Interactable[] interactables = s_cachedInteractables;
            InteractionDetector[] detectors = s_cachedDetectors;

            if (interactables != null)
            {
                for (int i = 0; i < interactables.Length; i++)
                {
                    Interactable interactable = interactables[i];
                    if (interactable == null)
                    {
                        continue;
                    }

                    DrawInteractableShape(interactable, ColorIdle);

                    if (s_showLabels)
                    {
                        Handles.Label(GetLabelPosition(interactable.transform.position, interactable), interactable.name, s_labelStyle);
                    }
                }
            }

            if (detectors == null)
            {
                return;
            }

            for (int i = 0; i < detectors.Length; i++)
            {
                InteractionDetector detector = detectors[i];
                if (detector == null)
                {
                    continue;
                }

                DrawDetectorRadius(detector, GetDetectorRadius(detector));

                if (s_showLabels)
                {
                    Handles.Label(detector.transform.position + Vector3.up * 0.5f, "Detector", s_labelStyle);
                }
            }
        }

        private static void DrawPlayModeVisualization()
        {
            RefreshCacheIfNeeded();
            Interactable[] interactables = s_cachedInteractables;
            InteractionDetector[] detectors = s_cachedDetectors;

            if (interactables != null)
            {
                for (int i = 0; i < interactables.Length; i++)
                {
                    Interactable interactable = interactables[i];
                    if (interactable == null)
                    {
                        continue;
                    }

                    Color stateColor = GetStateColor(interactable.CurrentState);
                    if (s_showStateColors)
                    {
                        DrawStateMarker(interactable, stateColor);
                    }

                    if (s_showLabels)
                    {
                        string stateLabel = interactable.CurrentState.ToString();
                        s_labelStyle.normal.textColor = stateColor;
                        Handles.Label(
                            GetLabelPosition(interactable.transform.position, interactable),
                            interactable.name + "\n[" + stateLabel + "]",
                            s_labelStyle);
                        s_labelStyle.normal.textColor = Color.white;
                    }
                }
            }

            if (detectors != null)
            {
                DrawDetectorRuntimeState(detectors, interactables);
            }

            SceneView.RepaintAll();
        }

        private static void DrawDetectorRuntimeState(InteractionDetector[] detectors, Interactable[] interactables)
        {
            for (int i = 0; i < detectors.Length; i++)
            {
                InteractionDetector detector = detectors[i];
                if (detector == null)
                {
                    continue;
                }

                Vector3 detectorPosition = detector.transform.position;
                float radius = GetDetectorRadius(detector);
                DrawDetectorRadius(detector, radius);

                IInteractable currentTarget = detector.CurrentInteractable.CurrentValue;
                if (currentTarget is MonoBehaviour targetBehaviour)
                {
                    Handles.color = ColorCandidate;
                    Handles.DrawLine(detectorPosition, targetBehaviour.transform.position);
                    Handles.DrawSolidDisc(targetBehaviour.transform.position + Vector3.up * 0.5f, GetDetectorPlaneNormal(detector), 0.15f);
                }

                if (!s_showConnections || interactables == null)
                {
                    continue;
                }

                for (int j = 0; j < interactables.Length; j++)
                {
                    Interactable interactable = interactables[j];
                    if (interactable == null)
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(detectorPosition, interactable.transform.position);
                    if (distance > radius)
                    {
                        continue;
                    }

                    Color lineColor = ColorConnection;
                    lineColor.a = 1f - distance / radius * 0.7f;
                    Handles.color = lineColor;
                    Handles.DrawDottedLine(detectorPosition, interactable.transform.position, 4f);
                }
            }
        }

        private static void DrawInteractableShape(Interactable interactable, Color color)
        {
            Handles.color = color;

            Collider2D collider2D = interactable.GetComponent<Collider2D>();
            if (collider2D != null)
            {
                Bounds bounds = collider2D.bounds;
                Handles.DrawWireCube(bounds.center, bounds.size);
                return;
            }

            Collider collider3D = interactable.GetComponent<Collider>();
            if (collider3D != null)
            {
                Bounds bounds = collider3D.bounds;
                Handles.DrawWireCube(bounds.center, bounds.size);
                return;
            }

            Vector3 normal = GetInteractionPlaneNormal(interactable);
            Handles.DrawWireDisc(interactable.transform.position, normal, 0.5f);
        }

        private static void DrawStateMarker(Interactable interactable, Color stateColor)
        {
            Vector3 normal = GetInteractionPlaneNormal(interactable);
            Vector3 position = interactable.transform.position + GetLabelUp(interactable) * 0.1f;

            Handles.color = stateColor;
            Handles.DrawSolidDisc(position, normal, 0.3f);
            Handles.DrawWireDisc(position, normal, 0.4f);

            if (!interactable.IsInteracting)
            {
                return;
            }

            float pulse = Mathf.Sin(Time.realtimeSinceStartup * 4f) * 0.5f + 0.5f;
            Color pulseColor = stateColor;
            pulseColor.a = pulse * 0.5f;
            Handles.color = pulseColor;
            Handles.DrawSolidDisc(position, normal, 0.6f);
        }

        private static void DrawDetectorRadius(InteractionDetector detector, float radius)
        {
            Handles.color = ColorDetector;
            Handles.DrawWireDisc(detector.transform.position, GetDetectorPlaneNormal(detector), radius);
        }

        private static Vector3 GetLabelPosition(Vector3 position, Interactable interactable)
        {
            return position + GetLabelUp(interactable) * 1.5f;
        }

        private static Vector3 GetLabelUp(Interactable interactable)
        {
            return interactable.GetComponent<Collider2D>() != null ? Vector3.up : Vector3.up;
        }

        private static Vector3 GetInteractionPlaneNormal(Interactable interactable)
        {
            return interactable.GetComponent<Collider2D>() != null ? Vector3.forward : Vector3.up;
        }

        private static Vector3 GetDetectorPlaneNormal(InteractionDetector detector)
        {
            return GetDetectorIs2D(detector) ? Vector3.forward : Vector3.up;
        }

        private static float GetDetectorRadius(InteractionDetector detector)
        {
            s_radiusField ??= typeof(InteractionDetector).GetField(
                "detectionRadius",
                BindingFlags.NonPublic | BindingFlags.Instance);

            return s_radiusField != null ? (float)s_radiusField.GetValue(detector) : 3f;
        }

        private static bool GetDetectorIs2D(InteractionDetector detector)
        {
            s_is2DField ??= typeof(InteractionDetector).GetField(
                "is2D",
                BindingFlags.NonPublic | BindingFlags.Instance);

            return s_is2DField != null && (bool)s_is2DField.GetValue(detector);
        }

        private static Color GetStateColor(InteractionStateType state)
        {
            return state switch
            {
                InteractionStateType.Starting => ColorStarting,
                InteractionStateType.InProgress => ColorInProgress,
                InteractionStateType.Completing => ColorCompleting,
                InteractionStateType.Completed => ColorCompleting,
                InteractionStateType.Cancelled => ColorCancelled,
                _ => ColorIdle
            };
        }
    }
}
