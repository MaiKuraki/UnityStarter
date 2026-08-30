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

        /// <summary>
        /// Includes the folder path in PerSprite atlas keys. Off by default so existing projects keep
        /// generating the same atlas file names — flipping it renames every PerSprite atlas, which
        /// breaks anything loading them by path.
        /// </summary>
        [SerializeField] private bool collisionSafeAtlasKeys = false;

        [SerializeField] private AtlasKeyCasing atlasKeyCasing = AtlasKeyCasing.Preserve;

        /// <summary>
        /// Folders the pipeline never touches, whatever the rules say: no atlas membership, no import
        /// settings, no rename prompts. The atlas output folder is always excluded on top of this
        /// list, without configuration, so the tool's own output can never feed back into its input.
        /// </summary>
        [SerializeField] private List<string> globalExcludedFolderPaths = new List<string>();

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
        public bool CollisionSafeAtlasKeys => collisionSafeAtlasKeys;
        public AtlasKeyCasing AtlasKeyCasing => atlasKeyCasing;
        public IReadOnlyList<string> GlobalExcludedFolderPaths => globalExcludedFolderPaths;
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

            // New projects get collision-safe and lowercased keys: there is no legacy atlas file name
            // to preserve, and both settings remove a class of cross-machine surprise.
            settings.collisionSafeAtlasKeys = true;
            settings.atlasKeyCasing = AtlasKeyCasing.Lower;

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
