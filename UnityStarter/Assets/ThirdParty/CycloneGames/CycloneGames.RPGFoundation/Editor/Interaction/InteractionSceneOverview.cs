using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime.Interaction;
using Object = UnityEngine.Object;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    public sealed class InteractionSceneOverview : EditorWindow
    {
        private const double RefreshInterval = 0.5d;

        private static readonly Comparison<Interactable> CompareByName = CompareInteractableName;
        private static readonly Comparison<Interactable> CompareByPriority = CompareInteractablePriority;
        private static readonly Comparison<Interactable> CompareByDistance = CompareInteractableDistance;
        private static readonly Comparison<Interactable> CompareByState = CompareInteractableState;

        private static readonly Color ColorDisabledRow = new(0.5f, 0.5f, 0.5f, 0.3f);
        private static readonly Color ColorAutoRow = new(0.3f, 0.7f, 1f, 0.3f);
        private static readonly Color ColorInteractingRow = new(1f, 0.6f, 0.2f, 0.3f);
        private static readonly Color ColorDefaultRow = new(0.3f, 0.8f, 0.3f, 0.2f);

        private readonly List<Interactable> _cachedInteractables = new(64);
        private Vector2 _scrollPosition;
        private string _searchFilter = string.Empty;
        private bool _showOnlyEnabled;
        private bool _showOnlyAuto;
        private SortMode _sortMode = SortMode.Name;
        private double _lastRefreshTime;

        private static Vector3 s_distanceSortOrigin;

        private enum SortMode
        {
            Name,
            Priority,
            Distance,
            State
        }

        [MenuItem("Tools/CycloneGames/Interaction/Scene Overview")]
        public static void ShowWindow()
        {
            var window = GetWindow<InteractionSceneOverview>();
            window.titleContent = new GUIContent("Interactions", "Scene interaction overview");
            window.minSize = new Vector2(480f, 320f);
            window.Show();
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

        private void OnFocus()
        {
            RefreshCache();
        }

        private void OnGUI()
        {
            if (EditorApplication.timeSinceStartup - _lastRefreshTime > RefreshInterval)
            {
                RefreshCache();
            }

            DrawToolbar();
            DrawSummary();
            DrawInteractableList();
            DrawFooter();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            RefreshCache();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                RefreshCache();
            }

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Filter", GUILayout.Width(38f));
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(160f));

            GUILayout.Space(8f);
            _showOnlyEnabled = GUILayout.Toggle(_showOnlyEnabled, "Enabled", EditorStyles.toolbarButton, GUILayout.Width(72f));
            _showOnlyAuto = GUILayout.Toggle(_showOnlyAuto, "Auto", EditorStyles.toolbarButton, GUILayout.Width(56f));

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("Sort", GUILayout.Width(30f));
            _sortMode = (SortMode)EditorGUILayout.EnumPopup(_sortMode, EditorStyles.toolbarDropDown, GUILayout.Width(92f));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummary()
        {
            int active = 0;
            int auto = 0;
            int interacting = 0;

            for (int i = 0; i < _cachedInteractables.Count; i++)
            {
                Interactable interactable = _cachedInteractables[i];
                if (interactable == null)
                {
                    continue;
                }

                if (interactable.isActiveAndEnabled)
                {
                    active++;
                }

                if (interactable.AutoInteract)
                {
                    auto++;
                }

                if (Application.isPlaying && interactable.IsInteracting)
                {
                    interacting++;
                }
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            DrawBadge("Total: " + _cachedInteractables.Count, InteractionInspectorUiUtility.ColorCore);
            DrawBadge("Active: " + active, InteractionInspectorUiUtility.ColorRuntime);
            DrawBadge("Auto: " + auto, ColorAutoRow);
            DrawBadge("Busy: " + interacting, ColorInteractingRow);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawBadge(string text, Color color)
        {
            Rect rect = GUILayoutUtility.GetRect(96f, 20f, GUILayout.Width(96f));
            InteractionInspectorUiUtility.DrawStatusBadge(rect, text, color);
        }

        private void DrawInteractableList()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            bool hasResults = false;
            Transform referenceTransform = null;

            if (Application.isPlaying)
            {
                InteractionDetector detector = Object.FindAnyObjectByType<InteractionDetector>();
                if (detector != null)
                {
                    referenceTransform = detector.transform;
                }
            }

            SortCache(referenceTransform);

            for (int i = 0; i < _cachedInteractables.Count; i++)
            {
                Interactable interactable = _cachedInteractables[i];
                if (interactable == null || !MatchesFilter(interactable))
                {
                    continue;
                }

                hasResults = true;
                DrawInteractableRow(interactable, referenceTransform);
            }

            if (!hasResults)
            {
                EditorGUILayout.HelpBox("No interactables match the current filter.", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private bool MatchesFilter(Interactable interactable)
        {
            if (_showOnlyEnabled && !interactable.IsInteractable)
            {
                return false;
            }

            if (_showOnlyAuto && !interactable.AutoInteract)
            {
                return false;
            }

            if (string.IsNullOrEmpty(_searchFilter))
            {
                return true;
            }

            if (interactable.name.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string prompt = interactable.InteractionPrompt;
            return !string.IsNullOrEmpty(prompt) && prompt.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SortCache(Transform referenceTransform)
        {
            for (int i = _cachedInteractables.Count - 1; i >= 0; i--)
            {
                if (_cachedInteractables[i] == null)
                {
                    _cachedInteractables.RemoveAt(i);
                }
            }

            switch (_sortMode)
            {
                case SortMode.Priority:
                    _cachedInteractables.Sort(CompareByPriority);
                    break;
                case SortMode.Distance:
                    if (referenceTransform != null)
                    {
                        s_distanceSortOrigin = referenceTransform.position;
                        _cachedInteractables.Sort(CompareByDistance);
                    }
                    else
                    {
                        _cachedInteractables.Sort(CompareByName);
                    }
                    break;
                case SortMode.State:
                    _cachedInteractables.Sort(CompareByState);
                    break;
                default:
                    _cachedInteractables.Sort(CompareByName);
                    break;
            }
        }

        private void DrawInteractableRow(Interactable interactable, Transform referenceTransform)
        {
            GUI.backgroundColor = GetRowColor(interactable);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUI.backgroundColor = Color.white;

            EditorGUILayout.LabelField(GetStateToken(interactable), GUILayout.Width(48f));

            if (GUILayout.Button(interactable.name, EditorStyles.linkLabel, GUILayout.Width(160f)))
            {
                Selection.activeGameObject = interactable.gameObject;
                EditorGUIUtility.PingObject(interactable);
                SceneView.FrameLastActiveSceneView();
            }

            EditorGUILayout.LabelField(interactable.InteractionPrompt, GUILayout.Width(140f));
            EditorGUILayout.LabelField(interactable.Channel.ToString(), GUILayout.Width(72f));
            EditorGUILayout.LabelField("P:" + interactable.Priority, GUILayout.Width(42f));

            if (referenceTransform != null)
            {
                float distance = Vector3.Distance(interactable.Position, referenceTransform.position);
                EditorGUILayout.LabelField(distance.ToString("F1") + "m", GUILayout.Width(54f));
            }
            else
            {
                EditorGUILayout.LabelField("-", GUILayout.Width(54f));
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField(interactable.CurrentState.ToString(), GUILayout.Width(90f));
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying || !interactable.IsInteractable))
            {
                if (GUILayout.Button("Run", GUILayout.Width(42f)))
                {
                    interactable.TryInteractAsync().Forget();
                    EditorApplication.delayCall += RefreshCache;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private static Color GetRowColor(Interactable interactable)
        {
            if (!interactable.isActiveAndEnabled)
            {
                return ColorDisabledRow;
            }

            if (interactable.AutoInteract)
            {
                return ColorAutoRow;
            }

            if (Application.isPlaying && interactable.IsInteracting)
            {
                return ColorInteractingRow;
            }

            return ColorDefaultRow;
        }

        private static string GetStateToken(Interactable interactable)
        {
            if (!interactable.isActiveAndEnabled)
            {
                return "Off";
            }

            if (interactable.AutoInteract)
            {
                return "Auto";
            }

            if (!Application.isPlaying)
            {
                return "Edit";
            }

            return interactable.CurrentState switch
            {
                InteractionStateType.Starting => "Start",
                InteractionStateType.InProgress => "Run",
                InteractionStateType.Completing => "End",
                InteractionStateType.Completed => "Done",
                InteractionStateType.Cancelled => "Stop",
                _ => "Idle"
            };
        }

        private void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("Scene interactables: " + _cachedInteractables.Count);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Gizmos", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                GenericMenu menu = new();
                menu.AddItem(new GUIContent("Show All Gizmos"), InteractableGizmoSettings.ShowGizmos,
                    ToggleInteractableGizmos);
                menu.AddItem(new GUIContent("Show Labels"), InteractableGizmoSettings.ShowLabels,
                    ToggleInteractableLabels);
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

        private static void ToggleInteractableGizmos()
        {
            InteractableGizmoSettings.ShowGizmos = !InteractableGizmoSettings.ShowGizmos;
        }

        private static void ToggleInteractableLabels()
        {
            InteractableGizmoSettings.ShowLabels = !InteractableGizmoSettings.ShowLabels;
        }

        private static int CompareInteractableName(Interactable a, Interactable b)
        {
            if (a == null)
            {
                return b == null ? 0 : 1;
            }

            if (b == null)
            {
                return -1;
            }

            return string.Compare(a.name, b.name, StringComparison.Ordinal);
        }

        private static int CompareInteractablePriority(Interactable a, Interactable b)
        {
            if (a == null)
            {
                return b == null ? 0 : 1;
            }

            if (b == null)
            {
                return -1;
            }

            int priorityCompare = b.Priority.CompareTo(a.Priority);
            return priorityCompare != 0 ? priorityCompare : string.Compare(a.name, b.name, StringComparison.Ordinal);
        }

        private static int CompareInteractableDistance(Interactable a, Interactable b)
        {
            if (a == null)
            {
                return b == null ? 0 : 1;
            }

            if (b == null)
            {
                return -1;
            }

            float distanceA = (a.Position - s_distanceSortOrigin).sqrMagnitude;
            float distanceB = (b.Position - s_distanceSortOrigin).sqrMagnitude;
            int distanceCompare = distanceA.CompareTo(distanceB);
            return distanceCompare != 0 ? distanceCompare : string.Compare(a.name, b.name, StringComparison.Ordinal);
        }

        private static int CompareInteractableState(Interactable a, Interactable b)
        {
            if (a == null)
            {
                return b == null ? 0 : 1;
            }

            if (b == null)
            {
                return -1;
            }

            int stateCompare = a.CurrentState.CompareTo(b.CurrentState);
            return stateCompare != 0 ? stateCompare : string.Compare(a.name, b.name, StringComparison.Ordinal);
        }
    }
}
