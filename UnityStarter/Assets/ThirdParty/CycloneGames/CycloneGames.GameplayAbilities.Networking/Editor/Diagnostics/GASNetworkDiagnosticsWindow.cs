using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Networking.Editor.Diagnostics
{
    public sealed class GASNetworkDiagnosticsWindow : EditorWindow
    {
        private Vector2 _scroll;
        private GASNetworkDiagnosticsPreset _preset;
        private GASNetworkDiagnosticReport _report;
        private bool _resultFoldout = true;

        [MenuItem(GASNetworkEditorMenuPaths.DiagnosticsWindow)]
        private static void Open()
        {
            GetWindow<GASNetworkDiagnosticsWindow>("GAS Networking");
        }

        [MenuItem(GASNetworkEditorMenuPaths.RunDiagnosticsCheck)]
        private static void RunAndLog()
        {
            GASNetworkDiagnosticReport report = GASNetworkDiagnostics.Run();
            GASNetworkDiagnostics.LogReport(report);
        }

        private void OnEnable()
        {
            _report = GASNetworkDiagnostics.Run(_preset);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6f);
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect presetRect = row;
            presetRect.width = Math.Max(0f, row.width - 78f);
            Rect buttonRect = row;
            buttonRect.x = row.xMax - 72f;
            buttonRect.width = 72f;

            _preset = (GASNetworkDiagnosticsPreset)EditorGUI.ObjectField(presetRect, "Preset", _preset, typeof(GASNetworkDiagnosticsPreset), false);
            if (GUI.Button(buttonRect, "Scan"))
            {
                _report = GASNetworkDiagnostics.Run(_preset);
                GASNetworkDiagnostics.LogReport(_report);
                _resultFoldout = true;
            }

            if (_report == null)
                _report = GASNetworkDiagnostics.Run(_preset);

            EditorGUILayout.Space(4f);
            InspectorUiUtility.DrawSectionHeader(
                "GAS Networking Diagnostics",
                "Validates GameplayAbilities networking contracts and scene bootstrap readiness.",
                new Color(0.42f, 0.72f, 0.92f));

            EditorGUILayout.Space(6f);
            DrawResult();
        }

        private void DrawResult()
        {
            _resultFoldout = InspectorUiUtility.DrawFoldoutHeader("Check Result", _resultFoldout, new Color(0.22f, 0.38f, 0.52f));
            if (!_resultFoldout)
                return;

            InspectorUiUtility.DrawSummaryRow("Errors", _report.ErrorCount, new Color(0.86f, 0.24f, 0.20f));
            InspectorUiUtility.DrawSummaryRow("Warnings", _report.WarningCount, new Color(0.95f, 0.65f, 0.18f));
            InspectorUiUtility.DrawSummaryRow("Info", _report.InfoCount, new Color(0.35f, 0.62f, 0.86f));

            IReadOnlyList<GASNetworkDiagnosticIssue> issues = _report.Issues;
            if (issues.Count == 0)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox("No issues were found for the current GAS networking setup.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4f);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < issues.Count; i++)
            {
                InspectorUiUtility.DrawIssue(issues[i]);
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
