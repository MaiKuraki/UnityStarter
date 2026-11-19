using CycloneGames.AssetManagement.Editor;
using CycloneGames.UIFramework.Editor;

namespace CycloneGames.UIFramework.Editor.Integrations
{
    /// <summary>
    /// Bridges the UIFramework creator's provider-neutral location resolution to
    /// CycloneGames.AssetManagement, which delegates to the active provider.
    /// </summary>
    public sealed class AssetManagementUIWindowLocationResolver : IUIWindowLocationResolver
    {
        public string ResolveLocation(string assetGuid, string assetPath)
        {
            return AssetRefLocationResolverRegistry.Resolve(assetGuid, assetPath);
        }
    }
}
