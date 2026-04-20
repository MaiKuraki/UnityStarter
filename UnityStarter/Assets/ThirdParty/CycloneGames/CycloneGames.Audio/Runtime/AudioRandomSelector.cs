// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
        /// Index of the last selected node (-1 if none).
        /// Used for avoidRepeat mode.
        /// </summary>
        private int lastSelectedIndex = -1;

        /// <summary>
        /// When true, avoids playing the same node twice in a row (when more than one node exists).
        /// </summary>
        [SerializeField]
        private bool avoidRepeat = false;

        public override void ProcessNode(ActiveEvent activeEvent)
        {
            if (this.input.ConnectedNodes == null || this.input.ConnectedNodes.Length == 0)
            {
                Debug.LogWarningFormat("No connected nodes for {0}", this.name);
                return;
            }

            int count = this.input.ConnectedNodes.Length;
            int nodeNum;

            if (weights != null && weights.Length == count && HasNonZeroWeight())
            {
                nodeNum = WeightedRandom(count);
            }
            else
            {
                nodeNum = Random.Range(0, count);
            }

            if (avoidRepeat && count > 1 && nodeNum == lastSelectedIndex)
            {
                nodeNum = (nodeNum + Random.Range(1, count)) % count;
            }

            lastSelectedIndex = nodeNum;
            ProcessConnectedNode(nodeNum, activeEvent);
        }

        private int WeightedRandom(int count)
        {
            float totalWeight = 0f;
            for (int i = 0; i < count; i++)
                totalWeight += Mathf.Max(0f, weights[i]);

            if (totalWeight <= 0f)
                return Random.Range(0, count);

            float roll = Random.Range(0f, totalWeight);
            float cumulative = 0f;
            for (int i = 0; i < count; i++)
            {
                cumulative += Mathf.Max(0f, weights[i]);
                if (roll < cumulative) return i;
            }
            return count - 1;
        }

        private bool HasNonZeroWeight()
        {
            for (int i = 0; i < weights.Length; i++)
                if (weights[i] > 0f) return true;
            return false;
        }

#if UNITY_EDITOR

        private const float NodeWidth   = 250f;
        private const float TitleBarH   = 18f;  // Unity GUI.Window internal title bar
        private const float RowH        = 19f;
        private const float RowGap      =  2f;
        private const float BottomPad   =  8f;

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
            // title bar + avoid-repeat row + padding
            float h = TitleBarH + RowH + RowGap + BottomPad;
            if (connCount > 0)
                h += RowH + RowGap + connCount * (RowH + RowGap); // "Weights:" header + per-node rows
            return h;
        }

        // Override DrawNode to update height BEFORE GUI.Window measures the rect
        public override void DrawNode(int id)
        {
            int connCount = (input != null && input.ConnectedNodes != null) ? input.ConnectedNodes.Length : 0;
            this.nodeRect.height = CalcHeight(connCount);
            this.nodeRect = GUI.Window(id, this.nodeRect, DrawWindow, this.name);
            DrawInput();
            DrawOutput();
        }

        protected override void DrawProperties()
        {
            EditorGUI.BeginChangeCheck();
            avoidRepeat = EditorGUILayout.Toggle("Avoid Repeat", avoidRepeat);
            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(this);

            int connCount = (input != null && input.ConnectedNodes != null) ? input.ConnectedNodes.Length : 0;
            if (connCount == 0) return;

            // Sync weights array — preserve existing values, default new slots to 1
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

            // Header row: "Weights:" on left, "%" hint on right
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Weights:", EditorStyles.boldLabel, GUILayout.Width(100f));
            EditorGUILayout.LabelField("%", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(34f));
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            for (int i = 0; i < connCount; i++)
            {
                // Use the source node name, truncated if long
                string srcName = (input.ConnectedNodes[i] != null && input.ConnectedNodes[i].ParentNode != null)
                    ? input.ConnectedNodes[i].ParentNode.name
                    : $"Node {i}";
                if (srcName.Length > 13) srcName = srcName.Substring(0, 12) + "…";

                float prob = (totalW > 0.0001f) ? weights[i] / totalW * 100f : 0f;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(srcName, GUILayout.Width(100f));
                weights[i] = Mathf.Max(0f, EditorGUILayout.FloatField(weights[i], GUILayout.Width(50f)));
                EditorGUILayout.LabelField($"{prob:0.#}%", EditorStyles.miniLabel, GUILayout.Width(38f));
                EditorGUILayout.EndHorizontal();
            }
            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(this);
        }

#endif
    }
}