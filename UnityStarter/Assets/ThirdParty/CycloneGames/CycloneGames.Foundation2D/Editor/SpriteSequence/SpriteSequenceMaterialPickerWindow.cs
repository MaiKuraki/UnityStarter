#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CycloneGames.Foundation2D.Runtime;

namespace CycloneGames.Foundation2D.Editor
{
    internal sealed class SpriteSequenceMaterialPickerWindow : EditorWindow
    {
        private readonly List<SpriteSequenceRendererEditorUtility.MaterialCandidate> _allCandidates = new();
        private readonly List<SpriteSequenceRendererEditorUtility.MaterialCandidate> _filteredCandidates = new();

        private Action<Material> _onSelect;
        private string _shaderName;
        private string _searchText = string.Empty;
        private Vector2 _listScroll;
        private Vector2 _detailsScroll;
        private int _selectedIndex;

        public static void Open(string title, string shaderName, List<SpriteSequenceRendererEditorUtility.MaterialCandidate> candidates, Action<Material> onSelect)
        {
            SpriteSequenceMaterialPickerWindow window = CreateInstance<SpriteSequenceMaterialPickerWindow>();
            window.titleContent = new GUIContent(title);
            window.minSize = new Vector2(760f, 420f);
            window._shaderName = shaderName;
            window._onSelect = onSelect;
            window._allCandidates.Clear();
            window._allCandidates.AddRange(candidates);
            window.RebuildFilter();
            window.ShowUtility();
        }

        private void OnGUI()
        {
            DrawHeader();

            EditorGUILayout.BeginHorizontal();
            DrawCandidateList();
            DrawDetailsPane();
            EditorGUILayout.EndHorizontal();

            DrawFooter();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Material Candidate Picker", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox($"Shader: {_shaderName}. Search candidates, inspect the score reasoning, preview the material, then assign the most appropriate one.", MessageType.None);
            EditorGUI.BeginChangeCheck();
            _searchText = EditorGUILayout.TextField("Search", _searchText);
            if (EditorGUI.EndChangeCheck())
            {
                RebuildFilter();
            }
        }

        private void DrawCandidateList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(360f));
            EditorGUILayout.LabelField($"Candidates ({_filteredCandidates.Count})", EditorStyles.boldLabel);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            for (int i = 0; i < _filteredCandidates.Count; i++)
            {
                SpriteSequenceRendererEditorUtility.MaterialCandidate candidate = _filteredCandidates[i];
                GUIStyle style = i == _selectedIndex ? EditorStyles.helpBox : EditorStyles.label;
                EditorGUILayout.BeginVertical(style);
                if (GUILayout.Button($"{candidate.Material.name}    Score {candidate.Score}", EditorStyles.label))
                {
                    _selectedIndex = i;
                    Repaint();
                }

                EditorGUILayout.LabelField(candidate.Path, EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDetailsPane()
        {
            EditorGUILayout.BeginVertical();
            if (_filteredCandidates.Count == 0)
            {
                EditorGUILayout.HelpBox("No candidates match the current search.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, _filteredCandidates.Count - 1);
            SpriteSequenceRendererEditorUtility.MaterialCandidate candidate = _filteredCandidates[_selectedIndex];

            EditorGUILayout.LabelField(candidate.Material.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Score: {candidate.Score}", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(candidate.Path, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(6f);

            Texture preview = AssetPreview.GetAssetPreview(candidate.Material);
            if (preview == null)
            {
                preview = AssetPreview.GetMiniThumbnail(candidate.Material);
            }

            Rect previewRect = GUILayoutUtility.GetRect(160f, 160f, GUILayout.ExpandWidth(false));
            EditorGUI.DrawRect(previewRect, new Color(0.14f, 0.14f, 0.14f));
            if (preview != null)
            {
                GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit);
            }
            else
            {
                EditorGUI.LabelField(previewRect, "No Preview", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Score Reasoning", EditorStyles.boldLabel);
            _detailsScroll = EditorGUILayout.BeginScrollView(_detailsScroll);
            DrawScoreDetails(candidate);
            EditorGUILayout.ObjectField("Material", candidate.Material, typeof(Material), false);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private static void DrawScoreDetails(SpriteSequenceRendererEditorUtility.MaterialCandidate candidate)
        {
            if (candidate.ScoreDetails == null || candidate.ScoreDetails.Length == 0)
            {
                EditorGUILayout.HelpBox("No score detail available.", MessageType.None);
                return;
            }

            for (int i = 0; i < candidate.ScoreDetails.Length; i++)
            {
                SpriteSequenceRendererEditorUtility.ScoreDetail detail = candidate.ScoreDetails[i];
                string points = detail.Points >= 0 ? $"+{detail.Points}" : detail.Points.ToString();
                EditorGUILayout.LabelField($"- {detail.Label}: {points}", EditorStyles.miniBoldLabel);
                if (!string.IsNullOrEmpty(detail.Evidence))
                {
                    EditorGUILayout.LabelField($"  {detail.Evidence}", EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(100f)))
            {
                Close();
            }

            using (new EditorGUI.DisabledScope(_filteredCandidates.Count == 0))
            {
                if (GUILayout.Button("Assign Selected", GUILayout.Width(140f)))
                {
                    _selectedIndex = Mathf.Clamp(_selectedIndex, 0, _filteredCandidates.Count - 1);
                    _onSelect?.Invoke(_filteredCandidates[_selectedIndex].Material);
                    Close();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void RebuildFilter()
        {
            _filteredCandidates.Clear();
            string query = string.IsNullOrWhiteSpace(_searchText) ? null : _searchText.Trim();
            for (int i = 0; i < _allCandidates.Count; i++)
            {
                SpriteSequenceRendererEditorUtility.MaterialCandidate candidate = _allCandidates[i];
                if (string.IsNullOrEmpty(query) || ContainsIgnoreCase(candidate.Material.name, query) || ContainsIgnoreCase(candidate.Path, query) || ContainsAnyScoreDetail(candidate.ScoreDetails, query))
                {
                    _filteredCandidates.Add(candidate);
                }
            }

            _selectedIndex = 0;
        }

        private static bool ContainsIgnoreCase(string source, string value)
        {
            return !string.IsNullOrEmpty(source) && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsAnyScoreDetail(SpriteSequenceRendererEditorUtility.ScoreDetail[] details, string query)
        {
            if (details == null)
            {
                return false;
            }

            for (int i = 0; i < details.Length; i++)
            {
                if (ContainsIgnoreCase(details[i].Label, query) || ContainsIgnoreCase(details[i].Evidence, query) || details[i].Points.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
