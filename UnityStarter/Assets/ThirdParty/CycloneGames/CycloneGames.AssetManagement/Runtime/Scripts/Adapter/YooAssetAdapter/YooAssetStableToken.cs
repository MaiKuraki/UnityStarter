#if CYCLONEGAMES_HAS_YOOASSET
using System;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Validates identifiers that YooAsset uses as directory or file-name components.
    /// The successful path performs a bounded character scan without allocating.
    /// </summary>
    internal static class YooAssetStableToken
    {
        internal const int MaxPackageNameLength = 128;
        internal const int MaxPackageVersionLength = 128;

        internal static bool IsValidPackageName(string value)
        {
            return IsValid(value, MaxPackageNameLength) && !IsWindowsDeviceName(value);
        }

        internal static bool IsValidPackageVersion(string value)
        {
            return IsValid(value, MaxPackageVersionLength);
        }

        internal static void ValidatePackageName(string value, string parameterName)
        {
            if (!IsValidPackageName(value))
            {
                throw new ArgumentException(
                    $"YooAsset package name must contain 1 to {MaxPackageNameLength} ASCII letters, digits, dots, hyphens, or underscores; " +
                    "it must start and end with a letter or digit, must not contain consecutive dots, and must not be a reserved platform name.",
                    parameterName);
            }
        }

        internal static void ValidatePackageVersion(string value, string parameterName)
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
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
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
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '.')
                {
                    baseLength = i;
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

            for (int i = 0; i < length; i++)
            {
                char character = value[i];
                if (character >= 'a' && character <= 'z')
                {
                    character = (char)(character - ('a' - 'A'));
                }

                if (character != expected[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
#endif
