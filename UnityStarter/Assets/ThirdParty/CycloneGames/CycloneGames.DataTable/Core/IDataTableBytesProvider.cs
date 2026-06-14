namespace CycloneGames.DataTable
{
    public interface IDataTableBytesProvider
    {
        byte[] GetBytes(string tableName);

        bool TryGetBytes(string tableName, out byte[] bytes);
    }
}
