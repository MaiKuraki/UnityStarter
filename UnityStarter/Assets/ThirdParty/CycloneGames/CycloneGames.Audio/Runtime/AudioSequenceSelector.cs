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

        private const float NodeWidth  = 230f;
        private const float TitleBarH  =  18f;
        private const float RowH       =  19f;
        private const float RowGap     =   2f;
        private const float BottomPad  =   8f;

        [SerializeField]
        private bool autoSortByNodeY = true;

        private bool NeedsSortByNodeY()
        {
            if (this.input == null || this.input.ConnectedNodes == null || this.input.ConnectedNodes.Length < 2)
                return false;

            float prevY = float.NegativeInfinity;
            for (int i = 0; i < this.input.ConnectedNodes.Length; i++)
            {
                AudioNodeOutput output = this.input.ConnectedNodes[i];
                float y = output != null && output.ParentNode != null
                    ? output.ParentNode.NodeRect.y
                    : prevY;
                if (y < prevY)
                    return true;
                prevY = y;
            }
            return false;
        }

        private void AutoSortConnectionsIfNeeded()
        {
            if (!autoSortByNodeY || this.input == null) return;
            if (!NeedsSortByNodeY()) return;
            this.input.SortConnections();
            EditorUtility.SetDirty(this.input);
            EditorUtility.SetDirty(this);
        }

        // ── Per-mode visual data ─────────────────────────────────────────
        private struct ModeVisual
        {
            public string icon;     // unicode glyph used as a badge
            public string label;    // short display name
            public string pattern;  // playback sequence diagram
            public string desc;     // one-sentence explanation
            public Color  tint;     // helpBox background tint
        }

        // Three strategies and their visual representations:
        //   Sequential      → fixed loop:  ①→②→③→①→②→③
        //   Shuffle         → fair shuffle: ②→③→①‥reshuffle‥③→①→②
        //   RandomNoRepeat  → biased random that avoids immediate repeats
        private ModeVisual GetModeVisual()
        {
            switch (mode)
            {
                case SequenceMode.Shuffle:
                    return new ModeVisual
                    {
                        icon    = "⇄",
                        label   = "Shuffle",
                        pattern = "②→③→①  |  ③→①→②  |  …",
                        desc    = "Every branch plays once per cycle in a random order, then reshuffles. Most fair distribution.",
                        tint    = new Color(0.20f, 0.52f, 0.38f)
                    };
                case SequenceMode.RandomNoRepeat:
                    return new ModeVisual
                    {
                        icon    = "≠",
                        label   = "Random No Repeat",
                        pattern = "②→①→②→③→①→③→…",
                        desc    = "Avoids the immediately previous pick only. Same branch can reappear after one different pick (e.g. ②→①→②). Not a fair cycle.",
                        tint    = new Color(0.58f, 0.33f, 0.18f)
                    };
                default: // Sequential
                    return new ModeVisual
                    {
                        icon    = "▶",
                        label   = "Sequential",
                        pattern = "①→②→③→①→②→③→…",
                        desc    = "Plays branches in a fixed order, looping back to the first after the last.",
                        tint    = new Color(0.24f, 0.40f, 0.68f)
                    };
            }
        }

        private float CalcHeight()
        {
            ModeVisual v = GetModeVisual();
            float contentW = NodeWidth - 28f;
            float descH    = EditorStyles.wordWrappedMiniLabel.CalcHeight(new GUIContent(v.desc),    contentW);
            float patternH = EditorStyles.wordWrappedMiniLabel.CalcHeight(new GUIContent(v.pattern), contentW);
            // title bar + (mini label + dropdown) + gap + info card + bottom pad
            float boxH = RowH + RowGap + patternH + RowGap + descH + 24f;
            return TitleBarH + RowH + RowGap + RowH + RowGap + boxH + BottomPad;
        }

        public override void InitializeNode(Vector2 position)
        {
            this.name = "Sequence Selector";
            this.nodeRect.width    = NodeWidth;
            this.nodeRect.height   = CalcHeight();
            this.nodeRect.position = position;
            AddInput();
            AddOutput();
        }

        public override void DrawNode(int id)
        {
            AutoSortConnectionsIfNeeded();
            this.nodeRect.width  = NodeWidth;
            this.nodeRect.height = CalcHeight();
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
            EditorGUILayout.LabelField("Mode", EditorStyles.miniLabel);
            mode = (SequenceMode)EditorGUILayout.EnumPopup(mode);
            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(this);

            ModeVisual v = GetModeVisual();

            // ── Tinted info card ─────────────────────────────────────────
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = v.tint;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = oldBg;

            // Icon badge + mode name on one line
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white }
            };
            EditorGUILayout.LabelField($"{v.icon}  {v.label}", headerStyle);

            // Playback sequence diagram
            GUIStyle patternStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.80f, 0.92f, 0.80f) },
                fontStyle = FontStyle.Italic
            };
            EditorGUILayout.LabelField(v.pattern, patternStyle);

            // One-sentence description
            EditorGUILayout.LabelField(v.desc, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.EndVertical();
        }

#endif
    }
}