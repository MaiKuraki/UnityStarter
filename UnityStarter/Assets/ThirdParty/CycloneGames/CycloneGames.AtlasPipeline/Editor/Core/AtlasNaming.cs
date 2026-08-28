using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using CycloneGames.Logging;

namespace CycloneGames.AtlasPipeline
{
    public sealed class AtlasRenameRequest
    {
        public AtlasRenameRequest(
            string assetPath,
            string currentFileName,
            string suggestedFileName,
            string reason)
        {
            AssetPath = assetPath ?? string.Empty;
            CurrentFileName = currentFileName ?? string.Empty;
            SuggestedFileName = suggestedFileName ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        public string AssetPath { get; }
        public string CurrentFileName { get; }
        public string SuggestedFileName { get; }
        public string Reason { get; }
        public bool Selected { get; set; } = true;
    }

    public readonly struct AtlasRenameResult
    {
        public AtlasRenameResult(
            int renamedCount,
            IReadOnlyList<string> renamedEntries,
            IReadOnlyList<string> failures)
        {
            RenamedCount = renamedCount;
            RenamedEntries = renamedEntries ?? Array.Empty<string>();
            Failures = failures ?? Array.Empty<string>();
        }

        public int RenamedCount { get; }
        public IReadOnlyList<string> RenamedEntries { get; }
        public IReadOnlyList<string> Failures { get; }
    }

    /// <summary>
    /// Portable atlas source naming policy and rename workflow. It is deliberately conservative:
    /// file stems must be non-empty, short, free of whitespace/control characters/Unity-unfriendly
    /// punctuation, and must not collide with reserved Windows device names.
    /// </summary>
    public static class AtlasNaming
    {
        private const int MaximumStemLength = 100;

        /// <summary>
        /// When true, only ASCII letters/digits/underscore/hyphen are allowed. Defaults to false
        /// (Unicode letters such as CJK are accepted) to preserve historical behavior. The pipeline
        /// writes this flag after loading settings. When tightened, files with non-ASCII names flow
        /// through the existing invalid-name prompt, review window, and build validation path.
        /// </summary>
        public static bool AsciiOnlyNames { get; set; }

        private static readonly HashSet<string> ReservedWindowsNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CON",
                "PRN",
                "AUX",
                "NUL",
                "COM1",
                "COM2",
                "COM3",
                "COM4",
                "COM5",
                "COM6",
                "COM7",
                "COM8",
                "COM9",
                "LPT1",
                "LPT2",
                "LPT3",
                "LPT4",
                "LPT5",
                "LPT6",
                "LPT7",
                "LPT8",
                "LPT9",
            };

        public static bool IsValidAtlasFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            string stem = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(stem)
                || stem.Length > MaximumStemLength
                || !string.Equals(stem, stem.Trim(), StringComparison.Ordinal)
                || stem.StartsWith(".", StringComparison.Ordinal)
                || IsReservedWindowsName(stem))
            {
                return false;
            }

            for (int i = 0; i < stem.Length; i++)
            {
                if (!IsSafeNameCharacter(stem[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsValidAtlasAssetPath(string assetPath)
        {
            return IsValidAtlasFileName(Path.GetFileName(assetPath));
        }

        public static bool TrySuggestSafeFileName(string fileName, out string safeFileName)
        {
            safeFileName = string.Empty;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            string extension = Path.GetExtension(fileName);
            string stem = Path.GetFileNameWithoutExtension(fileName);
            var builder = new StringBuilder(stem.Length);
            bool previousWasSeparator = false;

            for (int i = 0; i < stem.Length; i++)
            {
                char c = stem[i];
                if (IsSafeNameCharacter(c))
                {
                    builder.Append(c);
                    previousWasSeparator = false;
                    continue;
                }

                if (!previousWasSeparator)
                {
                    builder.Append('_');
                    previousWasSeparator = true;
                }
            }

            string safeStem = builder.ToString().Trim('_', '-', '.');
            if (string.IsNullOrEmpty(safeStem))
            {
                safeStem = "Sprite";
            }

            if (IsReservedWindowsName(safeStem))
            {
                safeStem += "_";
            }

            if (safeStem.Length > MaximumStemLength)
            {
                safeStem = safeStem.Substring(0, MaximumStemLength).TrimEnd('_', '-');
                if (string.IsNullOrEmpty(safeStem))
                {
                    safeStem = "Sprite";
                }
            }

            safeFileName = safeStem + extension;
            return true;
        }

        public static List<AtlasRenameRequest> CollectInvalidAtlasNames(
            AtlasPipelineSettings settings)
        {
            var requests = new List<AtlasRenameRequest>();
            if (settings == null)
            {
                return requests;
            }

            IReadOnlyList<AtlasImportRule> importRules = settings.ImportRules;
            if (importRules == null)
            {
                return requests;
            }

            var allAssetPaths = new List<string>();
            var existingAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visitedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < importRules.Count; i++)
            {
                AtlasImportRule rule = importRules[i];
                string folder = rule.NormalizedSourceFolder;
                if (string.IsNullOrEmpty(folder)
                    || !AssetDatabase.IsValidFolder(folder)
                    || !visitedFolders.Add(folder))
                {
                    continue;
                }

                string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
                for (int g = 0; g < guids.Length; g++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[g]).Replace('\\', '/');
                    if (!IsSupportedImagePath(path))
                    {
                        continue;
                    }

                    // The scan must match ResolveRule: MatchesPath && !IsPathExcluded &&
                    // granularity != None. Checking only IsPathExcluded would pull in files this
                    // rule does not govern (IsPathExcluded returns false for non-matching paths),
                    // producing false positives that block the build.
                    if (!rule.MatchesPath(path) || rule.IsPathExcluded(path))
                    {
                        continue;
                    }

                    if (rule.AtlasGranularity == AtlasGranularity.None)
                    {
                        continue;
                    }

                    allAssetPaths.Add(path);
                    existingAssetPaths.Add(path);
                }
            }

            allAssetPaths.Sort(StringComparer.Ordinal);
            var usedTargetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < allAssetPaths.Count; i++)
            {
                string assetPath = allAssetPaths[i];
                string currentFileName = Path.GetFileName(assetPath);
                if (IsValidAtlasFileName(currentFileName))
                {
                    continue;
                }

                if (!TrySuggestSafeFileName(currentFileName, out string safeFileName))
                {
                    continue;
                }

                string targetFileName = MakeUniqueTargetFileName(
                    assetPath,
                    safeFileName,
                    existingAssetPaths,
                    usedTargetPaths);

                requests.Add(new AtlasRenameRequest(
                    assetPath,
                    currentFileName,
                    targetFileName,
                    "File name contains whitespace, reserved words, or non-portable characters."));
            }

            return requests;
        }

        public static AtlasRenameResult ApplyRenames(
            IReadOnlyList<AtlasRenameRequest> requests)
        {
            var renamedEntries = new List<string>();
            var failures = new List<string>();
            if (requests == null)
            {
                return new AtlasRenameResult(0, renamedEntries, failures);
            }

            for (int i = 0; i < requests.Count; i++)
            {
                AtlasRenameRequest request = requests[i];
                if (request == null || !request.Selected)
                {
                    continue;
                }

                if (!IsSupportedImagePath(request.AssetPath)
                    || string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(request.AssetPath)))
                {
                    failures.Add(
                        $"{request.CurrentFileName}: source asset is no longer available.");
                    continue;
                }

                string directory = Path.GetDirectoryName(request.AssetPath)
                    ?.Replace('\\', '/') ?? string.Empty;
                string targetPath = directory + "/" + request.SuggestedFileName;
                if (string.Equals(request.AssetPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(targetPath)))
                {
                    failures.Add(
                        $"{request.CurrentFileName}: target name '{request.SuggestedFileName}' already exists.");
                    continue;
                }

                string error = AssetDatabase.RenameAsset(
                    request.AssetPath,
                    request.SuggestedFileName);
                if (!string.IsNullOrEmpty(error))
                {
                    failures.Add($"{request.CurrentFileName}: {error}");
                }
                else
                {
                    renamedEntries.Add(
                        $"{request.CurrentFileName} -> {request.SuggestedFileName}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return new AtlasRenameResult(
                renamedEntries.Count,
                renamedEntries,
                failures);
        }

        public static string BuildPreview(
            IReadOnlyList<AtlasRenameRequest> requests,
            int maxEntries = 12)
        {
            if (requests == null || requests.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            int shownCount = Math.Min(requests.Count, maxEntries);
            for (int i = 0; i < shownCount; i++)
            {
                builder.Append("• ");
                builder.Append(requests[i].CurrentFileName);
                builder.Append("  ->  ");
                builder.Append(requests[i].SuggestedFileName);
                builder.AppendLine();
            }

            int remaining = requests.Count - shownCount;
            if (remaining > 0)
            {
                builder.AppendLine($"... and {remaining} more");
            }

            return builder.ToString().TrimEnd();
        }

        public static void LogApplySummary(AtlasRenameResult result)
        {
            var builder = new StringBuilder();
            builder.Append("[CycloneGames Atlas Pipeline] Atlas source rename summary");
            builder.AppendLine();
            builder.Append("Renamed: ");
            builder.Append(result.RenamedCount);
            builder.Append(". Failed: ");
            builder.Append(result.Failures.Count);
            builder.Append('.');

            int maxEntries = Math.Min(result.RenamedEntries.Count, 10);
            for (int i = 0; i < maxEntries; i++)
            {
                builder.AppendLine();
                builder.Append("  ");
                builder.Append(result.RenamedEntries[i]);
            }

            if (result.RenamedEntries.Count > maxEntries)
            {
                builder.AppendLine();
                builder.Append("  ... and ");
                builder.Append(result.RenamedEntries.Count - maxEntries);
                builder.Append(" more");
            }

            int maxFailures = Math.Min(result.Failures.Count, 10);
            for (int i = 0; i < maxFailures; i++)
            {
                builder.AppendLine();
                builder.Append("  [Failed] ");
                builder.Append(result.Failures[i]);
            }

            if (result.Failures.Count > maxFailures)
            {
                builder.AppendLine();
                builder.Append("  ... and ");
                builder.Append(result.Failures.Count - maxFailures);
                builder.Append(" more failures");
            }

            AtlasPipelineLog.Channel.Info(builder.ToString());
        }

        private static string MakeUniqueTargetFileName(
            string assetPath,
            string safeFileName,
            HashSet<string> existingAssetPaths,
            HashSet<string> usedTargetPaths)
        {
            string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/') ?? string.Empty;
            string candidateFileName = safeFileName;
            string candidatePath = directory + "/" + candidateFileName;
            int suffix = 2;

            while (usedTargetPaths.Contains(candidatePath)
                   || (existingAssetPaths.Contains(candidatePath)
                       && !string.Equals(
                           candidatePath,
                           assetPath,
                           StringComparison.OrdinalIgnoreCase)))
            {
                string extension = Path.GetExtension(safeFileName);
                string stem = Path.GetFileNameWithoutExtension(safeFileName);
                string suffixText = "_" + suffix;

                // Re-truncate after appending the suffix, otherwise a name that is still invalid
                // after renaming loops forever: rescan, rename, rescan.
                int maxStemLength = MaximumStemLength - suffixText.Length;
                if (stem.Length > maxStemLength)
                {
                    stem = stem.Substring(0, Math.Max(maxStemLength, 1)).TrimEnd('_', '-');
                }

                candidateFileName = stem + suffixText + extension;
                candidatePath = directory + "/" + candidateFileName;
                suffix++;
            }

            usedTargetPaths.Add(candidatePath);
            return candidateFileName;
        }

        private static bool IsSafeNameCharacter(char c)
        {
            if (AsciiOnlyNames && c > 0x7F)
            {
                // Tightened policy: non-ASCII characters (CJK, full-width, emoji) are invalid.
                // char.IsLetterOrDigit would accept Unicode letters, which is not portable across
                // cross-platform builds and some toolchains.
                return false;
            }

            return char.IsLetterOrDigit(c) || c == '_' || c == '-';
        }

        private static bool IsReservedWindowsName(string stem)
        {
            return ReservedWindowsNames.Contains(stem);
        }

        internal static bool IsSupportedImagePath(string assetPath)
        {
            string extension = Path.GetExtension(assetPath);
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
        }
    }
}
