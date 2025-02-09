using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Enums;
using AdvancedSceneManager.Utility;
using UnityEngine;

namespace AdvancedSceneManager.DependencyInjection
{

    /// <inheritdoc cref="Models.Helpers.SettingsProxy.project"/>
    public interface IProjectSettings : DependencyInjectionUtility.IInjectable
    {

        /// <inheritdoc cref="ASMSettings.allowCollectionLocking"/>
        bool allowCollectionLocking { get; set; }

        /// <inheritdoc cref="ASMSettings.allowExcludingCollectionsFromBuild"/>
        bool allowExcludingCollectionsFromBuild { get; set; }

        /// <inheritdoc cref="ASMSettings.allowSceneLocking"/>
        bool allowSceneLocking { get; set; }

        /// <inheritdoc cref="ASMSettings.assetPath"/>
        string assetPath { get; set; }

        /// <inheritdoc cref="ASMSettings.buildProfile"/>
        Profile buildProfile { get; }

        /// <inheritdoc cref="ASMSettings.buildUnitySplashScreenColor"/>
        Color buildUnitySplashScreenColor { get; }

        /// <inheritdoc cref="ASMSettings.checkForDuplicateSceneOperations"/>
        bool checkForDuplicateSceneOperations { get; set; }

        /// <inheritdoc cref="ASMSettings.customData"/>
        ASMSettings.CustomData customData { get; }

        /// <inheritdoc cref="ASMSettings.defaultProfile"/>
        Profile defaultProfile { get; set; }

        /// <inheritdoc cref="ASMSettings.enableCrossSceneReferences"/>
        bool enableCrossSceneReferences { get; set; }

        /// <inheritdoc cref="ASMSettings.fadeScene"/>
        Scene fadeScene { get; set; }

        /// <inheritdoc cref="ASMSettings.forceProfile"/>
        Profile forceProfile { get; set; }

        /// <inheritdoc cref="ASMSettings.preventSpammingEventMethods"/>
        bool preventSpammingEventMethods { get; set; }

        /// <inheritdoc cref="ASMSettings.reverseUnloadOrderOnCollectionClose"/>
        bool reverseUnloadOrderOnCollectionClose { get; set; }

        /// <inheritdoc cref="ASMSettings.sceneImportOption"/>
        SceneImportOption sceneImportOption { get; set; }

        /// <inheritdoc cref="ASMSettings.spamCheckCooldown"/>
        float spamCheckCooldown { get; set; }

        /// <inheritdoc cref="ASMSettings.Save"/>
        void Save();

        /// <inheritdoc cref="ASMScriptableSingleton{T}.SaveNow"/>
        void SaveNow();

#if UNITY_EDITOR
        /// <inheritdoc cref="ASMSettings.SetBuildProfile"/>
        void SetBuildProfile(Profile profile);
#endif

    }

}
