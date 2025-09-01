namespace CycloneGames.AssetManagement.Runtime
{
    public interface IAssetPathBuilderFactory
    {
        IAssetPathBuilder Create(string type);
    }
}