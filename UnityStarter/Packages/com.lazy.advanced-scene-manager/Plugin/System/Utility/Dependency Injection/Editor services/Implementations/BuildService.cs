#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AdvancedSceneManager.DependencyInjection.Editor;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace AdvancedSceneManager.DependencyInjection
{

    public static partial class DependencyInjectionUtility
    {

        sealed class BuildService : IBuildManager
        {

            private BuildService()
            {
                BuildUtility.preBuild += preBuild;
                BuildUtility.postBuild += postBuild;
            }

            public static BuildService instance { get; } = new();

            public event Action<BuildReport> preBuild;
            public event Action<BuildUtility.PostBuildEventArgs> postBuild;

            public BuildReport DoBuild(string path, bool attachProfiler = false, bool runGameWhenBuilt = false, bool dev = true, BuildOptions customOptions = BuildOptions.None) => BuildUtility.DoBuild(path, attachProfiler, runGameWhenBuilt, dev, customOptions);
            public BuildReport DoBuild(BuildPlayerOptions options) => BuildUtility.DoBuild(options);

            public IEnumerable<(EditorBuildSettingsScene buildScene, BuildUtility.Reason reason)> GetOrderedList() => BuildUtility.GetOrderedList();

            public bool IsEnabled(string path, out BuildUtility.Reason reason) => BuildUtility.IsEnabled(path, out reason);
            public bool IsIncluded(Scene scene, out BuildUtility.Reason reason) => BuildUtility.IsIncluded(scene, out reason);

            public void UpdateSceneList() => BuildUtility.UpdateSceneList();
            public void UpdateSceneList(bool ignorePlaymodeCheck) => BuildUtility.UpdateSceneList(ignorePlaymodeCheck);
        }


    }

}

#endif