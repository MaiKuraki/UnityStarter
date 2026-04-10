using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    public class CameraDebugWindow : EditorWindow
    {
        private const int DefaultCapacity = 600;
        private const int MinCapacity = 120;
        private const int MaxCapacity = 2048;

        private static readonly Color editableFoldoutColor = new Color(0.50f, 0.58f, 0.38f);
        private static readonly Color readOnlyFoldoutColor = new Color(0.30f, 0.50f, 0.70f);
        private static readonly Color alertRuleFoldoutColor = new Color(0.56f, 0.45f, 0.63f);
        private static readonly Color alertStatusFoldoutColor = new Color(0.56f, 0.50f, 0.40f);

        private enum SamplingMode
        {
            Off,
            Basic,
            Full
        }

        private enum AlertSeverity
        {
            None,
            Warning,
            Critical
        }

        private CameraManager targetManager;
        private bool autoBindSelected = true;
        private SamplingMode samplingMode = SamplingMode.Basic;
        private float sampleRateHz = 30f;
        private int capacity = DefaultCapacity;

        private bool alertsEnabled = true;
        private bool showAlertRules = true;
        private bool showAlertStatus = true;
        private bool showNormalMetrics = false;
        private bool showEditableSection = true;
        private bool showReadOnlySection = true;

        private float fovDeltaWarning = 1.5f;
        private float fovDeltaCritical = 3.5f;
        private float blendRemainingWarning = 0.75f;
        private float blendRemainingCritical = 1.50f;
        private float blendStallWarning = 0.25f;
        private float blendStallCritical = 0.50f;
        private float blendAlphaDeltaEpsilon = 0.002f;
        private float linearSpeedWarning = 8f;
        private float linearSpeedCritical = 15f;
        private float angularSpeedWarning = 180f;
        private float angularSpeedCritical = 320f;

        private float nextSampleTime;

        private float[] timeBuffer;
        private float[] fovBuffer;
        private float[] blendAlphaBuffer;
        private float[] blendRemainingBuffer;
        private float[] linearSpeedBuffer;
        private float[] angularSpeedBuffer;

        private int writeIndex;
        private int sampleCount;

        private bool hasPreviousPose;
        private CameraPose previousPose;
        private bool hasPreviousBlendAlpha;
        private float previousBlendAlpha;
        private float blendStallDuration;
        private float latestFovDelta;
        private Vector2 scrollPosition;

        private GUIStyle foldoutLabelStyle;
        private bool stylesInitialized;

        [MenuItem("Tools/CycloneGames/GameplayFramework/Camera Debug Window")]
        public static void OpenWindow()
        {
            GetWindow<CameraDebugWindow>("Camera Debug");
        }

        public static void OpenWithTarget(CameraManager manager)
        {
            CameraDebugWindow window = GetWindow<CameraDebugWindow>("Camera Debug");
            window.targetManager = manager;
            window.Focus();
        }

        private void OnEnable()
        {
            EnsureBuffers();
            EnsureStyles();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (!Application.isPlaying)
            {
                Repaint();
                return;
            }

            if (targetManager == null && autoBindSelected)
            {
                TryAutoBindFromSelection();
            }

            if (targetManager == null || samplingMode == SamplingMode.Off)
            {
                Repaint();
                return;
            }

            float interval = 1f / Mathf.Max(1f, sampleRateHz);
            if (Time.realtimeSinceStartup < nextSampleTime)
            {
                Repaint();
                return;
            }

            nextSampleTime = Time.realtimeSinceStartup + interval;
            CaptureSample(interval);
            Repaint();
        }

        private void EnsureBuffers()
        {
            if (timeBuffer != null && timeBuffer.Length == capacity) return;

            timeBuffer = new float[capacity];
            fovBuffer = new float[capacity];
            blendAlphaBuffer = new float[capacity];
            blendRemainingBuffer = new float[capacity];
            linearSpeedBuffer = new float[capacity];
            angularSpeedBuffer = new float[capacity];

            writeIndex = 0;
            sampleCount = 0;
            hasPreviousPose = false;
            hasPreviousBlendAlpha = false;
            previousBlendAlpha = 0f;
            blendStallDuration = 0f;
            latestFovDelta = 0f;
        }

        private void CaptureSample(float deltaTime)
        {
            if (!targetManager.HasCurrentPose) return;

            CameraPose pose = targetManager.CurrentPose;
            CameraBlendState blend = targetManager.BlendState;

            float linearSpeed = 0f;
            float angularSpeed = 0f;
            float fovDelta = 0f;
            if (samplingMode == SamplingMode.Full && hasPreviousPose)
            {
                float dt = Mathf.Max(0.0001f, deltaTime);
                linearSpeed = Vector3.Distance(previousPose.Position, pose.Position) / dt;
                angularSpeed = Quaternion.Angle(previousPose.Rotation, pose.Rotation) / dt;
            }

            if (hasPreviousPose)
            {
                fovDelta = Mathf.Abs(pose.Fov - previousPose.Fov);
            }

            timeBuffer[writeIndex] = Time.realtimeSinceStartup;
            fovBuffer[writeIndex] = pose.Fov;
            blendAlphaBuffer[writeIndex] = blend.NormalizedTime;
            blendRemainingBuffer[writeIndex] = blend.Remaining;
            linearSpeedBuffer[writeIndex] = linearSpeed;
            angularSpeedBuffer[writeIndex] = angularSpeed;

            writeIndex = (writeIndex + 1) % capacity;
            if (sampleCount < capacity)
            {
                sampleCount++;
            }

            previousPose = pose;
            hasPreviousPose = true;
            latestFovDelta = fovDelta;

            if (blend.IsActive)
            {
                if (hasPreviousBlendAlpha && Mathf.Abs(blend.NormalizedTime - previousBlendAlpha) <= blendAlphaDeltaEpsilon)
                {
                    blendStallDuration += deltaTime;
                }
                else
                {
                    blendStallDuration = 0f;
                }

                previousBlendAlpha = blend.NormalizedTime;
                hasPreviousBlendAlpha = true;
            }
            else
            {
                hasPreviousBlendAlpha = false;
                previousBlendAlpha = 0f;
                blendStallDuration = 0f;
            }
        }

        private int GetBufferIndex(int logicalIndex)
        {
            int start = sampleCount == capacity ? writeIndex : 0;
            return (start + logicalIndex) % capacity;
        }

        private void TryAutoBindFromSelection()
        {
            if (Selection.activeGameObject == null) return;
            targetManager = Selection.activeGameObject.GetComponent<CameraManager>();
        }

        private void OnGUI()
        {
            EnsureStyles();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));

            DrawEditableSection();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to monitor runtime camera telemetry.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            if (targetManager == null)
            {
                EditorGUILayout.HelpBox("No CameraManager is currently bound.", MessageType.Warning);
                if (GUILayout.Button("Find First CameraManager In Scene"))
                {
                    targetManager = FindFirstObjectByType<CameraManager>();
                }
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawReadOnlySection();
            DrawEditableAlertRuleSection();
            EditorGUILayout.EndScrollView();
        }

        protected virtual void DrawEditableSection()
        {
            showEditableSection = DrawFoldoutHeader("Editable Settings", showEditableSection, editableFoldoutColor);
            if (!showEditableSection) return;

            EditorGUILayout.BeginVertical(GUI.skin.box);
            DrawSectionHeader("Editable Configuration", "Fields in this section are writable and affect debug behavior.", new Color(1f, 0.76f, 0.38f, 1f));

            DrawEditableDataSourceSettings();
            DrawEditableSamplingSettings();
            DrawEditableActionButtons();
            DrawEditableExtensionSettings();

            EditorGUILayout.EndVertical();
        }

        protected virtual void DrawEditableDataSourceSettings()
        {
            EditorGUILayout.LabelField("Data Source", EditorStyles.boldLabel);
            targetManager = (CameraManager)EditorGUILayout.ObjectField("CameraManager", targetManager, typeof(CameraManager), true);
            autoBindSelected = EditorGUILayout.ToggleLeft("Auto bind from selected GameObject", autoBindSelected);
            EditorGUILayout.Space(4);
        }

        protected virtual void DrawEditableSamplingSettings()
        {
            EditorGUILayout.LabelField("Sampling", EditorStyles.boldLabel);
            samplingMode = (SamplingMode)EditorGUILayout.EnumPopup("Mode", samplingMode);
            sampleRateHz = EditorGUILayout.Slider("Sample Rate (Hz)", sampleRateHz, 5f, 120f);

            int newCapacity = EditorGUILayout.IntSlider("Buffer Capacity", capacity, MinCapacity, MaxCapacity);
            if (newCapacity != capacity)
            {
                capacity = newCapacity;
                EnsureBuffers();
            }

            EditorGUILayout.Space(4);
        }

        protected virtual void DrawEditableActionButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Rebind Scene CameraManager"))
            {
                targetManager = FindFirstObjectByType<CameraManager>();
            }
            if (GUILayout.Button("Clear Buffer"))
            {
                EnsureBuffers();
            }
            EditorGUILayout.EndHorizontal();
        }

        protected virtual void DrawEditableExtensionSettings() { }

        protected virtual void DrawReadOnlySection()
        {
            showReadOnlySection = DrawFoldoutHeader("Read-Only Runtime Data", showReadOnlySection, readOnlyFoldoutColor);
            if (!showReadOnlySection) return;

            EditorGUILayout.BeginVertical(GUI.skin.box);
            DrawSectionHeader("Read-Only Telemetry", "Runtime values are locked for observation only.", new Color(0.42f, 0.78f, 1f, 1f));

            DrawLiveState();
            DrawAlertStatus();
            DrawCharts();
            DrawReadOnlyExtensionData();

            EditorGUILayout.EndVertical();
        }

        protected virtual void DrawReadOnlyExtensionData() { }

        protected virtual void DrawEditableAlertRuleSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            DrawSectionHeader("Alert Rule Tuning", "Threshold values are editable and define warning/critical boundaries.", new Color(1f, 0.76f, 0.38f, 1f));

            alertsEnabled = EditorGUILayout.ToggleLeft("Enable threshold alerts", alertsEnabled);
            if (!alertsEnabled)
            {
                EditorGUILayout.HelpBox("Alerts are disabled.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            showAlertRules = DrawFoldoutHeader("Alert Rules", showAlertRules, alertRuleFoldoutColor);
            if (showAlertRules)
            {
                DrawAlertRules();
                DrawEditableAlertRuleExtensions();
            }

            EditorGUILayout.EndVertical();
        }

        protected virtual void DrawEditableAlertRuleExtensions() { }

        private void DrawAlertRules()
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("FOV Delta Per Sample", EditorStyles.miniBoldLabel);
            fovDeltaWarning = EditorGUILayout.FloatField("Warning", Mathf.Max(0f, fovDeltaWarning));
            fovDeltaCritical = EditorGUILayout.FloatField("Critical", Mathf.Max(fovDeltaWarning, fovDeltaCritical));

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Blend Remaining", EditorStyles.miniBoldLabel);
            blendRemainingWarning = EditorGUILayout.FloatField("Warning (s)", Mathf.Max(0f, blendRemainingWarning));
            blendRemainingCritical = EditorGUILayout.FloatField("Critical (s)", Mathf.Max(blendRemainingWarning, blendRemainingCritical));

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Blend Stall", EditorStyles.miniBoldLabel);
            blendStallWarning = EditorGUILayout.FloatField("Warning (s)", Mathf.Max(0f, blendStallWarning));
            blendStallCritical = EditorGUILayout.FloatField("Critical (s)", Mathf.Max(blendStallWarning, blendStallCritical));
            blendAlphaDeltaEpsilon = EditorGUILayout.Slider("Blend Alpha Epsilon", blendAlphaDeltaEpsilon, 0.0001f, 0.02f);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Motion Speed (Full Sampling)", EditorStyles.miniBoldLabel);
            linearSpeedWarning = EditorGUILayout.FloatField("Linear Warning (m/s)", Mathf.Max(0f, linearSpeedWarning));
            linearSpeedCritical = EditorGUILayout.FloatField("Linear Critical (m/s)", Mathf.Max(linearSpeedWarning, linearSpeedCritical));
            angularSpeedWarning = EditorGUILayout.FloatField("Angular Warning (deg/s)", Mathf.Max(0f, angularSpeedWarning));
            angularSpeedCritical = EditorGUILayout.FloatField("Angular Critical (deg/s)", Mathf.Max(angularSpeedWarning, angularSpeedCritical));

            showNormalMetrics = EditorGUILayout.ToggleLeft("Show normal metrics", showNormalMetrics);
        }

        private void DrawAlertStatus()
        {
            showAlertStatus = DrawFoldoutHeader("Active Alerts (Read-Only)", showAlertStatus, alertStatusFoldoutColor);
            if (!showAlertStatus) return;

            CameraBlendState blendState = targetManager.BlendState;

            int displayedAlerts = 0;
            AlertSeverity fovSeverity = EvaluateThreshold(latestFovDelta, fovDeltaWarning, fovDeltaCritical);
            displayedAlerts += DrawMetricAlert("FOV delta/sample", latestFovDelta, "deg", fovSeverity, fovDeltaWarning, fovDeltaCritical);

            AlertSeverity blendRemainingSeverity = EvaluateThreshold(blendState.Remaining, blendRemainingWarning, blendRemainingCritical);
            displayedAlerts += DrawMetricAlert("Blend remaining", blendState.Remaining, "s", blendRemainingSeverity, blendRemainingWarning, blendRemainingCritical);

            AlertSeverity blendStallSeverity = EvaluateThreshold(blendStallDuration, blendStallWarning, blendStallCritical);
            displayedAlerts += DrawMetricAlert("Blend stall duration", blendStallDuration, "s", blendStallSeverity, blendStallWarning, blendStallCritical);

            if (samplingMode == SamplingMode.Full && sampleCount > 0)
            {
                int lastIndex = GetBufferIndex(sampleCount - 1);
                float latestLinearSpeed = linearSpeedBuffer[lastIndex];
                float latestAngularSpeed = angularSpeedBuffer[lastIndex];

                AlertSeverity linearSeverity = EvaluateThreshold(latestLinearSpeed, linearSpeedWarning, linearSpeedCritical);
                displayedAlerts += DrawMetricAlert("Linear speed", latestLinearSpeed, "m/s", linearSeverity, linearSpeedWarning, linearSpeedCritical);

                AlertSeverity angularSeverity = EvaluateThreshold(latestAngularSpeed, angularSpeedWarning, angularSpeedCritical);
                displayedAlerts += DrawMetricAlert("Angular speed", latestAngularSpeed, "deg/s", angularSeverity, angularSpeedWarning, angularSpeedCritical);
            }
            else
            {
                EditorGUILayout.HelpBox("Speed alerts require Full sampling mode.", MessageType.None);
            }

            if (displayedAlerts == 0)
            {
                EditorGUILayout.HelpBox("No active alerts.", MessageType.Info);
            }
        }

        private static AlertSeverity EvaluateThreshold(float value, float warning, float critical)
        {
            if (value >= critical) return AlertSeverity.Critical;
            if (value >= warning) return AlertSeverity.Warning;
            return AlertSeverity.None;
        }

        private int DrawMetricAlert(string label, float value, string unit, AlertSeverity severity, float warning, float critical)
        {
            if (severity == AlertSeverity.None && !showNormalMetrics)
            {
                return 0;
            }

            string message = label + ": " + value.ToString("F3") + " " + unit
                + " (warn >= " + warning.ToString("F3") + ", crit >= " + critical.ToString("F3") + ")";

            if (severity == AlertSeverity.Critical)
            {
                EditorGUILayout.HelpBox("CRITICAL - " + message, MessageType.Error);
                return 1;
            }

            if (severity == AlertSeverity.Warning)
            {
                EditorGUILayout.HelpBox("WARNING - " + message, MessageType.Warning);
                return 1;
            }

            EditorGUILayout.HelpBox("OK - " + message, MessageType.None);
            return 0;
        }

        private void DrawLiveState()
        {
            EditorGUILayout.LabelField("Live State (Read-Only)", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField("Initialized", targetManager.IsInitialized ? "Yes" : "No");
                EditorGUILayout.LabelField("Dirty", targetManager.CameraStateDirty ? "Yes" : "No");

                if (targetManager.HasCurrentPose)
                {
                    CameraPose pose = targetManager.CurrentPose;
                    EditorGUILayout.Vector3Field("Position", pose.Position);
                    EditorGUILayout.Vector3Field("Rotation (Euler)", pose.Rotation.eulerAngles);
                    EditorGUILayout.FloatField("FOV", pose.Fov);
                }
                else
                {
                    EditorGUILayout.TextField("Pose", "Not available yet");
                }

                CameraBlendState blendState = targetManager.BlendState;
                EditorGUILayout.LabelField("Blend Active", blendState.IsActive ? "Yes" : "No");
                EditorGUILayout.TextField("Blend Remaining", blendState.Remaining.ToString("F3") + " s");
                EditorGUILayout.Slider("Blend Alpha", blendState.NormalizedTime, 0f, 1f);
            }
        }

        private void DrawCharts()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Time-Series Charts (Read-Only)", EditorStyles.boldLabel);

            DrawGraph("FOV", fovBuffer, 20f, 140f, new Color(0.30f, 0.75f, 1.00f));
            DrawGraph("Blend Alpha", blendAlphaBuffer, 0f, 1f, new Color(1.00f, 0.40f, 0.75f));
            DrawGraph("Blend Remaining (s)", blendRemainingBuffer, 0f, -1f, new Color(0.45f, 1.00f, 0.60f));

            if (samplingMode == SamplingMode.Full)
            {
                DrawGraph("Linear Speed (m/s)", linearSpeedBuffer, 0f, -1f, new Color(1.00f, 0.80f, 0.35f));
                DrawGraph("Angular Speed (deg/s)", angularSpeedBuffer, 0f, -1f, new Color(0.80f, 0.65f, 1.00f));
            }
        }

        private void DrawGraph(string title, float[] values, float minY, float maxY, Color lineColor)
        {
            const float height = 110f;
            Rect rect = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));

            EditorGUI.DrawRect(rect, new Color(0.11f, 0.11f, 0.11f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(0.22f, 0.22f, 0.22f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0.22f, 0.22f, 0.22f));

            GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 18f), title, EditorStyles.miniBoldLabel);

            if (sampleCount < 2)
            {
                GUI.Label(new Rect(rect.x + 6f, rect.center.y - 8f, rect.width - 12f, 18f), "Waiting for samples...", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            float localMin = minY;
            float localMax = maxY;
            if (maxY < minY)
            {
                localMin = float.MaxValue;
                localMax = float.MinValue;
                for (int i = 0; i < sampleCount; i++)
                {
                    int bi = GetBufferIndex(i);
                    float v = values[bi];
                    if (v < localMin) localMin = v;
                    if (v > localMax) localMax = v;
                }

                if (Mathf.Abs(localMax - localMin) < 0.0001f)
                {
                    localMax = localMin + 1f;
                }
            }

            Handles.BeginGUI();
            Color oldColor = Handles.color;
            Handles.color = new Color(1f, 1f, 1f, 0.08f);

            for (int i = 1; i < 4; i++)
            {
                float y = Mathf.Lerp(rect.y + 24f, rect.yMax - 8f, i / 4f);
                Handles.DrawLine(new Vector3(rect.x + 2f, y), new Vector3(rect.xMax - 2f, y));
            }

            Handles.color = lineColor;
            Vector3 prev = Vector3.zero;
            for (int i = 0; i < sampleCount; i++)
            {
                int bi = GetBufferIndex(i);
                float t = sampleCount <= 1 ? 0f : (float)i / (sampleCount - 1);
                float x = Mathf.Lerp(rect.x + 4f, rect.xMax - 4f, t);

                float normalizedY = Mathf.InverseLerp(localMin, localMax, values[bi]);
                float y = Mathf.Lerp(rect.yMax - 8f, rect.y + 24f, normalizedY);
                Vector3 p = new Vector3(x, y, 0f);
                if (i > 0)
                {
                    Handles.DrawLine(prev, p);
                }
                prev = p;
            }

            Handles.color = oldColor;
            Handles.EndGUI();

            int lastIndex = GetBufferIndex(sampleCount - 1);
            float lastValue = values[lastIndex];
            GUI.Label(new Rect(rect.x + 6f, rect.yMax - 18f, rect.width - 12f, 16f),
                "Latest: " + lastValue.ToString("F3") + "    Min: " + localMin.ToString("F3") + "    Max: " + localMax.ToString("F3"),
                EditorStyles.miniLabel);
        }

        private static void DrawSectionHeader(string title, string subtitle, Color titleColor)
        {
            Color oldColor = GUI.color;
            GUI.color = titleColor;
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUI.color = oldColor;
            EditorGUILayout.HelpBox(subtitle, MessageType.None);
        }

        private void EnsureStyles()
        {
            if (stylesInitialized) return;

            foldoutLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft
            };

            stylesInitialized = true;
        }

        private bool DrawFoldoutHeader(string title, bool foldout, Color color)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 22);

            Color bgColor = foldout ? color : new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f);
            EditorGUI.DrawRect(rect, bgColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), Color.black * 0.2f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Color.black * 0.2f);

            Rect labelRect = new Rect(rect.x + 20f, rect.y, rect.width - 20f, rect.height);
            EditorGUI.LabelField(labelRect, title, foldoutLabelStyle);

            Rect arrowRect = new Rect(rect.x + 5f, rect.y, 15f, rect.height);
            EditorGUI.LabelField(arrowRect, foldout ? "v" : ">", foldoutLabelStyle);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                foldout = !foldout;
                Event.current.Use();
            }

            return foldout;
        }
    }
}
