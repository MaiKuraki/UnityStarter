#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Stopwatch = System.Diagnostics.Stopwatch;

using UnityEditor;
using UnityEngine;

namespace CycloneGames.AssetManagement.Editor
{
    /// <summary>
    /// Validates serialized AssetRef and SceneRef GUID/location pairs without loading every asset.
    /// File reads are bounded and streaming. Runtime locations are reported but never rewritten because their
    /// syntax and rename policy belong to the selected provider and product content pipeline.
    /// </summary>
    public static class AssetRefValidator
    {
        private const int MAX_SCAN_WORKERS = 4;
        private const string GUID_MARKER = "m_GUID: ";
        private const string LOCATION_MARKER = "m_Location: ";

        private enum FieldKind : byte
        {
            None = 0,
            Guid = 1,
            Location = 2,
        }

        private readonly struct ParsedField
        {
            public readonly FieldKind Kind;
            public readonly int Indent;
            public readonly string Value;

            public ParsedField(FieldKind kind, int indent, string value)
            {
                Kind = kind;
                Indent = indent;
                Value = value;
            }
        }

        private readonly struct TextRef
        {
            public readonly int FileIndex;
            public readonly string Guid;
            public readonly string StoredLocation;

            public TextRef(int fileIndex, string guid, string storedLocation)
            {
                FileIndex = fileIndex;
                Guid = guid;
                StoredLocation = storedLocation;
            }
        }

        [MenuItem("Tools/CycloneGames/AssetManagement/Validate All AssetRefs")]
        public static void ValidateAll()
        {
            ValidateAllInternal();
        }

        private static void ValidateAllInternal()
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                EditorUtility.DisplayProgressBar(
                    "AssetRef Validation",
                    "Collecting serialized asset paths...",
                    0f);

                List<string> paths = CollectScannableAssetPaths();
                int totalFiles = paths.Count;
                if (totalFiles == 0)
                {
                    Debug.Log("[AssetRef Validation] No scannable assets found.");
                    return;
                }

                EditorUtility.DisplayProgressBar(
                    "AssetRef Validation",
                    $"Scanning {totalFiles} files...",
                    0.1f);

                var resultsPerFile = new List<TextRef>[totalFiles];
                var scanErrors = new string[totalFiles];
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(
                        1,
                        Math.Min(Environment.ProcessorCount, MAX_SCAN_WORKERS)),
                };

                Parallel.For(0, totalFiles, parallelOptions, fileIndex =>
                {
                    try
                    {
                        resultsPerFile[fileIndex] = ScanFile(
                            Path.GetFullPath(paths[fileIndex]),
                            fileIndex);
                    }
                    catch (Exception ex) when (IsExpectedFileReadFailure(ex))
                    {
                        scanErrors[fileIndex] = ex.Message;
                    }
                });

                int totalRefs = 0;
                int scanFailureCount = 0;
                for (int i = 0; i < totalFiles; i++)
                {
                    totalRefs += resultsPerFile[i]?.Count ?? 0;
                    if (!string.IsNullOrEmpty(scanErrors[i]))
                    {
                        scanFailureCount++;
                    }
                }

                if (totalRefs == 0)
                {
                    stopwatch.Stop();
                    LogScanFailures(paths, scanErrors);
                    LogSummary(
                        brokenCount: 0,
                        emptyLocationCount: 0,
                        scanFailureCount,
                        totalFiles,
                        totalRefs,
                        stopwatch.ElapsedMilliseconds);
                    return;
                }

                var allRefs = new TextRef[totalRefs];
                int targetIndex = 0;
                for (int i = 0; i < totalFiles; i++)
                {
                    List<TextRef> fileResults = resultsPerFile[i];
                    int count = fileResults?.Count ?? 0;
                    for (int j = 0; j < count; j++)
                    {
                        allRefs[targetIndex++] = fileResults[j];
                    }
                }

                EditorUtility.DisplayProgressBar(
                    "AssetRef Validation",
                    $"Resolving {totalRefs} GUIDs...",
                    0.7f);

                int brokenCount = 0;
                int emptyLocationCount = 0;
                var errors = new List<string>(Math.Max(16, scanFailureCount));
                for (int i = 0; i < totalRefs; i++)
                {
                    TextRef reference = allRefs[i];
                    string resolvedPath = AssetDatabase.GUIDToAssetPath(reference.Guid);
                    if (string.IsNullOrEmpty(resolvedPath))
                    {
                        brokenCount++;
                        errors.Add(
                            $"[BROKEN] {paths[reference.FileIndex]} -> missing GUID: {reference.Guid}");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(reference.StoredLocation))
                    {
                        continue;
                    }

                    emptyLocationCount++;
                    errors.Add(
                        $"[EMPTY LOCATION] {paths[reference.FileIndex]} -> GUID {reference.Guid} resolves to {resolvedPath}, but no provider runtime location is stored.");
                }

                LogScanFailures(paths, scanErrors, errors);
                for (int i = 0; i < errors.Count; i++)
                {
                    Debug.LogError(errors[i]);
                }

                stopwatch.Stop();
                LogSummary(
                    brokenCount,
                    emptyLocationCount,
                    scanFailureCount,
                    totalFiles,
                    totalRefs,
                    stopwatch.ElapsedMilliseconds);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static List<string> CollectScannableAssetPaths()
        {
            string[] allPaths = AssetDatabase.GetAllAssetPaths();
            var paths = new List<string>(Math.Max(16, allPaths.Length / 4));
            for (int i = 0; i < allPaths.Length; i++)
            {
                string path = allPaths[i];
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    !IsScannableAssetPath(path))
                {
                    continue;
                }

                paths.Add(path);
            }

            return paths;
        }

        private static bool IsScannableAssetPath(string path)
        {
            return path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".overrideController", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".playable", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".signal", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".lighting", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".mask", StringComparison.OrdinalIgnoreCase);
        }

        private static List<TextRef> ScanFile(string fullPath, int fileIndex)
        {
            List<TextRef> results = null;
            ParsedField previous = default;
            using (var stream = new FileStream(
                       fullPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete,
                       bufferSize: 16 * 1024,
                       useAsync: false))
            using (var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    ParsedField current = ParseField(line);
                    if (current.Kind != FieldKind.None &&
                        previous.Kind != FieldKind.None &&
                        current.Kind != previous.Kind &&
                        current.Indent == previous.Indent)
                    {
                        string guid = current.Kind == FieldKind.Guid ? current.Value : previous.Value;
                        string location = current.Kind == FieldKind.Location ? current.Value : previous.Value;
                        if (IsUnityGuid(guid))
                        {
                            results ??= new List<TextRef>(4);
                            results.Add(new TextRef(fileIndex, guid, location));
                        }
                    }

                    previous = current;
                }
            }

            return results;
        }

        private static ParsedField ParseField(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return default;
            }

            int firstContent = 0;
            while (firstContent < line.Length && line[firstContent] == ' ')
            {
                firstContent++;
            }

            if (MatchesMarker(line, firstContent, GUID_MARKER))
            {
                return new ParsedField(
                    FieldKind.Guid,
                    firstContent,
                    line.Substring(firstContent + GUID_MARKER.Length).TrimEnd());
            }

            if (MatchesMarker(line, firstContent, LOCATION_MARKER))
            {
                return new ParsedField(
                    FieldKind.Location,
                    firstContent,
                    line.Substring(firstContent + LOCATION_MARKER.Length).TrimEnd());
            }

            return default;
        }

        private static bool MatchesMarker(string line, int startIndex, string marker)
        {
            return line.Length - startIndex >= marker.Length &&
                   string.CompareOrdinal(line, startIndex, marker, 0, marker.Length) == 0;
        }

        private static bool IsUnityGuid(string value)
        {
            if (value == null || value.Length != 32)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool isHex = (character >= '0' && character <= '9') ||
                             (character >= 'a' && character <= 'f') ||
                             (character >= 'A' && character <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsExpectedFileReadFailure(Exception exception)
        {
            return exception is IOException ||
                   exception is UnauthorizedAccessException ||
                   exception is NotSupportedException ||
                   exception is ArgumentException;
        }

        private static void LogScanFailures(
            IReadOnlyList<string> paths,
            IReadOnlyList<string> scanErrors,
            List<string> destination = null)
        {
            for (int i = 0; i < scanErrors.Count; i++)
            {
                string error = scanErrors[i];
                if (string.IsNullOrEmpty(error))
                {
                    continue;
                }

                string message = $"[UNREADABLE] {paths[i]} -> {error}";
                if (destination == null)
                {
                    Debug.LogError(message);
                }
                else
                {
                    destination.Add(message);
                }
            }
        }

        private static void LogSummary(
            int brokenCount,
            int emptyLocationCount,
            int scanFailureCount,
            int totalFiles,
            int totalRefs,
            long elapsedMilliseconds)
        {
            string summary =
                $"{totalFiles} files, {totalRefs} refs, {brokenCount} broken, " +
                $"{emptyLocationCount} empty runtime locations, {scanFailureCount} unreadable " +
                $"({elapsedMilliseconds} ms).";

            if (brokenCount > 0 || emptyLocationCount > 0 || scanFailureCount > 0)
            {
                Debug.LogError($"[AssetRef Validation] {summary}");
            }
            else
            {
                Debug.Log($"[AssetRef Validation] {summary}");
            }
        }
    }
}
#endif
