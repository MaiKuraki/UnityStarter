#if UNITY_EDITOR
using System.Collections.Generic;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;

namespace AdvancedSceneManager.DependencyInjection.Editor
{

    /// <inheritdoc cref="Models.Helpers.SettingsProxy.user"/>
    public interface IUserSettings : DependencyInjectionUtility.IInjectable
    {

        /// <inheritdoc cref="ASMUserSettings.activeProfile"/>
        Profile activeProfile { get; set; }

        /// <inheritdoc cref="ASMUserSettings.alwaysDisplaySearch"/>
        bool alwaysDisplaySearch { get; set; }

        /// <inheritdoc cref="ASMUserSettings.alwaysSaveScenesWhenEnteringPlayMode"/>
        bool alwaysSaveScenesWhenEnteringPlayMode { get; set; }

        /// <inheritdoc cref="ASMUserSettings.displayCollectionAdditiveButton"/>
        bool displayCollectionAdditiveButton { get; set; }

        /// <inheritdoc cref="ASMUserSettings.displayCollectionOpenButton"/>
        bool displayCollectionOpenButton { get; set; }

        /// <inheritdoc cref="ASMUserSettings.displayCollectionPlayButton"/>
        bool displayCollectionPlayButton { get; set; }

        /// <inheritdoc cref="ASMUserSettings.displayHierarchyIndicators"/>
        bool displayHierarchyIndicators { get; set; }

        /// <inheritdoc cref="ASMUserSettings.displayIncludeInBuildToggle"/>
        bool displayIncludeInBuildToggle { get; set; }

        /// <inheritdoc cref="ASMUserSettings.displayProfileButton"/>
        bool displayProfileButton { get; set; }

        /// <inheritdoc cref="ASMUserSettings.editorOnly"/>
        bool editorOnly { get; }

        /// <inheritdoc cref="ASMUserSettings.logBuildScenes"/>
        bool logBuildScenes { get; set; }

        /// <inheritdoc cref="ASMUserSettings.logImport"/>
        bool logImport { get; set; }

        /// <inheritdoc cref="ASMUserSettings.logLoading"/>
        bool logLoading { get; set; }

        /// <inheritdoc cref="ASMUserSettings.logOperation"/>
        bool logOperation { get; set; }

        /// <inheritdoc cref="ASMUserSettings.logStartup"/>
        bool logStartup { get; set; }

        /// <inheritdoc cref="ASMUserSettings.logTracking"/>
        bool logTracking { get; set; }

        /// <inheritdoc cref="ASMUserSettings.openCollectionOnSceneAssetOpen"/>
        bool openCollectionOnSceneAssetOpen { get; set; }

        /// <inheritdoc cref="ASMUserSettings.pinnedOverlayCollections"/>
        IEnumerable<SceneCollection> pinnedOverlayCollections { get; }

        /// <inheritdoc cref="ASMUserSettings.startupProcessOnCollectionPlay"/>
        bool startupProcessOnCollectionPlay { get; set; }

        /// <inheritdoc cref="ASMUserSettings.toolbarButtonCount"/>
        int toolbarButtonCount { get; set; }

        /// <inheritdoc cref="ASMUserSettings.toolbarPlayButtonOffset"/>
        float toolbarPlayButtonOffset { get; set; }

        /// <inheritdoc cref="ASMUserSettings.PinCollectionToOverlay"/>
        void PinCollectionToOverlay(SceneCollection collection, int? index = null);

        /// <inheritdoc cref="ASMUserSettings.ToolbarAction"/>
        void ToolbarAction(int i, out SceneCollection collection, out bool runStartupProcess);

        /// <inheritdoc cref="ASMUserSettings.ToolbarAction"/>
        void ToolbarAction(int i, SceneCollection collection, bool runStartupProcess);

        /// <inheritdoc cref="ASMUserSettings.UnpinCollectionFromOverlay"/>
        void UnpinCollectionFromOverlay(SceneCollection collection);

        /// <inheritdoc cref="ASMSettings.Save"/>
        void Save();

        /// <inheritdoc cref="ASMScriptableSingleton{T}.SaveNow"/>
        void SaveNow();

    }

}

#endif