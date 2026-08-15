using System;
using System.IO;

namespace CycloneGames.Analyzers.Tests
{
    internal static class AnalyzerTestPaths
    {
        private static readonly Lazy<string> UnityProjectRoot =
            new Lazy<string>(FindUnityProjectRoot, isThreadSafe: true);

        internal static string ResolveProjectRelativePath(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) || Path.IsPathRooted(sourcePath))
            {
                return sourcePath;
            }

            return Path.GetFullPath(Path.Combine(
                UnityProjectRoot.Value,
                sourcePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string FindUnityProjectRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                string marker = Path.Combine(
                    current.FullName,
                    "ProjectSettings",
                    "ProjectVersion.txt");
                if (File.Exists(marker) &&
                    Directory.Exists(Path.Combine(current.FullName, "Assets")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate the Unity project root for analyzer source-path tests.");
        }
    }
}
