using CycloneGames.AnimationRuntime.Core;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Editor
{
    /// <summary>
    /// Play-mode runtime panel: metric cards, pose/phase diagnostics, one card per layer and a time bar
    /// per playback. Every string is rebuilt on the throttled refresh; painting allocates nothing.
    /// </summary>
    internal sealed class AnimationRuntimeRuntimePanel
    {
        private const int MetricCount = 6;
        private static readonly int MaxLayers = AnimationRuntimeLimits.MaxLayers;
        private static readonly int MaxSlots = AnimationRuntimeLimits.MaxLayerCapacity;

        private static readonly string[] MetricLabels =
        {
            "Layers", "Playables", "Playbacks", "Dropped", "Eval", "Reuse"
        };

        private static readonly string[] PhaseLabels =
        {
            "FixedUpdate", "Update", "AnimatorMove", "AnimatorIK", "LateUpdate", "Commit"
        };

        // Bit index in AnimationRuntimePhase for each entry above.
        private static readonly int[] PhaseBits = { 2, 0, 4, 5, 1, 3 };

        private static readonly Color PhaseOnColor = new Color(0.18f, 0.62f, 0.38f, 0.85f);
        private static readonly Color PhaseOffColor = EditorGUIUtility.isProSkin
            ? new Color(1f, 1f, 1f, 0.07f)
            : new Color(0f, 0f, 0f, 0.06f);

        private static GUIStyle _phaseStyle;

        private static readonly Color PanelColor = EditorGUIUtility.isProSkin
            ? new Color(1f, 1f, 1f, 0.035f)
            : new Color(0f, 0f, 0f, 0.035f);

        private static readonly Color BarBackColor = EditorGUIUtility.isProSkin
            ? new Color(1f, 1f, 1f, 0.09f)
            : new Color(0f, 0f, 0f, 0.09f);

        private static readonly Color HeaderColor = EditorGUIUtility.isProSkin
            ? new Color(1f, 1f, 1f, 0.06f)
            : new Color(0f, 0f, 0f, 0.05f);

        private readonly UnityEditor.Editor _owner;
        private readonly string[] _metricValues = new string[MetricCount];
        private readonly Color[] _metricColors = new Color[MetricCount];

        private readonly string[] _layerHeaders = new string[MaxLayers];
        private readonly string[] _layerWeights = new string[MaxLayers];
        private readonly string[] _layerOccupancy = new string[MaxLayers];
        private readonly string[] _layerDropped = new string[MaxLayers];
        private readonly bool[] _layerExternal = new bool[MaxLayers];
        private readonly float[] _layerWeightValues = new float[MaxLayers];
        private readonly int[] _layerSnapshotCount = new int[MaxLayers];
        private readonly AnimationPlaybackSnapshot[][] _layerSnapshots = new AnimationPlaybackSnapshot[MaxLayers][];
        private readonly string[][] _playbackRows = new string[MaxLayers][];

        private double _nextRefresh;
        private int _layerCount;
        private ulong _poseHash;
        private ulong _lastPoseHash = ulong.MaxValue;
        private bool _poseChanged;
        private string _poseText = string.Empty;
        private string _phaseCommitLabel = "Commit";
        private long _droppedClipEvents;
        private int _phaseMask;

        internal AnimationRuntimeRuntimePanel(UnityEditor.Editor owner)
        {
            _owner = owner;

            for (int layer = 0; layer < MaxLayers; layer++)
            {
                _layerHeaders[layer] = "Layer " + layer;
                _layerSnapshots[layer] = new AnimationPlaybackSnapshot[MaxSlots];
                _playbackRows[layer] = new string[MaxSlots];
            }
        }

        internal void Draw(AnimationRuntimeBehaviour behaviour)
        {
            AnimationRuntimeGraph graph = behaviour.Graph;

            if (graph == null || !graph.IsInitialized)
            {
                if (Application.isPlaying)
                {
                    EditorGUILayout.HelpBox(
                        "Graph is not initialized. Check the Animator binding and the Console.",
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "Not initialized in Edit Mode. Runtime counters appear in Play Mode.",
                        EditorStyles.miniLabel);
                }

                return;
            }

            bool refreshed = InspectorUiUtility.ShouldRefresh(ref _nextRefresh);
            if (refreshed)
            {
                Refresh(behaviour, graph);
            }

            DrawMetrics();
            DrawDiagnostics();

            for (int layer = 0; layer < _layerCount; layer++)
            {
                DrawLayer(layer);
            }

            if (refreshed && Application.isPlaying)
            {
                _owner.Repaint();
            }
        }

        private void Refresh(AnimationRuntimeBehaviour behaviour, AnimationRuntimeGraph graph)
        {
            AnimationRuntimeStats stats = graph.GetStats();
            _layerCount = graph.LayerCount;
            _phaseMask = behaviour.PhaseOrderMask;
            _phaseCommitLabel = "Commit@" + ShortPhaseName(behaviour.CommitPhase);
            _droppedClipEvents = graph.DroppedClipEvents;

            _poseHash = graph.ComputePoseHash();
            _poseChanged = _poseHash != _lastPoseHash;
            _lastPoseHash = _poseHash;
            _poseText = _poseHash.ToString("X16");

            long commits = stats.Commits;
            long evaluations = stats.Evaluations;
            long reuseTotal = stats.ReusedPlayables + stats.RecreatedPlayables;

            _metricValues[0] = _layerCount.ToString();
            _metricValues[1] = stats.PlayableCount.ToString();
            _metricValues[2] = stats.ActivePlaybacks + " / " + stats.PeakPlaybacks;
            _metricValues[3] = stats.DroppedPlaybacks.ToString();
            _metricValues[4] = evaluations + " / " + commits;
            _metricValues[5] = (reuseTotal > 0 ? (float)stats.ReusedPlayables / reuseTotal * 100f : 0f).ToString("0") + "%";

            for (int i = 0; i < MetricCount; i++)
            {
                _metricColors[i] = InspectorUiUtility.NeutralColor;
            }

            if (stats.ActivePlaybacks > 0)
            {
                _metricColors[2] = InspectorUiUtility.SuccessColor;
            }

            if (stats.DroppedPlaybacks > 0)
            {
                _metricColors[3] = InspectorUiUtility.WarningColor;
            }

            if (commits > 0 && evaluations < commits)
            {
                _metricColors[4] = InspectorUiUtility.SuccessColor;
            }

            if (reuseTotal > 0 && stats.ReusedPlayables == reuseTotal)
            {
                _metricColors[5] = InspectorUiUtility.SuccessColor;
            }

            int layers = _layerCount < MaxLayers ? _layerCount : MaxLayers;
            for (int layer = 0; layer < layers; layer++)
            {
                _layerWeightValues[layer] = graph.GetLayerWeight(layer);
                _layerExternal[layer] = graph.IsLayerExternallySourced(layer);
                _layerWeights[layer] = _layerWeightValues[layer].ToString("0.00");

                int active = graph.GetLayerActivePlaybacks(layer);
                int capacity = graph.GetLayerCapacity(layer);
                _layerOccupancy[layer] = _layerExternal[layer] ? "external" : active + " / " + capacity;

                long dropped = graph.GetDroppedClipEvents(layer);
                _layerDropped[layer] = dropped > 0 ? "drop " + dropped : string.Empty;

                _layerSnapshotCount[layer] = graph.CopyPlaybackSnapshots(layer, _layerSnapshots[layer]);
                for (int i = 0; i < _layerSnapshotCount[layer]; i++)
                {
                    AnimationPlaybackSnapshot playback = _layerSnapshots[layer][i];
                    _playbackRows[layer][i] = "#" + playback.InstanceId
                        + "  ch" + playback.PlaybackChannel
                        + " clip" + playback.ClipChannel
                        + "   t " + playback.Time.ToString("0.00")
                        + "   w " + playback.Weight.ToString("0.00")
                        + (playback.Connected ? string.Empty : "  pending");
                }
            }
        }

        private static string ShortPhaseName(AnimationCommitPhase phase)
        {
            switch (phase)
            {
                case AnimationCommitPhase.Update:
                    return "Upd";
                case AnimationCommitPhase.FixedUpdate:
                    return "Fixed";
                default:
                    return "Late";
            }
        }

        private void DrawMetrics()
        {
            float width = EditorGUIUtility.currentViewWidth - 26f;
            bool twoColumns = width >= 360f;
            int columns = twoColumns ? 2 : 1;
            float cellWidth = width / columns;

            for (int i = 0; i < MetricCount; i += columns)
            {
                EditorGUILayout.BeginHorizontal();
                for (int column = 0; column < columns; column++)
                {
                    int index = i + column;
                    if (index < MetricCount)
                    {
                        DrawMetric(index, cellWidth - 3f);
                    }
                    else
                    {
                        GUILayout.FlexibleSpace();
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawMetric(int index, float width)
        {
            Rect rect = GUILayoutUtility.GetRect(width, 36f, GUILayout.Width(width));
            EditorGUI.DrawRect(rect, PanelColor);

            EditorGUI.LabelField(new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, 14f),
                MetricLabels[index], EditorStyles.miniLabel);

            Color previous = GUI.contentColor;
            GUI.contentColor = _metricColors[index];
            EditorGUI.LabelField(new Rect(rect.x + 6f, rect.y + 15f, rect.width - 12f, 18f),
                _metricValues[index], EditorStyles.boldLabel);
            GUI.contentColor = previous;
        }

        private static GUIStyle PhaseStyle
        {
            get
            {
                if (_phaseStyle == null)
                {
                    _phaseStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter
                    };
                }

                return _phaseStyle;
            }
        }

        private void DrawDiagnostics()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Pose", EditorStyles.miniLabel, GUILayout.Width(40f));

            Color previous = GUI.contentColor;
            GUI.contentColor = _poseChanged ? InspectorUiUtility.WarningColor : InspectorUiUtility.SuccessColor;
            EditorGUILayout.LabelField(_poseText, EditorStyles.miniLabel);
            GUI.contentColor = previous;

            EditorGUILayout.LabelField(_poseChanged ? "changed" : "stable",
                EditorStyles.miniLabel, GUILayout.Width(52f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Phase", EditorStyles.miniLabel, GUILayout.Width(40f));

            float available = EditorGUIUtility.currentViewWidth - 40f - 26f;
            const float gap = 2f;
            const int columns = 3;
            float chipWidth = Mathf.Max(48f, (available - gap * (columns - 1)) / columns);

            EditorGUILayout.BeginVertical();
            for (int rowIndex = 0; rowIndex < 2; rowIndex++)
            {
                Rect row = GUILayoutUtility.GetRect(available, 16f, GUILayout.Width(available));
                for (int column = 0; column < columns; column++)
                {
                    int i = rowIndex * columns + column;
                    if (i >= PhaseLabels.Length)
                    {
                        break;
                    }

                    Rect chip = new Rect(row.x + column * (chipWidth + gap), row.y, chipWidth, row.height);
                    bool on = (_phaseMask & (1 << PhaseBits[i])) != 0;
                    string label = i == PhaseLabels.Length - 1 ? _phaseCommitLabel : PhaseLabels[i];

                    EditorGUI.DrawRect(chip, on ? PhaseOnColor : PhaseOffColor);

                    previous = GUI.contentColor;
                    GUI.contentColor = on ? Color.white : InspectorUiUtility.NeutralColor;
                    GUI.Label(chip, label, PhaseStyle);
                    GUI.contentColor = previous;
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (_droppedClipEvents > 0)
            {
                previous = GUI.contentColor;
                GUI.contentColor = InspectorUiUtility.WarningColor;
                EditorGUILayout.LabelField("clip events dropped " + _droppedClipEvents, EditorStyles.miniLabel);
                GUI.contentColor = previous;
            }
        }

        private void DrawLayer(int layer)
        {
            EditorGUILayout.Space(4f);

            Rect header = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(header, HeaderColor);

            EditorGUI.LabelField(new Rect(header.x + 4f, header.y + 1f, 54f, 18f),
                _layerHeaders[layer], EditorStyles.miniBoldLabel);

            Rect bar = new Rect(header.x + 60f, header.y + 6f, header.width - 168f, 8f);
            Color barColor = _layerExternal[layer] ? InspectorUiUtility.WarningColor : InspectorUiUtility.RuntimeColor;
            DrawBar(bar, _layerWeightValues[layer], barColor, _layerWeights[layer]);

            EditorGUI.LabelField(new Rect(header.xMax - 104f, header.y + 1f, 100f, 18f),
                _layerOccupancy[layer], EditorStyles.miniLabel);

            if (_layerExternal[layer])
            {
                EditorGUILayout.LabelField("  driven by an external Playable", EditorStyles.miniLabel);
                return;
            }

            if (_layerDropped[layer].Length > 0)
            {
                Color previous = GUI.contentColor;
                GUI.contentColor = InspectorUiUtility.WarningColor;
                EditorGUILayout.LabelField("  " + _layerDropped[layer], EditorStyles.miniLabel);
                GUI.contentColor = previous;
            }

            for (int i = 0; i < _layerSnapshotCount[layer]; i++)
            {
                AnimationPlaybackSnapshot playback = _layerSnapshots[layer][i];

                EditorGUILayout.LabelField("  " + _playbackRows[layer][i], EditorStyles.miniLabel);

                Rect timeBar = GUILayoutUtility.GetRect(0f, 4f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(timeBar, BarBackColor);

                float normalized = playback.Length > 1e-6f
                    ? (float)(playback.Time / playback.Length)
                    : 0f;
                normalized = normalized < 0f ? 0f : (normalized > 1f ? 1f : normalized);
                if (normalized > 0f)
                {
                    EditorGUI.DrawRect(
                        new Rect(timeBar.x, timeBar.y, timeBar.width * normalized, timeBar.height),
                        InspectorUiUtility.RuntimeColor);
                }
            }
        }

        private static void DrawBar(Rect rect, float value, Color color, string label)
        {
            EditorGUI.DrawRect(rect, BarBackColor);

            float clamped = value < 0f ? 0f : (value > 1f ? 1f : value);
            if (clamped > 0f)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width * clamped, rect.height), color);
            }

            GUI.Label(new Rect(rect.x + 4f, rect.y - 3f, rect.width - 8f, rect.height + 6f),
                label, EditorStyles.miniLabel);
        }
    }
}
