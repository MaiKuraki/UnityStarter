using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Cysharp.Threading.Tasks;
using CycloneGames.RPGFoundation.Runtime.Interaction;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    public class InteractionSceneOverview : EditorWindow
    {
        private Vector2 _scrollPos;
        private string _searchFilter = "";
        private bool _showOnlyEnabled = false;
        private bool _showOnlyAuto = false;
        private SortMode _sortMode = SortMode.Name;

        private readonly List<Interactable> _cachedInteractables = new(64);
        private double _lastRefreshTime;
        private const double RefreshInterval = 0.5;

        private enum SortMode { Name, Priority, Distance, State }

        [MenuItem("Tools/CycloneGames/Interaction/Scene Overview")]
        public static void ShowWindow()
        {
            var window = GetWindow<InteractionSceneOverview>();
            window.titleContent = new GUIContent("🎯 Interactions", "Scene interaction overview");
            window.minSize = new Vector2(400, 300);
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            RefreshCache();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            RefreshCache();
        }

        private void OnFocus()
        {
            RefreshCache();
        }

        private void OnGUI()
        {
            if (EditorApplication.timeSinceStartup - _lastRefreshTime > RefreshInterval)
                RefreshCache();

            DrawToolbar();
            DrawInteractableList();
            DrawFooter();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("↻ Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
                RefreshCache();

            GUILayout.Space(10);

            EditorGUILayout.LabelField("Filter:", GUILayout.Width(40));
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(150));

            GUILayout.Space(10);

            _showOnlyEnabled = GUILayout.Toggle(_showOnlyEnabled, "Enabled Only", EditorStyles.toolbarButton);
            _showOnlyAuto = GUILayout.Toggle(_showOnlyAuto, "Auto Only", EditorStyles.toolbarButton);

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("Sort:", GUILayout.Width(35));
            _sortMode = (SortMode)EditorGUILayout.EnumPopup(_sortMode, EditorStyles.toolbarDropDown, GUILayout.Width(80));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawInteractableList()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            bool hasResults = false;
            Transform playerTransform = null;

            if (Application.isPlaying)
            {
                var detector = Object.FindAnyObjectByType<InteractionDetector>();
                if (detector != null)
                    playerTransform = detector.transform;
            }

            SortCache(playerTransform);

            foreach (var interactable in _cachedInteractables)
            {
                if (interactable == null) continue;
                if (!MatchesFilter(interactable)) continue;

                hasResults = true;
                DrawInteractableRow(interactable, playerTransform);
            }

            if (!hasResults)
            {
                EditorGUILayout.HelpBox("No interactables found matching the filter.", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private bool MatchesFilter(Interactable interactable)
        {
            if (_showOnlyEnabled && !interactable.IsInteractable) return false;
            if (_showOnlyAuto && !interactable.AutoInteract) return false;

            if (!string.IsNullOrEmpty(_searchFilter))
            {
                if (!interactable.name.ToLowerInvariant().Contains(_searchFilter.ToLowerInvariant()) &&
                    !interactable.InteractionPrompt.ToLowerInvariant().Contains(_searchFilter.ToLowerInvariant()))
                    return false;
            }

            return true;
        }

        private void SortCache(Transform playerTransform)
        {
            // Remove any null/destroyed objects first
            _cachedInteractables.RemoveAll(x => x == null);

            switch (_sortMode)
            {
                case SortMode.Name:
                    _cachedInteractables.Sort((a, b) =>
                    {
                        if (a == null) return 1;
                        if (b == null) return -1;
                        return string.Compare(a.name, b.name, System.StringComparison.Ordinal);
                    });
                    break;

                case SortMode.Priority:
                    _cachedInteractables.Sort((a, b) =>
                    {
                        if (a == null) return 1;
                        if (b == null) return -1;
                        return b.Priority.CompareTo(a.Priority);
                    });
                    break;

                case SortMode.Distance:
                    if (playerTransform != null)
                    {
                        Vector3 playerPos = playerTransform.position;
                        _cachedInteractables.Sort((a, b) =>
                        {
                            if (a == null) return 1;
                            if (b == null) return -1;
                            float distA = (a.Position - playerPos).sqrMagnitude;
                            float distB = (b.Position - playerPos).sqrMagnitude;
                            return distA.CompareTo(distB);
                        });
                    }
                    break;

                case SortMode.State:
                    _cachedInteractables.Sort((a, b) =>
                    {
                        if (a == null) return 1;
                        if (b == null) return -1;
                        return a.CurrentState.CompareTo(b.CurrentState);
                    });
                    break;
            }
        }

        private void DrawInteractableRow(Interactable interactable, Transform playerTransform)
        {
            Color bgColor = GetRowColor(interactable);
            GUI.backgroundColor = bgColor;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUI.backgroundColor = Color.white;

            // Icon
            string icon = GetStateIcon(interactable);
            EditorGUILayout.LabelField(icon, GUILayout.Width(24));

            // Name
            if (GUILayout.Button(interactable.name, EditorStyles.linkLabel, GUILayout.Width(150)))
            {
                Selection.activeGameObject = interactable.gameObject;
                EditorGUIUtility.PingObject(interactable);
                SceneView.FrameLastActiveSceneView();
            }

            // Prompt
            EditorGUILayout.LabelField(interactable.InteractionPrompt, GUILayout.Width(100));

            // Priority
            EditorGUILayout.LabelField($"P:{interactable.Priority}", GUILayout.Width(40));

            // Distance
            if (playerTransform != null)
            {
                float dist = Vector3.Distance(interactable.Position, playerTransform.position);
                EditorGUILayout.LabelField($"{dist:F1}m", GUILayout.Width(50));
            }

            // State (runtime only)
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField(interactable.CurrentState.ToString(), GUILayout.Width(80));
            }

            // Quick actions
            GUI.enabled = interactable.IsInteractable && Application.isPlaying;
            if (GUILayout.Button("▶", GUILayout.Width(24)))
            {
                // Trigger interaction and schedule refresh (object may be destroyed)
                interactable.TryInteractAsync().Forget();
                EditorApplication.delayCall += RefreshCache;
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private Color GetRowColor(Interactable interactable)
        {
            if (!interactable.isActiveAndEnabled)
                return new Color(0.5f, 0.5f, 0.5f, 0.3f);
            if (interactable.AutoInteract)
                return new Color(0.3f, 0.7f, 1f, 0.3f);
            if (Application.isPlaying && interactable.IsInteracting)
                return new Color(1f, 0.6f, 0.2f, 0.3f);
            return new Color(0.3f, 0.8f, 0.3f, 0.2f);
        }

        private string GetStateIcon(Interactable interactable)
        {
            if (!interactable.isActiveAndEnabled) return "⭘";
            if (interactable.AutoInteract) return "⚡";
            if (Application.isPlaying)
            {
                return interactable.CurrentState switch
                {
                    InteractionStateType.Idle => "●",
                    InteractionStateType.Starting => "▶",
                    InteractionStateType.InProgress => "◉",
                    InteractionStateType.Completing => "◐",
                    InteractionStateType.Completed => "✓",
                    InteractionStateType.Cancelled => "✗",
                    _ => "?"
                };
            }
            return "●";
        }

        private void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField($"Total: {_cachedInteractables.Count} interactables in scene");

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Gizmos", EditorStyles.toolbarButton))
            {
                GenericMenu menu = new GenericMenu();
                menu.AddItem(new GUIContent("Show All Gizmos"), InteractableGizmoSettings.ShowGizmos,
                    () => InteractableGizmoSettings.ShowGizmos = !InteractableGizmoSettings.ShowGizmos);
                menu.AddItem(new GUIContent("Show Labels"), InteractableGizmoSettings.ShowLabels,
                    () => InteractableGizmoSettings.ShowLabels = !InteractableGizmoSettings.ShowLabels);
                menu.ShowAsContext();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void RefreshCache()
        {
            _lastRefreshTime = EditorApplication.timeSinceStartup;
            _cachedInteractables.Clear();
            _cachedInteractables.AddRange(Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None));
        }
    }
}