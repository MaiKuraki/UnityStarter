#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using UnityEditor;
using UnityEditor.Build.Reporting;
using static AdvancedSceneManager.Editor.Utility.BuildUtility;

namespace AdvancedSceneManager.DependencyInjection.Editor
{

    /// <inheritdoc cref="BuildUtility"/>
    public interface IBuildManager : DependencyInjectionUtility.IInjectable
    {

        /// <inheritdoc cref="BuildUtility.UpdateSceneList"/>
        void UpdateSceneList();

        /// <inheritdoc cref="BuildUtility.UpdateSceneList"/>
        void UpdateSceneList(bool ignorePlaymodeCheck);

        /// <inheritdoc cref="BuildUtility.GetOrderedList"/>
        IEnumerable<(EditorBuildSettingsScene buildScene, Reason reason)> GetOrderedList();

        /// <inheritdoc cref="BuildUtility.IsIncluded"/>
        bool IsIncluded(Scene scene, out Reason reason);

        /// <inheritdoc cref="BuildUtility.IsEnabled"/>
        bool IsEnabled(string path, out Reason reason);

        /// <inheritdoc cref="BuildUtility.DoBuild"/>
        BuildReport DoBuild(string path, bool attachProfiler = false, bool runGameWhenBuilt = false, bool dev = true, BuildOptions customOptions = BuildOptions.None);

        /// <inheritdoc cref="BuildUtility.DoBuild"/>
        BuildReport DoBuild(BuildPlayerOptions options);

        /// <inheritdoc cref="BuildUtility.preBuild"/>
        event Action<BuildReport> preBuild;

        /// <inheritdoc cref="BuildUtility.postBuild"/>
        event Action<PostBuildEventArgs> postBuild;

    }


}

#endif