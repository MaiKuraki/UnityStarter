using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace CycloneGames.Editor.Build
{
    public static class BuildUtils
    {
        public static void CopyAllFilesRecursively(string sourceFolderPath, string destinationFolderPath)
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

        public static Type GetTypeInAllAssemblies(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = assembly.GetType(typeName);
                if (t != null) return t;
            }
            return null;
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
    }
}