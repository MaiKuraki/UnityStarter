using UnityEditor;
using UnityEngine;

namespace CycloneGames.InputSystem.Editor
{
    [FilePath(
        "UserSettings/CycloneGames.InputSystem.EditorSettings.asset",
        FilePathAttribute.Location.ProjectFolder)]
    internal sealed class InputEditorSettings : ScriptableSingleton<InputEditorSettings>
    {
        [SerializeField] private string _userConfigSubPath = string.Empty;
        [SerializeField] private string _codegenAssetFolder = "Assets";
        [SerializeField] private string _codegenNamespace = "YourGame.Input.Generated";
        [SerializeField] private string _defaultConfigAssetFolder = "Assets/StreamingAssets";

        internal string UserConfigSubdirectory
        {
            get => _userConfigSubPath ?? string.Empty;
            set => _userConfigSubPath = value ?? string.Empty;
        }

        internal string CodegenFolder
        {
            get => string.IsNullOrWhiteSpace(_codegenAssetFolder) ? "Assets" : _codegenAssetFolder;
            set => _codegenAssetFolder = string.IsNullOrWhiteSpace(value) ? "Assets" : value;
        }

        internal string GeneratedNamespace
        {
            get => string.IsNullOrWhiteSpace(_codegenNamespace) ? "YourGame.Input.Generated" : _codegenNamespace;
            set => _codegenNamespace = value ?? string.Empty;
        }

        internal string DefaultConfigFolder
        {
            get => string.IsNullOrWhiteSpace(_defaultConfigAssetFolder)
                ? "Assets/StreamingAssets"
                : _defaultConfigAssetFolder;
            set => _defaultConfigAssetFolder = string.IsNullOrWhiteSpace(value)
                ? "Assets/StreamingAssets"
                : value;
        }

        internal void SaveSettings()
        {
            Save(true);
        }
    }
}
