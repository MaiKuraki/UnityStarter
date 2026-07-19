using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.InputSystem.Editor
{
    internal static class InputEditorFileUtility
    {
        private static readonly char[] PortableInvalidPathCharacters =
            { '<', '>', ':', '"', '|', '?', '*' };

        internal static bool TryResolveAssetFile(
            DefaultAsset folder,
            string fileName,
            out string assetPath,
            out string absolutePath,
            out string error)
        {
            assetPath = null;
            absolutePath = null;
            error = null;

            if (folder == null)
            {
                error = "Select a project folder under Assets.";
                return false;
            }

            string folderAssetPath = AssetDatabase.GetAssetPath(folder);
            if (!AssetDatabase.IsValidFolder(folderAssetPath))
            {
                error = "The selected object is not a project folder.";
                return false;
            }

            if (!IsSafeFileName(fileName))
            {
                error = "The output file name is invalid.";
                return false;
            }

            assetPath = string.Concat(folderAssetPath.TrimEnd('/', '\\'), "/", fileName);
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (!TryResolveProjectAssetPath(projectRoot, assetPath, out absolutePath, out error))
            {
                assetPath = null;
                return false;
            }

            return true;
        }

        internal static bool TryResolveProjectAssetPath(
            string projectRoot,
            string assetPath,
            out string absolutePath,
            out string error)
        {
            absolutePath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                error = "The Unity project root is unavailable.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(assetPath) || assetPath.Length > 1024)
            {
                error = "The asset path must contain 1..1024 characters.";
                return false;
            }

            string normalizedAssetPath = assetPath.Replace('\\', '/');
            if (!normalizedAssetPath.Equals("Assets", StringComparison.Ordinal) &&
                !normalizedAssetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "Generated project files must remain under Assets.";
                return false;
            }

            if (ContainsUnsafeText(normalizedAssetPath))
            {
                error = "The asset path contains unsupported characters.";
                return false;
            }
            if (!HasSafePathSegments(normalizedAssetPath))
            {
                error = "The asset path contains an empty, traversal, or non-portable segment.";
                return false;
            }

            try
            {
                string canonicalProjectRoot = Path.GetFullPath(projectRoot);
                string canonicalAssetsRoot = Path.GetFullPath(Path.Combine(canonicalProjectRoot, "Assets"));
                string candidate = Path.GetFullPath(Path.Combine(
                    canonicalProjectRoot,
                    normalizedAssetPath.Replace('/', Path.DirectorySeparatorChar)));

                if (!IsContainedPath(canonicalAssetsRoot, candidate))
                {
                    error = "The generated file path escapes the project Assets directory.";
                    return false;
                }
                if (ContainsReparsePointBelowRoot(canonicalAssetsRoot, candidate))
                {
                    error = "The generated file path crosses a symbolic-link or reparse-point boundary.";
                    return false;
                }

                absolutePath = candidate;
                return true;
            }
            catch (Exception exception) when (IsRecoverablePathException(exception))
            {
                error = $"The output path is invalid ({exception.GetType().Name}).";
                return false;
            }
        }

        internal static bool TryResolveUserConfigPath(
            string persistentDataRoot,
            string subdirectory,
            string fileName,
            out string absolutePath,
            out string error)
        {
            absolutePath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(persistentDataRoot))
            {
                error = "PersistentData is unavailable.";
                return false;
            }

            if (!IsSafeFileName(fileName))
            {
                error = "The user configuration file name is invalid.";
                return false;
            }

            string relativeDirectory = (subdirectory ?? string.Empty).Trim();
            if (relativeDirectory.Length > 512)
            {
                error = "The user configuration subdirectory must contain at most 512 characters.";
                return false;
            }
            if (ContainsUnsafeText(relativeDirectory) || Path.IsPathRooted(relativeDirectory))
            {
                error = "The user configuration subdirectory must be a safe relative path.";
                return false;
            }

            string normalizedDirectory = relativeDirectory.Replace('\\', '/');
            if (normalizedDirectory.Length > 0 && !HasSafePathSegments(normalizedDirectory))
            {
                error = "The user configuration subdirectory contains an empty, traversal, or non-portable segment.";
                return false;
            }

            try
            {
                string canonicalRoot = Path.GetFullPath(persistentDataRoot);
                string candidate = Path.GetFullPath(Path.Combine(
                    canonicalRoot,
                    normalizedDirectory.Replace('/', Path.DirectorySeparatorChar),
                    fileName));

                if (!IsContainedPath(canonicalRoot, candidate))
                {
                    error = "The user configuration path escapes PersistentData.";
                    return false;
                }
                if (ContainsReparsePointBelowRoot(canonicalRoot, candidate))
                {
                    error = "The user configuration path crosses a symbolic-link or reparse-point boundary.";
                    return false;
                }

                absolutePath = candidate;
                return true;
            }
            catch (Exception exception) when (IsRecoverablePathException(exception))
            {
                error = $"The user configuration path is invalid ({exception.GetType().Name}).";
                return false;
            }
        }

        internal static bool TryWriteBytesTransactional(
            string targetPath,
            byte[] bytes,
            out string backupPath,
            out string error)
        {
            backupPath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                error = "The target path is empty.";
                return false;
            }

            bytes ??= Array.Empty<byte>();
            string temporaryPath = null;
            string recoveryBackupPath = null;

            try
            {
                string canonicalTarget = Path.GetFullPath(targetPath);
                string directory = Path.GetDirectoryName(canonicalTarget);
                if (string.IsNullOrEmpty(directory))
                {
                    error = "The target directory is unavailable.";
                    return false;
                }
                if (!IsSafeFileName(Path.GetFileName(canonicalTarget)))
                {
                    error = "The target filename is invalid or reserved for transactional sidecars.";
                    return false;
                }

                Directory.CreateDirectory(directory);
                temporaryPath = string.Concat(
                    canonicalTarget,
                    ".tmp.",
                    Guid.NewGuid().ToString("N"));

                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(canonicalTarget))
                {
                    string stableBackupPath = string.Concat(canonicalTarget, ".bak");
                    recoveryBackupPath = string.Concat(
                        stableBackupPath,
                        ".tmp.",
                        Guid.NewGuid().ToString("N"));
                    backupPath = recoveryBackupPath;

                    try
                    {
                        File.Replace(temporaryPath, canonicalTarget, recoveryBackupPath, true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        if (!TryReplaceWithRollback(
                                canonicalTarget,
                                temporaryPath,
                                recoveryBackupPath,
                                out string fallbackError))
                        {
                            backupPath = null;
                            error = fallbackError;
                            return false;
                        }
                    }

                    try
                    {
                        PromoteRecoveryBackup(recoveryBackupPath, stableBackupPath);
                        recoveryBackupPath = null;
                        backupPath = stableBackupPath;
                    }
                    catch (Exception promotionException) when (IsRecoverableWriteException(promotionException))
                    {
                        _ = promotionException;
                        // The primary replacement has already committed. Report success and retain the
                        // transaction-specific recovery copy instead of returning a false failure result.
                        temporaryPath = null;
                        backupPath = recoveryBackupPath;
                        recoveryBackupPath = null;
                        return true;
                    }
                }
                else
                {
                    File.Move(temporaryPath, canonicalTarget);
                }

                temporaryPath = null;
                return true;
            }
            catch (Exception exception) when (IsRecoverableWriteException(exception))
            {
                error = $"Failed to commit the file transaction ({exception.GetType().Name}).";
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(temporaryPath))
                {
                    try
                    {
                        if (File.Exists(temporaryPath))
                        {
                            File.Delete(temporaryPath);
                        }
                    }
                    catch (Exception cleanupException) when (IsRecoverableWriteException(cleanupException))
                    {
                        string cleanupMessage =
                            $"Failed to remove the temporary transaction file ({cleanupException.GetType().Name}).";
                        error = string.IsNullOrEmpty(error)
                            ? cleanupMessage
                            : string.Concat(error, " ", cleanupMessage);
                    }
                }
            }
        }

        internal static void ImportAssetAtPath(string assetPath)
        {
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }
        }

        internal static string ToSafeDisplayText(string value, string fallback = "Operation failed.")
        {
            if (string.IsNullOrEmpty(value))
            {
                return fallback;
            }

            const int maxLength = 512;
            int length = Math.Min(value.Length, maxLength);
            var characters = new char[length];
            int outputIndex = 0;
            for (int index = 0; index < length; index++)
            {
                char character = value[index];
                if (character == '\r' || character == '\n' || character == '\t')
                {
                    characters[outputIndex++] = ' ';
                    continue;
                }

                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 < length && char.IsLowSurrogate(value[index + 1]))
                    {
                        UnicodeCategory pairCategory = CharUnicodeInfo.GetUnicodeCategory(value, index);
                        if (IsUnsafeCategory(pairCategory))
                        {
                            characters[outputIndex++] = '?';
                            index++;
                        }
                        else
                        {
                            characters[outputIndex++] = character;
                            characters[outputIndex++] = value[++index];
                        }
                    }
                    else
                    {
                        characters[outputIndex++] = '?';
                    }
                    continue;
                }

                UnicodeCategory category = char.GetUnicodeCategory(character);
                characters[outputIndex++] = char.IsLowSurrogate(character) ||
                                            char.IsControl(character) ||
                                            category == UnicodeCategory.Format ||
                                            category == UnicodeCategory.PrivateUse ||
                                            category == UnicodeCategory.LineSeparator ||
                                            category == UnicodeCategory.ParagraphSeparator
                    ? '?'
                    : character;
            }

            string sanitized = new string(characters, 0, outputIndex).Trim();
            return sanitized.Length == 0 ? fallback : sanitized;
        }

        private static bool IsSafeFileName(string fileName)
        {
            return !string.IsNullOrWhiteSpace(fileName) &&
                   fileName.Length <= 255 &&
                   fileName.Equals(Path.GetFileName(fileName), StringComparison.Ordinal) &&
                   !ContainsUnsafeText(fileName) &&
                   fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                   !IsReservedSidecarFileName(fileName);
        }

        private static bool IsReservedSidecarFileName(string fileName)
        {
            return fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                   fileName.IndexOf(".tmp.", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsUnsafeText(string value)
        {
            if (value == null)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (char.IsHighSurrogate(character))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    {
                        return true;
                    }

                    UnicodeCategory pairCategory = CharUnicodeInfo.GetUnicodeCategory(value, i);
                    if (IsUnsafeCategory(pairCategory))
                    {
                        return true;
                    }
                    i++;
                    continue;
                }

                if (char.IsLowSurrogate(character) ||
                    char.IsControl(character) ||
                    IsUnsafeCategory(char.GetUnicodeCategory(character)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSafePathSegments(string normalizedPath)
        {
            string[] segments = normalizedPath.Split('/');
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                string segment = segments[segmentIndex];
                if (segment.Length == 0 ||
                    segment.Equals(".", StringComparison.Ordinal) ||
                    segment.Equals("..", StringComparison.Ordinal) ||
                    segment.IndexOfAny(PortableInvalidPathCharacters) >= 0 ||
                    segment.EndsWith(" ", StringComparison.Ordinal) ||
                    segment.EndsWith(".", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsUnsafeCategory(UnicodeCategory category)
        {
            return category == UnicodeCategory.Format ||
                   category == UnicodeCategory.PrivateUse ||
                   category == UnicodeCategory.LineSeparator ||
                   category == UnicodeCategory.ParagraphSeparator;
        }

        private static bool TryReplaceWithRollback(
            string targetPath,
            string temporaryPath,
            string backupPath,
            out string error)
        {
            error = null;
            bool originalMoved = false;
            try
            {
                File.Move(targetPath, backupPath);
                originalMoved = true;
                File.Move(temporaryPath, targetPath);
                return true;
            }
            catch (Exception replaceException) when (IsRecoverableWriteException(replaceException))
            {
                bool restored = !originalMoved;
                if (originalMoved && !File.Exists(targetPath) && File.Exists(backupPath))
                {
                    try
                    {
                        File.Move(backupPath, targetPath);
                        restored = true;
                    }
                    catch (Exception restoreException) when (IsRecoverableWriteException(restoreException))
                    {
                        error = string.Concat(
                            "The fallback replacement failed and the original file could not be restored (",
                            restoreException.GetType().Name,
                            ").");
                        return false;
                    }
                }

                error = restored
                    ? $"The fallback replacement failed ({replaceException.GetType().Name}); the original file was restored."
                    : $"The fallback replacement failed ({replaceException.GetType().Name}); the original file remains in the backup.";
                return false;
            }
        }

        private static void PromoteRecoveryBackup(
            string recoveryBackupPath,
            string stableBackupPath)
        {
            if (!File.Exists(recoveryBackupPath)) return;
            if (!File.Exists(stableBackupPath))
            {
                File.Move(recoveryBackupPath, stableBackupPath);
                return;
            }

            try
            {
                File.Replace(recoveryBackupPath, stableBackupPath, null, true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Delete(stableBackupPath);
                File.Move(recoveryBackupPath, stableBackupPath);
            }
        }

        private static bool IsContainedPath(string canonicalRoot, string candidate)
        {
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            string rootWithSeparator = canonicalRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            return candidate.Equals(canonicalRoot, comparison) ||
                   candidate.StartsWith(rootWithSeparator, comparison);
        }

        private static bool ContainsReparsePointBelowRoot(string canonicalRoot, string candidate)
        {
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            try
            {
                string current = File.Exists(candidate) || Directory.Exists(candidate)
                    ? candidate
                    : Path.GetDirectoryName(candidate);
                while (!string.IsNullOrEmpty(current) &&
                       !current.Equals(canonicalRoot, comparison) &&
                       IsContainedPath(canonicalRoot, current))
                {
                    if ((File.Exists(current) || Directory.Exists(current)) &&
                        (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        return true;
                    }

                    string parent = Path.GetDirectoryName(current);
                    if (string.IsNullOrEmpty(parent) || parent.Equals(current, comparison))
                    {
                        break;
                    }
                    current = parent;
                }
            }
            catch (Exception exception) when (IsRecoverableWriteException(exception))
            {
                return true;
            }

            return false;
        }

        private static bool IsRecoverablePathException(Exception exception)
        {
            return exception is ArgumentException ||
                   exception is NotSupportedException ||
                   exception is PathTooLongException;
        }

        private static bool IsRecoverableWriteException(Exception exception)
        {
            return exception is IOException ||
                   exception is UnauthorizedAccessException ||
                   exception is PlatformNotSupportedException ||
                   IsRecoverablePathException(exception);
        }
    }
}
