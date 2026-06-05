#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    [CreateAssetMenu(fileName = "LocalizationCatalogBuildSettings", menuName = "CycloneGames/Localization/Catalog Build Settings")]
    public sealed class LocalizationCatalogBuildSettings : ScriptableObject
    {
        [SerializeField] private DefaultAsset outputFolder;
        [SerializeField] private string outputFileName = "LocalizationCatalog.asset";
        [SerializeField] private string catalogVersion = LocalizationCatalogBuildOptions.DefaultVersion;
        [SerializeField] private bool validateBeforeBuild = true;
        [SerializeField] private bool selectBuiltAsset = true;

        public DefaultAsset OutputFolder => outputFolder;
        public string OutputFileName => outputFileName;
        public string CatalogVersion => catalogVersion;
        public bool ValidateBeforeBuild => validateBeforeBuild;
        public bool SelectBuiltAsset => selectBuiltAsset;

        public string OutputPath
        {
            get
            {
                string folderPath = GetOutputFolderPath();
                string fileName = string.IsNullOrEmpty(outputFileName) ? "LocalizationCatalog.asset" : outputFileName;
                if (!fileName.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    fileName += ".asset";

                return folderPath + "/" + fileName;
            }
        }

        public LocalizationCatalogBuildOptions ToBuildOptions()
        {
            return new LocalizationCatalogBuildOptions
            {
                OutputPath = OutputPath,
                Version = string.IsNullOrEmpty(catalogVersion) ? LocalizationCatalogBuildOptions.DefaultVersion : catalogVersion,
                ValidateBeforeBuild = validateBeforeBuild,
                SelectBuiltAsset = selectBuiltAsset,
            };
        }

        private string GetOutputFolderPath()
        {
            if (outputFolder == null) return "Assets";

            string path = AssetDatabase.GetAssetPath(outputFolder);
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
                return "Assets";

            return path;
        }
    }
}
#endif
