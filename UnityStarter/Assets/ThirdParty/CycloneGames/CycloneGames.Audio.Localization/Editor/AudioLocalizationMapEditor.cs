// Copyright (c) CycloneGames
// Licensed under the MIT License.

using CycloneGames.Audio.Runtime.Integrations.Localization;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CycloneGames.Audio.Editor.Integrations.Localization
{
    [CustomEditor(typeof(AudioLocalizationMap))]
    [CanEditMultipleObjects]
    public sealed class AudioLocalizationMapEditor : UnityEditor.Editor
    {
        private string validationMessage;
        private MessageType validationMessageType;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Mappings are exact. Add every permitted text-locale to voice-locale relationship explicitly; no language-only fallback is inferred.",
                MessageType.Info);
            DrawPropertiesExcluding(serializedObject, "m_Script");

            if (serializedObject.ApplyModifiedProperties())
                validationMessage = null;

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("Validate Localization Map"))
                ValidateTargets();

            if (!string.IsNullOrEmpty(validationMessage))
                EditorGUILayout.HelpBox(validationMessage, validationMessageType);
        }

        private void ValidateTargets()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                var map = targets[i] as AudioLocalizationMap;
                if (map == null)
                    continue;

                if (!map.TryValidate(out string error))
                {
                    validationMessage = $"Map '{map.name}' is invalid: {error}";
                    validationMessageType = MessageType.Error;
                    return;
                }
            }

            validationMessage = targets.Length == 1
                ? "Localization map is valid."
                : $"All {targets.Length} localization maps are valid.";
            validationMessageType = MessageType.Info;
        }

        [MenuItem("Tools/CycloneGames/Audio/Validate All Localization Maps")]
        private static void ValidateAllMapsMenu()
        {
            AudioLocalizationMapValidationSummary summary =
                AudioLocalizationMapValidation.ValidateAll(logValidSummary: true);
            if (summary.InvalidCount > 0)
            {
                EditorUtility.DisplayDialog(
                    "Audio Localization Validation",
                    $"Maps: {summary.MapCount}\nInvalid: {summary.InvalidCount}",
                    "OK");
            }
        }
    }

    internal readonly struct AudioLocalizationMapValidationSummary
    {
        public AudioLocalizationMapValidationSummary(int mapCount, int invalidCount)
        {
            MapCount = mapCount;
            InvalidCount = invalidCount;
        }

        public int MapCount { get; }
        public int InvalidCount { get; }
    }

    internal static class AudioLocalizationMapValidation
    {
        public static AudioLocalizationMapValidationSummary ValidateAll(bool logValidSummary)
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioLocalizationMap");
            int invalidCount = 0;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var map = AssetDatabase.LoadAssetAtPath<AudioLocalizationMap>(path);
                if (map == null)
                {
                    invalidCount++;
                    Debug.LogError($"Audio localization map '{path}' is invalid: The asset could not be loaded.");
                    continue;
                }

                if (!map.TryValidate(out string error))
                {
                    invalidCount++;
                    Debug.LogError($"Audio localization map '{path}' is invalid: {error}", map);
                }
            }

            var summary = new AudioLocalizationMapValidationSummary(guids.Length, invalidCount);
            if (logValidSummary && invalidCount == 0)
            {
                Debug.Log($"Audio localization map validation passed for {guids.Length} map assets.");
            }

            return summary;
        }
    }

    internal sealed class AudioLocalizationMapBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            AudioLocalizationMapValidationSummary summary =
                AudioLocalizationMapValidation.ValidateAll(logValidSummary: false);
            if (summary.InvalidCount > 0)
            {
                throw new BuildFailedException(
                    $"Audio localization map validation failed. Maps: {summary.MapCount}, Invalid: {summary.InvalidCount}.");
            }
        }
    }
}
