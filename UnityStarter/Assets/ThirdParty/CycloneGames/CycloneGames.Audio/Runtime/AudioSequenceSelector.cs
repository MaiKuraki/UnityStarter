// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    public enum SequenceMode
    {
        Sequential,
        Shuffle,
        RandomNoRepeat
    }

    /// <summary>
    /// Iterates over each of its connections when the event is played.
    /// Supports sequential, shuffle, and random-no-repeat modes.
    /// </summary>
    public class AudioSequenceSelector : AudioNode
    {
        [SerializeField]
        private SequenceMode mode = SequenceMode.Sequential;

        private int currentNode = 0;
        private int[] shuffleOrder;
        private int shuffleIndex;
        private int lastRandomIndex = -1;

        public override void ProcessNode(ActiveEvent activeEvent)
        {
            if (this.input.ConnectedNodes == null || this.input.ConnectedNodes.Length == 0)
            {
                Debug.LogWarningFormat("No connected nodes for {0}", this.name);
                return;
            }

            int count = this.input.ConnectedNodes.Length;
            int nodeNum;

            switch (mode)
            {
                case SequenceMode.Shuffle:
                    nodeNum = GetShuffledIndex(count);
                    break;
                case SequenceMode.RandomNoRepeat:
                    nodeNum = GetRandomNoRepeat(count);
                    break;
                default: // Sequential
                    nodeNum = this.currentNode;
                    this.currentNode = (this.currentNode + 1) % count;
                    break;
            }

            ProcessConnectedNode(nodeNum, activeEvent);
        }

        private int GetShuffledIndex(int count)
        {
            if (shuffleOrder == null || shuffleOrder.Length != count || shuffleIndex >= count)
            {
                RebuildShuffleOrder(count);
            }

            int result = shuffleOrder[shuffleIndex];
            shuffleIndex++;
            return result;
        }

        private void RebuildShuffleOrder(int count)
        {
            shuffleOrder = new int[count];
            for (int i = 0; i < count; i++) shuffleOrder[i] = i;

            // Fisher-Yates shuffle
            for (int i = count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                int tmp = shuffleOrder[i];
                shuffleOrder[i] = shuffleOrder[j];
                shuffleOrder[j] = tmp;
            }
            shuffleIndex = 0;
        }

        private int GetRandomNoRepeat(int count)
        {
            if (count <= 1) return 0;
            int nodeNum = Random.Range(0, count);
            if (nodeNum == lastRandomIndex)
                nodeNum = (nodeNum + Random.Range(1, count)) % count;
            lastRandomIndex = nodeNum;
            return nodeNum;
        }

        public override void Reset()
        {
            this.currentNode = 0;
            this.shuffleIndex = 0;
            this.shuffleOrder = null;
            this.lastRandomIndex = -1;
        }

#if UNITY_EDITOR

        public override void InitializeNode(Vector2 position)
        {
            this.name = "Sequence Selector";
            this.nodeRect.height = 70;
            this.nodeRect.width = 180;
            this.nodeRect.position = position;
            AddInput();
            AddOutput();
        }

        protected override void DrawProperties()
        {
            mode = (SequenceMode)EditorGUILayout.EnumPopup("Mode", mode);
        }

#endif
    }
}