// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.IO;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Converts a resolved audio location into a URI accepted by <c>UnityWebRequest</c>.
    /// </summary>
    /// <remarks>
    /// Absolute file paths must be expressed as <c>file://</c> URIs; raw paths work on some desktop
    /// players but are not the documented form and fail on platforms that route through a URI parser.
    /// Android StreamingAssets (<c>jar:file://…!/assets/…</c>) and WebGL (<c>https://…</c>) roots are
    /// already URIs and are passed through unchanged.
    /// </remarks>
    internal static class AudioClipUri
    {
        /// <summary>
        /// Builds the request URI for <paramref name="resolvedLocation"/>.
        /// </summary>
        internal static bool TryCreate(string resolvedLocation, out string uri, out string error)
        {
            uri = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(resolvedLocation))
            {
                error = "Audio location is empty.";
                return false;
            }

            if (IsUriLike(resolvedLocation))
            {
                if (!Uri.TryCreate(resolvedLocation, UriKind.Absolute, out _))
                {
                    error = $"Audio location is not a valid absolute URI: {resolvedLocation}";
                    return false;
                }

                uri = resolvedLocation;
                return true;
            }

            if (!Path.IsPathRooted(resolvedLocation))
            {
                error = $"Audio location must be absolute or a URI: {resolvedLocation}";
                return false;
            }

            try
            {
                var fileUri = new Uri(Path.GetFullPath(resolvedLocation));
                if (!fileUri.IsFile)
                {
                    error = $"Audio location could not be represented as a file URI: {resolvedLocation}";
                    return false;
                }

                uri = fileUri.AbsoluteUri;
                return true;
            }
            catch (ArgumentException)
            {
                error = $"Audio location is not a valid path: {resolvedLocation}";
                return false;
            }
            catch (NotSupportedException)
            {
                error = $"Audio location is not a supported path: {resolvedLocation}";
                return false;
            }
            catch (PathTooLongException)
            {
                error = "Audio location exceeds the platform path limit.";
                return false;
            }
            catch (UriFormatException)
            {
                error = $"Audio location could not be represented as a file URI: {resolvedLocation}";
                return false;
            }
        }

        private static bool IsUriLike(string value)
        {
            return value.IndexOf("://", StringComparison.Ordinal) >= 0 ||
                   value.StartsWith("jar:", StringComparison.OrdinalIgnoreCase);
        }
    }
}
