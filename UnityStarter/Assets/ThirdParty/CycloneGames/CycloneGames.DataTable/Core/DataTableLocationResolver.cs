using System;

namespace CycloneGames.DataTable
{
    public sealed class DataTableLocationResolver : IDataTableLocationResolver
    {
        private readonly string _baseDirectory;
        private readonly string _dataExtension;
        private readonly DataTableLoadLimits _limits;

        public DataTableLocationResolver(
            string baseDirectory,
            string dataExtension = ".bytes",
            DataTableLoadLimits? limits = null)
        {
            _limits = limits ?? DataTableLoadLimits.Default;
            _limits.EnsureValid(nameof(limits));
            _baseDirectory = _limits.NormalizeLocation(baseDirectory);
            if (string.IsNullOrEmpty(_baseDirectory))
            {
                throw new ArgumentException("Base directory is null or empty.", nameof(baseDirectory));
            }

            _dataExtension = DataTableNameUtility.NormalizeDataExtension(dataExtension);
        }

        public string Resolve(string tableName)
        {
            string normalizedName = _limits.NormalizeTableName(tableName, _dataExtension);
            if (string.IsNullOrEmpty(normalizedName))
            {
                throw new ArgumentException("Table name is null or empty.", nameof(tableName));
            }

            int locationLength = checked(
                _baseDirectory.Length + 1 + normalizedName.Length + _dataExtension.Length);
            if (locationLength > _limits.MaxLocationLength)
            {
                throw new InvalidOperationException(
                    $"Resolved data-table location exceeds the configured limit. " +
                    $"Length={locationLength}, Limit={_limits.MaxLocationLength}.");
            }

            return _baseDirectory + "/" + normalizedName + _dataExtension;
        }
    }
}
