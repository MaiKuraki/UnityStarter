namespace CycloneGames.Service
{
    public interface IAssetPathBuilderFactory
    {
        IAssetPathBuilder Create(string type);
    }
}