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
            AudioNodeOutput[] connectedNodes = this.input != null ? this.input.ConnectedNodes : null;
            if (connectedNodes == null || connectedNodes.Length == 0)
            {
                Debug.LogWarningFormat("No connected nodes for {0}", this.name);
                return;
            }

            int count = connectedNodes.Length;
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
            return EditorUtilityCache.NeedsSortByNodeY(this.input);
        }

        private void AutoSortConnectionsIfNeeded()
        {
            if (!autoSortByNodeY || this.input == null) return;
            if (!NeedsSortByNodeY()) return;
            this.input.SortConnections();
            EditorUtility.SetDirty(this.input);
            EditorUtility.SetDirty(this);
        }

        private static GUIStyle headerStyle;
        private static GUIStyle patternStyle;
        private static readonly GUIContent DescContent = new GUIContent();
        private static readonly GUIContent PatternContent = new GUIContent();

        private struct ModeVisual
        {
            public string icon;
            public string label;    // short display name
            public string pattern;  // playback sequence diagram
            public string desc;     // one-sentence explanation
            public Color  tint;     // helpBox background tint
        }

        private static void EnsureStyles()
        {
            if (headerStyle != null)
            {
                return;
            }

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white }
            };

            patternStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.80f, 0.92f, 0.80f) },
                fontStyle = FontStyle.Italic
            };
        }

        private ModeVisual GetModeVisual()
        {
            switch (mode)
            {
                case SequenceMode.Shuffle:
                    return new ModeVisual
                    {
                        icon    = "SHF",
                        label   = "Shuffle",
                        pattern = "2 -> 3 -> 1  |  3 -> 1 -> 2  |  ...",
                        desc    = "Every branch plays once per cycle in a random order, then reshuffles. Most fair distribution.",
                        tint    = new Color(0.20f, 0.52f, 0.38f)
                    };
                case SequenceMode.RandomNoRepeat:
                    return new ModeVisual
                    {
                        icon    = "NR",
                        label   = "Random No Repeat",
                        pattern = "2 -> 1 -> 2 -> 3 -> 1 -> 3 -> ...",
                        desc    = "Avoids the immediately previous pick only. Same branch can reappear after one different pick (for example, 2 -> 1 -> 2). Not a fair cycle.",
                        tint    = new Color(0.58f, 0.33f, 0.18f)
                    };
                default: // Sequential
                    return new ModeVisual
                    {
                        icon    = "SEQ",
                        label   = "Sequential",
                        pattern = "1 -> 2 -> 3 -> 1 -> 2 -> 3 -> ...",
                        desc    = "Plays branches in a fixed order, looping back to the first after the last.",
                        tint    = new Color(0.24f, 0.40f, 0.68f)
                    };
            }
        }

        private float CalcHeight()
        {
            ModeVisual v = GetModeVisual();
            float contentW = NodeWidth - 28f;
            DescContent.text = v.desc;
            PatternContent.text = v.pattern;
            float descH    = EditorStyles.wordWrappedMiniLabel.CalcHeight(DescContent,    contentW);
            float patternH = EditorStyles.wordWrappedMiniLabel.CalcHeight(PatternContent, contentW);
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
            EnsureStyles();

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

            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = v.tint;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = oldBg;

            EditorGUILayout.LabelField($"{v.icon}  {v.label}", headerStyle);

            EditorGUILayout.LabelField(v.pattern, patternStyle);

            EditorGUILayout.LabelField(v.desc, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.EndVertical();
        }

#endif
    }
}
