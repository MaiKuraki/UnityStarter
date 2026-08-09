using UnityEditor;

namespace CycloneGames.DataTable.Unity.Editor
{
    /// <summary>
    /// Single assembly-local entry point for inspecting and operating the Luban pipeline.
    /// </summary>
    internal static class DataTableLubanAuthoring
    {
        internal static bool IsBusy =>
            DataTableLubanAuthoringCoordinator.IsInspecting ||
            DataTableLubanAuthoringCoordinator.IsOperationInProgress;

        internal static void RefreshStatus(DataTableLubanSettings settings)
        {
            DataTableLubanAuthoringCoordinator.RequestInspection(
                settings,
                force: true,
                publishDiagnostics: true);
        }

        internal static void SaveSettings(DataTableLubanSettings settings)
        {
            DataTableLubanAuthoringCoordinator.SaveSettings(settings);
        }

        internal static void Generate(DataTableLubanSettings settings)
        {
            DataTableLubanAuthoringCoordinator.ExecuteOperation(
                settings,
                DataTableLubanOperation.Generate);
        }

        internal static void Check(DataTableLubanSettings settings)
        {
            DataTableLubanAuthoringCoordinator.ExecuteOperation(
                settings,
                DataTableLubanOperation.Check);
        }

        internal static void Recover(DataTableLubanSettings settings)
        {
            DataTableLubanAuthoringCoordinator.ExecuteOperation(
                settings,
                DataTableLubanOperation.Recover);
        }

        internal static bool CancelActiveOperation()
        {
            return DataTableLubanAuthoringCoordinator.RequestSafeCancellation();
        }

        [MenuItem("Tools/CycloneGames/DataTable/Open Settings", priority = 2100)]
        private static void OpenSettingsMenu()
        {
            if (!TryLoadForMenu(out DataTableLubanSettings settings))
            {
                return;
            }

            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
            DataTableEditorDiagnostics.Publish(
                DataTableDiagnosticLevel.Info,
                "Opened DataTable Luban settings at '" +
                AssetDatabase.GetAssetPath(settings) + "'.");
        }

        [MenuItem("Tools/CycloneGames/DataTable/Generate", priority = 2120)]
        private static void GenerateMenu()
        {
            if (TryLoadForMenu(out DataTableLubanSettings settings))
            {
                Generate(settings);
            }
        }

        [MenuItem("Tools/CycloneGames/DataTable/Check", priority = 2121)]
        private static void CheckMenu()
        {
            if (TryLoadForMenu(out DataTableLubanSettings settings))
            {
                Check(settings);
            }
        }

        [MenuItem("Tools/CycloneGames/DataTable/Recover", priority = 2122)]
        private static void RecoverMenu()
        {
            if (TryLoadForMenu(out DataTableLubanSettings settings))
            {
                Recover(settings);
            }
        }

        private static bool TryLoadForMenu(out DataTableLubanSettings settings)
        {
            if (DataTableLubanSettings.TryLoad(out settings, out string error))
            {
                return true;
            }

            DataTableEditorDiagnostics.Publish(DataTableDiagnosticLevel.Warning, error);
            EditorUtility.DisplayDialog("DataTable Luban", error, "OK");
            return false;
        }
    }
}
