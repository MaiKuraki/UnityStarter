using System;
using System.IO;

using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Creates explicit, discoverable runtime telemetry paths for packaged players and the Editor.
    /// </summary>
    public static class AssetRuntimeTelemetryPaths
    {
        public const string DEFAULT_FILE_NAME = "asset-runtime-telemetry.jsonl";
        public const string ROOT_FOLDER_NAME = "CycloneGames";
        public const string PACKAGE_FOLDER_NAME = "AssetManagement";
        public const string DIAGNOSTICS_FOLDER_NAME = "Diagnostics";

        public static string GetDefaultPersistentJsonLinesPath(string fileName = DEFAULT_FILE_NAME)
        {
            ValidateFileName(fileName);
            return Path.Combine(
                Application.persistentDataPath,
                ROOT_FOLDER_NAME,
                PACKAGE_FOLDER_NAME,
                DIAGNOSTICS_FOLDER_NAME,
                fileName);
        }

        private static void ValidateFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Telemetry file name cannot be null or empty.", nameof(fileName));
            }

            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || fileName.IndexOf('/') >= 0
                || fileName.IndexOf('\\') >= 0)
            {
                throw new ArgumentException("Telemetry file name must be a single file name without directory separators.", nameof(fileName));
            }
        }
    }
}
