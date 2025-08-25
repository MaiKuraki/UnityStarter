using CycloneGames.AssetManagement;

namespace CycloneGames.UIFramework.Samples
{
    public class UIAssetFactory : IAssetPathBuilderFactory
    {
        public IAssetPathBuilder Create(string type)
        {
            switch (type)
            {
                case "UI":
                    return new CIGAGameJam25UIAssetPathBuilder();
                default:
                    return null;
            }
        }
    }
    
    public class CIGAGameJam25UIAssetPathBuilder : IAssetPathBuilder
    {
        public string GetAssetPath(string key)
        {
            return $"Assets/ThirdParty/CycloneGames/CycloneGames.UIFramework/Samples/{key}.asset";
        }
    }
}