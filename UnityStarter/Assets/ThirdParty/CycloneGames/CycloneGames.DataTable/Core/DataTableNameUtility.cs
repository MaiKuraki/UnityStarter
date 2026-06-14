using System;

namespace CycloneGames.DataTable
{
    public static class DataTableNameUtility
    {
        public static string NormalizeTableName(string tableName, string dataExtension = ".bytes")
        {
            string normalizedExtension = NormalizeDataExtension(dataExtension);
            string normalized = NormalizePath(tableName);
            if (!string.IsNullOrEmpty(normalizedExtension) &&
                normalized.EndsWith(normalizedExtension, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - normalizedExtension.Length);
            }

            return normalized;
        }

        public static string NormalizeDataExtension(string dataExtension)
        {
            if (string.IsNullOrWhiteSpace(dataExtension))
            {
                return string.Empty;
            }

            string normalizedExtension = dataExtension.Trim();
            return normalizedExtension.StartsWith(".", StringComparison.Ordinal)
                ? normalizedExtension
                : "." + normalizedExtension;
        }

        public static string NormalizePath(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().Trim('/', '\\').Replace('\\', '/');
        }
    }
}
