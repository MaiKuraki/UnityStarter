using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.AtlasPipeline
{
    /// <summary>
    /// Project-owned, Build-compatible configuration for the CycloneGames atlas pipeline. Rules are data
    /// rather than code so artists and build engineers can evolve the pipeline without recompiling.
    /// </summary>
    public sealed class AtlasPipelineSettings : ScriptableObject
    {
        public const string DefaultAssetPath = "Assets/Settings/AtlasPipelineSettings.asset";

        [SerializeField] private int schemaVersion = 2;
        [SerializeField] private bool autoImport = true;
        [SerializeField] private bool autoGenerateAtlases = true;
        [SerializeField] private string outputAtlasFolder = "Assets/Atlas";
        [SerializeField] private int atlasPadding = 4;
        [SerializeField] private bool enableRotation = true;
        [SerializeField] private bool enableTightPacking = true;
        [SerializeField] private int blockOffset = 1;
        [SerializeField] private bool includeInBuild = true;
        [SerializeField] private bool asciiOnlyNames = false;
        [SerializeField] private List<AtlasImportRule> importRules = new List<AtlasImportRule>();

        public int SchemaVersion => schemaVersion;
        public bool AutoImport => autoImport;
        public bool AutoGenerateAtlases => autoGenerateAtlases;
        public string OutputAtlasFolder => outputAtlasFolder ?? DefaultOutputAtlasFolder;
        public int AtlasPadding => atlasPadding;
        public bool EnableRotation => enableRotation;
        public bool EnableTightPacking => enableTightPacking;
        public int BlockOffset => blockOffset;
        public bool IncludeInBuild => includeInBuild;
        public bool AsciiOnlyNames => asciiOnlyNames;
        public IReadOnlyList<AtlasImportRule> ImportRules => importRules;

        private const string DefaultOutputAtlasFolder = "Assets/Atlas";

        public static AtlasPipelineSettings CreateDefault()
        {
            var settings = CreateInstance<AtlasPipelineSettings>();
            settings.name = "AtlasPipelineSettings";
            settings.schemaVersion = 2;
            settings.autoImport = true;
            settings.autoGenerateAtlases = true;
            settings.outputAtlasFolder = DefaultOutputAtlasFolder;
            settings.atlasPadding = 4;
            settings.enableRotation = true;
            settings.enableTightPacking = true;
            settings.blockOffset = 1;
            settings.includeInBuild = true;
            const string uiFolder = "Assets/UI";
            const string sceneFolder = "Assets/Scene";
            settings.importRules = new List<AtlasImportRule>
            {
                AtlasImportRule.Create(
                    "UI",
                    uiFolder,
                    AtlasTextureFormat.Astc6x6,
                    AtlasTextureFormat.Astc6x6,
                    AtlasGranularity.PerSourceFolder,
                    "UI",
                    webglFormat: AtlasTextureFormat.Astc6x6,
                    spriteMode: AtlasSpriteMode.Single),
                AtlasImportRule.Create(
                    "Scene",
                    sceneFolder,
                    AtlasTextureFormat.Astc6x6,
                    AtlasTextureFormat.Astc6x6,
                    AtlasGranularity.PerSourceFolder,
                    "Scene",
                    webglFormat: AtlasTextureFormat.Astc6x6,
                    spriteMode: AtlasSpriteMode.Single),
            };
            return settings;
        }

        public string NormalizedOutputAtlasFolder
        {
            get
            {
                string value = OutputAtlasFolder.Replace('\\', '/').Trim();
                return value.TrimEnd('/');
            }
        }

        public bool IsOutputFolderValid =>
            !string.IsNullOrEmpty(NormalizedOutputAtlasFolder)
            && NormalizedOutputAtlasFolder.StartsWith("Assets/", StringComparison.Ordinal)
            && !NormalizedOutputAtlasFolder.EndsWith("/", StringComparison.Ordinal);
    }
}
