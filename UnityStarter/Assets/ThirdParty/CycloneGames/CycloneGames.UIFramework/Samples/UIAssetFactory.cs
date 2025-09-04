using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.UIFramework.Runtime.Samples
{
    public class UIAssetFactory : IAssetPathBuilderFactory
    {
        public IAssetPathBuilder Create(string type)
        {
            switch (type)
            {
                case "UI":
                    return new UIAssetPathBuilder();
                default:
                    return null;
            }
        }
    }
    
    public class UIAssetPathBuilder : IAssetPathBuilder
    {
        public string GetAssetPath(string key)
        {
            return key;
        }
    }
}
