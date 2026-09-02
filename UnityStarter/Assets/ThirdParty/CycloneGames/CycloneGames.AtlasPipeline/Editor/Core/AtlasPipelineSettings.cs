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

        /// <summary>
        /// Expands each sprite's colour into fully transparent neighbours before packing. Keeps edges
        /// from bleeding when the atlas is filtered, at the cost of a slightly larger atlas. Off only
        /// if the art already carries its own padding.
        /// </summary>
        [SerializeField] private bool enableAlphaDilation = true;

        [SerializeField] private int blockOffset = 1;
        [SerializeField] private bool includeInBuild = true;
        [SerializeField] private bool asciiOnlyNames = false;

        /// <summary>
        /// Includes the folder path in PerSprite atlas keys. Off by default so existing projects keep
        /// generating the same atlas file names — flipping it renames every PerSprite atlas, which
        /// breaks anything loading them by path.
        /// </summary>
        [SerializeField] private bool collisionSafeAtlasKeys = false;

        /// <summary>
        /// Splits an atlas that cannot fit its configured max size into multiple page files
        /// (`key__p000`, `key__p001`, ...). On by default, because the alternative is a build that
        /// fails the day a folder outgrows its atlas.
        /// This is safe to turn on: a single page is never renamed, so an atlas that already fits
        /// keeps its exact file name and only atlases that would otherwise have failed start
        /// producing pages.
        /// Existing projects keep whatever is serialized in their settings asset. Flipping a
        /// project's output layout during an upgrade is a decision rather than a migration, so
        /// validation names this switch at the moment an atlas actually overflows instead of
        /// changing it behind the project's back.
        /// Pages remain a last resort after folder structure: an atlas that needs many pages is
        /// reported, because it means the bucket really should have been split by rule.
        /// </summary>
        [SerializeField] private bool autoPageOverflowingAtlases = true;

        [SerializeField] private AtlasKeyCasing atlasKeyCasing = AtlasKeyCasing.Preserve;

        /// <summary>
        /// Folders the pipeline never touches, whatever the rules say: no atlas membership, no import
        /// settings, no rename prompts. The atlas output folder is always excluded on top of this
        /// list, without configuration, so the tool's own output can never feed back into its input.
        /// </summary>
        [SerializeField] private List<string> globalExcludedFolderPaths = new List<string>();

        [SerializeField] private List<AtlasImportRule> importRules = new List<AtlasImportRule>();

        /// <summary>
        /// Authoritative rule list. Each entry is its own asset, so two contributors editing two
        /// rules never touch the same file — the reason rules stopped being an inline array.
        /// </summary>
        [SerializeField] private List<AtlasRuleAsset> ruleAssets = new List<AtlasRuleAsset>();

        // Resolved view of the active rules, rebuilt on demand. Not serialized.
        [NonSerialized] private List<AtlasImportRule> _resolvedRules;

        public int SchemaVersion => schemaVersion;
        public bool AutoImport => autoImport;
        public bool AutoGenerateAtlases => autoGenerateAtlases;
        public string OutputAtlasFolder => outputAtlasFolder ?? DefaultOutputAtlasFolder;
        public int AtlasPadding => atlasPadding;
        public bool EnableRotation => enableRotation;
        public bool EnableTightPacking => enableTightPacking;
        public bool EnableAlphaDilation => enableAlphaDilation;
        public int BlockOffset => blockOffset;
        public bool IncludeInBuild => includeInBuild;
        public bool AsciiOnlyNames => asciiOnlyNames;
        public bool CollisionSafeAtlasKeys => collisionSafeAtlasKeys;
        public bool AutoPageOverflowingAtlases => autoPageOverflowingAtlases;
        public AtlasKeyCasing AtlasKeyCasing => atlasKeyCasing;
        public IReadOnlyList<string> GlobalExcludedFolderPaths => globalExcludedFolderPaths;

        /// <summary>Rule assets referenced by this project, in configuration order.</summary>
        public IReadOnlyList<AtlasRuleAsset> RuleAssets => ruleAssets;

        /// <summary>
        /// Appends a rule asset to the registered list. The caller owns the transaction: record the
        /// settings object with <c>Undo.RecordObject</c> before calling, and mark it dirty after —
        /// this mutator only touches the list and the resolved-rule cache.
        /// </summary>
        internal void RegisterRuleAsset(AtlasRuleAsset asset)
        {
            if (asset == null)
            {
                return;
            }

            if (ruleAssets == null)
            {
                ruleAssets = new List<AtlasRuleAsset>();
            }

            ruleAssets.Add(asset);
            _resolvedRules = null;
        }

        /// <summary>
        /// True when the settings asset still carries legacy inline rules and no rule assets: the
        /// state a project written before rule assets existed is in, and the migration source.
        /// </summary>
        public bool HasLegacyInlineRules =>
            (ruleAssets == null || ruleAssets.Count == 0)
            && importRules != null
            && importRules.Count > 0;

        /// <summary>
        /// The active rules, resolved from rule assets. Falls back to the legacy inline list while
        /// the migration has not produced assets, so a failed migration degrades to the previous
        /// behaviour instead of losing rules.
        /// </summary>
        public IReadOnlyList<AtlasImportRule> ImportRules
        {
            get
            {
                if (_resolvedRules != null)
                {
                    return _resolvedRules;
                }

                if (ruleAssets != null && ruleAssets.Count > 0)
                {
                    var resolved = new List<AtlasImportRule>(ruleAssets.Count);
                    for (int i = 0; i < ruleAssets.Count; i++)
                    {
                        AtlasImportRule rule = ruleAssets[i]?.Rule;
                        if (rule != null)
                        {
                            resolved.Add(rule);
                        }
                    }

                    _resolvedRules = resolved;
                }
                else
                {
                    _resolvedRules = importRules ?? new List<AtlasImportRule>();
                }

                return _resolvedRules;
            }
        }

        /// <summary>
        /// Replaces the rule list with rule assets and clears the legacy inline list. Called by the
        /// one-time migration, after every rule asset was written successfully.
        /// </summary>
        internal void AdoptRuleAssets(List<AtlasRuleAsset> assets)
        {
            ruleAssets = assets ?? new List<AtlasRuleAsset>();
            importRules = new List<AtlasImportRule>();
            _resolvedRules = null;
        }

        /// <summary>
        /// Unity calls this after inspector edits, which is exactly when the resolved-rule cache has
        /// to be dropped: the window edits rule assets through their own SerializedObjects, and a
        /// stale cache would keep feeding the pipeline the pre-edit rules.
        /// </summary>
        private void OnValidate()
        {
            _resolvedRules = null;
        }

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
            settings.enableAlphaDilation = true;
            settings.blockOffset = 1;

            // Baked by default: the safe baseline for the majority of projects (monolithic builds).
            // Hot-update projects get a warning in the pipeline window when an asset-management
            // system is detected together with this setting, because "this project is monolithic"
            // is not detectable while "this project uses YooAsset" is.
            settings.includeInBuild = true;

            // New projects get collision-safe and lowercased keys: there is no legacy atlas file name
            // to preserve, and both settings remove a class of cross-machine surprise.
            settings.collisionSafeAtlasKeys = true;
            settings.atlasKeyCasing = AtlasKeyCasing.Lower;
            settings.autoPageOverflowingAtlases = true;

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
