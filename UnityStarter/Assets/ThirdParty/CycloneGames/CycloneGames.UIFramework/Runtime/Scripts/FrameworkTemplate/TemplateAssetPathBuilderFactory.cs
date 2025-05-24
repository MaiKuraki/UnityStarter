#if UNITY_EDITOR
using CycloneGames.Service;

namespace CycloneGames.UIFramework.Editor // Or a runtime namespace if used in builds
{
    /// <summary>
    /// This is an example implementation. You must define your own AssetPathBuilderFactory for your project,
    /// potentially loading configurations or using different strategies for path construction.
    /// </summary>
    public class TemplateAssetPathBuilderFactory : IAssetPathBuilderFactory
    {
        public IAssetPathBuilder Create(string type)
        {
            switch (type)
            {
                case "UI":
                    return new TemplateUIPathBuilder();
                // Add other types as needed, e.g., "Audio", "Characters"
                default:
                    UnityEngine.Debug.LogError($"[TemplateAssetPathBuilderFactory] Unknown asset path builder type requested: {type}");
                    return null;
            }
        }
    }
}
#endif