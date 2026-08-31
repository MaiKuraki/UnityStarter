using System;
using System.Collections.Generic;
using CycloneGames.AtlasPipeline.Pure;
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

        // Legacy-mobile fallbacks, appended when the ASTC-only list turned out to be unusable for
        // projects whose minimum device spec predates ASTC. ASTC needs OpenGL ES 3.1 / Vulkan on
        // Android and an A8 GPU or later on iOS; devices below that must fall back to ETC2 and PVRTC
        // respectively. Before these existed, RGBA32 was the only non-ASTC option on mobile, and an
        // uncompressed 2048px atlas costs 16 MB of VRAM against 0.5 MB for ETC2 RGBA8.
        //
        // The numeric values are serialized into AtlasPipelineSettings assets. Never renumber them;
        // only append.
        Etc2Rgba8 = 8,
        Etc2Rgb4 = 9,
        PvrtcRgba4 = 10,
        PvrtcRgb4 = 11,
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
    /// Tri-state override for atlas-level toggles that have a global default: inherit the global
    /// value, force it on, or force it off.
    /// Used for include-in-build (a project mixing installer-baked bootstrap UI with hot-updated
    /// art needs per-rule control) and for alpha dilation (pixel-art rules may want it off while
    /// normal filtered UI keeps it on).
    /// </summary>
    public enum AtlasToggleOverride
    {
        Inherit = 0,
        ForceOn = 1,
        ForceOff = 2,
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

        // Per-platform atlas texture size overrides. Zero means "inherit atlasMaxTextureSize", which
        // is both the migration-safe default for assets written before these fields existed and the
        // least surprising authoring model: the single "Atlas Max" field stays authoritative until a
        // platform is overridden explicitly.
        // This is the cheapest low-end lever available — a smaller atlas on Android costs nothing in
        // package size, unlike shipping a second resolution — but it is a quality lever, not a
        // capacity lever: halving the atlas size makes the same content four times less likely to
        // fit, so it has to be paired with paging.
        [SerializeField] private int androidAtlasMaxSize;
        [SerializeField] private int iphoneAtlasMaxSize;
        [SerializeField] private int webglAtlasMaxSize;
        [SerializeField] private int standaloneAtlasMaxSize;

        // Tri-state overrides for atlas-level toggles. Inherit (0) is both the migration-safe
        // default for assets written before these fields existed and the least surprising authoring
        // model: the global switch stays authoritative until a rule overrides it.
        [SerializeField] private AtlasToggleOverride includeInBuildOverride;
        [SerializeField] private AtlasToggleOverride alphaDilationOverride;
        [SerializeField] private bool warnTextureSize = true;
        [SerializeField] private List<string> pathKeywords = new List<string>();
        [SerializeField] private List<string> excludedFolderPaths = new List<string>();
        [SerializeField] private List<string> excludedNameKeywords = new List<string>();

        // Where this rule's atlases are written, relative to the project-wide output folder. Empty
        // means the output root itself, which is the behaviour every project had before this field
        // existed. Several rules naming the same subfolder share one package — that is how a project
        // splits its atlases across asset packages without giving up the single output root the
        // exclusion test and the orphan sweep depend on.
        [SerializeField] private string outputSubfolder = string.Empty;

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
        public int AndroidAtlasMaxSize => androidAtlasMaxSize;
        public int IphoneAtlasMaxSize => iphoneAtlasMaxSize;
        public int WebglAtlasMaxSize => webglAtlasMaxSize;
        public int StandaloneAtlasMaxSize => standaloneAtlasMaxSize;
        public AtlasToggleOverride IncludeInBuildOverride => includeInBuildOverride;
        public AtlasToggleOverride AlphaDilationOverride => alphaDilationOverride;

        /// <summary>
        /// Folder this rule writes into, relative to the output root; empty for the root itself.
        /// Always the sanitized form, so the value the generator builds paths from and the value the
        /// fingerprint is computed from can never disagree — an unsanitized read here would make the
        /// recorded fingerprint describe a path that was never written.
        /// </summary>
        public string OutputSubfolder =>
            _resolvedOutputSubfolder ??= AtlasPathUtility.SanitizeSubfolder(outputSubfolder);

        private string _resolvedOutputSubfolder;

        /// <summary>
        /// Resolves a tri-state override against the global default. Kept in one place so the writer
        /// and the "has the configuration changed" comparison resolve it identically.
        /// </summary>
        public static bool ResolveToggle(
            AtlasToggleOverride overrideValue,
            bool globalDefault)
        {
            switch (overrideValue)
            {
                case AtlasToggleOverride.ForceOn:
                    return true;
                case AtlasToggleOverride.ForceOff:
                    return false;
                default:
                    return globalDefault;
            }
        }

        /// <summary>
        /// Whether this atlas is baked into the player build. See the includeInBuildOverride field:
        /// a hot-updated project forces this off for rules whose atlases ship in asset packages,
        /// while bootstrap UI baked into the installer keeps it on.
        /// </summary>
        public bool ResolveIncludeInBuild(bool globalDefault)
        {
            return ResolveToggle(includeInBuildOverride, globalDefault);
        }

        /// <summary>
        /// Whether packing dilates each sprite's edge colour into the padding. See the
        /// alphaDilationOverride field.
        /// </summary>
        /// <summary>
        /// Whether packing dilates each sprite's edge colour into the padding. Force On / Force Off
        /// always win — useful when a project genuinely needs dilation on a pixel-art rule or off a
        /// filtered-UI rule. Inherit uses the smart default: pixel-art rules get dilation off
        /// (point filtering does not sample the padding, so dilation adds nothing for pixel art);
        /// non-pixel-art rules follow the global setting. The smart default matters in mixed projects:
        /// without it, setting the global to On would also turn dilation on for pixel-art atlases,
        /// which is at best pointless and at worst amplifies a dirty edge into a visible halo.
        /// </summary>
        public bool ResolveAlphaDilation(bool globalDefault)
        {
            if (alphaDilationOverride == AtlasToggleOverride.ForceOn)
            {
                return true;
            }

            if (alphaDilationOverride == AtlasToggleOverride.ForceOff)
            {
                return false;
            }

            return pixelArt ? false : globalDefault;
        }

        /// <summary>
        /// Atlas texture size for one platform. A non-positive override means "inherit", so projects
        /// configured before per-platform sizes existed keep behaving exactly as they did.
        /// </summary>
        public int GetAtlasMaxTextureSize(AtlasPlatform platform)
        {
            int overrideValue;
            switch (platform)
            {
                case AtlasPlatform.Android:
                    overrideValue = androidAtlasMaxSize;
                    break;
                case AtlasPlatform.Iphone:
                    overrideValue = iphoneAtlasMaxSize;
                    break;
                case AtlasPlatform.Webgl:
                    overrideValue = webglAtlasMaxSize;
                    break;
                case AtlasPlatform.Standalone:
                    overrideValue = standaloneAtlasMaxSize;
                    break;
                default:
                    overrideValue = 0;
                    break;
            }

            return overrideValue > 0 ? overrideValue : atlasMaxTextureSize;
        }
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
        /// Clears the resolved caches so the next access re-resolves the source folder GUID and
        /// re-sanitizes the output subfolder. Called by the pipeline after settings load or a
        /// rescan — a cached subfolder read from a rule edited in the inspector would otherwise
        /// keep pointing at the old package.
        /// </summary>
        internal void RefreshResolvedFolder()
        {
            _resolvedSourceFolder = null;
            _resolvedOutputSubfolder = null;
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

            // Zero means "inherit atlasMaxTextureSize"; see the field declarations.
            int androidAtlasMaxSize = 0,
            int iphoneAtlasMaxSize = 0,
            int webglAtlasMaxSize = 0,
            int standaloneAtlasMaxSize = 0,
            AtlasToggleOverride includeInBuildOverride = AtlasToggleOverride.Inherit,
            AtlasToggleOverride alphaDilationOverride = AtlasToggleOverride.Inherit,
            bool warnTextureSize = true,
            IEnumerable<string> pathKeywords = null,
            IEnumerable<string> excludedFolderPaths = null,
            IEnumerable<string> excludedNameKeywords = null,
            string outputSubfolder = null)
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
                androidAtlasMaxSize = androidAtlasMaxSize,
                iphoneAtlasMaxSize = iphoneAtlasMaxSize,
                webglAtlasMaxSize = webglAtlasMaxSize,
                standaloneAtlasMaxSize = standaloneAtlasMaxSize,
                includeInBuildOverride = includeInBuildOverride,
                alphaDilationOverride = alphaDilationOverride,
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
                outputSubfolder = outputSubfolder ?? string.Empty,
            };
        }

        public bool ResolveAtlasRotation(bool globalEnableRotation)
        {
            // Pixel art is a hard block, not a default. Rotated packing samples at non-integer
            // texels, which turns crisp pixel art into a shimmering mess — there is no project that
            // wants that, so Enabled is not an escape hatch here the way it is for alpha dilation.
            // Note the deliberate asymmetry with ResolveAlphaDilation: dilation on pixel art is
            // merely pointless (nothing samples the padding), so Force On may still opt in there,
            // while rotation actively destroys the art and may not be opted into at all.
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

        /// <summary>
        /// True when this rule owns the path: it falls under the rule's folder and keywords, and is
        /// not excluded. This is the question every real caller asks, and asking it through
        /// <see cref="MatchesPath"/> plus <see cref="IsPathExcluded"/> evaluated the folder and
        /// keyword match twice per asset per rule — once by the caller, once inside the exclusion
        /// check's own guard.
        /// </summary>
        public bool OwnsPath(string normalizedAssetPath)
        {
            return MatchesPath(normalizedAssetPath)
                   && !IsExcludedWithinMatch(normalizedAssetPath);
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

            return IsExcludedWithinMatch(normalizedAssetPath);
        }

        /// <summary>
        /// The exclusion lists, evaluated for a path this rule is already known to match. Callers
        /// must have established the match first; <see cref="OwnsPath"/> is the combined form.
        /// </summary>
        private bool IsExcludedWithinMatch(string normalizedAssetPath)
        {
            for (int i = 0; i < excludedFolderPaths.Count; i++)
            {
                string excludedFolder = excludedFolderPaths[i]?
                    .Replace('\\', '/').Trim().TrimEnd('/') ?? string.Empty;
                if (string.IsNullOrEmpty(excludedFolder))
                {
                    continue;
                }

                // Allocation-free prefix match, the same shape as MatchesPath. Concatenating
                // excludedFolder + "/" built a throwaway string per asset per rule per entry, which
                // measured as 152 bytes per asset on a full rescan — the only remaining allocation
                // in the rule-matching path.
                if (string.Equals(
                        normalizedAssetPath,
                        excludedFolder,
                        StringComparison.OrdinalIgnoreCase)
                    || (normalizedAssetPath.Length > excludedFolder.Length
                        && normalizedAssetPath.StartsWith(
                            excludedFolder,
                            StringComparison.OrdinalIgnoreCase)
                        && normalizedAssetPath[excludedFolder.Length] == '/'))
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
