using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Networking.Editor.Diagnostics
{
    [CustomEditor(typeof(GASNetworkDiagnosticsPreset))]
    public sealed class GASNetworkDiagnosticsPresetEditor : UnityEditor.Editor
    {
        private static readonly GUIContent RequireBridgeTypeContent = new GUIContent(
            "Require Bridge Type",
            "Reports an error when NetworkedAbilityBridge is not loaded.");

        private static readonly GUIContent RequireAbilitySystemRuntimeContent = new GUIContent(
            "Require Ability Runtime",
            "Reports an error when AbilitySystemComponent is not loaded.");

        private static readonly GUIContent RequireNetworkRuntimeContent = new GUIContent(
            "Require Network Runtime",
            "Reports an error when Cyclone INetworkManager contract is not loaded.");

        private static readonly GUIContent WarnNoNetworkManagerContent = new GUIContent(
            "Warn Missing Scene Manager",
            "Reports a warning when no INetworkManager component exists in the open scenes.");

        private static readonly GUIContent CheckOptionalSdkContent = new GUIContent(
            "Check Optional SDK Packages",
            "Reports loaded optional SDKs such as Mirror or Nakama without adding hard gameplay dependencies.");

        private SerializedProperty _requireBridgeType;
        private SerializedProperty _requireAbilitySystemRuntime;
        private SerializedProperty _requireCycloneNetworkRuntime;
        private SerializedProperty _warnWhenNoNetworkManagerInOpenScenes;
        private SerializedProperty _checkOptionalSdkPackages;
        private GASNetworkDiagnosticReport _lastReport;
        private bool _rulesFoldout = true;
        private bool _resultFoldout = true;

        private void OnEnable()
        {
            _requireBridgeType = serializedObject.FindProperty("_requireBridgeType");
            _requireAbilitySystemRuntime = serializedObject.FindProperty("_requireAbilitySystemRuntime");
            _requireCycloneNetworkRuntime = serializedObject.FindProperty("_requireCycloneNetworkRuntime");
            _warnWhenNoNetworkManagerInOpenScenes = serializedObject.FindProperty("_warnWhenNoNetworkManagerInOpenScenes");
            _checkOptionalSdkPackages = serializedObject.FindProperty("_checkOptionalSdkPackages");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            InspectorUiUtility.DrawSectionHeader(
                "GAS Networking Diagnostics",
                "Configures editor checks for GameplayAbilities network integration without binding runtime code to a concrete transport SDK.",
                new Color(0.42f, 0.72f, 0.92f));

            EditorGUILayout.Space(4f);
            _rulesFoldout = InspectorUiUtility.DrawFoldoutHeader("Validation Rules", _rulesFoldout, new Color(0.25f, 0.43f, 0.58f));
            if (_rulesFoldout)
            {
                EditorGUILayout.HelpBox(
                    "Use this asset as an editor-only validation profile for GAS networking bootstrap and package wiring.",
                    MessageType.None);

                EditorGUILayout.PropertyField(_requireBridgeType, RequireBridgeTypeContent);
                EditorGUILayout.PropertyField(_requireAbilitySystemRuntime, RequireAbilitySystemRuntimeContent);
                EditorGUILayout.PropertyField(_requireCycloneNetworkRuntime, RequireNetworkRuntimeContent);
                EditorGUILayout.PropertyField(_warnWhenNoNetworkManagerInOpenScenes, WarnNoNetworkManagerContent);
                EditorGUILayout.PropertyField(_checkOptionalSdkPackages, CheckOptionalSdkContent);
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(8f);
            DrawToolbar();

            if (_lastReport == null)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox("Run a check to validate GAS networking integration for the currently open scenes.", MessageType.Info);
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

            if (GUI.Button(runRect, "Run Check"))
            {
                _lastReport = GASNetworkDiagnostics.Run((GASNetworkDiagnosticsPreset)target);
                GASNetworkDiagnostics.LogReport(_lastReport);
                _resultFoldout = true;
            }

            if (GUI.Button(windowRect, "Open Window"))
            {
                EditorApplication.ExecuteMenuItem(GASNetworkEditorMenuPaths.DiagnosticsWindow);
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

            IReadOnlyList<GASNetworkDiagnosticIssue> issues = _lastReport.Issues;
            if (issues.Count == 0)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox("No issues were found for the current GAS networking setup.", MessageType.Info);
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
