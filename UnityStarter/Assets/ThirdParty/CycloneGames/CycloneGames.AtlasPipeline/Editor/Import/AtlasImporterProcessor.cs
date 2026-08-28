using UnityEditor;

namespace CycloneGames.AtlasPipeline
{
    /// <summary>
    /// Applies Atlas import rules before a texture imports, then updates only the affected atlas
    /// keys from the postprocessor change set. No source texture is force-reimported from this
    /// class, which keeps imports single-pass and avoids the common AssetPostprocessor loop.
    /// </summary>
    public sealed class AtlasImporterProcessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            AtlasPipelineSettings settings = AtlasPipeline.TryGetSettings();
            if (settings == null || !settings.AutoImport)
            {
                return;
            }

            if (assetImporter is TextureImporter importer)
            {
                AtlasPipeline.ApplyImportSettings(importer, assetPath);
                AtlasPipeline.CheckTextureSize(assetPath);
            }
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            AtlasPipeline.HandleAssetChanges(
                importedAssets,
                deletedAssets,
                movedAssets,
                movedFromAssetPaths);
        }
    }
}
