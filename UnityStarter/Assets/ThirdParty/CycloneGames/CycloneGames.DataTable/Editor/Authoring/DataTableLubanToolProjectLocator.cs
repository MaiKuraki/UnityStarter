using System;
using System.IO;
using UnityEditor;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;

namespace CycloneGames.DataTable.Unity.Editor
{
    internal static class DataTableLubanToolProjectLocator
    {
        internal const string ToolProjectRelativePath =
            "Tools~/CodeGen/CycloneGames.DataTable.CodeGen.csproj";

        internal static string ResolveToolProjectPath(DataTableLubanSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            MonoScript script = MonoScript.FromScriptableObject(settings);
            string scriptAssetPath = script == null
                ? string.Empty
                : AssetDatabase.GetAssetPath(script);
            if (string.IsNullOrEmpty(scriptAssetPath))
            {
                throw new InvalidOperationException(
                    "Could not locate the DataTableLubanSettings script asset.");
            }

            string packageRoot = ResolvePackageRoot(scriptAssetPath);
            string projectPath = Path.GetFullPath(Path.Combine(
                packageRoot,
                ToolProjectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException(
                    "The DataTable CodeGen project was not found relative to the package root.",
                    projectPath);
            }

            return projectPath;
        }

        internal static string ResolvePackageRoot(string scriptAssetPath)
        {
            if (string.IsNullOrWhiteSpace(scriptAssetPath))
            {
                throw new ArgumentException("Script asset path is required.", nameof(scriptAssetPath));
            }

            string normalizedAssetPath = scriptAssetPath.Replace('\\', '/');
            PackageManagerInfo package = PackageManagerInfo.FindForAssetPath(normalizedAssetPath);
            if (package != null && !string.IsNullOrEmpty(package.resolvedPath))
            {
                return Path.GetFullPath(package.resolvedPath);
            }

            string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            string scriptPath = Path.GetFullPath(Path.Combine(
                projectRoot,
                normalizedAssetPath.Replace('/', Path.DirectorySeparatorChar)));
            string directory = Path.GetDirectoryName(scriptPath);
            while (!string.IsNullOrEmpty(directory))
            {
                string manifestPath = Path.Combine(directory, "package.json");
                if (File.Exists(manifestPath))
                {
                    return directory;
                }

                string parent = Path.GetDirectoryName(directory);
                if (string.Equals(parent, directory, StringComparison.Ordinal))
                {
                    break;
                }

                directory = parent;
            }

            throw new DirectoryNotFoundException(
                "Could not find the package root that owns script asset: " + normalizedAssetPath);
        }

        internal static string GetPackageAssetRoot(DataTableLubanSettings settings)
        {
            MonoScript script = MonoScript.FromScriptableObject(settings);
            string path = script == null ? string.Empty : AssetDatabase.GetAssetPath(script);
            const string editorSegment = "/Editor/";
            int editorIndex = path.LastIndexOf(editorSegment, StringComparison.Ordinal);
            return editorIndex < 0 ? string.Empty : path.Substring(0, editorIndex);
        }
    }
}
