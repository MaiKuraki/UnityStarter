namespace CycloneGames.AssetManagement
{
    public interface IAssetPathBuilderFactory
    {
        IAssetPathBuilder Create(string type);
    }
}