using System;
using System.Collections.Generic;
using System.IO;
using CycloneGames.UIFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.UIFramework.Editor
{
    internal static class UIWindowCreationValidator
    {
        private static readonly HashSet<string> CSharpKeywords = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
            "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
            "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
            "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
            "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
            "object", "operator", "out", "override", "params", "private", "protected",
            "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
            "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while"
        };

        public static bool HasExistingFiles(UIWindowCreationRequest request)
        {
            if (!TryBuildPaths(request, out UIWindowCreationPaths paths, out _))
            {
                return false;
            }

            return AssetOrMetaExists(paths.ScriptFilePath) ||
                   AssetOrMetaExists(paths.PrefabFilePath) ||
                   AssetOrMetaExists(paths.ConfigFilePath) ||
                   (request.UseMvp &&
                    (AssetOrMetaExists(paths.ViewInterfaceFilePath) ||
                     AssetOrMetaExists(paths.PresenterFilePath)));
        }

        public static void GetExistingFiles(UIWindowCreationRequest request, List<string> results)
        {
            results.Clear();

            if (!TryBuildPaths(request, out UIWindowCreationPaths paths, out _))
            {
                return;
            }

            AddIfExists(results, "Script", paths.ScriptFilePath);
            AddIfExists(results, "Prefab", paths.PrefabFilePath);
            AddIfExists(results, "Config", paths.ConfigFilePath);

            if (request.UseMvp)
            {
                AddIfExists(results, "View Interface", paths.ViewInterfaceFilePath);
                AddIfExists(results, "Presenter", paths.PresenterFilePath);
            }
        }

        public static string Validate(UIWindowCreationRequest request, List<string> scratchErrors)
        {
            scratchErrors.Clear();

            if (string.IsNullOrEmpty(request.WindowName))
            {
                scratchErrors.Add("- Window name is required");
            }
            else if (!IsValidCSharpIdentifier(request.WindowName))
            {
                scratchErrors.Add($"- Window name '{request.WindowName}' is not a valid C# identifier");
            }
            else if (FindLoadedType(request.WindowName, request.NamespaceName) != null)
            {
                scratchErrors.Add(
                    $"- Type '{BuildFullTypeName(request.WindowName, request.NamespaceName)}' already exists in a loaded assembly");
            }

            if (!string.IsNullOrEmpty(request.NamespaceName) && !IsValidNamespace(request.NamespaceName))
            {
                scratchErrors.Add($"- Namespace '{request.NamespaceName}' is not a valid C# namespace");
            }

            if (request.ScriptFolder == null)
            {
                scratchErrors.Add("- Script folder reference is missing");
            }
            if (request.PrefabFolder == null)
            {
                scratchErrors.Add("- Prefab folder reference is missing");
            }
            if (request.ConfigFolder == null)
            {
                scratchErrors.Add("- Configuration folder reference is missing");
            }
            if (request.UseMvp && request.PresenterFolder == null)
            {
                scratchErrors.Add("- Presenter folder reference is required when using MVP");
            }
            if (request.Layer == null)
            {
                scratchErrors.Add("- UILayer configuration is required");
            }
            if (request.SourceMode == UIWindowConfiguration.PrefabSource.PathLocation &&
                !request.AutoFillLocationFromPrefabPath &&
                string.IsNullOrWhiteSpace(request.RuntimeLocation))
            {
                scratchErrors.Add("- PathLocation requires a runtime location when auto-fill is disabled");
            }

            if (scratchErrors.Count > 0)
            {
                return "Missing Required Fields:\n\n" + string.Join("\n", scratchErrors);
            }

            if (!TryBuildPaths(request, out UIWindowCreationPaths paths, out string pathError))
            {
                return pathError;
            }

            scratchErrors.Clear();

            AddMissingFolder(scratchErrors, "Script folder", paths.ScriptFolderPath);
            AddMissingFolder(scratchErrors, "Prefab folder", paths.PrefabFolderPath);
            AddMissingFolder(scratchErrors, "Config folder", paths.ConfigFolderPath);
            if (request.UseMvp)
            {
                AddMissingFolder(scratchErrors, "Presenter folder", paths.PresenterFolderPath);
            }

            if (scratchErrors.Count > 0)
            {
                return "Folders No Longer Exist:\n\nThe following folders may have been deleted or moved:\n" +
                       string.Join("\n", scratchErrors) +
                       "\n\nPlease re-select the folders.";
            }

            scratchErrors.Clear();
            AddExistingFile(scratchErrors, "Script", paths.ScriptFilePath);
            AddExistingFile(scratchErrors, "Prefab", paths.PrefabFilePath);
            AddExistingFile(scratchErrors, "Config", paths.ConfigFilePath);

            if (request.UseMvp)
            {
                AddExistingFile(scratchErrors, "View Interface", paths.ViewInterfaceFilePath);
                AddExistingFile(scratchErrors, "Presenter", paths.PresenterFilePath);
            }

            if (scratchErrors.Count > 0)
            {
                return "Files Already Exist:\n\nThe following files would be overwritten:\n" +
                       string.Join("\n", scratchErrors) +
                       "\n\nPlease delete or rename them first, or choose a different window name.";
            }

            if (request.SourceMode == UIWindowConfiguration.PrefabSource.AssetReference &&
                string.IsNullOrWhiteSpace(request.RuntimeLocation))
            {
                string autoDerived = UIWindowLocationResolverRegistry.Resolve(null, paths.PrefabFilePath);
                if (string.IsNullOrEmpty(autoDerived))
                {
                    return "Asset Reference Requires a Location:\n\n" +
                           "No asset management integration could auto-derive a provider location for the generated prefab. " +
                           "Configure the active integration (for example a YooAsset collector that covers the prefab folder) " +
                           "or enter a manual override.";
                }
            }

            UIWindowAssemblyValidator.Validate(paths, request.UseMvp, scratchErrors);
            if (scratchErrors.Count > 0)
            {
                return "Assembly Boundary Validation Failed:\n\n" +
                       string.Join("\n", scratchErrors) +
                       "\n\nThe creator does not modify asmdef or asmref files. Choose compatible output folders or update assembly references explicitly.";
            }

            return string.Empty;
        }

        public static bool TryBuildPaths(UIWindowCreationRequest request, out UIWindowCreationPaths paths, out string errorMessage)
        {
            paths = default;
            errorMessage = string.Empty;

            if (request.ScriptFolder == null || request.PrefabFolder == null || request.ConfigFolder == null)
            {
                errorMessage = "Missing required folder references.";
                return false;
            }
            if (request.UseMvp && request.PresenterFolder == null)
            {
                errorMessage = "Presenter folder reference is required when using MVP.";
                return false;
            }
            if (string.IsNullOrEmpty(request.WindowName))
            {
                errorMessage = "Window name is required.";
                return false;
            }

            if (!TryNormalizeAssetFolderPath(
                    AssetDatabase.GetAssetPath(request.ScriptFolder),
                    out string scriptFolderPath,
                    out errorMessage) ||
                !TryNormalizeAssetFolderPath(
                    AssetDatabase.GetAssetPath(request.PrefabFolder),
                    out string prefabFolderPath,
                    out errorMessage) ||
                !TryNormalizeAssetFolderPath(
                    AssetDatabase.GetAssetPath(request.ConfigFolder),
                    out string configFolderPath,
                    out errorMessage))
            {
                return false;
            }

            string presenterFolderPath = string.Empty;
            if (request.UseMvp &&
                !TryNormalizeAssetFolderPath(
                    AssetDatabase.GetAssetPath(request.PresenterFolder),
                    out presenterFolderPath,
                    out errorMessage))
            {
                return false;
            }

            paths = new UIWindowCreationPaths(
                scriptFolderPath,
                prefabFolderPath,
                configFolderPath,
                presenterFolderPath,
                request.WindowName);

            if (!TryValidateAssetFilePath(paths.ScriptFilePath, ".cs", out _, out errorMessage) ||
                !TryValidateAssetFilePath(paths.PrefabFilePath, ".prefab", out _, out errorMessage) ||
                !TryValidateAssetFilePath(paths.ConfigFilePath, ".asset", out _, out errorMessage) ||
                (request.UseMvp &&
                 (!TryValidateAssetFilePath(paths.ViewInterfaceFilePath, ".cs", out _, out errorMessage) ||
                  !TryValidateAssetFilePath(paths.PresenterFilePath, ".cs", out _, out errorMessage))))
            {
                paths = default;
                return false;
            }

            return true;
        }

        public static bool IsValidCSharpIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return false;
            }

            if (CSharpKeywords.Contains(identifier))
            {
                return false;
            }

            if (!char.IsLetter(identifier[0]) && identifier[0] != '_')
            {
                return false;
            }

            for (int i = 1; i < identifier.Length; i++)
            {
                char c = identifier[i];
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    return false;
                }
            }

            return true;
        }

        private static Type FindLoadedType(string typeName, string namespaceName)
        {
            string fullName = BuildFullTypeName(typeName, namespaceName);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static string BuildFullTypeName(string typeName, string namespaceName)
        {
            return string.IsNullOrEmpty(namespaceName)
                ? typeName
                : namespaceName + "." + typeName;
        }

        public static bool IsValidNamespace(string namespaceName)
        {
            if (string.IsNullOrEmpty(namespaceName))
            {
                return true;
            }

            int segmentStart = 0;
            for (int i = 0; i <= namespaceName.Length; i++)
            {
                if (i != namespaceName.Length && namespaceName[i] != '.')
                {
                    continue;
                }

                int segmentLength = i - segmentStart;
                if (segmentLength <= 0 || !IsValidCSharpIdentifier(namespaceName.Substring(segmentStart, segmentLength)))
                {
                    return false;
                }

                segmentStart = i + 1;
            }

            return true;
        }

        internal static bool TryValidateAssetFilePath(
            string assetPath,
            string requiredExtension,
            out string canonicalPath,
            out string error)
        {
            return UIWindowPathUtility.TryValidateAssetFilePath(
                assetPath,
                requiredExtension,
                out canonicalPath,
                out error);
        }

        internal static bool TryGetAbsoluteAssetPath(
            string assetPath,
            out string absolutePath,
            out string error)
        {
            return UIWindowPathUtility.TryGetAbsoluteAssetPath(assetPath, out absolutePath, out error);
        }

        internal static bool TryEnsureOutputAvailable(
            string assetPath,
            string requiredExtension,
            out string absolutePath,
            out string error)
        {
            return UIWindowPathUtility.TryEnsureOutputAvailable(
                assetPath,
                requiredExtension,
                out absolutePath,
                out error);
        }

        private static bool TryNormalizeAssetFolderPath(
            string path,
            out string normalized,
            out string error)
        {
            return UIWindowPathUtility.TryNormalizeAssetFolderPath(path, out normalized, out error);
        }

        private static void AddIfExists(List<string> results, string label, string path)
        {
            if (AssetOrMetaExists(path))
            {
                results.Add(label + ": " + path);
            }
        }

        private static void AddMissingFolder(List<string> results, string label, string path)
        {
            string folderPath = path != null && path.EndsWith("/") ? path.Substring(0, path.Length - 1) : path;
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                results.Add("- " + label + ": " + folderPath);
            }
        }

        private static void AddExistingFile(List<string> results, string label, string path)
        {
            if (!TryGetAbsoluteAssetPath(path, out string absolutePath, out _))
            {
                return;
            }

            bool assetExists = File.Exists(absolutePath);
            bool metaExists = File.Exists(absolutePath + ".meta");
            if (assetExists || metaExists)
            {
                string suffix = !assetExists && metaExists ? " (orphan .meta)" : string.Empty;
                results.Add("- " + label + ": " + path + suffix);
            }
        }

        private static bool AssetOrMetaExists(string assetPath)
        {
            return TryGetAbsoluteAssetPath(assetPath, out string absolutePath, out _) &&
                   (File.Exists(absolutePath) || File.Exists(absolutePath + ".meta"));
        }
    }

    internal readonly struct UIWindowCreationPaths
    {
        public readonly string ScriptFolderPath;
        public readonly string PrefabFolderPath;
        public readonly string ConfigFolderPath;
        public readonly string PresenterFolderPath;
        public readonly string ScriptFilePath;
        public readonly string PrefabFilePath;
        public readonly string ConfigFilePath;
        public readonly string ViewInterfaceFilePath;
        public readonly string PresenterFilePath;

        public UIWindowCreationPaths(
            string scriptFolderPath,
            string prefabFolderPath,
            string configFolderPath,
            string presenterFolderPath,
            string windowName)
        {
            ScriptFolderPath = scriptFolderPath;
            PrefabFolderPath = prefabFolderPath;
            ConfigFolderPath = configFolderPath;
            PresenterFolderPath = presenterFolderPath;
            ScriptFilePath = scriptFolderPath + windowName + ".cs";
            PrefabFilePath = prefabFolderPath + windowName + ".prefab";
            ConfigFilePath = configFolderPath + windowName + "_Config.asset";
            ViewInterfaceFilePath = scriptFolderPath + "I" + windowName + "View.cs";
            PresenterFilePath = presenterFolderPath + windowName + "Presenter.cs";
        }
    }
}
