using System.Text;

namespace CycloneGames.Analyzers.Verifier
{
    /// <summary>
    /// Creates and owns an isolated temporary Unity project below the operating-system temporary directory.
    /// Deletion is guarded by an owner-marker file, so only a directory this instance created can be removed.
    /// Retained projects (failures, unconfirmed termination, or --keep-temporary-project) stay on disk with
    /// their path printed for diagnosis.
    /// </summary>
    internal sealed class TemporaryUnityProject : IDisposable
    {
        private const string OwnerMarkerFileName = ".cg-analyzer-verifier.owner";
        private readonly string _ownerToken = Guid.NewGuid().ToString("N");
        private bool _retained;
        private bool _disposed;

        private TemporaryUnityProject(string root)
        {
            Root = root;
        }

        internal string Root { get; }

        internal static TemporaryUnityProject Create()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "CycloneGames.AnalyzerVerification-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            var project = new TemporaryUnityProject(root);
            File.WriteAllText(
                Path.Combine(root, OwnerMarkerFileName),
                project._ownerToken);
            return project;
        }

        internal void Prepare(
            string installedAnalyzer,
            string installedAnalyzerMeta,
            string fixtureSource,
            string projectVersion)
        {
            string analyzerDirectory = Path.Combine(Root, "Assets", "Analyzers");
            string projectSettingsDirectory = Path.Combine(Root, "ProjectSettings");
            string packagesDirectory = Path.Combine(Root, "Packages");
            Directory.CreateDirectory(analyzerDirectory);
            Directory.CreateDirectory(projectSettingsDirectory);
            Directory.CreateDirectory(packagesDirectory);

            File.Copy(installedAnalyzer, Path.Combine(analyzerDirectory, "CycloneGames.Analyzers.dll"));
            File.Copy(installedAnalyzerMeta, Path.Combine(analyzerDirectory, "CycloneGames.Analyzers.dll.meta"));
            File.Copy(projectVersion, Path.Combine(projectSettingsDirectory, "ProjectVersion.txt"));
            File.Copy(fixtureSource, Path.Combine(Root, "Assets", "ForbiddenUnityApiViolation.cs"));
            File.WriteAllText(
                Path.Combine(packagesDirectory, "manifest.json"),
                "{\n  \"dependencies\": {}\n}\n",
                new UTF8Encoding(false));
        }

        internal void RetainForDiagnosis()
        {
            _retained = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_retained)
            {
                return;
            }

            string markerPath = Path.Combine(Root, OwnerMarkerFileName);
            if (!File.Exists(markerPath) ||
                !string.Equals(
                    File.ReadAllText(markerPath).Trim(),
                    _ownerToken,
                    StringComparison.Ordinal))
            {
                // Never delete a directory this instance did not create and own.
                return;
            }

            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                // Best-effort: a still-locked file (e.g. Unity holding the log) leaves the directory behind.
                // The owner marker proves it is disposable, so this is safe.
            }
        }
    }
}
