using System;

namespace Build.Pipeline.Editor
{
    internal static class RuntimeVersionInfoPathPolicy
    {
        internal const string AssetFileName = "VersionInfoData.asset";
        internal const string DefaultAssetPath =
            "Assets/Build/Runtime/Resources/VersionInfoData.asset";

        internal static void Validate(string path)
        {
            BuildPathPolicy.ValidatePortableProjectRelativePath(
                path,
                "VersionInfoData path");
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)
                || !path.EndsWith("/" + AssetFileName, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"VersionInfoData path must be a project-relative Assets path ending in '{AssetFileName}'.",
                    nameof(path));
            }

            string[] segments = path.Split('/');
            bool containsResourcesDirectory = false;
            for (int index = 1; index < segments.Length - 1; index++)
            {
                if (string.Equals(
                        segments[index],
                        "Resources",
                        StringComparison.Ordinal))
                {
                    containsResourcesDirectory = true;
                    break;
                }
            }

            if (!containsResourcesDirectory)
            {
                throw new ArgumentException(
                    "VersionInfoData must be generated below an exact 'Resources' directory so the runtime asset is included and discoverable.",
                    nameof(path));
            }
        }
    }
}
