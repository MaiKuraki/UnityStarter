using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace Build.Pipeline.Editor
{
    public static class BuildProfileResolver
    {
        public static BuildData ResolveInteractive()
        {
            if (Selection.activeObject is BuildData selected)
            {
                return selected;
            }

            return ResolveSingleProfile(
                "Select a BuildData asset before running the command when the project contains multiple build profiles.");
        }

        public static BuildData ResolveCommandLine(string profilePath)
        {
            if (string.IsNullOrWhiteSpace(profilePath))
            {
                return ResolveSingleProfile(
                    $"Pass {BuildCommandLineOptionNames.Profile} Assets/<path>/<profile>.asset " +
                    "when the project contains multiple build profiles.");
            }

            string normalized = profilePath.Replace('\\', '/').Trim();
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal)
                || !normalized.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                throw new BuildFailedException(
                    $"Build profile must be a project-relative .asset path below Assets: '{profilePath}'.");
            }

            try
            {
                BuildPathPolicy.ValidatePortableProjectRelativePath(normalized, "Build profile");
            }
            catch (ArgumentException exception)
            {
                throw new BuildFailedException(
                    $"Build profile must be a portable project-relative .asset path below Assets: '{profilePath}'. " +
                    exception.Message);
            }

            BuildData profile = AssetDatabase.LoadAssetAtPath<BuildData>(normalized);
            if (profile == null)
            {
                throw new BuildFailedException($"BuildData was not found at '{normalized}'.");
            }

            return profile;
        }

        private static BuildData ResolveSingleProfile(string multipleProfilesInstruction)
        {
            string[] paths = AssetDatabase.FindAssets("t:BuildData")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            if (paths.Length == 0)
            {
                throw new BuildFailedException(
                    "No BuildData asset was found. Create one with Assets/Create/CycloneGames/Build/Build Profile.");
            }

            if (paths.Length != 1)
            {
                throw new BuildFailedException(
                    $"Found {paths.Length} BuildData assets. {multipleProfilesInstruction}\n" +
                    string.Join("\n", paths.Select(path => "  - " + path)));
            }

            BuildData profile = AssetDatabase.LoadAssetAtPath<BuildData>(paths[0]);
            if (profile == null)
            {
                throw new BuildFailedException($"Failed to load BuildData at '{paths[0]}'.");
            }

            return profile;
        }
    }
}
