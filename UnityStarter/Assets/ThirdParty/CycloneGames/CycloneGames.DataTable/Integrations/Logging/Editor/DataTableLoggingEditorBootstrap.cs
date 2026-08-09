using UnityEditor;

namespace CycloneGames.DataTable.Unity.Editor.Logging
{
    /// <summary>
    /// Optional Editor composition root for CycloneGames.Logging. It never replaces a sink owned
    /// by the project or another integration.
    /// </summary>
    [InitializeOnLoad]
    internal static class DataTableLoggingEditorBootstrap
    {
        private static readonly DataTableLogWriterAdapter OwnedAdapter =
            new DataTableLogWriterAdapter();

        static DataTableLoggingEditorBootstrap()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseOwnedDiagnostics;
            EditorApplication.quitting += ReleaseOwnedDiagnostics;
            InstallIfAvailable();
        }

        internal static void InstallIfAvailable()
        {
            DataTableDiagnostics.TryInstall(OwnedAdapter);
        }

        internal static void ReleaseOwnedDiagnostics()
        {
            DataTableDiagnostics.TryReset(OwnedAdapter);
        }
    }
}
