using System;
using System.Collections.Generic;

namespace CycloneGames.DataTable
{
    public sealed class DataTableBytesCache : IDataTableBytesProvider
    {
        private readonly Dictionary<string, byte[]> _bytesByTableName;
        private readonly string _dataExtension;

        public DataTableBytesCache(int capacity = 8, string dataExtension = ".bytes")
        {
            _bytesByTableName = new Dictionary<string, byte[]>(Math.Max(0, capacity), StringComparer.Ordinal);
            _dataExtension = DataTableNameUtility.NormalizeDataExtension(dataExtension);
        }

        public int Count => _bytesByTableName.Count;

        public void Add(string tableName, byte[] bytes)
        {
            string normalizedName = Normalize(tableName);
            if (string.IsNullOrEmpty(normalizedName))
            {
                throw new ArgumentException("Table name is null or empty.", nameof(tableName));
            }

            if (bytes == null || bytes.Length == 0)
            {
                throw new ArgumentException($"Table bytes are null or empty. Table={normalizedName}", nameof(bytes));
            }

            _bytesByTableName.Add(normalizedName, bytes);
        }

        public void Set(string tableName, byte[] bytes)
        {
            string normalizedName = Normalize(tableName);
            if (string.IsNullOrEmpty(normalizedName))
            {
                throw new ArgumentException("Table name is null or empty.", nameof(tableName));
            }

            if (bytes == null || bytes.Length == 0)
            {
                throw new ArgumentException($"Table bytes are null or empty. Table={normalizedName}", nameof(bytes));
            }

            _bytesByTableName[normalizedName] = bytes;
        }

        public byte[] GetBytes(string tableName)
        {
            string normalizedName = Normalize(tableName);
            if (_bytesByTableName.TryGetValue(normalizedName, out byte[] bytes))
            {
                return bytes;
            }

            throw new KeyNotFoundException($"Data table bytes are not loaded: {normalizedName}");
        }

        public bool TryGetBytes(string tableName, out byte[] bytes)
        {
            return _bytesByTableName.TryGetValue(Normalize(tableName), out bytes);
        }

        public void Clear()
        {
            _bytesByTableName.Clear();
        }

        private string Normalize(string tableName)
        {
            return DataTableNameUtility.NormalizeTableName(tableName, _dataExtension);
        }
    }
}
