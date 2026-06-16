using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Networking.Editor.Diagnostics
{
    public sealed class NetworkBootstrapDiagnosticsWindow : EditorWindow
    {
        private Vector2 _scroll;
        private NetworkBootstrapPreset _preset;
        private NetworkBootstrapReport _report;
        private bool _resultFoldout = true;

        [MenuItem("Tools/CycloneGames/Networking/Bootstrap Diagnostics")]
        private static void Open()
        {
            GetWindow<NetworkBootstrapDiagnosticsWindow>("Network Bootstrap");
        }

        [MenuItem("Tools/CycloneGames/Networking/Run Bootstrap Check")]
        private static void RunAndLog()
        {
            NetworkBootstrapReport report = NetworkBootstrapDiagnostics.Run();
            NetworkBootstrapDiagnostics.LogReport(report);
        }

        private void OnEnable()
        {
            _report = NetworkBootstrapDiagnostics.Run(_preset);
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
            _preset = (NetworkBootstrapPreset)EditorGUI.ObjectField(presetRect, "Preset", _preset, typeof(NetworkBootstrapPreset), false);
            if (GUI.Button(buttonRect, "Scan"))
            {
                _report = NetworkBootstrapDiagnostics.Run(_preset);
            }

            if (_report == null)
                _report = NetworkBootstrapDiagnostics.Run(_preset);

            EditorGUILayout.Space(4f);
            InspectorUiUtility.DrawSectionHeader(
                "Bootstrap Diagnostics",
                "Validates the open scenes against Cyclone network contracts without binding to a concrete SDK.",
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

            IReadOnlyList<NetworkBootstrapIssue> issues = _report.Issues;
            if (issues.Count == 0)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox("No issues were found in the currently open scenes.", MessageType.Info);
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
