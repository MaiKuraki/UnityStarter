using System.IO;
using System.Text;

using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    public static class AssetPatchJournalPaths
    {
        private const string DEFAULT_DIRECTORY = "CycloneGames/AssetManagement/PatchJournal";

        public static string GetDefaultJournalPath(string packageName)
        {
            string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(packageName) ? "Default" : packageName);
            return Path.Combine(Application.persistentDataPath, DEFAULT_DIRECTORY, safeName + ".json");
        }

        private static string SanitizeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                builder.Append(IsInvalid(c, invalid) ? '_' : c);
            }

            return builder.Length == 0 ? "Default" : builder.ToString();
        }

        private static bool IsInvalid(char value, char[] invalid)
        {
            for (int i = 0; i < invalid.Length; i++)
            {
                if (value == invalid[i])
                {
                    return true;
                }
            }

            return value == '/' || value == '\\';
        }
    }
}
