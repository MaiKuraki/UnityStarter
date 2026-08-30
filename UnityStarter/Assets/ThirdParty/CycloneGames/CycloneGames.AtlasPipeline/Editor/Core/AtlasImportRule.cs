using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AtlasPipeline
{
    public enum AtlasSpriteMode
    {
        Single = 0,
        Multiple = 1,
    }

    public enum AtlasTextureFormat
    {
        Astc4x4 = 0,
        Astc5x5 = 1,
        Astc6x6 = 2,
        Astc8x8 = 3,
        Rgba32 = 4,
        Dxt1 = 5,
        Dxt5 = 6,
        Bc7 = 7,
    }

    public enum AtlasGranularity
    {
        None = 0,
        PerSourceFolder = 1,
        PerChildFolder = 2,
        PerSprite = 3,
    }

    public enum AtlasRotationMode
    {
        Inherit = 0,
        Enabled = 1,
        Disabled = 2,
    }

    /// <summary>
    /// How atlas keys — and therefore the generated .spriteatlasv2 file names — are cased.
    /// </summary>
    public enum AtlasKeyCasing
    {
        /// <summary>
        /// Keep whatever casing the source art and rule groups use. Atlas keys of two spellings
        /// differing only by case still converge on one bucket, but the output file name depends on
        /// which spelling wins, and that is not obvious from the rule configuration.
        /// </summary>
        Preserve = 0,

        /// <summary>
        /// Lowercase every atlas key. The output file name becomes predictable from the rule
        /// configuration alone, and two groups spelled "UI" and "ui" resolve to one file name instead
        /// of racing. Recommended for new projects; enabling it on an existing project renames every
        /// atlas file.
        /// </summary>
        Lower = 1,
    }

    /// <summary>
    /// One data-driven import and atlas rule. Rules are matched by normalized folder prefix and
    /// keep the importer/postprocessor free of per-project branching.
    /// </summary>
    [Serializable]
    public sealed class AtlasImportRule
    {
        [SerializeField] private string name = "Rule";
        [SerializeField] private string sourceFolder = string.Empty;
        [SerializeField] private string sourceFolderGuid = string.Empty;
        [SerializeField] private AtlasSpriteMode spriteMode = AtlasSpriteMode.Single;
        [SerializeField] private float pixelsPerUnit = 24f;
        [SerializeField] private AtlasTextureFormat androidFormat = AtlasTextureFormat.Astc6x6;
        [SerializeField] private AtlasTextureFormat iphoneFormat = AtlasTextureFormat.Astc6x6;
        [SerializeField] private AtlasTextureFormat webglFormat = AtlasTextureFormat.Astc6x6;
        [SerializeField] private AtlasTextureFormat standaloneFormat = AtlasTextureFormat.Bc7;
        [SerializeField] private bool pixelArt;
        [SerializeField] private bool mipmaps;
        [SerializeField] private bool readable;
        [SerializeField] private FilterMode filterMode = FilterMode.Bilinear;
        [SerializeField] private TextureWrapMode wrapMode = TextureWrapMode.Clamp;
        [Range(0, 100)]
        [SerializeField] private int compressionQuality = AtlasPlatformFormats.DefaultCompressionQuality;
        [SerializeField] private AtlasGranularity atlasGranularity = AtlasGranularity.PerSourceFolder;
        [SerializeField] private AtlasRotationMode atlasRotationMode = AtlasRotationMode.Inherit;
        [SerializeField] private string atlasGroup = "General";
        [SerializeField] private int recommendedMaxTextureSize = 2048;
        [SerializeField] private int atlasMaxTextureSize = 2048;
        [SerializeField] private bool warnTextureSize = true;
        [SerializeField] private List<string> pathKeywords = new List<string>();
        [SerializeField] private List<string> excludedFolderPaths = new List<string>();
        [SerializeField] private List<string> excludedNameKeywords = new List<string>();

        public string Name => string.IsNullOrWhiteSpace(name) ? "Rule" : name;
        public string SourceFolder => sourceFolder ?? string.Empty;
        public AtlasSpriteMode SpriteMode => spriteMode;
        public float PixelsPerUnit => pixelsPerUnit;
        public AtlasTextureFormat AndroidFormat => androidFormat;
        public AtlasTextureFormat IphoneFormat => iphoneFormat;
        public AtlasTextureFormat WebglFormat => webglFormat;
        public AtlasTextureFormat StandaloneFormat => standaloneFormat;
        public bool PixelArt => pixelArt;
        public bool Mipmaps => mipmaps;
        public bool Readable => readable;
        public FilterMode FilterMode => filterMode;
        public TextureWrapMode WrapMode => wrapMode;
        public int CompressionQuality => compressionQuality;
        public AtlasGranularity AtlasGranularity => atlasGranularity;
        public AtlasRotationMode AtlasRotationMode => atlasRotationMode;
        public string AtlasGroup => string.IsNullOrWhiteSpace(atlasGroup) ? "General" : atlasGroup;
        public int RecommendedMaxTextureSize => recommendedMaxTextureSize;
        public int AtlasMaxTextureSize => atlasMaxTextureSize;
        public bool WarnTextureSize => warnTextureSize;
        public IReadOnlyList<string> PathKeywords => pathKeywords;
        public IReadOnlyList<string> ExcludedFolderPaths => excludedFolderPaths;
        public IReadOnlyList<string> ExcludedNameKeywords => excludedNameKeywords;

        private string _resolvedSourceFolder;

        /// <summary>
        /// Position of this rule in the pipeline's resolved rule list, assigned every time that list
        /// is rebuilt. It is a cache key: atlas buckets store it so the rule that owns an atlas can be
        /// resolved by index instead of re-running path matching over every member of the atlas.
        /// Not serialized — it is derived from the owning settings asset on every load.
        /// </summary>
        internal int PipelineIndex { get; set; } = -1;

        public string NormalizedSourceFolder
        {
            get
            {
                if (_resolvedSourceFolder == null)
                {
                    _resolvedSourceFolder = ResolveSourceFolderNow();
                }

                return _resolvedSourceFolder;
            }
        }

        public string SourceFolderGuid => sourceFolderGuid ?? string.Empty;

        /// <summary>
        /// Resolves the source folder's current path. The folder GUID is resolved first, so a
        /// folder renamed inside Unity (its .meta GUID is stable) keeps the rule pointing at the
        /// new path. If the GUID is empty or stale (the folder and its meta were deleted), the
        /// historical path string is used as a fallback so validation reports a missing folder
        /// instead of silently failing to match.
        /// </summary>
        private string ResolveSourceFolderNow()
        {
            string resolved = sourceFolder ?? string.Empty;
            if (!string.IsNullOrEmpty(sourceFolderGuid))
            {
                string fromGuid = AssetDatabase.GUIDToAssetPath(sourceFolderGuid);
                if (!string.IsNullOrEmpty(fromGuid))
                {
                    resolved = fromGuid;
                }
            }

            return resolved.Replace('\\', '/').Trim().TrimEnd('/');
        }

        /// <summary>
        /// Clears the resolved-folder cache so the next access re-resolves the GUID.
        /// Called by the pipeline after settings load or a rescan.
        /// </summary>
        internal void RefreshResolvedFolder()
        {
            _resolvedSourceFolder = null;
        }

        /// <summary>
        /// Writes the current (GUID-resolved) path back into the serialized path field so the
        /// .asset stays clean after a folder rename. The GUID remains the authoritative reference.
        /// </summary>
        internal void UpdateSourceFolderPath(string resolvedPath)
        {
            sourceFolder = resolvedPath;
        }

        /// <summary>
        /// One-time migration for legacy rules that have a path but no GUID: resolves the GUID
        /// from the current path and caches it. Returns whether a value was written (callers use
        /// it to decide whether to mark the settings dirty).
        /// </summary>
        internal bool HealSourceFolderGuid()
        {
            if (!string.IsNullOrEmpty(sourceFolderGuid))
            {
                return false;
            }

            string path = sourceFolder;
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                return false;
            }

            sourceFolderGuid = guid;
            return true;
        }

        public bool IsValid
        {
            get
            {
                string folder = NormalizedSourceFolder;
                return !string.IsNullOrEmpty(folder)
                       && (string.Equals(folder, "Assets", StringComparison.Ordinal)
                           || folder.StartsWith("Assets/", StringComparison.Ordinal));
            }
        }

        public static AtlasImportRule Create(
            string name,
            string sourceFolder,
            AtlasTextureFormat androidFormat,
            AtlasTextureFormat iphoneFormat,
            AtlasGranularity atlasGranularity,
            string atlasGroup,
            AtlasTextureFormat webglFormat = AtlasTextureFormat.Astc6x6,
            AtlasTextureFormat standaloneFormat = AtlasTextureFormat.Bc7,
            bool pixelArt = false,
            AtlasRotationMode atlasRotationMode = AtlasRotationMode.Inherit,
            AtlasSpriteMode spriteMode = AtlasSpriteMode.Single,
            float pixelsPerUnit = 24f,
            bool mipmaps = false,
            bool readable = false,
            FilterMode filterMode = FilterMode.Bilinear,
            TextureWrapMode wrapMode = TextureWrapMode.Clamp,
            int compressionQuality = AtlasPlatformFormats.DefaultCompressionQuality,
            int recommendedMaxTextureSize = 2048,
            int atlasMaxTextureSize = 2048,
            bool warnTextureSize = true,
            IEnumerable<string> pathKeywords = null,
            IEnumerable<string> excludedFolderPaths = null,
            IEnumerable<string> excludedNameKeywords = null)
        {
            return new AtlasImportRule
            {
                name = name,
                sourceFolder = sourceFolder,
                androidFormat = androidFormat,
                iphoneFormat = iphoneFormat,
                webglFormat = webglFormat,
                standaloneFormat = standaloneFormat,
                pixelArt = pixelArt,
                atlasGranularity = atlasGranularity,
                atlasRotationMode = atlasRotationMode,
                atlasGroup = atlasGroup,
                spriteMode = spriteMode,
                pixelsPerUnit = pixelsPerUnit,
                mipmaps = mipmaps,
                readable = readable,
                filterMode = filterMode,
                wrapMode = wrapMode,
                compressionQuality = compressionQuality,
                recommendedMaxTextureSize = recommendedMaxTextureSize,
                atlasMaxTextureSize = atlasMaxTextureSize,
                warnTextureSize = warnTextureSize,
                pathKeywords = pathKeywords == null
                    ? new List<string>()
                    : new List<string>(pathKeywords),
                excludedFolderPaths = excludedFolderPaths == null
                    ? new List<string>()
                    : new List<string>(excludedFolderPaths),
                excludedNameKeywords = excludedNameKeywords == null
                    ? new List<string>()
                    : new List<string>(excludedNameKeywords),
            };
        }

        public bool ResolveAtlasRotation(bool globalEnableRotation)
        {
            // Pixel-art rules always disable rotation: rotated packing introduces non-integer
            // texel sampling, which produces heavy artifacts for pixel art. This overrides any
            // explicit setting.
            if (pixelArt)
            {
                return false;
            }

            switch (atlasRotationMode)
            {
                case AtlasRotationMode.Enabled:
                    return true;
                case AtlasRotationMode.Disabled:
                    return false;
                default:
                    return globalEnableRotation;
            }
        }

        public bool MatchesPath(string normalizedAssetPath)
        {
            if (string.IsNullOrEmpty(normalizedAssetPath) || !IsValid)
            {
                return false;
            }

            string folder = NormalizedSourceFolder;
            if (!string.Equals(normalizedAssetPath, folder, StringComparison.OrdinalIgnoreCase))
            {
                // Allocation-free prefix match: StartsWith(folder + "/") concatenated a new
                // string per asset per rule, which was the largest allocation source under
                // asset-count x rule-count. StartsWith(folder) plus a boundary check is equivalent.
                if (normalizedAssetPath.Length <= folder.Length
                    || !normalizedAssetPath.StartsWith(
                        folder,
                        StringComparison.OrdinalIgnoreCase)
                    || normalizedAssetPath[folder.Length] != '/')
                {
                    return false;
                }
            }

            if (pathKeywords.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < pathKeywords.Count; i++)
            {
                string keyword = pathKeywords[i]?.Trim();
                if (!string.IsNullOrEmpty(keyword)
                    && normalizedAssetPath.IndexOf(
                        keyword,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsPathExcluded(string normalizedAssetPath)
        {
            if (string.IsNullOrEmpty(normalizedAssetPath))
            {
                return true;
            }

            if (!MatchesPath(normalizedAssetPath))
            {
                return false;
            }

            for (int i = 0; i < excludedFolderPaths.Count; i++)
            {
                string excludedFolder = excludedFolderPaths[i]?
                    .Replace('\\', '/').Trim().TrimEnd('/') ?? string.Empty;
                if (string.IsNullOrEmpty(excludedFolder))
                {
                    continue;
                }

                if (string.Equals(
                        normalizedAssetPath,
                        excludedFolder,
                        StringComparison.OrdinalIgnoreCase)
                    || normalizedAssetPath.StartsWith(
                        excludedFolder + "/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            for (int i = 0; i < excludedNameKeywords.Count; i++)
            {
                string keyword = excludedNameKeywords[i]?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(keyword)
                    && normalizedAssetPath.IndexOf(
                        keyword,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
