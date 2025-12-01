using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public static class BuildUtils
    {
        public static void CopyAllFilesRecursively(string sourceFolderPath, string destinationFolderPath, string[] ignoreExtensions = null)
        {
            // Check if the source directory exists
            if (!Directory.Exists(sourceFolderPath))
            {
                throw new DirectoryNotFoundException($"Source directory does not exist: {sourceFolderPath}");
            }

            // Ensure the destination directory exists
            try
            {
                if (!Directory.Exists(destinationFolderPath))
                {
                    Directory.CreateDirectory(destinationFolderPath);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error creating destination directory: {destinationFolderPath}. Exception: {ex.Message}");
            }

            // Get the files in the source directory and copy them to the destination directory
            foreach (string sourceFilePath in Directory.GetFiles(sourceFolderPath, "*", SearchOption.AllDirectories))
            {
                // Check ignore extensions
                if (ignoreExtensions != null)
                {
                    string ext = Path.GetExtension(sourceFilePath);
                    bool skip = false;
                    foreach (string ignoreExt in ignoreExtensions)
                    {
                        if (ext.Equals(ignoreExt, StringComparison.OrdinalIgnoreCase))
                        {
                            skip = true;
                            break;
                        }
                    }
                    if (skip) continue;
                }

                // Create a relative path that is the same for both source and destination
                string relativePath = sourceFilePath.Substring(sourceFolderPath.Length + 1);
                string destinationFilePath = Path.Combine(destinationFolderPath, relativePath);

                // Ensure the directory for the destination file exists
                string destinationFileDirectory = Path.GetDirectoryName(destinationFilePath);
                if (!Directory.Exists(destinationFileDirectory))
                {
                    Directory.CreateDirectory(destinationFileDirectory);
                }

                // Copy the file and overwrite if it already exists
                try
                {
                    File.Copy(sourceFilePath, destinationFilePath, true);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error copying file: {sourceFilePath} to {destinationFilePath}. Exception: {ex.Message}");
                }
            }
        }

        private static readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();

        /// <summary>
        /// Gets a type by name from all loaded assemblies. Results are cached for performance.
        /// </summary>
        public static Type GetTypeInAllAssemblies(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            // Check cache first
            if (_typeCache.TryGetValue(typeName, out Type cachedType))
            {
                return cachedType;
            }

            // Search in all assemblies
            Type foundType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = assembly.GetType(typeName);
                    if (t != null)
                    {
                        foundType = t;
                        break;
                    }
                }
                catch
                {
                    // Ignore assembly load errors
                }
            }

            // Cache the result (even if null, to avoid repeated searches)
            _typeCache[typeName] = foundType;
            return foundType;
        }

        /// <summary>
        /// Clears the type cache. Call this if assemblies are reloaded.
        /// </summary>
        public static void ClearTypeCache()
        {
            _typeCache.Clear();
        }

        public static void SetField(object target, string fieldName, object value)
        {
            if (target == null) return;

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(target, value);
            }
            else
            {
                Debug.LogWarning($"Reflection: Field {fieldName} not found on {target.GetType().Name}");
            }
        }

        public static void CreateDirectory(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath)) return;
            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"[BuildUtils] Failed to create directory: {directoryPath}. Error: {ex.Message}");
            }
        }

        public static void DeleteDirectory(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath)) return;
            if (Directory.Exists(directoryPath))
            {
                bool deleted = false;
                int attempts = 0;

                while (!deleted && attempts < 3)
                {
                    try
                    {
                        if (attempts > 0) System.Threading.Thread.Sleep(500);

                        // Remove read-only attributes
                        var dirInfo = new DirectoryInfo(directoryPath);
                        dirInfo.Attributes = FileAttributes.Normal;
                        foreach (var info in dirInfo.GetFileSystemInfos("*", SearchOption.AllDirectories))
                        {
                            info.Attributes = FileAttributes.Normal;
                        }

                        Directory.Delete(directoryPath, true);
                        deleted = true;
                    }
                    catch (Exception ex)
                    {
                        attempts++;
                        Debug.LogWarning($"[BuildUtils] Standard delete failed for {directoryPath} (Attempt {attempts}/3): {ex.Message}");
                    }
                }

                if (!deleted)
                {
                    try
                    {
                        string parentDir = Path.GetDirectoryName(directoryPath);
                        string tempName = $"_Trash_{Guid.NewGuid()}";
                        string tempPath = Path.Combine(parentDir, tempName);

                        Debug.Log($"[BuildUtils] Moving locked directory to temp path: {tempPath}");
                        Directory.Move(directoryPath, tempPath);

                        try
                        {
                            Directory.Delete(tempPath, true);
                        }
                        catch
                        {
                            Debug.LogWarning($"[BuildUtils] Could not delete temp trash folder {tempPath}, but original folder is cleared.");
                        }

                        deleted = true;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[BuildUtils] Move-then-delete strategy also failed: {ex.Message}");
                    }
                }

                if (!deleted)
                {
                    throw new Exception($"[BuildUtils] Failed to delete directory: {directoryPath}. File might be locked by another process.");
                }
            }
        }

        public static void ClearDirectory(string directoryPath)
        {
            DeleteDirectory(directoryPath);
            CreateDirectory(directoryPath);
        }

        public static void CopyFile(string sourcePath, string destPath, bool overwrite = true)
        {
            try
            {
                string destDir = Path.GetDirectoryName(destPath);
                if (!Directory.Exists(destDir))
                {
                    CreateDirectory(destDir);
                }

                File.Copy(sourcePath, destPath, overwrite);
            }
            catch (Exception ex)
            {
                throw new Exception($"[BuildUtils] Failed to copy file from {sourcePath} to {destPath}. Error: {ex.Message}");
            }
        }
    }
}