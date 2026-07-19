using System;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Provides borrowed, read-only payload memory. Returned memory remains valid only for the
    /// documented lifetime of the provider and must not outlive its owning scope.
    /// </summary>
    public interface IDataTableBytesProvider
    {
        ReadOnlyMemory<byte> GetBytes(string tableName);

        bool TryGetBytes(string tableName, out ReadOnlyMemory<byte> bytes);
    }
}
