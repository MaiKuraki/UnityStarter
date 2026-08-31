using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AtlasPipeline
{
    /// <summary>
    /// Manual confirmation surface for invalid atlas source names. The pipeline never renames source
    /// assets without an explicit developer action; this window is the single place where the
    /// proposed old-name/new-name mapping is reviewed and approved.
    /// </summary>
    public sealed class AtlasRenameWindow : EditorWindow
    {
        private List<AtlasRenameRequest> _requests = new List<AtlasRenameRequest>();
        private Vector2 _scrollPosition;
        private string _filter = string.Empty;
        private string _feedbackTitle = string.Empty;
        private string _feedbackMessage = string.Empty;

        private const int MaxVisibleEntries = 200;

        [MenuItem("Tools/CycloneGames/Atlas Pipeline/Review Atlas Names")]
        public static void ShowWindow()
        {
            ShowWindow(null);
        }

        public static void ShowWindow(IReadOnlyList<AtlasRenameRequest> requests)
        {
            AtlasRenameWindow window = GetWindow<AtlasRenameWindow>(
                "CycloneGames Atlas Rename Review");
            window.minSize = new Vector2(560f, 460f);
            if (requests != null)
            {
                window._requests = new List<AtlasRenameRequest>(requests);
            }

            window.Show();
            window.Repaint();
        }

        private void OnEnable()
        {
            if (_requests.Count == 0)
            {
                RefreshRequests();
            }
        }

        private void OnGUI()
        {
            AtlasInspectorUiUtility.DrawInspectorTitle(
                "Atlas Name Review",
                "Review proposed sprite file renames before applying them to the project.",
                AtlasInspectorUiUtility.ImportColor);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawSummary();
            DrawRequestList();
            DrawActions();
            DrawFeedback();

            EditorGUILayout.EndScrollView();
        }

        private void DrawSummary()
        {
            AtlasInspectorUiUtility.BeginPanel();
            EditorGUILayout.BeginHorizontal();
            AtlasInspectorUiUtility.DrawMetric(
                "Invalid",
                _requests.Count.ToString(),
                AtlasInspectorUiUtility.WarningColor);
            int selected = CountSelected();
            AtlasInspectorUiUtility.DrawMetric(
                "Selected",
                selected.ToString(),
                selected == 0
                    ? AtlasInspectorUiUtility.NeutralColor
                    : AtlasInspectorUiUtility.SuccessColor);
            EditorGUILayout.EndHorizontal();

            AtlasInspectorUiUtility.DrawStatusRow(
                "Policy",
                "No whitespace, reserved names, or non-portable punctuation",
                AtlasInspectorUiUtility.NeutralColor);
            AtlasInspectorUiUtility.EndPanel();
            EditorGUILayout.Space(5f);
        }

        private void DrawRequestList()
        {
            if (_requests.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No invalid atlas source names were found in the configured source folders.",
                    MessageType.Info);
                return;
            }

            // Filter field: rendering every invalid name at once would freeze the window with tens of
            // thousands of entries. Filtering plus a render cap keeps it responsive.
            _filter = EditorGUILayout.TextField(
                "Filter",
                _filter,
                EditorStyles.toolbarSearchField);

            int shown = 0;
            for (int i = 0; i < _requests.Count && shown < MaxVisibleEntries; i++)
            {
                AtlasRenameRequest request = _requests[i];
                if (!MatchesFilter(request))
                {
                    continue;
                }

                shown++;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                request.Selected = EditorGUILayout.ToggleLeft(
                    $"{request.CurrentFileName}  ->  {request.SuggestedFileName}",
                    request.Selected);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(request.Reason, EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2f);
            }

            if (shown == 0)
            {
                EditorGUILayout.HelpBox(
                    "No entries match the current filter.",
                    MessageType.Info);
            }
            else if (shown < _requests.Count)
            {
                EditorGUILayout.HelpBox(
                    $"Showing {shown} of {_requests.Count} entries. "
                    + "Refine the filter to narrow down the list.",
                    MessageType.Info);
            }
        }

        private bool MatchesFilter(AtlasRenameRequest request)
        {
            if (string.IsNullOrEmpty(_filter))
            {
                return true;
            }

            return request.CurrentFileName.IndexOf(
                       _filter,
                       StringComparison.OrdinalIgnoreCase) >= 0
                   || request.SuggestedFileName.IndexOf(
                       _filter,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawActions()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
            {
                SetAllSelected(true);
            }

            if (GUILayout.Button("Select None"))
            {
                SetAllSelected(false);
            }

            if (GUILayout.Button("Refresh"))
            {
                RefreshRequests();
            }

            GUI.enabled = CountSelected() > 0;
            if (GUILayout.Button("Apply Selected Renames"))
            {
                AtlasRenameResult result = AtlasNaming.ApplyRenames(_requests);
                AtlasNaming.LogApplySummary(result);
                SetFeedback(
                    "Rename complete",
                    $"Renamed {result.RenamedCount} asset(s), failed {result.Failures.Count}.");
                RefreshRequests();
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawFeedback()
        {
            if (string.IsNullOrEmpty(_feedbackTitle))
            {
                return;
            }

            EditorGUILayout.Space(5f);
            EditorGUILayout.HelpBox(
                $"{_feedbackTitle}: {_feedbackMessage}",
                MessageType.None);
        }

        private void RefreshRequests()
        {
            AtlasPipelineSettings settings = AtlasPipeline.TryGetSettings();
            _requests = settings != null
                ? AtlasNaming.CollectInvalidAtlasNames(
                    settings,
                    AtlasPipeline.ResolveRule)
                : new List<AtlasRenameRequest>();
        }

        private int CountSelected()
        {
            int count = 0;
            for (int i = 0; i < _requests.Count; i++)
            {
                if (_requests[i].Selected)
                {
                    count++;
                }
            }

            return count;
        }

        private void SetAllSelected(bool selected)
        {
            for (int i = 0; i < _requests.Count; i++)
            {
                _requests[i].Selected = selected;
            }
        }

        private void SetFeedback(string title, string message)
        {
            _feedbackTitle = title;
            _feedbackMessage = message;
            Repaint();
        }
    }
}
