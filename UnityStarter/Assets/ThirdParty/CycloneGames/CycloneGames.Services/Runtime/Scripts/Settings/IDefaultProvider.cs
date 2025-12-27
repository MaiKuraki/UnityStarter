namespace CycloneGames.Service.Runtime
{
    public interface IDefaultProvider<T> where T : struct
    {
        T GetDefault();
    }
}