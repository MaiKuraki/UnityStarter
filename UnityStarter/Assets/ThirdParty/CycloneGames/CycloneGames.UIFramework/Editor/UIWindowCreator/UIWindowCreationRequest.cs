using CycloneGames.UIFramework.Runtime;
using UnityEditor;

namespace CycloneGames.UIFramework.Editor
{
    internal readonly struct UIWindowCreationRequest
    {
        public readonly string WindowName;
        public readonly string NamespaceName;
        public readonly DefaultAsset ScriptFolder;
        public readonly DefaultAsset PrefabFolder;
        public readonly DefaultAsset ConfigFolder;
        public readonly DefaultAsset PresenterFolder;
        public readonly UILayerConfiguration Layer;
        public readonly bool UseMvp;
        public readonly UIWindowConfiguration.PrefabSource SourceMode;
        public readonly bool AutoFillLocationFromPrefabPath;

        public UIWindowCreationRequest(
            string windowName,
            string namespaceName,
            DefaultAsset scriptFolder,
            DefaultAsset prefabFolder,
            DefaultAsset configFolder,
            DefaultAsset presenterFolder,
            UILayerConfiguration layer,
            bool useMvp,
            UIWindowConfiguration.PrefabSource sourceMode,
            bool autoFillLocationFromPrefabPath)
        {
            WindowName = windowName != null ? windowName.Trim() : string.Empty;
            NamespaceName = namespaceName != null ? namespaceName.Trim() : string.Empty;
            ScriptFolder = scriptFolder;
            PrefabFolder = prefabFolder;
            ConfigFolder = configFolder;
            PresenterFolder = presenterFolder;
            Layer = layer;
            UseMvp = useMvp;
            SourceMode = sourceMode;
            AutoFillLocationFromPrefabPath = autoFillLocationFromPrefabPath;
        }
    }
}
