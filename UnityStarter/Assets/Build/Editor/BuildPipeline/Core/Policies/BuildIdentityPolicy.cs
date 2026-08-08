using System;
using System.Globalization;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Shared authoring and execution policy for identity text written to
    /// PlayerSettings, paths, logs, and CI manifests.
    /// </summary>
    public static class BuildIdentityPolicy
    {
        public const int MaximumApplicationIdentifierCharacters = 255;
        public const int MaximumApplicationVersionCharacters = 64;
        public const int MaximumBuildIdentifierCharacters = 64;

        public static void ValidateBuildIdentifier(
            string value,
            string displayName)
        {
            ValidatePlainText(
                value,
                displayName,
                MaximumBuildIdentifierCharacters);

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool isLowerAsciiLetter = character >= 'a' && character <= 'z';
                bool isAsciiDigit = character >= '0' && character <= '9';
                bool isSeparator = index > 0
                    && (character == '-' || character == '_' || character == '.');
                if (!isLowerAsciiLetter && !isAsciiDigit && !isSeparator)
                {
                    throw new ArgumentException(
                        $"{displayName} must use lowercase ASCII letters, digits, '.', '_' or '-'; " +
                        "the first character must be a letter or digit.",
                        nameof(value));
                }
            }
        }

        public static void ValidatePlainText(
            string value,
            string displayName,
            int maximumCharacters)
        {
            string name = string.IsNullOrWhiteSpace(displayName)
                ? "Value"
                : displayName;
            if (maximumCharacters <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumCharacters),
                    maximumCharacters,
                    "The character budget must be positive.");
            }

            if (string.IsNullOrWhiteSpace(value)
                || value.Length > maximumCharacters
                || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"{name} is required, may not have surrounding whitespace, and may not exceed {maximumCharacters} characters.",
                    nameof(value));
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                UnicodeCategory category = char.GetUnicodeCategory(character);
                if (char.IsControl(character)
                    || char.IsSurrogate(character)
                    || category == UnicodeCategory.Format
                    || category == UnicodeCategory.PrivateUse)
                {
                    throw new ArgumentException(
                        $"{name} contains an unsupported control or invisible character.",
                        nameof(value));
                }
            }
        }

        public static void ValidateApplicationIdentifier(string value)
        {
            ValidatePlainText(
                value,
                "Application identifier",
                MaximumApplicationIdentifierCharacters);
            string[] segments = value.Split('.');
            if (segments.Length < 2)
            {
                throw new ArgumentException(
                    "Application identifier must contain at least two dot-separated ASCII identifier segments.",
                    nameof(value));
            }

            foreach (string segment in segments)
            {
                if (segment.Length == 0
                    || segment.Length > 63
                    || !IsAsciiLetter(segment[0]))
                {
                    throw new ArgumentException(
                        $"Application identifier contains an invalid segment: '{segment}'.",
                        nameof(value));
                }

                for (int index = 1; index < segment.Length; index++)
                {
                    char character = segment[index];
                    if (!IsAsciiLetter(character)
                        && (character < '0' || character > '9'))
                    {
                        throw new ArgumentException(
                            $"Application identifier contains an invalid segment: '{segment}'. " +
                            "Use only ASCII letters and digits so one profile remains valid on Android and Apple targets.",
                            nameof(value));
                    }
                }
            }
        }

        /// <summary>
        /// Validates one cross-platform native application version. The profile
        /// intentionally uses the conservative three-integer form accepted by
        /// Apple bundle marketing versions and the other supported targets.
        /// Build/content identity is carried separately by PackageVersion.
        /// </summary>
        public static void ValidateApplicationVersion(string value)
        {
            ValidatePlainText(
                value,
                "Application version",
                MaximumApplicationVersionCharacters);
            string[] segments = value.Split('.');
            if (segments.Length != 3)
            {
                throw new ArgumentException(
                    "Application version must contain exactly three dot-separated unsigned integer components, for example '1.2.3'.",
                    nameof(value));
            }

            foreach (string segment in segments)
            {
                if (segment.Length == 0
                    || (segment.Length > 1 && segment[0] == '0'))
                {
                    throw new ArgumentException(
                        $"Application version contains an invalid component: '{segment}'.",
                        nameof(value));
                }

                uint component = 0;
                for (int index = 0; index < segment.Length; index++)
                {
                    char character = segment[index];
                    if (character < '0' || character > '9')
                    {
                        throw new ArgumentException(
                            $"Application version contains an invalid component: '{segment}'. Use ASCII digits only.",
                            nameof(value));
                    }

                    try
                    {
                        component = checked(component * 10 + (uint)(character - '0'));
                    }
                    catch (OverflowException exception)
                    {
                        throw new ArgumentException(
                            $"Application version component is outside the UInt32 range: '{segment}'.",
                            nameof(value),
                            exception);
                    }
                }
            }
        }

        private static bool IsAsciiLetter(char value)
        {
            return (value >= 'a' && value <= 'z')
                || (value >= 'A' && value <= 'Z');
        }
    }
}
