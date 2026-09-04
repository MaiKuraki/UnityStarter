using System.Collections.Generic;
using System.IO;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayTags.Unity.Editor
{
    /// <summary>
    /// The editor host platform: points the registry at the project's authored tag files and at the
    /// sources other modules register.
    /// </summary>
    /// <remarks>
    /// Extends <see cref="GameplayTagHostPlatformBase"/>, so it inherits the copy-on-write project-source
    /// registry and only has to supply the editor-specific facts.
    /// </remarks>
    internal sealed class GameplayTagUnityEditorPlatform : GameplayTagHostPlatformBase
    {
        public override string Name => "Unity.Editor";

        public override bool IsRuntimePlaying => Application.isPlaying;

        public override bool TryLoadBuildTagData(out byte[] data)
        {
            // The editor builds its registry from the authored files, not from the baked manifest. Baking
            // happens in BuildTags and is verified there.
            data = null;
            return false;
        }

        public override string GetProjectTagSettingsDirectory()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ProjectSettings", "GameplayTags"));
        }

        public override void CollectProjectTagSources(List<IGameplayTagSource> destinations)
        {
            base.CollectProjectTagSources(destinations);

            // Authored tag files are the editor's source of truth and always apply, ahead of anything
            // another module registered.
            foreach (FileGameplayTagSource source in FileGameplayTagSource.GetAllFileSources())
                destinations.Add(source);
        }
    }

    /// <summary>
    /// Configures the Unity editor host without forcing reflection or file I/O during domain load.
    /// </summary>
    [InitializeOnLoad]
    public static class GameplayTagManagerEditorInitialization
    {
        private static GameplayTagUnityEditorPlatform s_EditorPlatform;

        static GameplayTagManagerEditorInitialization()
        {
            ConfigureEditorSources();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        internal static void ConfigureEditorSources()
        {
            GameplayTagUnityPlatformBootstrap.Configure();

            s_EditorPlatform ??= new GameplayTagUnityEditorPlatform();
            GameplayTagHost.Use(s_EditorPlatform);

            GameplayTagEditorWindow.RebindOpenWindows();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode && state != PlayModeStateChange.EnteredPlayMode)
                return;

            ConfigureEditorSources();
        }
    }
}
