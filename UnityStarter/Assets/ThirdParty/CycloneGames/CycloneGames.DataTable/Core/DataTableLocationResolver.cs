using System;

namespace CycloneGames.DataTable
{
    public sealed class DataTableLocationResolver : IDataTableLocationResolver
    {
        private readonly string _baseDirectory;
        private readonly string _dataExtension;

        public DataTableLocationResolver(string baseDirectory, string dataExtension = ".bytes")
        {
            _baseDirectory = DataTableNameUtility.NormalizePath(baseDirectory);
            if (string.IsNullOrEmpty(_baseDirectory))
            {
                throw new ArgumentException("Base directory is null or empty.", nameof(baseDirectory));
            }

            _dataExtension = DataTableNameUtility.NormalizeDataExtension(dataExtension);
        }

        public string Resolve(string tableName)
        {
            string normalizedName = DataTableNameUtility.NormalizeTableName(tableName, _dataExtension);
            if (string.IsNullOrEmpty(normalizedName))
            {
                throw new ArgumentException("Table name is null or empty.", nameof(tableName));
            }

            return _baseDirectory + "/" + normalizedName + _dataExtension;
        }
    }
}
