using System;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Defines the stable package and version token contract shared with the
    /// runtime YooAsset provider. The implementation remains dependency-free so
    /// build profiles can be validated when YooAsset is not installed.
    /// </summary>
    public static class YooAssetBuildTokenPolicy
    {
        public const int MaxPackageNameLength = 128;
        public const int MaxPackageVersionLength = 128;

        public static bool IsValidPackageName(string value)
        {
            return IsValid(value, MaxPackageNameLength) && !IsWindowsDeviceName(value);
        }

        public static bool IsValidPackageVersion(string value)
        {
            return IsValid(value, MaxPackageVersionLength);
        }

        public static void ValidatePackageName(string value, string parameterName)
        {
            if (!IsValidPackageName(value))
            {
                throw new ArgumentException(
                    $"YooAsset package name must contain 1 to {MaxPackageNameLength} ASCII letters, digits, dots, hyphens, or underscores; " +
                    "it must start and end with a letter or digit, must not contain consecutive dots, and must not be a reserved platform name.",
                    parameterName);
            }
        }

        public static void ValidatePackageVersion(string value, string parameterName)
        {
            if (!IsValidPackageVersion(value))
            {
                throw new ArgumentException(
                    $"YooAsset package version must contain 1 to {MaxPackageVersionLength} ASCII letters, digits, dots, hyphens, or underscores; " +
                    "it must start and end with a letter or digit and must not contain consecutive dots.",
                    parameterName);
            }
        }

        private static bool IsValid(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maxLength ||
                !IsAsciiLetterOrDigit(value[0]) || !IsAsciiLetterOrDigit(value[value.Length - 1]))
            {
                return false;
            }

            bool previousWasDot = false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (IsAsciiLetterOrDigit(character) || character == '-' || character == '_')
                {
                    previousWasDot = false;
                    continue;
                }

                if (character == '.' && !previousWasDot)
                {
                    previousWasDot = true;
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool IsAsciiLetterOrDigit(char character)
        {
            return character >= '0' && character <= '9' ||
                   character >= 'A' && character <= 'Z' ||
                   character >= 'a' && character <= 'z';
        }

        private static bool IsWindowsDeviceName(string value)
        {
            int baseLength = value.Length;
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] == '.')
                {
                    baseLength = index;
                    break;
                }
            }

            if (baseLength == 3)
            {
                return EqualsAsciiIgnoreCase(value, baseLength, "CON") ||
                       EqualsAsciiIgnoreCase(value, baseLength, "PRN") ||
                       EqualsAsciiIgnoreCase(value, baseLength, "AUX") ||
                       EqualsAsciiIgnoreCase(value, baseLength, "NUL");
            }

            if (baseLength == 4 && value[3] >= '1' && value[3] <= '9')
            {
                return EqualsAsciiIgnoreCase(value, 3, "COM") ||
                       EqualsAsciiIgnoreCase(value, 3, "LPT");
            }

            return false;
        }

        private static bool EqualsAsciiIgnoreCase(string value, int length, string expected)
        {
            if (length != expected.Length)
            {
                return false;
            }

            for (int index = 0; index < length; index++)
            {
                char character = value[index];
                if (character >= 'a' && character <= 'z')
                {
                    character = (char)(character - ('a' - 'A'));
                }

                if (character != expected[index])
                {
                    return false;
                }
            }

            return true;
        }
    }

    public enum YooAssetBuildPipelineKind
    {
        Scriptable,
        RawFile,
        Legacy,
        ArchiveFile
    }

    public enum YooAssetCompression
    {
        Uncompressed,
        LZMA,
        LZ4
    }

    public enum YooAssetFileNameStyle
    {
        HashName,
        BundleName,
        BundleNameAndHash
    }

    public enum YooAssetBundledCopyOption
    {
        None,
        ClearAndCopyAll,
        ClearAndCopyByTags,
        OnlyCopyAll,
        OnlyCopyByTags
    }

    public enum YooAssetVersionCollisionPolicy
    {
        FailIfVersionExists,
        ReplaceExactVersion
    }

    [Serializable]
    public sealed class YooAssetPackageProfile
    {
        [Tooltip("Whether this package participates in the content build.")]
        public bool enabled = true;

        [Tooltip("Exact package name from the YooAsset Bundle Collector settings.")]
        public string packageName = "DefaultPackage";

        [Tooltip("Build pipeline used for this package.")]
        public YooAssetBuildPipelineKind buildPipeline = YooAssetBuildPipelineKind.Scriptable;

        [Tooltip("Deterministic note stored in the package manifest.")]
        public string packageNote = "Generated by Build.Pipeline";

        [Tooltip("Compression used by Scriptable and Legacy pipelines.")]
        public YooAssetCompression compression = YooAssetCompression.LZ4;

        [Tooltip("Naming style for produced bundle files.")]
        public YooAssetFileNameStyle fileNameStyle = YooAssetFileNameStyle.HashName;

        [Tooltip("Controls which files are copied into the built-in package directory.")]
        public YooAssetBundledCopyOption bundledCopyOption = YooAssetBundledCopyOption.None;

        [Tooltip("Semicolon-separated tags used by tag-based bundled copy options.")]
        public string bundledCopyTags = string.Empty;

        [Tooltip("Use YooAsset's asset dependency database when supported by the selected pipeline.")]
        public bool useAssetDependencyDatabase = true;

        [Tooltip("Enable bundle sharing rules when supported by the selected pipeline.")]
        public bool enableSharePackRule = true;

        [Tooltip("Verify the produced package before the adapter reports success.")]
        public bool verifyBuildingResult = true;

        [Tooltip("Controls behavior when the exact package version directory already exists.")]
        public YooAssetVersionCollisionPolicy versionCollisionPolicy = YooAssetVersionCollisionPolicy.FailIfVersionExists;
    }

    [AssetContentProviderAuthoring(
        AssetContentProviderIds.YooAsset,
        "YooAsset",
        Description = "Build and publish one or more YooAsset packages.",
        RequiredEditorTypeName = "YooAsset.Editor.BundleCollectorSettingData",
        Order = 200)]
    [CreateAssetMenu(menuName = "CycloneGames/Build/YooAsset Build Config")]
    public sealed class YooAssetBuildConfig : ScriptableObject
    {
        [Tooltip("Project-relative YooAsset build root. Empty uses YooAsset's default Bundles directory.")]
        public string buildOutputRoot = "Bundles";

        [Tooltip("Project-relative built-in file root. Empty uses YooAsset's configured StreamingAssets root.")]
        public string bundledFileRoot = string.Empty;

        [Tooltip("Explicit package profiles. Package selection and pipeline choice never depend on EditorPrefs.")]
        public YooAssetPackageProfile[] packages = { new YooAssetPackageProfile() };
    }
}
