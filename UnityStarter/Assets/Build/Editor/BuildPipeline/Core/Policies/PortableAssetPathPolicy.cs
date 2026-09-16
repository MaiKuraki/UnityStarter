using System;
using System.Collections.Generic;
using System.Text;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Why one file-system segment (a single file or directory name) cannot be
    /// relied on across the supported build hosts and Player targets.
    /// </summary>
    public enum PortablePathViolationKind
    {
        None = 0,

        /// <summary>The segment is empty, or is the "." / ".." navigation entry.</summary>
        EmptySegment,

        /// <summary>The segment exceeds the portable 255-byte UTF-8 name limit.</summary>
        SegmentTooLong,

        /// <summary>The segment contains a C0/C1 control character.</summary>
        ControlCharacter,

        /// <summary>The segment contains a character outside printable ASCII.</summary>
        NonAsciiCharacter,

        /// <summary>The segment contains a character no supported host accepts in a name.</summary>
        InvalidCharacter,

        /// <summary>The segment is a reserved Windows device name.</summary>
        ReservedDeviceName,

        /// <summary>The segment ends with a period or space, which Windows silently drops.</summary>
        TrailingDotOrSpace,

        /// <summary>The segment is not well-formed Unicode and has no stable encoding.</summary>
        InvalidUnicode,

        /// <summary>The composed path exceeds the Win32 MAX_PATH budget.</summary>
        PathTooLong
    }

    /// <summary>
    /// One rejected segment together with the exact offending text, so a report can
    /// be acted on without re-deriving which rule rejected it.
    /// </summary>
    public readonly struct PortablePathSegmentViolation
    {
        public static readonly PortablePathSegmentViolation None = default;

        public PortablePathSegmentViolation(
            PortablePathViolationKind kind,
            string offendingText)
        {
            Kind = kind;
            OffendingText = offendingText ?? string.Empty;
        }

        public PortablePathViolationKind Kind { get; }

        public string OffendingText { get; }

        public bool HasViolation => Kind != PortablePathViolationKind.None;
    }

    /// <summary>
    /// Segment-level naming rules for content that must survive a Player build, a CDN
    /// upload, a case-sensitive checkout, and a Windows build agent.
    ///
    /// The rules exist because a name can be perfectly legal in the working copy and
    /// still fail after the build: Unity copies <c>StreamingAssets</c> verbatim into
    /// the Player, Android addresses those bytes through the APK zip index, WebGL
    /// addresses them through a URL, and Windows CI still reaches MAX_PATH-limited
    /// Win32 APIs. Catching the name at preflight keeps the failure attributable to
    /// the file instead of to an opaque vendor or platform error.
    ///
    /// These are deliberately write-free, allocation-light checks: the build
    /// preflight calls them for every scanned entry.
    /// </summary>
    public static class PortableAssetPathPolicy
    {
        /// <summary>
        /// Maximum UTF-8 byte count of one segment. 255 bytes is the portable limit
        /// shared by ext4 (255 bytes), APFS (255 UTF-8 bytes), and NTFS (255 UTF-16
        /// code units, which non-ASCII text reaches sooner).
        /// </summary>
        public const int MaximumSegmentUtf8ByteCount = 255;

        /// <summary>
        /// Maximum UTF-8 byte count of one repository-relative path. This is a
        /// sanity ceiling for authored configuration, not a platform limit.
        /// </summary>
        public const int MaximumPathUtf8ByteCount = 1024;

        private static readonly char[] InvalidSegmentCharacters =
        {
            '<', '>', ':', '"', '/', '\\', '|', '?', '*'
        };

        private static readonly HashSet<string> ReservedDeviceNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5",
                "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
                "LPT6", "LPT7", "LPT8", "LPT9"
            };

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>
        /// True when every character is printable ASCII. ASCII content stays
        /// addressable from whatever encoding a target platform happens to use for
        /// file names, URLs, and archive indexes.
        /// </summary>
        public static bool IsPureAscii(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] > '\u007F')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Validates one segment. The first violation wins so the report stays
        /// specific instead of listing every character in a CJK name.
        /// </summary>
        public static PortablePathSegmentViolation ValidateSegment(
            string segment,
            bool rejectNonAscii)
        {
            if (string.IsNullOrEmpty(segment))
            {
                return new PortablePathSegmentViolation(
                    PortablePathViolationKind.EmptySegment,
                    string.Empty);
            }

            if (string.Equals(segment, ".", StringComparison.Ordinal)
                || string.Equals(segment, "..", StringComparison.Ordinal))
            {
                return new PortablePathSegmentViolation(
                    PortablePathViolationKind.EmptySegment,
                    segment);
            }

            if (segment.EndsWith(".", StringComparison.Ordinal)
                || segment.EndsWith(" ", StringComparison.Ordinal))
            {
                return new PortablePathSegmentViolation(
                    PortablePathViolationKind.TrailingDotOrSpace,
                    segment.Substring(segment.Length - 1));
            }

            for (int index = 0; index < segment.Length; index++)
            {
                char character = segment[index];
                if (char.IsControl(character))
                {
                    return new PortablePathSegmentViolation(
                        PortablePathViolationKind.ControlCharacter,
                        DescribeCharacter(character));
                }

                if (char.IsSurrogate(character))
                {
                    // A well-formed pair is already reported as non-ASCII below when
                    // that rule is enabled; an unpaired surrogate has no usable
                    // encoding at all, so it is rejected unconditionally.
                    if (!char.IsSurrogatePair(segment, index))
                    {
                        return new PortablePathSegmentViolation(
                            PortablePathViolationKind.InvalidUnicode,
                            DescribeCharacter(character));
                    }

                    index++;
                    if (rejectNonAscii)
                    {
                        return new PortablePathSegmentViolation(
                            PortablePathViolationKind.NonAsciiCharacter,
                            segment.Substring(index - 1, 2));
                    }

                    continue;
                }

                if (character > '\u007F' && rejectNonAscii)
                {
                    return new PortablePathSegmentViolation(
                        PortablePathViolationKind.NonAsciiCharacter,
                        character.ToString());
                }

                for (int invalidIndex = 0;
                     invalidIndex < InvalidSegmentCharacters.Length;
                     invalidIndex++)
                {
                    if (character == InvalidSegmentCharacters[invalidIndex])
                    {
                        return new PortablePathSegmentViolation(
                            PortablePathViolationKind.InvalidCharacter,
                            character.ToString());
                    }
                }
            }

            int extensionSeparatorIndex = segment.IndexOf('.');
            string deviceName = extensionSeparatorIndex < 0
                ? segment
                : segment.Substring(0, extensionSeparatorIndex);
            if (ReservedDeviceNames.Contains(deviceName))
            {
                return new PortablePathSegmentViolation(
                    PortablePathViolationKind.ReservedDeviceName,
                    deviceName);
            }

            int byteCount;
            try
            {
                byteCount = StrictUtf8.GetByteCount(segment);
            }
            catch (EncoderFallbackException)
            {
                return new PortablePathSegmentViolation(
                    PortablePathViolationKind.InvalidUnicode,
                    segment);
            }

            if (byteCount > MaximumSegmentUtf8ByteCount)
            {
                return new PortablePathSegmentViolation(
                    PortablePathViolationKind.SegmentTooLong,
                    byteCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return PortablePathSegmentViolation.None;
        }

        /// <summary>
        /// Human-readable, actionable explanation for one segment violation. Kept
        /// next to the rule so the message and the check cannot drift apart.
        /// </summary>
        public static string DescribeSegmentViolation(
            PortablePathViolationKind kind,
            string offendingText)
        {
            string offending = string.IsNullOrEmpty(offendingText)
                ? string.Empty
                : $" '{offendingText}'";

            switch (kind)
            {
                case PortablePathViolationKind.EmptySegment:
                    return "is empty or is a '.'/'..' navigation segment.";
                case PortablePathViolationKind.SegmentTooLong:
                    return $"exceeds the portable file-name limit of {MaximumSegmentUtf8ByteCount} UTF-8 bytes " +
                        $"(actual{offending}).";
                case PortablePathViolationKind.ControlCharacter:
                    return $"contains the control character{offending}.";
                case PortablePathViolationKind.NonAsciiCharacter:
                    return $"contains the non-ASCII character{offending}. Rename it using ASCII letters, " +
                        "digits, '-', '_' or '.'. Unity copies StreamingAssets verbatim into the Player, and " +
                        "non-ASCII names break Android asset addressing, WebGL URL resolution, and " +
                        "case-sensitive checkouts.";
                case PortablePathViolationKind.InvalidCharacter:
                    return $"contains the reserved character{offending}.";
                case PortablePathViolationKind.ReservedDeviceName:
                    return $"is the reserved device name{offending}.";
                case PortablePathViolationKind.TrailingDotOrSpace:
                    return $"ends with a period or space{offending}, which Windows silently strips.";
                case PortablePathViolationKind.InvalidUnicode:
                    return $"is not well-formed Unicode{offending} and has no stable encoding.";
                case PortablePathViolationKind.PathTooLong:
                    return $"makes the composed path{offending} exceed the Win32 MAX_PATH budget.";
                default:
                    return "is not portable.";
            }
        }

        internal static string DescribeCharacter(char character)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "U+{0:X4}",
                (int)character);
        }
    }
}
