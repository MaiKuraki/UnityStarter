using System;
using System.IO;
using UnityEngine;

namespace CycloneGames.IO.Unity
{
    /// <summary>
    /// Creates platform-correct UnityWebRequest URIs from explicit Unity file locations.
    /// </summary>
    public static class UnityFileUri
    {
        public static string Create(string path, UnityFileLocation location)
        {
            if (TryCreate(path, location, out string uri, out UnityFileUriError error))
            {
                return uri;
            }

            throw CreateException(path, location, error);
        }

        public static bool TryCreate(
            string path,
            UnityFileLocation location,
            out string uri,
            out UnityFileUriError error)
        {
            uri = null;
            error = UnityFileUriError.None;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = UnityFileUriError.InvalidPath;
                return false;
            }

            try
            {
                switch (location)
                {
                    case UnityFileLocation.StreamingAssets:
                        return TryCreateStreamingAssetsUri(path, out uri, out error);
                    case UnityFileLocation.PersistentData:
                        return TryCreateSandboxedFileUri(
                            Application.persistentDataPath,
                            path,
                            out uri,
                            out error);
                    case UnityFileLocation.AbsolutePathOrUri:
                        return TryCreateAbsoluteUri(path, out uri, out error);
                    default:
                        error = UnityFileUriError.InvalidLocation;
                        return false;
                }
            }
            catch (ArgumentException)
            {
                error = UnityFileUriError.InvalidPath;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                error = UnityFileUriError.PathOutsideLocation;
                return false;
            }
            catch (IOException)
            {
                error = UnityFileUriError.InvalidPath;
                return false;
            }
            catch (NotSupportedException)
            {
                error = UnityFileUriError.InvalidPath;
                return false;
            }
            catch (UriFormatException)
            {
                error = UnityFileUriError.InvalidPath;
                return false;
            }
        }

        private static bool TryCreateStreamingAssetsUri(
            string relativePath,
            out string uri,
            out UnityFileUriError error)
        {
            string normalizedRelativePath = FilePathSandbox.NormalizeRelativePath(relativePath)
                .Replace('\\', '/');
            string rootPath = Application.streamingAssetsPath;
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                uri = null;
                error = UnityFileUriError.LocationUnavailable;
                return false;
            }

#if (UNITY_ANDROID || UNITY_WEBGL) && !UNITY_EDITOR
            uri = CombineUriPath(rootPath, normalizedRelativePath);
#else
            var sandbox = new FilePathSandbox(rootPath);
            uri = FormatFilePath(sandbox.Resolve(normalizedRelativePath));
#endif
            error = UnityFileUriError.None;
            return true;
        }

        private static bool TryCreateSandboxedFileUri(
            string rootPath,
            string relativePath,
            out string uri,
            out UnityFileUriError error)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                uri = null;
                error = UnityFileUriError.LocationUnavailable;
                return false;
            }

            var sandbox = new FilePathSandbox(rootPath);
            uri = FormatFilePath(sandbox.Resolve(relativePath));
            error = UnityFileUriError.None;
            return true;
        }

        private static bool TryCreateAbsoluteUri(
            string path,
            out string uri,
            out UnityFileUriError error)
        {
            if (Path.IsPathRooted(path))
            {
                uri = FormatFilePath(Path.GetFullPath(path));
                error = UnityFileUriError.None;
                return true;
            }

            if (Uri.TryCreate(path, UriKind.Absolute, out Uri parsedUri))
            {
                if (!IsSupportedScheme(parsedUri.Scheme))
                {
                    uri = null;
                    error = UnityFileUriError.UnsupportedScheme;
                    return false;
                }

                uri = parsedUri.AbsoluteUri;
                error = UnityFileUriError.None;
                return true;
            }

            uri = null;
            error = UnityFileUriError.InvalidPath;
            return false;
        }

        private static bool IsSupportedScheme(string scheme)
        {
            return string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || string.Equals(scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase)
                || string.Equals(scheme, "jar", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatFilePath(string absolutePath)
        {
            if (!Path.IsPathRooted(absolutePath))
            {
                throw new ArgumentException("An absolute file path was required.", nameof(absolutePath));
            }

            var fileUri = new Uri(Path.GetFullPath(absolutePath));
            if (!fileUri.IsFile)
            {
                throw new UriFormatException("The path could not be represented as a file URI.");
            }

            return fileUri.AbsoluteUri;
        }

        private static string CombineUriPath(string baseUri, string relativePath)
        {
            string[] segments = relativePath.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = Uri.EscapeDataString(segments[i]);
            }

            return baseUri.TrimEnd('/') + "/" + string.Join("/", segments);
        }

        private static Exception CreateException(
            string path,
            UnityFileLocation location,
            UnityFileUriError error)
        {
            switch (error)
            {
                case UnityFileUriError.PathOutsideLocation:
                    return new UnauthorizedAccessException(
                        $"Path '{path}' resolves outside {location}.");
                case UnityFileUriError.UnsupportedScheme:
                    return new NotSupportedException(
                        "Only http, https, file, and jar URI schemes are supported.");
                case UnityFileUriError.LocationUnavailable:
                    return new InvalidOperationException(
                        $"Unity file location {location} is unavailable.");
                case UnityFileUriError.InvalidLocation:
                    return new ArgumentOutOfRangeException(nameof(location));
                default:
                    return new ArgumentException(
                        $"Path '{path}' is invalid for {location}.",
                        nameof(path));
            }
        }
    }
}
