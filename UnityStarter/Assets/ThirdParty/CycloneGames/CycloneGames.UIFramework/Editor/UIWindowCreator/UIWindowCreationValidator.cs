using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace CycloneGames.UIFramework.Editor
{
    internal static class UIWindowCreationValidator
    {
        public static bool HasExistingFiles(UIWindowCreationRequest request)
        {
            if (!TryBuildPaths(request, out UIWindowCreationPaths paths, out _))
            {
                return false;
            }

            return File.Exists(paths.ScriptFilePath) ||
                   File.Exists(paths.PrefabFilePath) ||
                   File.Exists(paths.ConfigFilePath) ||
                   (request.UseMvp && (File.Exists(paths.ViewInterfaceFilePath) || File.Exists(paths.PresenterFilePath)));
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

            string scriptFolderPath = NormalizeAssetFolderPath(AssetDatabase.GetAssetPath(request.ScriptFolder));
            string prefabFolderPath = NormalizeAssetFolderPath(AssetDatabase.GetAssetPath(request.PrefabFolder));
            string configFolderPath = NormalizeAssetFolderPath(AssetDatabase.GetAssetPath(request.ConfigFolder));
            string presenterFolderPath = request.UseMvp
                ? NormalizeAssetFolderPath(AssetDatabase.GetAssetPath(request.PresenterFolder))
                : string.Empty;

            paths = new UIWindowCreationPaths(
                scriptFolderPath,
                prefabFolderPath,
                configFolderPath,
                presenterFolderPath,
                request.WindowName);
            return true;
        }

        public static bool IsValidCSharpIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
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

        private static string NormalizeAssetFolderPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            if (!path.StartsWith("Assets/") && path != "Assets")
            {
                path = "Assets/" + path;
            }

            if (!path.EndsWith("/"))
            {
                path += "/";
            }

            return path;
        }

        private static void AddIfExists(List<string> results, string label, string path)
        {
            if (File.Exists(path))
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
            if (File.Exists(path))
            {
                results.Add("- " + label + ": " + path);
            }
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
