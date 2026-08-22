using CycloneGames.AssetManagement.Editor;
using CycloneGames.UIFramework.Editor;

namespace CycloneGames.UIFramework.Editor.Integrations
{
    /// <summary>
    /// Bridges the UIFramework creator's provider-neutral location resolution to CycloneGames.AssetManagement,
    /// which in turn delegates to the active provider (YooAsset, Addressables, ...). Keeping this in its own
    /// assembly lets the creator reference it without hard-coding a dependency on any asset package.
    /// </summary>
    public sealed class AssetManagementUIWindowLocationResolver : IUIWindowLocationResolver
    {
        public string ResolveLocation(string assetGuid, string assetPath)
        {
            return AssetRefLocationResolverRegistry.Resolve(assetGuid, assetPath);
        }
    }
}
