#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Foundation2D.Editor
{
    internal sealed class SpriteSequenceMaterialPickerWindow : EditorWindow
    {
        private readonly List<SpriteSequenceRendererEditorUtility.MaterialCandidate> _allCandidates = new();
        private readonly List<SpriteSequenceRendererEditorUtility.MaterialCandidate> _filteredCandidates = new();
        private readonly List<GUIContent> _filteredCandidateLabels = new();

        private Action<Material> _onSelect;
        private string _shaderHelpText;
        private GUIContent _candidateCountLabel;
        private string _searchText = string.Empty;
        private Vector2 _listScroll;
        private Vector2 _detailsScroll;
        private int _selectedIndex;
        private int _previewMaterialId;
        private Texture _previewTexture;
        private bool _rebuildRequested;

        public static void Open(string title, string shaderName, List<SpriteSequenceRendererEditorUtility.MaterialCandidate> candidates, Action<Material> onSelect)
        {
            SpriteSequenceMaterialPickerWindow window = CreateInstance<SpriteSequenceMaterialPickerWindow>();
            window.titleContent = new GUIContent(title);
            window.minSize = new Vector2(640f, 400f);
            window._shaderHelpText = $"Shader: {shaderName}. Search candidates, inspect the score reasoning, preview the material, then assign the most appropriate one.";
            window._onSelect = onSelect;
            window._allCandidates.Clear();
            if (candidates != null)
            {
                window._allCandidates.AddRange(candidates);
            }
            window.RebuildFilter();
            window.ShowUtility();
        }

        private void OnDisable()
        {
            _onSelect = null;
            _shaderHelpText = null;
            _candidateCountLabel = null;
            _allCandidates.Clear();
            _filteredCandidates.Clear();
            _filteredCandidateLabels.Clear();
            _previewMaterialId = 0;
            _previewTexture = null;
        }

        private void OnGUI()
        {
            DrawHeader();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawCandidateList(Mathf.Clamp(position.width * 0.42f, 240f, 360f));
                DrawDetailsPane();
            }

            DrawFooter();
            if (_rebuildRequested && Event.current.type == EventType.Repaint)
            {
                _rebuildRequested = false;
                RebuildFilter();
                Repaint();
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Material Candidate Picker", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_shaderHelpText, MessageType.None);
            using (EditorGUI.ChangeCheckScope change = new())
            {
                _searchText = EditorGUILayout.TextField("Search", _searchText);
                if (change.changed)
                {
                    RebuildFilter();
                }
            }
        }

        private void DrawCandidateList(float width)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(width)))
            {
                EditorGUILayout.LabelField(_candidateCountLabel, EditorStyles.boldLabel);
                using (EditorGUILayout.ScrollViewScope scroll = new(_listScroll))
                {
                    _listScroll = scroll.scrollPosition;
                    for (int i = 0; i < _filteredCandidates.Count; i++)
                    {
                        SpriteSequenceRendererEditorUtility.MaterialCandidate candidate = _filteredCandidates[i];
                        if (candidate.Material == null)
                        {
                            _rebuildRequested = true;
                            continue;
                        }

                        GUIStyle style = i == _selectedIndex ? EditorStyles.helpBox : EditorStyles.label;
                        Rect rowRect;
                        using (EditorGUILayout.VerticalScope row = new(style))
                        {
                            EditorGUILayout.LabelField(_filteredCandidateLabels[i], i == _selectedIndex ? EditorStyles.boldLabel : EditorStyles.label);
                            EditorGUILayout.LabelField(candidate.Path, EditorStyles.miniLabel);
                            rowRect = row.rect;
                        }

                        Event current = Event.current;
                        if (current.type == EventType.MouseDown && current.button == 0 && rowRect.Contains(current.mousePosition))
                        {
                            _selectedIndex = i;
                            _previewMaterialId = 0;
                            _previewTexture = null;
                            Repaint();
                            current.Use();
                        }
                    }
                }
            }
        }

        private void DrawDetailsPane()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                if (_filteredCandidates.Count == 0)
                {
                    EditorGUILayout.HelpBox("No candidates match the current search.", MessageType.Info);
                    return;
                }

                _selectedIndex = Mathf.Clamp(_selectedIndex, 0, _filteredCandidates.Count - 1);
                SpriteSequenceRendererEditorUtility.MaterialCandidate candidate = _filteredCandidates[_selectedIndex];
                if (candidate.Material == null)
                {
                    _rebuildRequested = true;
                    EditorGUILayout.HelpBox("The selected material no longer exists. The candidate list will refresh.", MessageType.Warning);
                    return;
                }

                EditorGUILayout.LabelField(candidate.Material.name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Score: {candidate.Score}", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(candidate.Path, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(6f);

                Texture preview = GetMaterialPreview(candidate.Material);

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
                using (EditorGUILayout.ScrollViewScope scroll = new(_detailsScroll))
                {
                    _detailsScroll = scroll.scrollPosition;
                    DrawScoreDetails(candidate);
                    EditorGUILayout.ObjectField("Material", candidate.Material, typeof(Material), false);
                }
            }
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
            Material selectedMaterial = GetSelectedMaterial();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(100f)))
                {
                    Close();
                }

                using (new EditorGUI.DisabledScope(selectedMaterial == null))
                {
                    if (GUILayout.Button("Assign Selected", GUILayout.Width(140f)))
                    {
                        _onSelect?.Invoke(selectedMaterial);
                        Close();
                    }
                }
            }
        }

        private void RebuildFilter()
        {
            _filteredCandidates.Clear();
            _filteredCandidateLabels.Clear();
            for (int i = _allCandidates.Count - 1; i >= 0; i--)
            {
                if (_allCandidates[i].Material == null)
                {
                    _allCandidates.RemoveAt(i);
                }
            }

            string query = string.IsNullOrWhiteSpace(_searchText) ? null : _searchText.Trim();
            for (int i = 0; i < _allCandidates.Count; i++)
            {
                SpriteSequenceRendererEditorUtility.MaterialCandidate candidate = _allCandidates[i];
                if (string.IsNullOrEmpty(query) || ContainsIgnoreCase(candidate.Material.name, query) || ContainsIgnoreCase(candidate.Path, query) || ContainsAnyScoreDetail(candidate.ScoreDetails, query))
                {
                    _filteredCandidates.Add(candidate);
                    _filteredCandidateLabels.Add(new GUIContent(
                        $"{candidate.Material.name}    Score {candidate.Score}",
                        candidate.Path));
                }
            }

            _candidateCountLabel = new GUIContent($"Candidates ({_filteredCandidates.Count})");
            _selectedIndex = 0;
            _previewMaterialId = 0;
            _previewTexture = null;
        }

        private Material GetSelectedMaterial()
        {
            if (_filteredCandidates.Count == 0)
            {
                return null;
            }

            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, _filteredCandidates.Count - 1);
            Material material = _filteredCandidates[_selectedIndex].Material;
            if (material == null)
            {
                _rebuildRequested = true;
            }
            return material;
        }

        private Texture GetMaterialPreview(Material material)
        {
            if (material == null)
            {
                _previewMaterialId = 0;
                _previewTexture = null;
                return null;
            }

            int materialId = material.GetInstanceID();
            if (_previewMaterialId != materialId)
            {
                _previewMaterialId = materialId;
                _previewTexture = null;
            }

            if (_previewTexture == null)
            {
                _previewTexture = AssetPreview.GetAssetPreview(material);
                if (_previewTexture == null && !AssetPreview.IsLoadingAssetPreview(materialId))
                {
                    _previewTexture = AssetPreview.GetMiniThumbnail(material);
                }

                if (_previewTexture == null && AssetPreview.IsLoadingAssetPreview(materialId))
                {
                    Repaint();
                }
            }

            return _previewTexture;
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
