using UnityEditor;
using UnityEngine;
using Unity.Cinemachine;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    /// <summary>
    /// Custom editor for the CameraManager class and its subclasses.
    /// Displays runtime camera state during Play Mode.
    /// </summary>
    [CustomEditor(typeof(CameraManager), true)]
    public class CameraManagerEditor : UnityEditor.Editor
    {
        private static readonly Color editableHeaderColor = new Color(0.50f, 0.58f, 0.38f);
        private static readonly Color readOnlyHeaderColor = new Color(0.30f, 0.50f, 0.70f);
        private static readonly Color runtimeHeaderColor = new Color(0.36f, 0.36f, 0.58f);
        private static readonly Color poseHeaderColor = new Color(0.33f, 0.45f, 0.58f);
        private static readonly Color blendHeaderColor = new Color(0.45f, 0.35f, 0.53f);
        private static readonly Color stackHeaderColor = new Color(0.45f, 0.41f, 0.33f);

        private bool showEditableConfiguration = true;
        private bool showReadOnlyTelemetry = true;
        private bool showRuntimeTelemetry = true;
        private bool showPose = true;
        private bool showBlend = true;
        private bool showStack = true;

        private static readonly GUIContent editableConfigurationLabel = new GUIContent("Editable Configuration");
        private static readonly GUIContent readOnlyTelemetryLabel = new GUIContent("Read-Only Runtime Telemetry");
        private static readonly GUIContent runtimeTelemetryLabel = new GUIContent("Runtime Telemetry");
        private static readonly GUIContent poseLabel = new GUIContent("Current Pose");
        private static readonly GUIContent blendLabel = new GUIContent("Blend State");
        private static readonly GUIContent stackLabel = new GUIContent("View Target and Camera Stack");

        private void OnEnable()
        {
            EditorApplication.update += RepaintWhilePlaying;
        }

        private void OnDisable()
        {
            EditorApplication.update -= RepaintWhilePlaying;
        }

        private void RepaintWhilePlaying()
        {
            if (!Application.isPlaying) return;
            if (target == null) return;
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            var cameraManager = (CameraManager)target;
            serializedObject.Update();

            EditorGUILayout.LabelField("Camera Manager Status", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(GUI.skin.box);

            if (Application.isPlaying)
            {
                var activeCamera = cameraManager.ActiveVirtualCamera;

                if (activeCamera != null)
                {
                    GUI.color = new Color(0.7f, 1.0f, 0.7f);
                    EditorGUILayout.LabelField("Active Camera:", EditorStyles.miniBoldLabel);
                    EditorGUILayout.BeginHorizontal();
                    GUI.enabled = false;
                    EditorGUILayout.ObjectField(activeCamera, typeof(CinemachineCamera), false);
                    GUI.enabled = true;
                    if (GUILayout.Button("Ping", GUILayout.Width(50)))
                    {
                        EditorGUIUtility.PingObject(activeCamera);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    GUI.color = Color.yellow;
                    EditorGUILayout.LabelField("Active Camera: None", EditorStyles.boldLabel);
                }
            }
            else
            {
                GUI.color = Color.gray;
                EditorGUILayout.HelpBox("Active camera information will be displayed here during Play Mode.", MessageType.Info);
            }

            GUI.color = Color.white;
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Open Camera Debug Window"))
            {
                CameraDebugWindow.OpenWithTarget(cameraManager);
            }

            EditorGUILayout.Space(8);
            DrawEditableConfigurationSection(cameraManager);

            if (Application.isPlaying)
            {
                DrawReadOnlyTelemetrySection(cameraManager);
            }
            else
            {
                EditorGUILayout.HelpBox("Runtime telemetry will be shown in Play Mode as read-only data.", MessageType.Info);
            }

            EditorGUILayout.Space(10);
            serializedObject.ApplyModifiedProperties();
        }

        protected virtual void DrawEditableConfigurationSection(CameraManager cameraManager)
        {
            showEditableConfiguration = InspectorUiUtility.DrawFoldoutHeader(editableConfigurationLabel.text, showEditableConfiguration, editableHeaderColor);
            if (!showEditableConfiguration) return;

            EditorGUILayout.BeginVertical(GUI.skin.box);
            InspectorUiUtility.DrawSectionHeader("Editable Fields", "These fields are writable and persist on the component.", new Color(1f, 0.76f, 0.38f, 1f));
            InspectorUiUtility.DrawSerializedProperties(serializedObject, "tags");
            DrawEditableConfigurationExtensions(cameraManager);
            EditorGUILayout.EndVertical();
        }

        protected virtual void DrawReadOnlyTelemetrySection(CameraManager cameraManager)
        {
            showReadOnlyTelemetry = InspectorUiUtility.DrawFoldoutHeader(readOnlyTelemetryLabel.text, showReadOnlyTelemetry, readOnlyHeaderColor);
            if (!showReadOnlyTelemetry) return;

            EditorGUILayout.BeginVertical(GUI.skin.box);
            InspectorUiUtility.DrawSectionHeader("Runtime Observation", "This section is read-only and intended for diagnostics.", new Color(0.42f, 0.78f, 1f, 1f));

            DrawRuntimeTelemetry(cameraManager);
            DrawReadOnlyTelemetryExtensions(cameraManager);

            EditorGUILayout.EndVertical();
        }

        protected virtual void DrawRuntimeTelemetry(CameraManager cameraManager)
        {
            showRuntimeTelemetry = InspectorUiUtility.DrawFoldoutHeader(runtimeTelemetryLabel.text, showRuntimeTelemetry, runtimeHeaderColor);
            if (!showRuntimeTelemetry) return;

            DrawGeneralState(cameraManager);

            showPose = InspectorUiUtility.DrawFoldoutHeader(poseLabel.text, showPose, poseHeaderColor);
            if (showPose)
            {
                DrawPose(cameraManager);
            }

            showBlend = InspectorUiUtility.DrawFoldoutHeader(blendLabel.text, showBlend, blendHeaderColor);
            if (showBlend)
            {
                DrawBlend(cameraManager);
            }

            showStack = InspectorUiUtility.DrawFoldoutHeader(stackLabel.text, showStack, stackHeaderColor);
            if (showStack)
            {
                DrawStack(cameraManager);
            }
        }

        protected virtual void DrawGeneralState(CameraManager cameraManager)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField("Initialized", cameraManager.IsInitialized ? "Yes" : "No");
                EditorGUILayout.LabelField("State Dirty", cameraManager.CameraStateDirty ? "Yes" : "No");
                EditorGUILayout.LabelField("FOV Override", cameraManager.HasExplicitFovOverride ? "Yes" : "No");
                EditorGUILayout.LabelField("Locked FOV", cameraManager.GetLockedFOV().ToString("F2"));
                EditorGUILayout.LabelField("Default Blend Duration", cameraManager.DefaultBlendDuration.ToString("F3") + " s");
                EditorGUILayout.LabelField("Pending Blend Override",
                    cameraManager.HasPendingBlendDurationOverride
                        ? cameraManager.PendingBlendDurationOverride.ToString("F3") + " s"
                        : "None");
            }
        }

        protected virtual void DrawPose(CameraManager cameraManager)
        {
            if (!cameraManager.HasCurrentPose)
            {
                EditorGUILayout.HelpBox("No pose has been evaluated yet.", MessageType.Info);
                return;
            }

            CameraPose pose = cameraManager.CurrentPose;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Vector3Field("Position", pose.Position);
                EditorGUILayout.Vector3Field("Rotation (Euler)", pose.Rotation.eulerAngles);
                EditorGUILayout.FloatField("FOV", pose.Fov);
            }
        }

        protected virtual void DrawBlend(CameraManager cameraManager)
        {
            CameraBlendState blendState = cameraManager.BlendState;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField("Active", blendState.IsActive ? "Yes" : "No");
                EditorGUILayout.LabelField("Curve", blendState.HasCustomCurve ? "Custom" : blendState.CurveType.ToString());
                EditorGUILayout.LabelField("Duration", blendState.Duration.ToString("F3") + " s");
                EditorGUILayout.LabelField("Elapsed", blendState.Elapsed.ToString("F3") + " s");
                EditorGUILayout.LabelField("Remaining", blendState.Remaining.ToString("F3") + " s");
                EditorGUILayout.Slider("Normalized", blendState.NormalizedTime, 0f, 1f);
            }
        }

        protected virtual void DrawStack(CameraManager cameraManager)
        {
            PlayerController ownerController = cameraManager.OwnerController;
            CameraContext context = ownerController != null ? ownerController.GetCameraContext() : null;

            string currentTargetName = context != null && context.CurrentViewTarget != null
                ? context.CurrentViewTarget.name
                : "None";
            string pendingTargetName = cameraManager.PendingViewTargetTransform != null
                ? cameraManager.PendingViewTargetTransform.name
                : "None";
            string baseModeName = context != null && context.BaseCameraMode != null
                ? context.BaseCameraMode.GetType().Name
                : "None";
            string primaryModeName = context != null && context.GetPrimaryCameraMode() != null
                ? context.GetPrimaryCameraMode().GetType().Name
                : "None";

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField("Owner Controller", ownerController != null ? ownerController.name : "None");
                EditorGUILayout.LabelField("Current ViewTarget", currentTargetName);
                EditorGUILayout.LabelField("Pending ViewTarget TF", pendingTargetName);
                EditorGUILayout.LabelField("Base Mode", baseModeName);
                EditorGUILayout.LabelField("Primary Mode", primaryModeName);

                int modeCount = context != null ? context.CameraModeCount : 0;
                EditorGUILayout.LabelField("Overlay Modes", modeCount.ToString());
                for (int i = 0; i < modeCount; i++)
                {
                    CameraMode mode = context.GetCameraModeAt(i);
                    EditorGUILayout.LabelField("- " + i, mode != null ? mode.GetType().Name : "Null");
                }
            }
        }

        protected virtual void DrawEditableConfigurationExtensions(CameraManager cameraManager) { }

        protected virtual void DrawReadOnlyTelemetryExtensions(CameraManager cameraManager) { }
    }
}
