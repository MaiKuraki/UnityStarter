// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif
using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// An AudioNode for randomly choosing one of its connected nodes.
    /// Supports optional per-node weights for non-uniform probability.
    /// </summary>
    public class AudioRandomSelector : AudioNode
    {
        /// <summary>
        /// Per-node weights. When empty or mismatched, uniform distribution is used.
        /// </summary>
        [SerializeField]
        private float[] weights = new float[0];

        /// <summary>
        /// Ring buffer of recently selected node indices.
        /// </summary>
        private readonly int[] recentSelectionHistory = new int[MaxAvoidRepeatHistory];
        private int recentSelectionWriteIndex;
        private int recentSelectionCount;

        /// <summary>
        /// When true, avoids recently selected nodes when enough alternatives exist.
        /// </summary>
        [SerializeField]
        private bool avoidRepeat = false;
        [SerializeField]
        [Range(1, MaxAvoidRepeatHistory)]
        private int avoidRepeatHistory = 1;

        private const int MaxAvoidRepeatHistory = 8;

        public override void ProcessNode(ActiveEvent activeEvent)
        {
            AudioNodeOutput[] connectedNodes = this.input != null ? this.input.ConnectedNodes : null;
            if (connectedNodes == null || connectedNodes.Length == 0)
            {
                Debug.LogWarningFormat("No connected nodes for {0}", this.name);
                return;
            }

            int count = connectedNodes.Length;
            int historyCount = avoidRepeat ? Mathf.Min(Mathf.Max(avoidRepeatHistory, 1), count - 1) : 0;
            int nodeNum = weights != null && weights.Length == count
                ? WeightedRandom(count, historyCount)
                : UniformRandom(count, historyCount);

            AddRecentSelection(nodeNum);
            ProcessConnectedNode(nodeNum, activeEvent);
        }

        private int WeightedRandom(int count, int historyCount)
        {
            float totalWeight = 0f;
            for (int i = 0; i < count; i++)
            {
                if (historyCount > 0 && IsInRecentHistory(i, historyCount)) continue;
                totalWeight += Mathf.Max(0f, weights[i]);
            }

            if (totalWeight <= 0f)
            {
                return UniformRandom(count, 0);
            }

            float roll = Random.Range(0f, totalWeight);
            float cumulative = 0f;
            int lastEligible = -1;
            for (int i = 0; i < count; i++)
            {
                if (historyCount > 0 && IsInRecentHistory(i, historyCount)) continue;
                lastEligible = i;
                cumulative += Mathf.Max(0f, weights[i]);
                if (roll < cumulative)
                {
                    return i;
                }
            }

            return lastEligible >= 0 ? lastEligible : Random.Range(0, count);
        }

        private int UniformRandom(int count, int historyCount)
        {
            if (historyCount <= 0 || count <= 1)
                return Random.Range(0, count);

            int eligibleCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (!IsInRecentHistory(i, historyCount))
                    eligibleCount++;
            }

            if (eligibleCount <= 0)
                return Random.Range(0, count);

            int selectedOrdinal = Random.Range(0, eligibleCount);
            for (int i = 0; i < count; i++)
            {
                if (IsInRecentHistory(i, historyCount)) continue;
                if (selectedOrdinal == 0) return i;
                selectedOrdinal--;
            }

            return count - 1;
        }

        private bool IsInRecentHistory(int index, int historyCount)
        {
            int count = Mathf.Min(Mathf.Min(historyCount, recentSelectionCount), recentSelectionHistory.Length);
            for (int i = 0; i < count; i++)
            {
                int historyIndex = recentSelectionWriteIndex - 1 - i;
                if (historyIndex < 0) historyIndex += recentSelectionHistory.Length;
                if (recentSelectionHistory[historyIndex] == index)
                    return true;
            }

            return false;
        }

        private void AddRecentSelection(int index)
        {
            recentSelectionHistory[recentSelectionWriteIndex] = index;
            recentSelectionWriteIndex++;
            if (recentSelectionWriteIndex >= recentSelectionHistory.Length)
                recentSelectionWriteIndex = 0;
            if (recentSelectionCount < recentSelectionHistory.Length)
                recentSelectionCount++;
        }

#if UNITY_EDITOR

        private const float NodeWidth   = 290f;
        private const float TitleBarH   = 18f;  // Unity GUI.Window internal title bar
        private const float RowH        = 19f;
        private const float RowGap      =  2f;
        private const float BottomPad   =  8f;
        private const float NameFieldMinW = 108f;
        private const float WeightFieldW = 42f;
        private const float ProbFieldW   = 54f;

        [SerializeField]
        private bool autoSortByNodeY = true;
        private readonly Dictionary<AudioNodeOutput, float> weightByOutput = new Dictionary<AudioNodeOutput, float>(8);

        private bool NeedsSortByNodeY()
        {
            return EditorUtilityCache.NeedsSortByNodeY(this.input);
        }

        private void AutoSortConnectionsIfNeeded()
        {
            if (!autoSortByNodeY || this.input == null || !NeedsSortByNodeY()) return;

            this.weightByOutput.Clear();
            AudioNodeOutput[] before = this.input.ConnectedNodes;
            for (int i = 0; i < before.Length; i++)
            {
                float weight = (weights != null && i < weights.Length) ? weights[i] : 1f;
                this.weightByOutput[before[i]] = weight;
            }

            this.input.SortConnections();

            AudioNodeOutput[] after = this.input.ConnectedNodes;
            float[] reordered = new float[after.Length];
            for (int i = 0; i < after.Length; i++)
            {
                reordered[i] = (after[i] != null && this.weightByOutput.TryGetValue(after[i], out float w))
                    ? Mathf.Max(0f, w)
                    : 1f;
            }

            this.weightByOutput.Clear();
            weights = reordered;
            EditorUtility.SetDirty(this.input);
            EditorUtility.SetDirty(this);
        }

        public override void InitializeNode(Vector2 position)
        {
            this.name = "Random Selector";
            this.nodeRect.width    = NodeWidth;
            this.nodeRect.height   = CalcHeight(0);
            this.nodeRect.position = position;
            AddInput();
            AddOutput();
        }

        private float CalcHeight(int connCount)
        {
            // title bar + auto-sort row + avoid-repeat rows + padding
            float h = TitleBarH + RowH + RowGap + RowH + RowGap + RowH + RowGap + BottomPad;
            if (connCount > 0)
                h += (RowH + RowGap) + (RowH + RowGap) + connCount * (RowH + RowGap); // "Weights:" + table header + per-node rows
            return h;
        }

        // Override DrawNode to update height BEFORE GUI.Window measures the rect
        public override void DrawNode(int id)
        {
            AutoSortConnectionsIfNeeded();
            int connCount = (input != null && input.ConnectedNodes != null) ? input.ConnectedNodes.Length : 0;
            this.nodeRect.height = CalcHeight(connCount);
            this.nodeRect = GUI.Window(id, this.nodeRect, DrawWindow, this.name);
            DrawInput();
            DrawOutput();
        }

        protected override void DrawProperties()
        {
            EditorGUI.BeginChangeCheck();
            autoSortByNodeY = EditorGUILayout.ToggleLeft("Auto Sort by Node Y", autoSortByNodeY);
            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(this);

            EditorGUI.BeginChangeCheck();
            avoidRepeat = EditorGUILayout.Toggle("Avoid Repeat", avoidRepeat);
            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(this);

            using (new EditorGUI.DisabledScope(!avoidRepeat))
            {
                EditorGUI.BeginChangeCheck();
                avoidRepeatHistory = EditorGUILayout.IntSlider("Avoid Recent", avoidRepeatHistory, 1, MaxAvoidRepeatHistory);
                if (EditorGUI.EndChangeCheck())
                    EditorUtility.SetDirty(this);
            }

            int connCount = (input != null && input.ConnectedNodes != null) ? input.ConnectedNodes.Length : 0;
            if (connCount == 0) return;

            // Sync weights array while preserving existing values and defaulting new slots to 1.
            if (weights == null || weights.Length != connCount)
            {
                float[] newWeights = new float[connCount];
                for (int i = 0; i < connCount; i++)
                    newWeights[i] = (weights != null && i < weights.Length && weights[i] > 0f) ? weights[i] : 1f;
                weights = newWeights;
                EditorUtility.SetDirty(this);
            }

            // Compute total weight for probability display
            float totalW = 0f;
            for (int i = 0; i < connCount; i++)
                totalW += Mathf.Max(0f, weights[i]);

            // Header rows
            EditorGUILayout.LabelField("Weights:", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Node", EditorStyles.centeredGreyMiniLabel, GUILayout.MinWidth(NameFieldMinW), GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("W", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(WeightFieldW));
            EditorGUILayout.LabelField("Chance", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(ProbFieldW));
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            for (int i = 0; i < connCount; i++)
            {
                // Use source node name directly so branch identity stays clear
                string srcName = (input.ConnectedNodes[i] != null && input.ConnectedNodes[i].ParentNode != null)
                    ? input.ConnectedNodes[i].ParentNode.name
                    : $"Node {i}";
                float prob = (totalW > 0.0001f) ? weights[i] / totalW * 100f : 0f;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(srcName, EditorStyles.miniLabel, GUILayout.MinWidth(NameFieldMinW), GUILayout.ExpandWidth(true));
                weights[i] = Mathf.Max(0f, EditorGUILayout.FloatField(weights[i], GUILayout.Width(WeightFieldW)));
                EditorGUILayout.LabelField($"{prob:0.#}%", EditorStyles.miniLabel, GUILayout.Width(ProbFieldW));
                EditorGUILayout.EndHorizontal();
            }
            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(this);
        }

#endif
    }
}
