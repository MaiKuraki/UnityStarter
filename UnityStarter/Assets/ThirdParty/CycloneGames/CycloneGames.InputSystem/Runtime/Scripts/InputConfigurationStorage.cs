using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.InputSystem.Runtime
{
    public enum InputConfigurationStorageStatus
    {
        Success,
        NotFound,
        InvalidKey,
        InvalidContent,
        TooLarge,
        Unsupported,
        AccessDenied,
        IoError
    }

    public readonly struct InputConfigurationReadResult
    {
        public InputConfigurationStorageStatus Status { get; }
        public string Content { get; }
        public string Error { get; }
        public bool WasRecoveredFromBackup { get; }
        public bool IsSuccess => Status == InputConfigurationStorageStatus.Success;

        private InputConfigurationReadResult(
            InputConfigurationStorageStatus status,
            string content,
            string error,
            bool wasRecoveredFromBackup)
        {
            Status = status;
            Content = content;
            Error = error;
            WasRecoveredFromBackup = wasRecoveredFromBackup;
        }

        public static InputConfigurationReadResult Success(
            string content,
            bool wasRecoveredFromBackup = false)
        {
            return new InputConfigurationReadResult(
                InputConfigurationStorageStatus.Success,
                content,
                null,
                wasRecoveredFromBackup);
        }

        public static InputConfigurationReadResult Failure(
            InputConfigurationStorageStatus status,
            string error = null)
        {
            return new InputConfigurationReadResult(status, null, error, false);
        }
    }

    public readonly struct InputConfigurationStoreResult
    {
        public InputConfigurationStorageStatus Status { get; }
        public string Error { get; }
        public bool IsSuccess => Status == InputConfigurationStorageStatus.Success;

        private InputConfigurationStoreResult(InputConfigurationStorageStatus status, string error)
        {
            Status = status;
            Error = error;
        }

        public static InputConfigurationStoreResult Success()
        {
            return new InputConfigurationStoreResult(InputConfigurationStorageStatus.Success, null);
        }

        public static InputConfigurationStoreResult Failure(
            InputConfigurationStorageStatus status,
            string error = null)
        {
            return new InputConfigurationStoreResult(status, error);
        }
    }

    /// <summary>
    /// Read-only source for input configuration text. Keys are logical identifiers owned by the source.
    /// </summary>
    public interface IInputConfigurationSource
    {
        UniTask<InputConfigurationReadResult> LoadAsync(
            string key,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Explicit storage boundary for user input configuration. Implementations own their root and durability policy,
    /// must honor cancellation, and must enforce finite payload, I/O, retry, and shutdown budgets.
    /// </summary>
    public interface IInputConfigurationStore : IInputConfigurationSource
    {
        UniTask<InputConfigurationStoreResult> SaveAsync(
            string key,
            string content,
            CancellationToken cancellationToken = default);

        UniTask<InputConfigurationStoreResult> DeleteAsync(
            string key,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Root-confined local file store with bounded reads and crash-resistant replacement.
    /// The caller supplies relative logical keys; rooted paths and URIs are rejected.
    /// </summary>
    public sealed class FileInputConfigurationStore : IInputConfigurationStore
    {
        public const int DefaultMaximumBytes = 1024 * 1024;
        public const int MaximumSupportedBytes = 16 * 1024 * 1024;
        private const int MaximumKeyCharacters = 512;
        private const int MaximumSegmentUtf8Bytes = 255;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly char[] PortableInvalidFileNameCharacters =
            { '<', '>', ':', '"', '|', '?', '*' };
        private static readonly object OperationGatesSync = new object();
        private static readonly bool UsesCaseInsensitivePaths =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        private static readonly Dictionary<string, PathOperationGate> OperationGates =
            new Dictionary<string, PathOperationGate>(
                UsesCaseInsensitivePaths
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);

        private readonly string _rootDirectory;
        private readonly string _rootPrefix;
        private readonly int _maximumBytes;
        private readonly StringComparison _pathComparison;

        public FileInputConfigurationStore(string rootDirectory, int maximumBytes = DefaultMaximumBytes)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("A storage root is required.", nameof(rootDirectory));
            }

            if (maximumBytes <= 0 || maximumBytes > MaximumSupportedBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }

            _rootDirectory = Path.GetFullPath(rootDirectory);
            _rootPrefix = AppendDirectorySeparator(_rootDirectory);
            _maximumBytes = maximumBytes;
            _pathComparison = UsesCaseInsensitivePaths
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }

        public async UniTask<InputConfigurationReadResult> LoadAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            await UniTask.CompletedTask;
            return InputConfigurationReadResult.Failure(
                InputConfigurationStorageStatus.Unsupported,
                "WebGL persistence requires an explicit browser storage adapter.");
#else
            if (!TryResolveKey(key, out string path, out string error))
            {
                return InputConfigurationReadResult.Failure(InputConfigurationStorageStatus.InvalidKey, error);
            }

            using PathOperationLease operation =
                await AcquirePathOperationAsync(path, cancellationToken);
            if (ContainsReparsePoint(path))
            {
                return InputConfigurationReadResult.Failure(
                    InputConfigurationStorageStatus.InvalidKey,
                    "Symbolic links and reparse points are not valid configuration storage paths.");
            }

            try
            {
                bool recoveredFromBackup = false;
                string readPath = path;
                if (!File.Exists(readPath))
                {
                    string backupPath = path + ".bak";
                    if (!File.Exists(backupPath) || ContainsReparsePoint(backupPath))
                    {
                        return InputConfigurationReadResult.Failure(InputConfigurationStorageStatus.NotFound);
                    }

                    readPath = backupPath;
                    recoveredFromBackup = true;
                }

                using (var stream = new FileStream(
                           readPath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read,
                           4096,
                           FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    if (stream.Length > _maximumBytes)
                    {
                        return InputConfigurationReadResult.Failure(
                            InputConfigurationStorageStatus.TooLarge,
                            $"Configuration exceeds the {_maximumBytes}-byte limit.");
                    }

                    int length = checked((int)stream.Length);
                    var bytes = new byte[length];
                    int totalRead = 0;
                    while (totalRead < length)
                    {
                        int read = await stream.ReadAsync(
                            bytes,
                            totalRead,
                            length - totalRead,
                            cancellationToken);
                        if (read == 0)
                        {
                            break;
                        }

                        totalRead += read;
                    }

                    if (totalRead != length || stream.Length != length)
                    {
                        return InputConfigurationReadResult.Failure(
                            InputConfigurationStorageStatus.IoError,
                            "Configuration changed while it was being read.");
                    }

                    try
                    {
                        return InputConfigurationReadResult.Success(
                            StrictUtf8.GetString(bytes, 0, totalRead),
                            recoveredFromBackup);
                    }
                    catch (DecoderFallbackException)
                    {
                        return InputConfigurationReadResult.Failure(
                            InputConfigurationStorageStatus.InvalidContent,
                            "Configuration content is not valid UTF-8.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                return InputConfigurationReadResult.Failure(
                    InputConfigurationStorageStatus.AccessDenied,
                    "Configuration file access was denied.");
            }
            catch (Exception exception) when (exception is IOException || exception is NotSupportedException)
            {
                return InputConfigurationReadResult.Failure(
                    InputConfigurationStorageStatus.IoError,
                    $"Configuration read failed ({exception.GetType().Name}).");
            }
#endif
        }

        public async UniTask<InputConfigurationStoreResult> SaveAsync(
            string key,
            string content,
            CancellationToken cancellationToken = default)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            await UniTask.CompletedTask;
            return InputConfigurationStoreResult.Failure(
                InputConfigurationStorageStatus.Unsupported,
                "WebGL persistence requires an explicit browser storage adapter.");
#else
            if (!TryResolveKey(key, out string path, out string error))
            {
                return InputConfigurationStoreResult.Failure(InputConfigurationStorageStatus.InvalidKey, error);
            }

            using PathOperationLease operation =
                await AcquirePathOperationAsync(path, cancellationToken);
            string backupPath = path + ".bak";
            if (ContainsReparsePoint(path) || ContainsReparsePoint(backupPath))
            {
                return InputConfigurationStoreResult.Failure(
                    InputConfigurationStorageStatus.InvalidKey,
                    "Symbolic links and reparse points are not valid configuration storage paths.");
            }

            content ??= string.Empty;
            int byteCount;
            try
            {
                byteCount = StrictUtf8.GetByteCount(content);
            }
            catch (EncoderFallbackException)
            {
                return InputConfigurationStoreResult.Failure(
                    InputConfigurationStorageStatus.InvalidContent,
                    "Configuration content must be valid Unicode text.");
            }

            if (byteCount > _maximumBytes)
            {
                return InputConfigurationStoreResult.Failure(
                    InputConfigurationStorageStatus.TooLarge,
                    $"Configuration exceeds the {_maximumBytes}-byte limit.");
            }

            string directory = Path.GetDirectoryName(path);
            string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                byte[] bytes = StrictUtf8.GetBytes(content);
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
                    stream.Flush(true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                CommitTemporaryFile(temporaryPath, path, backupPath);
                return InputConfigurationStoreResult.Success();
            }
            catch (OperationCanceledException)
            {
                TryDeleteTemporaryFile(temporaryPath);
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                TryDeleteTemporaryFile(temporaryPath);
                return InputConfigurationStoreResult.Failure(
                    InputConfigurationStorageStatus.AccessDenied,
                    "Configuration file access was denied.");
            }
            catch (Exception exception) when (exception is IOException || exception is NotSupportedException)
            {
                TryDeleteTemporaryFile(temporaryPath);
                return InputConfigurationStoreResult.Failure(
                    InputConfigurationStorageStatus.IoError,
                    $"Configuration write failed ({exception.GetType().Name}).");
            }
#endif
        }

        public async UniTask<InputConfigurationStoreResult> DeleteAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            await UniTask.CompletedTask;
            return InputConfigurationStoreResult.Failure(
                InputConfigurationStorageStatus.Unsupported,
                "WebGL persistence requires an explicit browser storage adapter.");
#else
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryResolveKey(key, out string path, out string error))
            {
                return InputConfigurationStoreResult.Failure(
                    InputConfigurationStorageStatus.InvalidKey,
                    error);
            }

            using PathOperationLease operation =
                await AcquirePathOperationAsync(path, cancellationToken);
            string backupPath = path + ".bak";
            if (ContainsReparsePoint(path) || ContainsReparsePoint(backupPath))
            {
                return InputConfigurationStoreResult.Failure(
                    InputConfigurationStorageStatus.InvalidKey,
                    "Symbolic links and reparse points are not valid configuration storage paths.");
            }

            return DeleteResolvedPath(path, backupPath);
#endif
        }

        internal InputConfigurationStoreResult TryDeleteSynchronously(string key)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return InputConfigurationStoreResult.Failure(
                InputConfigurationStorageStatus.Unsupported,
                "WebGL persistence requires an explicit browser storage adapter.");
#else
            if (!TryResolveKey(key, out string path, out string error))
            {
                return InputConfigurationStoreResult.Failure(
                    InputConfigurationStorageStatus.InvalidKey,
                    error);
            }

            if (!TryAcquirePathOperation(path, out PathOperationLease operation))
            {
                return InputConfigurationStoreResult.Failure(
                    InputConfigurationStorageStatus.IoError,
                    "The configuration path is busy; retry through the asynchronous storage API.");
            }

            using (operation)
            {
                string backupPath = path + ".bak";
                if (ContainsReparsePoint(path) || ContainsReparsePoint(backupPath))
                {
                    return InputConfigurationStoreResult.Failure(
                        InputConfigurationStorageStatus.InvalidKey,
                        "Symbolic links and reparse points are not valid configuration storage paths.");
                }

                return DeleteResolvedPath(path, backupPath);
            }
#endif
        }

        private static InputConfigurationStoreResult DeleteResolvedPath(
            string path,
            string backupPath)
        {
            try
            {
                // Delete recovery state first. If deleting the primary then fails, the newest
                // primary remains authoritative instead of an older backup being resurrected.
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return InputConfigurationStoreResult.Success();
            }
            catch (UnauthorizedAccessException)
            {
                return InputConfigurationStoreResult.Failure(
                    InputConfigurationStorageStatus.AccessDenied,
                    "Configuration file access was denied.");
            }
            catch (Exception exception) when (exception is IOException || exception is NotSupportedException)
            {
                return InputConfigurationStoreResult.Failure(
                    InputConfigurationStorageStatus.IoError,
                    $"Configuration delete failed ({exception.GetType().Name}).");
            }
        }

        public bool TryResolveKey(string key, out string path, out string error)
        {
            path = null;
            error = null;

            if (string.IsNullOrWhiteSpace(key))
            {
                error = "A non-empty relative key is required.";
                return false;
            }

            if (key.Length > MaximumKeyCharacters)
            {
                error = $"Storage keys cannot exceed {MaximumKeyCharacters} characters.";
                return false;
            }

            if (key.IndexOf('\\') >= 0)
            {
                error = "Storage keys must use '/' as their only path separator.";
                return false;
            }

            if (ContainsForbiddenUnicode(key) ||
                !key.IsNormalized(NormalizationForm.FormC))
            {
                error = "Storage keys must be valid Unicode text normalized to Form C.";
                return false;
            }

            if (Path.IsPathRooted(key) || Uri.TryCreate(key, UriKind.Absolute, out _))
            {
                error = "Rooted paths and absolute URIs are not valid storage keys.";
                return false;
            }

            string[] segments = key.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    error = "Storage keys must use non-empty path segments without dot traversal.";
                    return false;
                }

                if (segment.IndexOfAny(PortableInvalidFileNameCharacters) >= 0 ||
                    segment.EndsWith(".", StringComparison.Ordinal) ||
                    segment.EndsWith(" ", StringComparison.Ordinal) ||
                    IsReservedWindowsDeviceName(segment) ||
                    IsReservedStorageSidecarName(segment))
                {
                    error = "Storage keys must use portable file-name segments.";
                    return false;
                }

                if (ContainsForbiddenUnicode(segment) ||
                    !segment.IsNormalized(NormalizationForm.FormC))
                {
                    error = "Storage key segments must be valid Unicode text normalized to Form C.";
                    return false;
                }

                if (StrictUtf8.GetByteCount(segment) > MaximumSegmentUtf8Bytes)
                {
                    error = $"Storage key segments cannot exceed {MaximumSegmentUtf8Bytes} UTF-8 bytes.";
                    return false;
                }
            }

            try
            {
                string candidate = _rootDirectory;
                for (int i = 0; i < segments.Length; i++)
                {
                    candidate = Path.Combine(candidate, segments[i]);
                }

                candidate = Path.GetFullPath(candidate);
                if (!candidate.StartsWith(_rootPrefix, _pathComparison))
                {
                    error = "The key resolves outside the configured storage root.";
                    return false;
                }

                path = candidate;
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                error = "The storage key is not a valid path on this platform.";
                return false;
            }
        }

        private static string AppendDirectorySeparator(string path)
        {
            string separator = Path.DirectorySeparatorChar.ToString();
            return path.EndsWith(separator, StringComparison.Ordinal) ? path : path + separator;
        }

        private static bool ContainsForbiddenUnicode(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                UnicodeCategory category;
                if (char.IsHighSurrogate(current))
                {
                    if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    {
                        return true;
                    }

                    category = CharUnicodeInfo.GetUnicodeCategory(value, index);
                    index++;
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

        private static bool IsReservedWindowsDeviceName(string segment)
        {
            int extensionIndex = segment.IndexOf('.');
            string stem = extensionIndex < 0 ? segment : segment.Substring(0, extensionIndex);
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (stem.Length == 4 &&
                (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)))
            {
                return stem[3] >= '1' && stem[3] <= '9';
            }

            return false;
        }

        private static bool IsReservedStorageSidecarName(string segment)
        {
            return segment.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                   segment.IndexOf(".tmp-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   segment.IndexOf(".bak.tmp.", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool ContainsReparsePoint(string candidate)
        {
            try
            {
                string current = File.Exists(candidate) || Directory.Exists(candidate)
                    ? candidate
                    : Path.GetDirectoryName(candidate);

                while (!string.IsNullOrEmpty(current))
                {
                    if ((File.Exists(current) || Directory.Exists(current)) &&
                        (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        return true;
                    }

                    string parent = Path.GetDirectoryName(current);
                    if (string.IsNullOrEmpty(parent) || parent.Equals(current, _pathComparison))
                    {
                        break;
                    }

                    current = parent;
                }
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is NotSupportedException)
            {
                // Resolution will fail safely during the actual operation. Avoid treating the path as trusted here.
                return true;
            }

            return false;
        }

        private static async UniTask<PathOperationLease> AcquirePathOperationAsync(
            string path,
            CancellationToken cancellationToken)
        {
            string gateKey = path.IsNormalized(NormalizationForm.FormC)
                ? path
                : path.Normalize(NormalizationForm.FormC);
            PathOperationGate gate;
            lock (OperationGatesSync)
            {
                if (!OperationGates.TryGetValue(gateKey, out gate))
                {
                    gate = new PathOperationGate();
                    OperationGates.Add(gateKey, gate);
                }

                gate.ReferenceCount++;
            }

            bool entered = false;
            try
            {
                await gate.Semaphore.WaitAsync(cancellationToken);
                entered = true;
                return new PathOperationLease(gateKey, gate);
            }
            finally
            {
                if (!entered)
                {
                    ReturnPathOperationGate(gateKey, gate);
                }
            }
        }

        private static bool TryAcquirePathOperation(string path, out PathOperationLease lease)
        {
            lease = null;
            string gateKey = path.IsNormalized(NormalizationForm.FormC)
                ? path
                : path.Normalize(NormalizationForm.FormC);
            PathOperationGate gate;
            lock (OperationGatesSync)
            {
                if (!OperationGates.TryGetValue(gateKey, out gate))
                {
                    gate = new PathOperationGate();
                    OperationGates.Add(gateKey, gate);
                }

                gate.ReferenceCount++;
            }

            bool entered = false;
            try
            {
                entered = gate.Semaphore.Wait(0);
                if (!entered) return false;
                lease = new PathOperationLease(gateKey, gate);
                return true;
            }
            finally
            {
                if (!entered)
                {
                    ReturnPathOperationGate(gateKey, gate);
                }
            }
        }

        private static void ReturnPathOperationGate(string path, PathOperationGate gate)
        {
            bool dispose = false;
            lock (OperationGatesSync)
            {
                gate.ReferenceCount--;
                if (gate.ReferenceCount == 0 &&
                    OperationGates.TryGetValue(path, out PathOperationGate current) &&
                    ReferenceEquals(current, gate))
                {
                    OperationGates.Remove(path);
                    dispose = true;
                }
            }

            if (dispose)
            {
                gate.Semaphore.Dispose();
            }
        }

        private sealed class PathOperationGate
        {
            internal readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);
            internal int ReferenceCount;
        }

        private sealed class PathOperationLease : IDisposable
        {
            private readonly string _path;
            private PathOperationGate _gate;

            internal PathOperationLease(string path, PathOperationGate gate)
            {
                _path = path;
                _gate = gate;
            }

            public void Dispose()
            {
                PathOperationGate gate = Interlocked.Exchange(ref _gate, null);
                if (gate == null) return;
                gate.Semaphore.Release();
                ReturnPathOperationGate(_path, gate);
            }
        }

        private static void CommitTemporaryFile(string temporaryPath, string targetPath, string backupPath)
        {
            if (!File.Exists(targetPath))
            {
                File.Move(temporaryPath, targetPath);
                return;
            }

            try
            {
                File.Replace(temporaryPath, targetPath, backupPath, true);
            }
            catch (PlatformNotSupportedException)
            {
                CommitWithRestoreFallback(temporaryPath, targetPath, backupPath);
            }
        }

        private static void CommitWithRestoreFallback(
            string temporaryPath,
            string targetPath,
            string backupPath)
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(targetPath, backupPath);
            try
            {
                File.Move(temporaryPath, targetPath);
            }
            catch
            {
                if (!File.Exists(targetPath) && File.Exists(backupPath))
                {
                    File.Move(backupPath, targetPath);
                }

                throw;
            }
        }

        private static void TryDeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // A stale temporary file is recoverable and must not hide the original failure.
            }
        }
    }
}
