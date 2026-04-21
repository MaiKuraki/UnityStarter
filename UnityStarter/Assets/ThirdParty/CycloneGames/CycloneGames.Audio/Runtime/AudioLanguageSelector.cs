// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// An AudioNode for branching an AudioEvent based on the AudioManager's language setting
    /// </summary>
    public class AudioLanguageSelector : AudioNode
    {
        /// <summary>
        /// Select a node with the current language in the AudioManager
        /// </summary>
        /// <param name="activeEvent">The existing runtime audio event</param>
        public override void ProcessNode(ActiveEvent activeEvent)
        {
            if (this.input.ConnectedNodes == null || this.input.ConnectedNodes.Length == 0)
            {
                Debug.LogWarningFormat("No connected nodes for {0}", this.name);
                return;
            }

            for (int i = 0; i < this.input.ConnectedNodes.Length; i++)
            {
                AudioNode tempNode = this.input.ConnectedNodes[i].ParentNode;
                if (tempNode.GetType() == typeof(AudioVoiceFile))
                {
                    AudioVoiceFile voiceNode = (AudioVoiceFile)tempNode;
                    if (voiceNode.Language == AudioManager.CurrentLanguage)
                    {
                        ProcessConnectedNode(i, activeEvent);
                        return;
                    }
                }
            }

            // Fallback: play first connected node if no language match found
            if (this.input.ConnectedNodes.Length > 0)
            {
                Debug.LogWarningFormat("AudioManager: Event \"{0}\" not localized for language {1}, falling back to first node", activeEvent.name, AudioManager.CurrentLanguage);
                ProcessConnectedNode(0, activeEvent);
            }
            else
            {
                Debug.LogErrorFormat("AudioManager: Event \"{0}\" has no connected voice files", activeEvent.name);
            }
        }

#if UNITY_EDITOR

    private const float NodeWidth = 270f;

    private const string UsageText = "Chooses the connected Voice File whose Language matches CurrentLanguage. If none match, it uses the first branch.";
    private const string LoadingText = "Embedded clips keep all language audio referenced by the bank. Use External Reference for per-language on-demand loading.";

    private static float CalcHeight()
    {
        GUIStyle titleStyle = EditorStyles.boldLabel;
        GUIStyle bodyStyle = EditorStyles.wordWrappedMiniLabel;

        float contentWidth = NodeWidth - 24f;
        float usageHeight = bodyStyle.CalcHeight(new GUIContent(UsageText), contentWidth);
        float loadingHeight = bodyStyle.CalcHeight(new GUIContent(LoadingText), contentWidth);
        float titleHeight = titleStyle.lineHeight;

        return 18f + 8f + titleHeight + usageHeight + 10f + titleHeight + loadingHeight + 16f;
    }

        /// <summary>
        /// EDITOR: Set the initial values for the node's properties
        /// </summary>
        /// <param name="position">The position of the node on the graph</param>
        public override void InitializeNode(Vector2 position)
        {
            this.name = "Language Selector";
            this.nodeRect.height = CalcHeight();
            this.nodeRect.width = NodeWidth;
            this.nodeRect.position = position;
            AddInput();
            AddOutput();
        }

        public override void DrawNode(int id)
        {
            this.nodeRect.height = CalcHeight();
            this.nodeRect.width = NodeWidth;
            this.nodeRect = GUI.Window(id, this.nodeRect, DrawWindow, this.name);
            DrawInput();
            DrawOutput();
        }

        protected override void DrawProperties()
        {
            GUIStyle title = new GUIStyle(EditorStyles.boldLabel)
            {
                wordWrap = false
            };

            GUIStyle wrap = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                richText = true
            };

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Usage", title);
            EditorGUILayout.LabelField(
                UsageText,
                wrap);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Loading", title);
            EditorGUILayout.LabelField(
                LoadingText,
                wrap);
            EditorGUILayout.EndVertical();
        }

#endif

    }
}