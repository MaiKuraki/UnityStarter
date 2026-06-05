#if UNITY_EDITOR
using CycloneGames.Localization.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    [CustomEditor(typeof(LocalizationCatalog))]
    public sealed class LocalizationCatalogEditor : UnityEditor.Editor
    {
        private bool _showStringTables;
        private bool _showAssetTables;

        public override void OnInspectorGUI()
        {
            var catalog = (LocalizationCatalog)target;
            DrawSummary(catalog);
            DrawBuildActions();
            DrawGeneratedTables(catalog);
        }

        private void DrawSummary(LocalizationCatalog catalog)
        {
            int stringEntryCount = CountStringEntries(catalog);
            int assetEntryCount = CountAssetEntries(catalog);

            EditorGUILayout.LabelField("Catalog Summary", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Schema Version", catalog.SchemaVersion);
                EditorGUILayout.TextField("Catalog Version", catalog.CatalogVersion);
                EditorGUILayout.TextField("Content Hash", catalog.ContentHash);
                EditorGUILayout.IntField("String Tables", catalog.StringTables.Count);
                EditorGUILayout.IntField("String Entries", stringEntryCount);
                EditorGUILayout.IntField("Asset Tables", catalog.AssetTables.Count);
                EditorGUILayout.IntField("Asset Entries", assetEntryCount);
            }

            if (catalog.SchemaVersion != LocalizationCatalog.CurrentSchemaVersion)
            {
                EditorGUILayout.HelpBox(
                    "Catalog schema version does not match the current runtime schema.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
        }

        private void DrawBuildActions()
        {
            EditorGUILayout.LabelField("Build Actions", EditorStyles.boldLabel);
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float width = (rect.width - spacing) * 0.5f;
            Rect buildRect = new Rect(rect.x, rect.y, width, rect.height);
            Rect selectRect = new Rect(rect.x + width + spacing, rect.y, width, rect.height);

            if (GUI.Button(buildRect, "Build From Settings"))
                BuildFromSettings();
            if (GUI.Button(selectRect, "Select Build Settings"))
                LocalizationCatalogBuilder.SelectSettingsMenu();

            EditorGUILayout.Space();
        }

        private void DrawGeneratedTables(LocalizationCatalog catalog)
        {
            EditorGUILayout.LabelField("Generated Tables", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This catalog is generated from localization source tables. Inspect generated table data here when debugging, but edit source tables and build settings instead of this asset.",
                MessageType.Info);

            DrawStringTableList(catalog);
            DrawAssetTableList(catalog);
        }

        private void DrawStringTableList(LocalizationCatalog catalog)
        {
            var tables = catalog.StringTables;
            if (tables.Count == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.LabelField("String Tables", "No generated string tables.");
                return;
            }

            _showStringTables = EditorGUILayout.Foldout(_showStringTables, "String Tables (" + tables.Count + ")", true);
            if (!_showStringTables) return;

            DrawTableHeader();
            using (new EditorGUI.DisabledScope(true))
            {
                for (int i = 0; i < tables.Count; i++)
                {
                    var table = tables[i];
                    if (table == null) continue;
                    DrawTableRow(table.TableId, table.LocaleId.Code, table.Entries.Count);
                }
            }
        }

        private void DrawAssetTableList(LocalizationCatalog catalog)
        {
            var tables = catalog.AssetTables;
            if (tables.Count == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.LabelField("Asset Tables", "No generated asset tables.");
                return;
            }

            _showAssetTables = EditorGUILayout.Foldout(_showAssetTables, "Asset Tables (" + tables.Count + ")", true);
            if (!_showAssetTables) return;

            DrawTableHeader();
            using (new EditorGUI.DisabledScope(true))
            {
                for (int i = 0; i < tables.Count; i++)
                {
                    var table = tables[i];
                    if (table == null) continue;
                    DrawTableRow(table.TableId, table.LocaleId.Code, table.Entries.Count);
                }
            }
        }

        private static void DrawTableHeader()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            float third = rect.width / 3f;
            EditorGUI.LabelField(new Rect(rect.x, rect.y, third, rect.height), "Table ID", EditorStyles.miniBoldLabel);
            EditorGUI.LabelField(new Rect(rect.x + third, rect.y, third, rect.height), "Locale", EditorStyles.miniBoldLabel);
            EditorGUI.LabelField(new Rect(rect.x + third * 2f, rect.y, third, rect.height), "Entries", EditorStyles.miniBoldLabel);
        }

        private static void DrawTableRow(string tableId, string localeCode, int entryCount)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            float third = rect.width / 3f;
            EditorGUI.TextField(new Rect(rect.x, rect.y, third - 2f, rect.height), tableId);
            EditorGUI.TextField(new Rect(rect.x + third, rect.y, third - 2f, rect.height), localeCode);
            EditorGUI.IntField(new Rect(rect.x + third * 2f, rect.y, third, rect.height), entryCount);
        }

        private static void BuildFromSettings()
        {
            LocalizationCatalogBuildSettings settings = LocalizationCatalogBuilder.FindSettingsAsset();
            if (settings == null)
            {
                EditorUtility.DisplayDialog(
                    "Localization Catalog",
                    "No LocalizationCatalogBuildSettings asset was found.",
                    "OK");
                return;
            }

            try
            {
                LocalizationCatalogBuilder.BuildCatalog(settings);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Localization Catalog",
                    "Catalog build failed. See Console for details.",
                    "OK");
            }
        }

        private static int CountStringEntries(LocalizationCatalog catalog)
        {
            int count = 0;
            var tables = catalog.StringTables;
            for (int i = 0; i < tables.Count; i++)
            {
                var table = tables[i];
                if (table != null)
                    count += table.Entries.Count;
            }

            return count;
        }

        private static int CountAssetEntries(LocalizationCatalog catalog)
        {
            int count = 0;
            var tables = catalog.AssetTables;
            for (int i = 0; i < tables.Count; i++)
            {
                var table = tables[i];
                if (table != null)
                    count += table.Entries.Count;
            }

            return count;
        }
    }
}
#endif
