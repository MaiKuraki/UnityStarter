using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Networking.Editor.Diagnostics
{
    [CustomEditor(typeof(NetworkBootstrapPreset))]
    [CanEditMultipleObjects]
    public sealed class NetworkBootstrapPresetEditor : UnityEditor.Editor
    {
        private static readonly GUIContent RequiredFeaturesContent = new GUIContent(
            "Required Features",
            "Backend capabilities that must exist in the currently open scenes, such as realtime transport or backend service support.");

        private static readonly GUIContent RequireCycloneTransportContent = new GUIContent(
            "Require Cyclone Transport",
            "Reports an error when no component implementing INetTransport exists in the open scenes.");

        private static readonly GUIContent RequireSingleMessageEndpointContent = new GUIContent(
            "Require Single Message Endpoint",
            "Reports a warning when more than one INetworkMessageEndpoint is active in the open scenes.");

        private static readonly GUIContent RequireRuntimeContextContent = new GUIContent(
            "Require Runtime Context",
            "Reports a warning when a Cyclone message endpoint does not expose INetworkRuntimeContext.");

        private static readonly GUIContent CheckOptionalPackagesContent = new GUIContent(
            "Check Optional SDK Packages",
            "Also checks loaded optional SDKs such as Mirror, Mirage, Nakama, or Best HTTP without adding hard runtime dependencies.");

        private SerializedProperty _requiredFeatures;
        private SerializedProperty _requireCycloneTransport;
        private SerializedProperty _requireSingleMessageEndpoint;
        private SerializedProperty _requireRuntimeContextForMessageEndpoints;
        private SerializedProperty _checkOptionalSdkPackages;
        private NetworkBootstrapReport _lastReport;
        private bool _rulesFoldout = true;
        private bool _resultFoldout = true;

        private void OnEnable()
        {
            _requiredFeatures = serializedObject.FindProperty("_requiredFeatures");
            _requireCycloneTransport = serializedObject.FindProperty("_requireCycloneTransport");
            _requireSingleMessageEndpoint = serializedObject.FindProperty("_requireSingleMessageEndpoint");
            _requireRuntimeContextForMessageEndpoints = serializedObject.FindProperty("_requireRuntimeContextForMessageEndpoints");
            _checkOptionalSdkPackages = serializedObject.FindProperty("_checkOptionalSdkPackages");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            InspectorUiUtility.DrawSectionHeader(
                "Network Bootstrap Preset",
                "Configures editor diagnostics for the open scenes without binding gameplay code to a concrete network SDK.",
                new Color(0.42f, 0.72f, 0.92f));

            EditorGUILayout.Space(4f);
            _rulesFoldout = InspectorUiUtility.DrawFoldoutHeader("Validation Rules", _rulesFoldout, new Color(0.25f, 0.43f, 0.58f));
            if (_rulesFoldout)
            {
                EditorGUILayout.HelpBox(
                    "Use this asset as a reusable validation profile for scene bootstrap checks. It only affects editor diagnostics and does not add runtime SDK coupling.",
                    MessageType.None);

                EditorGUILayout.PropertyField(_requiredFeatures, RequiredFeaturesContent);
                EditorGUILayout.PropertyField(_requireCycloneTransport, RequireCycloneTransportContent);
                EditorGUILayout.PropertyField(_requireSingleMessageEndpoint, RequireSingleMessageEndpointContent);
                EditorGUILayout.PropertyField(_requireRuntimeContextForMessageEndpoints, RequireRuntimeContextContent);
                EditorGUILayout.PropertyField(_checkOptionalSdkPackages, CheckOptionalPackagesContent);
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(8f);
            if (serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.HelpBox(
                    "Run Check is available only when a single preset is selected. Shared serialized settings remain editable.",
                    MessageType.Info);
            }

            DrawToolbar();

            if (_lastReport == null)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox("Run a check to validate the currently open scenes and write the result to Console.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(6f);
            DrawResult();
        }

        private void DrawToolbar()
        {
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect runRect = row;
            runRect.width = Mathf.Max(0f, row.width - 108f);

            Rect windowRect = row;
            windowRect.x = runRect.xMax + 6f;
            windowRect.width = 102f;

            using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects))
            {
                if (GUI.Button(runRect, "Run Check"))
                {
                    _lastReport = NetworkBootstrapDiagnostics.Run((NetworkBootstrapPreset)target);
                    NetworkBootstrapDiagnostics.LogReport(_lastReport);
                    _resultFoldout = true;
                }
            }

            if (GUI.Button(windowRect, "Open Window"))
            {
                EditorApplication.ExecuteMenuItem("Tools/CycloneGames/Networking/Bootstrap Diagnostics");
            }
        }

        private void DrawResult()
        {
            _resultFoldout = InspectorUiUtility.DrawFoldoutHeader("Check Result", _resultFoldout, new Color(0.22f, 0.38f, 0.52f));
            if (!_resultFoldout)
                return;

            InspectorUiUtility.DrawSummaryRow("Errors", _lastReport.ErrorCount, new Color(0.86f, 0.24f, 0.20f));
            InspectorUiUtility.DrawSummaryRow("Warnings", _lastReport.WarningCount, new Color(0.95f, 0.65f, 0.18f));
            InspectorUiUtility.DrawSummaryRow("Info", _lastReport.InfoCount, new Color(0.35f, 0.62f, 0.86f));

            IReadOnlyList<NetworkBootstrapIssue> issues = _lastReport.Issues;
            if (issues.Count == 0)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox("No issues were found in the currently open scenes.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4f);
            for (int i = 0; i < issues.Count; i++)
            {
                InspectorUiUtility.DrawIssue(issues[i]);
            }
        }
    }
}
