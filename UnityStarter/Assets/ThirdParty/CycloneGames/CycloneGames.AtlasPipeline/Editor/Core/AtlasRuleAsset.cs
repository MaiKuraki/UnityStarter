using UnityEngine;

namespace CycloneGames.AtlasPipeline
{
    /// <summary>
    /// One import rule as its own asset.
    /// Rules used to live in a single YAML array inside AtlasPipelineSettings, which made every rule
    /// edit a conflict for every other contributor and made the settings asset a single point of
    /// failure. As separate assets, a feature team can own its rule file the way it owns its art
    /// folder, and two people editing two rules never touch the same file.
    /// The data itself is still <see cref="AtlasImportRule"/> — a plain serializable class with no
    /// Unity lifecycle — so the rule logic, the fingerprints and every existing unit test work on
    /// exactly the same objects as before.
    /// </summary>
    public sealed class AtlasRuleAsset : ScriptableObject
    {
        [SerializeField] private AtlasImportRule rule = new AtlasImportRule();

        /// <summary>
        /// The rule data. Never null after Unity deserialization, because the field is initialized;
        /// null only for an asset created by hand with no data, which validation reports.
        /// </summary>
        public AtlasImportRule Rule => rule;

        /// <summary>Human-readable name for list rows and asset labels.</summary>
        public string DisplayName => rule != null ? rule.Name : name;

        /// <summary>
        /// Takes ownership of a rule object. Used once, by the migration from the legacy inline list:
        /// the source list is cleared right after, so the instance is never shared.
        /// </summary>
        internal void Initialize(AtlasImportRule source)
        {
            rule = source;
            name = source != null ? source.Name : "AtlasRule";
        }

        private void Reset()
        {
            // Reset runs when the asset is created via the Create menu; the pipeline's own window is
            // the intended authoring surface, but a sensible default keeps a hand-made asset usable.
            name = "AtlasRule";
        }
    }
}
