using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Bounded read-only source for StreamingAssets and explicitly allowed HTTPS configuration endpoints.
    /// </summary>
    public sealed class UriInputConfigurationSource : IInputConfigurationSource
    {
        internal const int MaximumUriCharacters = 4096;
        internal const int MaximumAllowedHttpsHosts = 64;
        internal const int MaximumHttpsHostCharacters = 253;
        private static readonly char[] QueryOrFragmentCharacters = { '?', '#' };
        private static readonly char[] HostStructureCharacters = { '/', '\\', '@', ':', '?', '#' };
        private static readonly char[] AuthorityTerminatorCharacters = { '/', '?', '#' };
        private readonly int _maximumBytes;
        private readonly int _timeoutSeconds;
        private readonly HashSet<string> _allowedHttpsHosts;

        public UriInputConfigurationSource(
            int maximumBytes = FileInputConfigurationStore.DefaultMaximumBytes,
            IEnumerable<string> allowedHttpsHosts = null,
            int timeoutSeconds = 30)
        {
            if (maximumBytes <= 0 || maximumBytes > FileInputConfigurationStore.MaximumSupportedBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }

            if (timeoutSeconds <= 0 || timeoutSeconds > 300)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            }

            _maximumBytes = maximumBytes;
            _timeoutSeconds = timeoutSeconds;
            if (allowedHttpsHosts != null)
            {
                _allowedHttpsHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int suppliedHostCount = 0;
                foreach (string allowedHost in allowedHttpsHosts)
                {
                    suppliedHostCount++;
                    if (suppliedHostCount > MaximumAllowedHttpsHosts)
                    {
                        throw new ArgumentException(
                            $"HTTPS host allowlists cannot contain more than {MaximumAllowedHttpsHosts} entries.",
                            nameof(allowedHttpsHosts));
                    }

                    if (!TryNormalizeAllowedHttpsHost(allowedHost, out string normalizedHost))
                    {
                        throw new ArgumentException(
                            "HTTPS host allowlists must contain bounded DNS names or IPv4 addresses without ports or URI syntax.",
                            nameof(allowedHttpsHosts));
                    }

                    _allowedHttpsHosts.Add(normalizedHost);
                }
            }
        }

        public async UniTask<InputConfigurationReadResult> LoadAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            await SwitchToUnityMainThreadAsync(cancellationToken);
            string uri;
            string error;
            bool validUri;
            try
            {
                validUri = TryNormalizeAllowedUri(key, out uri, out error);
            }
            catch (Exception exception) when (
                exception is UriFormatException ||
                exception is ArgumentException)
            {
                uri = null;
                error = "The configuration URI is invalid.";
                validUri = false;
            }

            if (!validUri)
            {
                return InputConfigurationReadResult.Failure(InputConfigurationStorageStatus.InvalidKey, error);
            }

            try
            {
                using (var request = UnityWebRequest.Get(uri))
                {
                    request.timeout = _timeoutSeconds;
                    request.redirectLimit = 0;
                    var handler = new BoundedDownloadHandler(_maximumBytes);
                    request.downloadHandler?.Dispose();
                    request.downloadHandler = handler;

                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    }

                    if (handler.ExceededLimit)
                    {
                        return InputConfigurationReadResult.Failure(
                            InputConfigurationStorageStatus.TooLarge,
                            $"Configuration exceeds the {_maximumBytes}-byte limit.");
                    }

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        return InputConfigurationReadResult.Success(handler.GetContentText());
                    }

                    if (request.responseCode == 404)
                    {
                        return InputConfigurationReadResult.Failure(InputConfigurationStorageStatus.NotFound);
                    }

                    return InputConfigurationReadResult.Failure(
                        InputConfigurationStorageStatus.IoError,
                        "The configuration request failed.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DecoderFallbackException)
            {
                return InputConfigurationReadResult.Failure(
                    InputConfigurationStorageStatus.InvalidContent,
                    "Configuration content is not valid UTF-8.");
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException &&
                exception is not AccessViolationException &&
                exception is not StackOverflowException)
            {
                return InputConfigurationReadResult.Failure(
                    InputConfigurationStorageStatus.IoError,
                    $"Configuration request failed ({exception.GetType().Name}).");
            }
        }

        internal bool TryNormalizeAllowedUri(string value, out string uri, out string error)
        {
            uri = null;
            error = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "A configuration URI is required.";
                return false;
            }

            if (value.Length > MaximumUriCharacters)
            {
                error = $"Configuration URIs cannot exceed {MaximumUriCharacters} characters.";
                return false;
            }

            if (ContainsForbiddenUnicode(value))
            {
                error = "The configuration URI contains an unsupported character.";
                return false;
            }

            bool startsWithHttps = value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            bool startsWithHttp = value.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            if ((startsWithHttps || startsWithHttp) &&
                !HasBoundedCredentialFreeWebAuthority(
                    value,
                    startsWithHttps ? "https://".Length : "http://".Length))
            {
                error = $"Web configuration hosts must contain 1 to {MaximumHttpsHostCharacters} characters without credentials.";
                return false;
            }

            if (TryNormalizeStreamingAssetsWebUri(value, out uri))
            {
                return ValidateNormalizedUriLength(ref uri, out error);
            }

            if (startsWithHttps)
            {
                if (!Uri.TryCreate(value, UriKind.Absolute, out Uri httpsUri) ||
                    !string.Equals(httpsUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    error = "The HTTPS configuration URI is invalid.";
                    return false;
                }

                if (!string.IsNullOrEmpty(httpsUri.UserInfo) || httpsUri.Port != 443 ||
                    !string.IsNullOrEmpty(httpsUri.Fragment))
                {
                    error = "HTTPS configuration URIs must use the default HTTPS port without credentials or fragments.";
                    return false;
                }

                string normalizedHost = httpsUri.IdnHost;
                if (string.IsNullOrEmpty(normalizedHost) ||
                    normalizedHost.Length > MaximumHttpsHostCharacters ||
                    _allowedHttpsHosts == null ||
                    !_allowedHttpsHosts.Contains(normalizedHost))
                {
                    error = "HTTPS configuration requires an explicit allowed host.";
                    return false;
                }

                uri = httpsUri.AbsoluteUri;
                return ValidateNormalizedUriLength(ref uri, out error);
            }

            if (value.StartsWith("jar:file://", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryNormalizeJarStreamingAssetsUri(value, out uri))
                {
                    error = "JAR configuration URIs must resolve inside StreamingAssets.";
                    return false;
                }

                return ValidateNormalizedUriLength(ref uri, out error);
            }

            if (startsWithHttp)
            {
                error = "Unencrypted HTTP configuration endpoints are not supported.";
                return false;
            }

            if (!TryGetLocalPath(value, out string localPath))
            {
                error = "Only StreamingAssets files, jar:file URIs, and explicitly allowed HTTPS endpoints are supported.";
                return false;
            }

            string streamingAssetsRoot;
            try
            {
                streamingAssetsRoot = Path.GetFullPath(Application.streamingAssetsPath);
                localPath = Path.GetFullPath(localPath);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                error = "The local configuration path is invalid.";
                return false;
            }

            if (!IsContainedPath(streamingAssetsRoot, localPath))
            {
                error = "Local default configuration must be inside StreamingAssets.";
                return false;
            }

            try
            {
                if (ContainsReparsePoint(streamingAssetsRoot, localPath))
                {
                    error = "Local configuration paths cannot traverse symbolic links or reparse points.";
                    return false;
                }
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException)
            {
                error = "The local configuration path could not be inspected safely.";
                return false;
            }

            uri = new Uri(localPath).AbsoluteUri;
            return ValidateNormalizedUriLength(ref uri, out error);
        }

        private static bool TryNormalizeAllowedHttpsHost(string value, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length > MaximumHttpsHostCharacters ||
                !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
                ContainsForbiddenUnicode(value) ||
                value.IndexOfAny(HostStructureCharacters) >= 0)
            {
                return false;
            }

            try
            {
                if (!Uri.TryCreate("https://" + value + "/", UriKind.Absolute, out Uri candidate) ||
                    !string.IsNullOrEmpty(candidate.UserInfo) ||
                    candidate.Port != 443 ||
                    !string.IsNullOrEmpty(candidate.Query) ||
                    !string.IsNullOrEmpty(candidate.Fragment))
                {
                    return false;
                }

                string idnHost = candidate.IdnHost;
                if (string.IsNullOrEmpty(idnHost) || idnHost.Length > MaximumHttpsHostCharacters)
                {
                    return false;
                }

                UriHostNameType hostType = Uri.CheckHostName(idnHost);
                if (hostType != UriHostNameType.Dns && hostType != UriHostNameType.IPv4)
                {
                    return false;
                }

                if (hostType == UriHostNameType.Dns)
                {
                    string hostWithoutRootDot = idnHost.EndsWith(".", StringComparison.Ordinal)
                        ? idnHost.Substring(0, idnHost.Length - 1)
                        : idnHost;
                    if (hostWithoutRootDot.Length == 0)
                    {
                        return false;
                    }

                    string[] labels = hostWithoutRootDot.Split('.');
                    for (int i = 0; i < labels.Length; i++)
                    {
                        string label = labels[i];
                        if (label.Length == 0 ||
                            label.Length > 63 ||
                            label[0] == '-' ||
                            label[label.Length - 1] == '-')
                        {
                            return false;
                        }

                        for (int characterIndex = 0; characterIndex < label.Length; characterIndex++)
                        {
                            char character = label[characterIndex];
                            if (!((character >= 'a' && character <= 'z') ||
                                  (character >= 'A' && character <= 'Z') ||
                                  (character >= '0' && character <= '9') ||
                                  character == '-'))
                            {
                                return false;
                            }
                        }
                    }
                }

                normalized = idnHost;
                return true;
            }
            catch (Exception exception) when (
                exception is UriFormatException ||
                exception is ArgumentException)
            {
                normalized = null;
                return false;
            }
        }

        private static bool HasBoundedCredentialFreeWebAuthority(
            string value,
            int authorityStart)
        {
            int authorityEnd = value.IndexOfAny(AuthorityTerminatorCharacters, authorityStart);
            if (authorityEnd < 0)
            {
                authorityEnd = value.Length;
            }

            int authorityLength = authorityEnd - authorityStart;
            if (authorityLength <= 0 ||
                value.IndexOf('@', authorityStart, authorityLength) >= 0 ||
                value.IndexOf('[', authorityStart, authorityLength) >= 0 ||
                value.IndexOf(']', authorityStart, authorityLength) >= 0)
            {
                return false;
            }

            int portSeparator = value.IndexOf(':', authorityStart, authorityLength);
            int hostEnd = portSeparator < 0 ? authorityEnd : portSeparator;
            int hostLength = hostEnd - authorityStart;
            if (hostLength <= 0 || hostLength > MaximumHttpsHostCharacters)
            {
                return false;
            }

            if (portSeparator < 0)
            {
                return true;
            }

            int portLength = authorityEnd - portSeparator - 1;
            if (portLength <= 0 || portLength > 5)
            {
                return false;
            }

            for (int index = portSeparator + 1; index < authorityEnd; index++)
            {
                if (value[index] < '0' || value[index] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateNormalizedUriLength(ref string uri, out string error)
        {
            if (string.IsNullOrEmpty(uri) || uri.Length > MaximumUriCharacters)
            {
                uri = null;
                error = $"Normalized configuration URIs cannot exceed {MaximumUriCharacters} characters.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool ContainsForbiddenUnicode(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                UnicodeCategory category;
                if (char.IsHighSurrogate(current))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    {
                        return true;
                    }

                    category = CharUnicodeInfo.GetUnicodeCategory(value, i);
                    i++;
                }
                else if (char.IsLowSurrogate(current))
                {
                    return true;
                }
                else
                {
                    category = char.GetUnicodeCategory(current);
                }

                if (category == UnicodeCategory.Control ||
                    category == UnicodeCategory.Format ||
                    category == UnicodeCategory.PrivateUse ||
                    category == UnicodeCategory.LineSeparator ||
                    category == UnicodeCategory.ParagraphSeparator)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryNormalizeStreamingAssetsWebUri(string value, out string normalized)
        {
            normalized = null;
            if (!Uri.TryCreate(Application.streamingAssetsPath, UriKind.Absolute, out Uri rootUri) ||
                !Uri.TryCreate(value, UriKind.Absolute, out Uri candidateUri) ||
                (rootUri.Scheme != Uri.UriSchemeHttp && rootUri.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            if (!string.Equals(rootUri.Scheme, candidateUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(rootUri.IdnHost, candidateUri.IdnHost, StringComparison.OrdinalIgnoreCase) ||
                rootUri.Port != candidateUri.Port ||
                !string.IsNullOrEmpty(candidateUri.UserInfo) ||
                !string.IsNullOrEmpty(candidateUri.Fragment))
            {
                return false;
            }

            string rootPath = Uri.UnescapeDataString(rootUri.AbsolutePath).TrimEnd('/') + "/";
            string candidatePath = Uri.UnescapeDataString(candidateUri.AbsolutePath);
            string[] segments = candidatePath.Replace('\\', '/').Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == "." || segments[i] == "..")
                {
                    return false;
                }
            }

            if (!candidatePath.StartsWith(rootPath, StringComparison.Ordinal))
            {
                return false;
            }

            normalized = candidateUri.AbsoluteUri;
            return true;
        }

        private static bool TryNormalizeJarStreamingAssetsUri(string value, out string normalized)
        {
            normalized = null;
            string root = Application.streamingAssetsPath;
            if (string.IsNullOrWhiteSpace(root) ||
                !root.StartsWith("jar:file://", StringComparison.OrdinalIgnoreCase) ||
                value.IndexOfAny(QueryOrFragmentCharacters) >= 0)
            {
                return false;
            }

            string decodedRoot;
            string decodedCandidate;
            try
            {
                decodedRoot = Uri.UnescapeDataString(root).Replace('\\', '/').TrimEnd('/') + "/";
                decodedCandidate = Uri.UnescapeDataString(value).Replace('\\', '/');
            }
            catch (UriFormatException)
            {
                return false;
            }

            string[] segments = decodedCandidate.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == "." || segments[i] == "..")
                {
                    return false;
                }
            }

            if (!decodedCandidate.StartsWith(decodedRoot, StringComparison.Ordinal))
            {
                return false;
            }

            normalized = value;
            return true;
        }

        private static bool TryGetLocalPath(string value, out string path)
        {
            path = null;
            try
            {
                if (value.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    var parsed = new Uri(value);
                    if (!parsed.IsFile ||
                        !string.IsNullOrEmpty(parsed.Query) ||
                        !string.IsNullOrEmpty(parsed.Fragment))
                    {
                        return false;
                    }

                    path = parsed.LocalPath;
                    return true;
                }

                if (Path.IsPathRooted(value))
                {
                    path = value;
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is UriFormatException ||
                exception is ArgumentException ||
                exception is NotSupportedException)
            {
                return false;
            }

            return false;
        }

        private static bool IsContainedPath(string root, string candidate)
        {
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            string separator = Path.DirectorySeparatorChar.ToString();
            string rootPrefix = root.EndsWith(separator, StringComparison.Ordinal)
                ? root
                : root + separator;
            return candidate.StartsWith(rootPrefix, comparison);
        }

        private static bool ContainsReparsePoint(string root, string candidate)
        {
            if (HasReparsePoint(root)) return true;
            string relative = candidate.Substring(root.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string[] segments = relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            string current = root;
            for (int i = 0; i < segments.Length; i++)
            {
                current = Path.Combine(current, segments[i]);
                if (!File.Exists(current) && !Directory.Exists(current)) break;
                if (HasReparsePoint(current)) return true;
            }

            return false;
        }

        private static bool HasReparsePoint(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return false;
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static async UniTask SwitchToUnityMainThreadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PlayerLoopHelper.IsMainThread)
            {
                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        private sealed class BoundedDownloadHandler : DownloadHandlerScript
        {
            private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
            private readonly byte[] _content;
            private int _length;

            public bool ExceededLimit { get; private set; }

            public BoundedDownloadHandler(int maximumBytes)
                : base(new byte[Math.Min(maximumBytes, 16 * 1024)])
            {
                _content = new byte[maximumBytes];
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength <= 0)
                {
                    return true;
                }

                if (dataLength > _content.Length - _length)
                {
                    ExceededLimit = true;
                    return false;
                }

                Buffer.BlockCopy(data, 0, _content, _length, dataLength);
                _length += dataLength;
                return true;
            }

            public string GetContentText()
            {
                return StrictUtf8.GetString(_content, 0, _length);
            }
        }
    }
}
