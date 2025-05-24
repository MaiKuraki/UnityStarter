using UnityEngine;
using System.IO; // For Path.Combine

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Defines the source location type for a file path.
    /// </summary>
    public enum UnityPathSource
    {
        /// <summary>
        /// File is located in the Assets/StreamingAssets folder.
        /// Path provided should be relative to the StreamingAssets folder (e.g., "Data/MyFile.json").
        /// </summary>
        StreamingAssets,

        /// <summary>
        /// File is located in Application.persistentDataPath.
        /// Path provided should be relative to the persistentDataPath folder (e.g., "UserContent/MyFile.dat").
        /// </summary>
        PersistentData,

        /// <summary>
        /// Path provided is an absolute file path on the system, or already a fully formatted URI
        /// (e.g., "http://...", "https://...", "file:///...").
        /// If an absolute file path, it will be converted to a "file:///" URI.
        /// </summary>
        AbsoluteOrFullUri
    }

    /// <summary>
    /// Utility class for generating platform-correct URIs for use with UnityWebRequest.
    /// </summary>
    public static class FilePathUtility
    {
        /// <summary>
        /// Gets a platform-correct URI suitable for use with UnityWebRequest.
        /// </summary>
        /// <param name="path">
        /// The path to the file. Its interpretation depends on the pathSource:
        /// - StreamingAssets: Relative path within StreamingAssets folder (e.g., "Textures/MyImage.png").
        /// - PersistentData: Relative path within Application.persistentDataPath (e.g., "Saves/MySave.json").
        /// - AbsoluteOrFullUri: An absolute file system path or a pre-formatted URI (http, https, file).
        /// </param>
        /// <param name="pathSource">The source location of the file.</param>
        /// <returns>A URI string suitable for UnityWebRequest, or null if inputs are invalid.</returns>
        public static string GetUnityWebRequestUri(string path, UnityPathSource pathSource)
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[UnityPathUtility] Path cannot be null or empty.");
                return null;
            }

            switch (pathSource)
            {
                case UnityPathSource.StreamingAssets:
                    return GetStreamingAssetsUri(path);

                case UnityPathSource.PersistentData:
                    // Path.Combine correctly handles joining parts of a path.
                    // TrimStart ensures relative path doesn't cause issues with Path.Combine if it starts with a slash.
                    string persistentFullPath = Path.Combine(Application.persistentDataPath, path.TrimStart('/', '\\'));
                    return FormatAbsolutePathAsFileUri(persistentFullPath);

                case UnityPathSource.AbsoluteOrFullUri:
                    // Check if it's already a known URI scheme.
                    // Using OrdinalIgnoreCase for robust comparison.
                    if (path.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWith("jar:file://",
                            System.StringComparison
                                .OrdinalIgnoreCase)) // Common for Android StreamingAssets if pre-formatted
                    {
                        return path; // It's a web or jar URI, return as is.
                    }

                    // If it starts with "file://", it's already a file URI (could be file:// or file:///).
                    // FormatAbsolutePathAsFileUri will handle and normalize it (e.g., ensure it's file:/// where appropriate).
                    // Otherwise, assume it's a raw absolute file system path that needs conversion.
                    return FormatAbsolutePathAsFileUri(path);

                default:
                    Debug.LogError($"[UnityPathUtility] Unsupported UnityPathSource: {pathSource}");
                    return null;
            }
        }

        private static string GetStreamingAssetsUri(string relativePath)
        {
            // Clean the relative path, removing leading slashes to ensure consistent behavior
            // with Path.Combine or direct string concatenation.
            string cleanRelativePath = relativePath.TrimStart('/', '\\');
            if (string.IsNullOrEmpty(cleanRelativePath))
            {
                Debug.LogError(
                    "[UnityPathUtility] Relative path for StreamingAssets is effectively empty after trimming. Cannot construct URI for a directory or empty file name.");
                return null; // Loading an empty file name or just a directory via UWR is usually not intended.
            }

#if UNITY_EDITOR
            // In the editor, Application.streamingAssetsPath is a direct file path to Project/Assets/StreamingAssets.
            string editorPath = Path.Combine(Application.streamingAssetsPath, cleanRelativePath);
            return FormatAbsolutePathAsFileUri(editorPath);
#elif UNITY_ANDROID
            // On Android, StreamingAssets files are typically within the APK/AAB.
            // Application.streamingAssetsPath usually looks like "jar:file:///data/app/your-package-name.apk!/assets".
            // UnityWebRequest handles these "jar:" URIs directly.
            // Unity's documentation recommends direct concatenation for these paths when used with UnityWebRequest.
            return Application.streamingAssetsPath + "/" + cleanRelativePath;
#elif UNITY_IOS
            // On iOS, Application.streamingAssetsPath points to a direct file path within the app bundle (e.g., AppName.app/Data/Raw).
            string iosPath = Path.Combine(Application.streamingAssetsPath, cleanRelativePath);
            return FormatAbsolutePathAsFileUri(iosPath);
#elif UNITY_WEBGL
            // For WebGL, Application.streamingAssetsPath is a URL (either relative to the deployment or an absolute one).
            // Path.Combine generally works for joining URL segments. UnityWebRequest can resolve relative URLs.
            return Path.Combine(Application.streamingAssetsPath, cleanRelativePath);
#else // Standalone platforms (Windows, Mac, Linux)
            // Application.streamingAssetsPath is a direct file path to the _Data/StreamingAssets/ folder.
            string standalonePath = Path.Combine(Application.streamingAssetsPath, cleanRelativePath);
            return FormatAbsolutePathAsFileUri(standalonePath);
#endif
        }

        /// <summary>
        /// Formats a given absolute file path (or a "file://" prefixed path) into a standard "file:///" URI.
        /// </summary>
        private static string FormatAbsolutePathAsFileUri(string absolutePathOrPotentialFileUri)
        {
            if (string.IsNullOrEmpty(absolutePathOrPotentialFileUri))
            {
                Debug.LogError("[UnityPathUtility] Path for URI formatting is null or empty.");
                return null;
            }

            // If it already seems to be a "file:///" URI, assume it's correctly formatted enough.
            if (absolutePathOrPotentialFileUri.StartsWith("file:///"))
            {
                return absolutePathOrPotentialFileUri;
            }

            // If it starts with "file://", System.Uri constructor will handle it.
            // It might be a local path needing a third slash, or a UNC path (file://server/share).

            try
            {
                // System.Uri is the most reliable way to convert an absolute path to a standard file URI.
                // It handles spaces, special characters (by percent-encoding them), and platform-specific path details.
                // E.g.: "C:\My Files\data.txt" -> "file:///C:/My%20Files/data.txt"
                //       "/Users/user/My Files/data.txt" -> "file:///Users/user/My%20Files/data.txt"
                System.Uri fileUri = new System.Uri(absolutePathOrPotentialFileUri);

                // Check if System.Uri recognized it as a file path.
                if (fileUri.IsFile)
                {
                    return fileUri.AbsoluteUri; // This will be the "file:///" formatted URI.
                }
                else
                {
                    // This case might occur if a non-file URI (e.g., "http://...") was mistakenly passed here,
                    // or if the path was so malformed System.Uri couldn't parse it as a file.
                    // The main GetUnityWebRequestUri method already filters out http/https/jar URIs.
                    Debug.LogWarning(
                        $"[UnityPathUtility] Path '{absolutePathOrPotentialFileUri}' was parsed by System.Uri but its scheme ('{fileUri.Scheme}') is not 'file'. Returning original path, but this might cause issues with UnityWebRequest if a file URI was expected.");
                    return absolutePathOrPotentialFileUri; // Fallback, though potentially problematic.
                }
            }
            catch (System.UriFormatException ex)
            {
                Debug.LogError(
                    $"[UnityPathUtility] Failed to parse path '{absolutePathOrPotentialFileUri}' as a URI: {ex.Message}. Ensure it's a valid absolute path or a 'file://' prefixed URI.");
                return null;
            }
            catch (System.ArgumentNullException)
            {
                // This should ideally be caught by the initial IsNullOrEmpty check.
                Debug.LogError("[UnityPathUtility] Absolute file path was null when attempting to create System.Uri.");
                return null;
            }
        }
    }
}