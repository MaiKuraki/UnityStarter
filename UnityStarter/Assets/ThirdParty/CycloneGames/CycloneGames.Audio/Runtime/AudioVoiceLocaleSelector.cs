// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using CycloneGames.Logging;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Selects one directly connected voice branch by stable voice-locale identity.
    /// </summary>
    public sealed class AudioVoiceLocaleSelector : AudioNode
    {
        [SerializeField]
        private AudioVoiceFile fallbackVoice;

        [NonSerialized]
        private long lastMissingLocaleWarningRevision;

        [NonSerialized]
        private bool hasMissingLocaleWarningRevision;

        [NonSerialized]
        private bool hasInvalidConfigurationWarning;

        [NonSerialized]
        private Dictionary<VoiceLocaleId, int> branchIndexByLocale;

        public AudioVoiceFile FallbackVoice => fallbackVoice;

        public override void ProcessNode(ActiveEvent activeEvent)
        {
            AudioNodeOutput[] connectedNodes = input != null ? input.ConnectedNodes : null;
            if (connectedNodes == null || connectedNodes.Length == 0)
            {
                if (!hasInvalidConfigurationWarning)
                {
                    hasInvalidConfigurationWarning = true;
                    Log.Error(
                        $"Voice locale selector '{name}' has no connected Voice File branches. Playback was skipped.");
                }

                return;
            }

            int selectedIndex = SelectConnectedNodeIndex(
                connectedNodes,
                AudioManager.CurrentVoiceLocaleSnapshot,
                fallbackVoice,
                GetBranchIndexScratch(connectedNodes.Length),
                out bool configurationValid);

            if (selectedIndex >= 0)
            {
                ProcessConnectedNode(selectedIndex, activeEvent);
                return;
            }

            if (!configurationValid)
            {
                if (!hasInvalidConfigurationWarning)
                {
                    hasInvalidConfigurationWarning = true;
                    Log.Error(
                        $"Voice locale selector '{name}' has invalid or duplicate branch metadata, or references a disconnected fallback. Playback was skipped.");
                }

                return;
            }

            long revision = AudioManager.VoiceLocaleRevision;
            if (hasMissingLocaleWarningRevision &&
                lastMissingLocaleWarningRevision == revision)
            {
                return;
            }

            hasMissingLocaleWarningRevision = true;
            lastMissingLocaleWarningRevision = revision;
            string eventName = activeEvent != null ? activeEvent.name : "<unknown>";
            string localeCode = AudioManager.CurrentVoiceLocale.IsValid
                ? AudioManager.CurrentVoiceLocale.Code
                : "<unset>";
            Log.Warning(
                $"Event '{eventName}' has no voice branch or explicit fallback for locale '{localeCode}'. Playback was skipped.");
        }

        public override void Reset()
        {
            lastMissingLocaleWarningRevision = 0;
            hasMissingLocaleWarningRevision = false;
            hasInvalidConfigurationWarning = false;
        }

        internal static int SelectConnectedNodeIndex(
            AudioNodeOutput[] connectedNodes,
            in AudioVoiceLocaleSnapshot voiceLocale,
            AudioVoiceFile fallbackVoice)
        {
            var branchIndexes = new Dictionary<VoiceLocaleId, int>(
                connectedNodes != null ? connectedNodes.Length : 0);
            return SelectConnectedNodeIndex(
                connectedNodes,
                voiceLocale,
                fallbackVoice,
                branchIndexes,
                out _);
        }

        private static int SelectConnectedNodeIndex(
            AudioNodeOutput[] connectedNodes,
            in AudioVoiceLocaleSnapshot voiceLocale,
            AudioVoiceFile fallbackVoice,
            Dictionary<VoiceLocaleId, int> branchIndexes,
            out bool configurationValid)
        {
            configurationValid = false;
            branchIndexes.Clear();
            if (connectedNodes == null || connectedNodes.Length == 0)
                return -1;

            int fallbackIndex = -1;
            for (int branchIndex = 0; branchIndex < connectedNodes.Length; branchIndex++)
            {
                AudioVoiceFile voice = GetVoiceNode(connectedNodes[branchIndex]);
                if (voice == null ||
                    !voice.TryGetVoiceLocale(out VoiceLocaleId locale) ||
                    !string.Equals(
                        voice.VoiceLocaleCode,
                        locale.Code,
                        StringComparison.Ordinal) ||
                    branchIndexes.ContainsKey(locale))
                {
                    return -1;
                }

                branchIndexes.Add(locale, branchIndex);
                if (voice == fallbackVoice)
                    fallbackIndex = branchIndex;
            }

            if (fallbackVoice != null && fallbackIndex < 0)
                return -1;

            configurationValid = true;
            if (voiceLocale.IsValid)
            {
                for (int localeIndex = 0; localeIndex < voiceLocale.Count; localeIndex++)
                {
                    if (branchIndexes.TryGetValue(
                            voiceLocale[localeIndex],
                            out int selectedIndex))
                    {
                        return selectedIndex;
                    }
                }
            }

            return fallbackIndex;
        }

        private Dictionary<VoiceLocaleId, int> GetBranchIndexScratch(int branchCount)
        {
            if (branchIndexByLocale == null)
            {
                branchIndexByLocale = new Dictionary<VoiceLocaleId, int>(branchCount);
            }

            return branchIndexByLocale;
        }

        private static AudioVoiceFile GetVoiceNode(AudioNodeOutput output) =>
            output != null ? output.ParentNode as AudioVoiceFile : null;

#if UNITY_EDITOR

        private const float NodeWidth = 300f;

        private const string UsageText =
            "Chooses a Voice File by exact primary locale, ordered locale fallbacks, then Fallback Voice. If none match, playback is skipped.";

        private const string LoadingText =
            "Embedded clips keep every referenced voice in the bank. For large catalogs, use locale-partitioned external voice packs and prepare content before changing locale.";

        private static readonly GUIContent UsageContent = new GUIContent(UsageText);
        private static readonly GUIContent LoadingContent = new GUIContent(LoadingText);
        private static GUIStyle titleStyle;
        private static GUIStyle wrapStyle;

        private static void EnsureStyles()
        {
            if (titleStyle != null)
                return;

            titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                wordWrap = false
            };

            wrapStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                richText = true
            };
        }

        private static float CalcHeight()
        {
            EnsureStyles();

            float contentWidth = NodeWidth - 24f;
            float usageHeight = wrapStyle.CalcHeight(UsageContent, contentWidth);
            float loadingHeight = wrapStyle.CalcHeight(LoadingContent, contentWidth);
            float titleHeight = titleStyle.lineHeight;
            return 18f + 8f + titleHeight + usageHeight + 10f + titleHeight +
                   loadingHeight + 34f + 16f;
        }

        public override void InitializeNode(Vector2 position)
        {
            name = "Voice Locale Selector";
            fallbackVoice = null;
            nodeRect.height = CalcHeight();
            nodeRect.width = NodeWidth;
            nodeRect.position = position;
            AddInput();
            AddOutput();
            EditorUtility.SetDirty(this);
        }

        public override void DrawNode(int id)
        {
            nodeRect.height = CalcHeight();
            nodeRect.width = NodeWidth;
            base.DrawNode(id);
        }

        protected override void DrawProperties()
        {
            EnsureStyles();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Usage", titleStyle);
            EditorGUILayout.LabelField(UsageText, wrapStyle);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Loading", titleStyle);
            EditorGUILayout.LabelField(LoadingText, wrapStyle);
            EditorGUILayout.EndVertical();

            AudioVoiceFile newFallback = EditorGUILayout.ObjectField(
                "Fallback Voice",
                fallbackVoice,
                typeof(AudioVoiceFile),
                false) as AudioVoiceFile;
            if (newFallback == fallbackVoice)
                return;

            Undo.RecordObject(this, "Set Voice Locale Fallback");
            fallbackVoice = newFallback;
            EditorUtility.SetDirty(this);
        }

#endif
    }
}
