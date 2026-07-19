using System.Collections.Generic;
using System.IO;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayTags.Unity.Editor
{
    /// <summary>
    /// Configures the Unity adapter without forcing reflection and file I/O during editor domain load.
    /// </summary>
    [InitializeOnLoad]
    public static class GameplayTagManagerEditorInitialization
    {
        static GameplayTagManagerEditorInitialization()
        {
            ConfigureEditorSources();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        internal static void ConfigureEditorSources()
        {
            GameplayTagUnityPlatformBootstrap.Configure();
            GameplayTagRuntimePlatform.GetProjectTagSettingsDirectory = GetProjectTagSettingsDirectory;
            GameplayTagRuntimePlatform.EnumerateProjectTagSources = EnumerateProjectTagSources;
        }

        private static string GetProjectTagSettingsDirectory()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ProjectSettings", "GameplayTags"));
        }

        private static IEnumerable<IGameplayTagSource> EnumerateProjectTagSources()
        {
            foreach (FileGameplayTagSource source in FileGameplayTagSource.GetAllFileSources())
                yield return source;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode && state != PlayModeStateChange.EnteredPlayMode)
                return;

            ConfigureEditorSources();
            GameplayTagEditorWindow.RebindOpenWindows();
        }
    }
}
