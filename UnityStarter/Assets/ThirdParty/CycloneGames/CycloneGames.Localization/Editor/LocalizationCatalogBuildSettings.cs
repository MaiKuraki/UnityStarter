#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using CycloneGames.Localization.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    [Flags]
    public enum LocalizationCatalogContentKind : byte
    {
        Strings = 1,
        Assets = 2,
        All = Strings | Assets,
    }

    [CreateAssetMenu(fileName = "LocalizationCatalogBuildSettings", menuName = "CycloneGames/Localization/Catalog Build Settings")]
    public sealed class LocalizationCatalogBuildSettings : ScriptableObject
    {
        [SerializeField] private LocalizationSettings localizationSettings;
        [SerializeField] private DefaultAsset outputFolder;
        [SerializeField] private string outputFileName = "LocalizationCatalog.asset";
        [SerializeField] private string catalogVersion = LocalizationCatalogBuildOptions.DefaultVersion;
        [SerializeField] private LocalizationCatalogContentKind contentKind = LocalizationCatalogContentKind.All;
        [SerializeField] private List<Locale> includedLocales = new List<Locale>();
        [SerializeField] private List<string> includedTableIds = new List<string>();
        [SerializeField] private bool validateBeforeBuild = true;
        [SerializeField] private bool selectBuiltAsset = true;

        public LocalizationSettings LocalizationSettings => localizationSettings;
        public DefaultAsset OutputFolder => outputFolder;
        public string OutputFileName => outputFileName;
        public string CatalogVersion => catalogVersion;
        public LocalizationCatalogContentKind ContentKind => contentKind;
        public IReadOnlyList<Locale> IncludedLocales => includedLocales;
        public IReadOnlyList<string> IncludedTableIds => includedTableIds;
        public bool ValidateBeforeBuild => validateBeforeBuild;
        public bool SelectBuiltAsset => selectBuiltAsset;

        public string OutputPath
        {
            get
            {
                return TryGetOutputPath(out string outputPath, out _) ? outputPath : string.Empty;
            }
        }

        public bool TryGetOutputPath(out string outputPath, out string error)
        {
            string folderPath = GetOutputFolderPath();
            if (string.IsNullOrEmpty(folderPath))
            {
                outputPath = null;
                error = "Output Folder must be a valid folder under Assets.";
                return false;
            }

            string fileName = string.IsNullOrWhiteSpace(outputFileName)
                ? "LocalizationCatalog.asset"
                : outputFileName;
            return LocalizationAssetPathUtility.TryNormalizeCatalogAssetPath(
                folderPath,
                fileName,
                out outputPath,
                out error);
        }

        public LocalizationCatalogBuildOptions ToBuildOptions()
        {
            if (!TryGetOutputPath(out string outputPath, out string error))
                throw new InvalidOperationException(error);

            return new LocalizationCatalogBuildOptions
            {
                Settings = localizationSettings,
                OutputPath = outputPath,
                Version = string.IsNullOrEmpty(catalogVersion) ? LocalizationCatalogBuildOptions.DefaultVersion : catalogVersion,
                ContentKind = contentKind,
                IncludedLocales = includedLocales,
                IncludedTableIds = includedTableIds,
                ValidateBeforeBuild = validateBeforeBuild,
                SelectBuiltAsset = selectBuiltAsset,
            };
        }

        private string GetOutputFolderPath()
        {
            if (outputFolder == null) return "Assets";

            string path = AssetDatabase.GetAssetPath(outputFolder);
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
                return null;

            return path;
        }
    }
}
#endif
