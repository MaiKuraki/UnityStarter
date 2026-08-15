namespace CycloneGames.Analyzers.Verifier
{
    /// <summary>
    /// Locates analyzer and Unity project directories through marker files instead of names or absolute paths,
    /// so the verifier keeps working after project renames, repository moves, or analyzer relocation.
    /// </summary>
    internal static class UnityProjectLocator
    {
        internal static string FindUnityProjectRoot(string startDirectory)
        {
            DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(startDirectory));
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "ProjectSettings", "ProjectVersion.txt")) &&
                    Directory.Exists(Path.Combine(current.FullName, "Assets")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate a Unity project root (a directory containing ProjectSettings/ProjectVersion.txt and Assets) from: " +
                startDirectory);
        }

        internal static string FindSolutionRoot(string startDirectory)
        {
            DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(startDirectory));
            while (current != null)
            {
                if (File.Exists(Path.Combine(
                        current.FullName,
                        "CycloneGames.Analyzers.Unity",
                        "CycloneGames.Analyzers.Unity.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate the analyzer solution root (a directory containing CycloneGames.Analyzers.Unity/CycloneGames.Analyzers.Unity.csproj) from: " +
                startDirectory);
        }
    }
}
