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

        public override void InitializeNode(Vector2 position)
        {
            this.name = "Random Selector";
            this.nodeRect.height = 80;
            this.nodeRect.width = 200;
            this.nodeRect.position = position;
            AddInput();
            AddOutput();
        }

        protected override void DrawProperties()
        {
            avoidRepeat = EditorGUILayout.Toggle("Avoid Repeat", avoidRepeat);

            int connCount = (input != null && input.ConnectedNodes != null) ? input.ConnectedNodes.Length : 0;
            if (connCount > 0)
            {
                // Sync weights array size
                if (weights == null || weights.Length != connCount)
                {
                    float[] newWeights = new float[connCount];
                    for (int i = 0; i < connCount; i++)
                        newWeights[i] = (weights != null && i < weights.Length) ? weights[i] : 1f;
                    weights = newWeights;
                }

                EditorGUILayout.LabelField("Weights:");
                for (int i = 0; i < connCount; i++)
                {
                    weights[i] = Mathf.Max(0f, EditorGUILayout.FloatField($"  Node {i}", weights[i]));
                }
            }
        }

#endif
    }
}