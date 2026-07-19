using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CycloneGames.Localization.Editor;
using CycloneGames.Localization.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.Localization.Tests.Editor
{
    public sealed class LocalizationEditorWorkflowTests
    {
        [TestCase(80f, 90f, 100f, 100f, false, false)]
        [TestCase(80f, 101f, 85f, 100f, false, true)]
        [TestCase(90f, 101f, 85f, 85f, true, true)]
        [TestCase(101f, 90f, 85f, 85f, true, true)]
        public void ScrollViewportReservesScrollbarExtentsAndTheirCoupledOverflow(
            float contentWidth,
            float contentHeight,
            float expectedWidth,
            float expectedHeight,
            bool expectedHorizontal,
            bool expectedVertical)
        {
            LocalizationScrollViewport viewport = LocalizationScrollViewport.Calculate(
                100f,
                100f,
                contentWidth,
                contentHeight,
                15f,
                15f);

            Assert.That(viewport.Width, Is.EqualTo(expectedWidth).Within(0.001f));
            Assert.That(viewport.Height, Is.EqualTo(expectedHeight).Within(0.001f));
            Assert.That(viewport.ShowsHorizontalScrollbar, Is.EqualTo(expectedHorizontal));
            Assert.That(viewport.ShowsVerticalScrollbar, Is.EqualTo(expectedVertical));
        }

        [Test]
        public void ExportProfilesMapIntentToEncodingWithoutAllowingInvalidCombinations()
        {
            var spreadsheet = new LocalizationCsvExportSelection(
                LocalizationCsvExportProfile.Spreadsheet,
                false,
                null,
                true);
            var automation = new LocalizationCsvExportSelection(
                LocalizationCsvExportProfile.Automation,
                true,
                3,
                false);

            Assert.That(spreadsheet.Encoding, Is.EqualTo(LocalizationCsvEncoding.Utf8WithBom));
            Assert.That(spreadsheet.FilteredOnly, Is.False);
            Assert.That(spreadsheet.TargetColumnIndex, Is.Null);
            Assert.That(spreadsheet.RegisteredLocalesOnly, Is.True);
            Assert.That(automation.Encoding, Is.EqualTo(LocalizationCsvEncoding.Utf8WithoutBom));
            Assert.That(automation.FilteredOnly, Is.True);
            Assert.That(automation.TargetColumnIndex, Is.EqualTo(3));
            Assert.That(automation.RegisteredLocalesOnly, Is.False);
        }

        [Test]
        public void CsvParserPreservesQuotedNewlinesQuotesAndUnicode()
        {
            const string csv = "Key,en,zh-CN\r\ngreeting,Hello,你好\r\nmultiline,\"First line\r\nSecond \"\"quoted\"\" line\",\"第一行\n第二行\"";

            bool parsed = LocalizationCsv.TryParse(
                csv,
                LocalizationCsvLimits.Default,
                out LocalizationCsvDocument document,
                out string error);

            Assert.That(parsed, Is.True, error);
            Assert.That(document.Rows.Count, Is.EqualTo(3));
            Assert.That(document.Rows[2][1], Is.EqualTo("First line\r\nSecond \"quoted\" line"));
            Assert.That(document.Rows[2][2], Is.EqualTo("第一行\n第二行"));
        }

        [Test]
        public void CsvParserRejectsInputBeyondByteLimitBeforeCreatingDocument()
        {
            var limits = new LocalizationCsvLimits(8, 10, 10, 10, 100);

            bool parsed = LocalizationCsv.TryParse("Key,语言", limits, out LocalizationCsvDocument document, out string error);

            Assert.That(parsed, Is.False);
            Assert.That(document, Is.Null);
            Assert.That(error, Does.Contain("byte limit"));
        }

        [Test]
        public void CsvWriterSupportsBothUtf8EncodingsAndRoundTripsUnicodeMultilineFields()
        {
            AssertCsvEncodingRoundTrip(LocalizationCsvEncoding.Utf8WithBom, true);
            AssertCsvEncodingRoundTrip(LocalizationCsvEncoding.Utf8WithoutBom, false);
        }

        private static void AssertCsvEncodingRoundTrip(
            LocalizationCsvEncoding encoding,
            bool expectsBom)
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
            var rows = new List<string[]>
            {
                new[] { "Key", "zh-CN" },
                new[] { "dialog", "第一行\n第二行 \"引号\", continued" },
            };

            try
            {
                bool written = LocalizationCsv.TryWriteFile(
                    path,
                    rows,
                    LocalizationCsvLimits.Default,
                    encoding,
                    out string writeError);

                Assert.That(written, Is.True, writeError);
                byte[] bytes = File.ReadAllBytes(path);
                Assert.That(bytes.Length, Is.GreaterThan(3));
                bool hasBom = bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                Assert.That(hasBom, Is.EqualTo(expectsBom));
                int offset = hasBom ? 3 : 0;
                Assert.That(Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset), Does.StartWith("Key,zh-CN\r\n"));

                bool parsed = LocalizationCsv.TryReadFile(
                    path,
                    LocalizationCsvLimits.Default,
                    out LocalizationCsvDocument document,
                    out string readError);

                Assert.That(parsed, Is.True, readError);
                Assert.That(document.Rows[1][1], Is.EqualTo(rows[1][1]));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Test]
        public void CsvWriterCountsBomAgainstOutputByteLimit()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
            var rows = new List<string[]> { new[] { "A" } };
            var limits = new LocalizationCsvLimits(3, 10, 10, 10, 100);

            try
            {
                bool written = LocalizationCsv.TryWriteFile(
                    path,
                    rows,
                    limits,
                    LocalizationCsvEncoding.Utf8WithBom,
                    out string error);

                Assert.That(written, Is.False);
                Assert.That(error, Does.Contain("byte limit"));
                Assert.That(File.Exists(path), Is.False);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Test]
        public void MetadataEntryRemovalRejectsLockedEntryAndUndoRestoresRemovedData()
        {
            var metadata = ScriptableObject.CreateInstance<StringTableMetadata>();
            try
            {
                var serialized = new SerializedObject(metadata);
                SerializedProperty entries = serialized.FindProperty("entries");
                entries.arraySize = 2;

                SerializedProperty editableEntry = entries.GetArrayElementAtIndex(0);
                editableEntry.FindPropertyRelative("Key").stringValue = "editable";
                editableEntry.FindPropertyRelative("Comment").stringValue = "Translator context";
                editableEntry.FindPropertyRelative("SourceRevision").intValue = 7;

                SerializedProperty lockedEntry = entries.GetArrayElementAtIndex(1);
                lockedEntry.FindPropertyRelative("Key").stringValue = "locked";
                lockedEntry.FindPropertyRelative("Locked").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var index = new LocalizationMetadataIndex();
                index.Bind(metadata);

                bool removedLocked = index.RemoveEntry(
                    "locked",
                    "Remove Locked Metadata Test",
                    out string lockedError);
                Assert.That(removedLocked, Is.False);
                Assert.That(lockedError, Does.Contain("unlock"));
                Assert.That(index.Contains("locked"), Is.True);

                bool removedEditable = index.RemoveEntry(
                    "editable",
                    "Remove Metadata Test",
                    out string removeError);
                Assert.That(removedEditable, Is.True, removeError);
                Assert.That(index.Contains("editable"), Is.False);
                Assert.That(index.Contains("locked"), Is.True);

                Undo.PerformUndo();
                index.Update();
                index.Rebuild();
                Assert.That(index.Contains("editable"), Is.True);
                Assert.That(index.Contains("locked"), Is.True);
                Assert.That(
                    index.GetEntry("editable").FindPropertyRelative("Comment").stringValue,
                    Is.EqualTo("Translator context"));
                Assert.That(
                    index.GetEntry("editable").FindPropertyRelative("SourceRevision").intValue,
                    Is.EqualTo(7));
            }
            finally
            {
                Object.DestroyImmediate(metadata);
            }
        }

        [TestCase("../Assets/Generated", "Catalog.asset")]
        [TestCase("Assets/../Outside", "Catalog.asset")]
        [TestCase("Assets/Generated", "../Catalog.asset")]
        [TestCase("C:/Project/Assets", "Catalog.asset")]
        [TestCase("Assets/Generated", "CON.asset")]
        public void CatalogPathRejectsTraversalAbsoluteAndNonPortablePaths(string folder, string fileName)
        {
            bool valid = LocalizationAssetPathUtility.TryNormalizeCatalogAssetPath(
                Path.Combine(Path.GetTempPath(), "LocalizationProject"),
                folder,
                fileName,
                out string assetPath,
                out string error);

            Assert.That(valid, Is.False);
            Assert.That(assetPath, Is.Null);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void CatalogPathNormalizesSafeProjectRelativeAssetPath()
        {
            bool valid = LocalizationAssetPathUtility.TryNormalizeCatalogAssetPath(
                Path.Combine(Path.GetTempPath(), "LocalizationProject"),
                "Assets/Generated/Localization",
                "RuntimeCatalog",
                out string assetPath,
                out string error);

            Assert.That(valid, Is.True, error);
            Assert.That(assetPath, Is.EqualTo("Assets/Generated/Localization/RuntimeCatalog.asset"));
        }

        [Test]
        public void VisibleRowCacheFindsOnlyRowsIntersectingViewport()
        {
            var cache = new LocalizationVisibleRowCache();
            cache.Rebuild(8, index => (index & 1) == 0, index => index == 2 ? 40f : 20f);

            LocalizationVisibleRange range = cache.FindVisibleRange(21f, 38f);

            Assert.That(cache.Count, Is.EqualTo(4));
            Assert.That(range.Start, Is.EqualTo(1));
            Assert.That(range.EndExclusive, Is.EqualTo(2));
            Assert.That(cache.GetSourceIndex(range.Start), Is.EqualTo(2));
            Assert.That(cache.GetTop(range.Start), Is.EqualTo(20f));
            Assert.That(cache.GetHeight(range.Start), Is.EqualTo(40f));
        }

        [Test]
        public void AuthoringLocaleUsesExplicitValueThenFallsBackToDefault()
        {
            var settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            var authoring = ScriptableObject.CreateInstance<Locale>();
            var fallback = ScriptableObject.CreateInstance<Locale>();

            try
            {
                SetLocaleCode(authoring, "en");
                SetLocaleCode(fallback, "fr");
                var serialized = new SerializedObject(settings);
                serialized.FindProperty("defaultLocale").objectReferenceValue = fallback;
                serialized.FindProperty("authoringLocale").objectReferenceValue = authoring;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                bool resolved = LocalizationEditorSettingsUtility.TryResolveAuthoringLocale(
                    settings,
                    out Locale resolvedLocale,
                    out string error);

                Assert.That(resolved, Is.True, error);
                Assert.That(resolvedLocale, Is.SameAs(authoring));

                serialized.Update();
                serialized.FindProperty("authoringLocale").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                resolved = LocalizationEditorSettingsUtility.TryResolveAuthoringLocale(
                    settings,
                    out resolvedLocale,
                    out error);

                Assert.That(resolved, Is.True, error);
                Assert.That(resolvedLocale, Is.SameAs(fallback));
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(authoring);
                Object.DestroyImmediate(fallback);
            }
        }

        [Test]
        public void FirstLocaleRegistrationCreatesVisibleSettingsConfiguration()
        {
            string folder = "Assets/__LocalizationEditorWorkflowTests_" + Guid.NewGuid().ToString("N");
            string settingsPath = folder + "/LocalizationSettings.asset";
            Assert.That(AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder)), Is.Not.Empty);
            var settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            AssetDatabase.CreateAsset(settings, settingsPath);

            try
            {
                bool registered = LocalizationLocaleAssetUtility.TryEnsureRegistered(
                    settings,
                    "qx-test123",
                    out Locale locale,
                    out bool created,
                    out string error);

                Assert.That(registered, Is.True, error);
                Assert.That(created, Is.True);
                Assert.That(locale.Id.Code, Is.EqualTo("qx-test123"));
                Assert.That(settings.DefaultLocale, Is.SameAs(locale));
                Assert.That(settings.AuthoringLocale, Is.SameAs(locale));
                Assert.That(settings.AvailableLocales, Has.Count.EqualTo(1));
                Assert.That(settings.AvailableLocales[0], Is.SameAs(locale));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void StringWorkspacePreservesAndCanRegisterTableLocaleOutsideSettings()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string folder = "Assets/__LocalizationRegistrationStateTests_" + suffix;
            Assert.That(AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder)), Is.Not.Empty);

            var sourceLocale = ScriptableObject.CreateInstance<Locale>();
            var inactiveLocale = ScriptableObject.CreateInstance<Locale>();
            var settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            MultiLanguageStringTableEditor window = null;
            try
            {
                SetLocaleCode(sourceLocale, "en");
                SetLocaleCode(inactiveLocale, "fr");
                AssetDatabase.CreateAsset(sourceLocale, folder + "/Locale_en.asset");
                AssetDatabase.CreateAsset(inactiveLocale, folder + "/Locale_fr.asset");
                AssetDatabase.CreateAsset(settings, folder + "/LocalizationSettings.asset");

                var settingsSerialized = new SerializedObject(settings);
                settingsSerialized.FindProperty("defaultLocale").objectReferenceValue = sourceLocale;
                settingsSerialized.FindProperty("authoringLocale").objectReferenceValue = sourceLocale;
                SerializedProperty locales = settingsSerialized.FindProperty("availableLocales");
                locales.arraySize = 1;
                locales.GetArrayElementAtIndex(0).objectReferenceValue = sourceLocale;
                settingsSerialized.ApplyModifiedPropertiesWithoutUndo();

                string tableId = "registration-state." + suffix;
                CreateStringTableAsset(folder + "/Source.asset", tableId, "en", "title", "Title");
                CreateStringTableAsset(folder + "/Inactive.asset", tableId, "fr", "title", "Titre");

                window = ScriptableObject.CreateInstance<MultiLanguageStringTableEditor>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(MultiLanguageStringTableEditor).GetField("localizationSettings", flags)
                    ?.SetValue(window, settings);
                typeof(MultiLanguageStringTableEditor).GetField("_requestedTableId", flags)
                    ?.SetValue(window, tableId);
                typeof(MultiLanguageStringTableEditor).GetMethod("DiscoverTables", flags)
                    ?.Invoke(window, null);

                var columns = (System.Collections.IEnumerable)typeof(MultiLanguageStringTableEditor)
                    .GetField("_columns", flags)?.GetValue(window);
                object inactiveColumn = null;
                foreach (object column in columns)
                {
                    Type columnType = column.GetType();
                    if (!string.Equals(
                            columnType.GetField("LocaleCode")?.GetValue(column) as string,
                            "fr",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    inactiveColumn = column;
                    Assert.That(columnType.GetField("IsRegistered")?.GetValue(column), Is.False);
                    break;
                }

                Assert.That(inactiveColumn, Is.Not.Null, "The inactive table asset must remain discoverable.");
                var safeExportColumns = (List<int>)typeof(MultiLanguageStringTableEditor)
                    .GetMethod("BuildExportColumnIndices", flags)
                    ?.Invoke(window, new object[] { null, true });
                Assert.That(
                    safeExportColumns,
                    Is.Empty,
                    "The default all-language export must exclude table locales outside Settings.");
                typeof(MultiLanguageStringTableEditor).GetMethod("RegisterExistingLocale", flags)
                    ?.Invoke(window, new[] { inactiveColumn });

                Assert.That(settings.AvailableLocales, Has.Count.EqualTo(2));
                Assert.That(settings.AvailableLocales, Does.Contain(inactiveLocale));
            }
            finally
            {
                if (window != null)
                    Object.DestroyImmediate(window);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void CatalogBuildFilterSelectsLocaleTableAndContentKind()
        {
            var en = ScriptableObject.CreateInstance<Locale>();
            var ja = ScriptableObject.CreateInstance<Locale>();
            try
            {
                SetLocaleCode(en, "en");
                SetLocaleCode(ja, "ja");
                var allowed = new HashSet<string>(StringComparer.Ordinal) { "en", "ja" };

                LocalizationCatalogBuilder.LocalizationCatalogBuildFilter filter =
                    LocalizationCatalogBuilder.LocalizationCatalogBuildFilter.Create(
                        LocalizationCatalogContentKind.Strings,
                        new[] { ja },
                        new[] { "ui" },
                        allowed);

                Assert.That(filter.IncludeStrings, Is.True);
                Assert.That(filter.IncludeAssets, Is.False);
                Assert.That(filter.Includes("ui", "ja"), Is.True);
                Assert.That(filter.Includes("ui", "en"), Is.False);
                Assert.That(filter.Includes("gameplay", "ja"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(en);
                Object.DestroyImmediate(ja);
            }
        }

        [Test]
        public void ConfiguredTableAssetsOpenTheirWorkspace()
        {
            var stringTable = ScriptableObject.CreateInstance<StringTable>();
            var metadata = ScriptableObject.CreateInstance<StringTableMetadata>();
            try
            {
                var tableSerialized = new SerializedObject(stringTable);
                tableSerialized.FindProperty("tableId").stringValue = "ui.open-test";
                tableSerialized.ApplyModifiedPropertiesWithoutUndo();
                var metadataSerialized = new SerializedObject(metadata);
                metadataSerialized.FindProperty("tableId").stringValue = "ui.open-test";
                metadataSerialized.FindProperty("tableType").enumValueIndex = (int)TableType.String;
                metadataSerialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(LocalizationAssetOpenHandler.TryOpen(stringTable), Is.True);
                Assert.That(LocalizationAssetOpenHandler.TryOpen(metadata), Is.True);
                Assert.That(
                    Resources.FindObjectsOfTypeAll<MultiLanguageStringTableEditor>().Length,
                    Is.GreaterThan(0));
            }
            finally
            {
                MultiLanguageStringTableEditor[] windows =
                    Resources.FindObjectsOfTypeAll<MultiLanguageStringTableEditor>();
                for (int index = 0; index < windows.Length; index++)
                    windows[index].Close();
                Object.DestroyImmediate(stringTable);
                Object.DestroyImmediate(metadata);
            }
        }

        [Test]
        public void CatalogBuilderProducesOnlySelectedPartition()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string folder = "Assets/__LocalizationCatalogPartitionTests_" + suffix;
            Assert.That(AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder)), Is.Not.Empty);

            var en = ScriptableObject.CreateInstance<Locale>();
            var ja = ScriptableObject.CreateInstance<Locale>();
            var settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            try
            {
                SetLocaleCode(en, "en");
                SetLocaleCode(ja, "ja");
                AssetDatabase.CreateAsset(en, folder + "/Locale_en.asset");
                AssetDatabase.CreateAsset(ja, folder + "/Locale_ja.asset");
                AssetDatabase.CreateAsset(settings, folder + "/LocalizationSettings.asset");

                var settingsSerialized = new SerializedObject(settings);
                settingsSerialized.FindProperty("defaultLocale").objectReferenceValue = en;
                settingsSerialized.FindProperty("authoringLocale").objectReferenceValue = en;
                SerializedProperty locales = settingsSerialized.FindProperty("availableLocales");
                locales.arraySize = 2;
                locales.GetArrayElementAtIndex(0).objectReferenceValue = en;
                locales.GetArrayElementAtIndex(1).objectReferenceValue = ja;
                settingsSerialized.ApplyModifiedPropertiesWithoutUndo();

                string selectedTableId = "ui." + suffix;
                CreateStringTableAsset(folder + "/UI_en.asset", selectedTableId, "en", "title", "Start");
                CreateStringTableAsset(folder + "/UI_ja.asset", selectedTableId, "ja", "title", "開始");
                CreateStringTableAsset(folder + "/Other_ja.asset", "other." + suffix, "ja", "title", "Other");

                LocalizationCatalogBuildResult result = LocalizationCatalogBuilder.BuildCatalog(
                    new LocalizationCatalogBuildOptions
                    {
                        Settings = settings,
                        OutputPath = folder + "/Catalog.asset",
                        Version = "partition-test",
                        ContentKind = LocalizationCatalogContentKind.Strings,
                        IncludedLocales = new[] { ja },
                        IncludedTableIds = new[] { selectedTableId },
                        ValidateBeforeBuild = false,
                        SelectBuiltAsset = false,
                    });

                Assert.That(result.StringTableCount, Is.EqualTo(1));
                Assert.That(result.AssetTableCount, Is.Zero);
                Assert.That(result.Catalog.StringTables[0].TableId, Is.EqualTo(selectedTableId));
                Assert.That(result.Catalog.StringTables[0].LocaleId.Code, Is.EqualTo("ja"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void StringWorkspaceKeepsAuthoringOrderAndImportsSubsetRowsOnly()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string folder = "Assets/__LocalizationSubsetImportTests_" + suffix;
            Assert.That(AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder)), Is.Not.Empty);

            var en = ScriptableObject.CreateInstance<Locale>();
            var fr = ScriptableObject.CreateInstance<Locale>();
            var settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            MultiLanguageStringTableEditor window = null;
            try
            {
                SetLocaleCode(en, "en");
                SetLocaleCode(fr, "fr");
                AssetDatabase.CreateAsset(en, folder + "/Locale_en.asset");
                AssetDatabase.CreateAsset(fr, folder + "/Locale_fr.asset");
                AssetDatabase.CreateAsset(settings, folder + "/LocalizationSettings.asset");

                var settingsSerialized = new SerializedObject(settings);
                settingsSerialized.FindProperty("defaultLocale").objectReferenceValue = en;
                settingsSerialized.FindProperty("authoringLocale").objectReferenceValue = en;
                SerializedProperty locales = settingsSerialized.FindProperty("availableLocales");
                locales.arraySize = 2;
                locales.GetArrayElementAtIndex(0).objectReferenceValue = en;
                locales.GetArrayElementAtIndex(1).objectReferenceValue = fr;
                settingsSerialized.ApplyModifiedPropertiesWithoutUndo();

                string tableId = "subset." + suffix;
                CreateStringTableAsset(
                    folder + "/Source.asset",
                    tableId,
                    "en",
                    new StringEntry { Key = "z.key", Value = "Source Z" },
                    new StringEntry { Key = "a.key", Value = "Source A" },
                    new StringEntry { Key = "item.10", Value = "Source Item 10" },
                    new StringEntry { Key = "item.2", Value = "Source Item 2" });
                StringTable target = CreateStringTableAsset(
                    folder + "/Target.asset",
                    tableId,
                    "fr",
                    new StringEntry { Key = "z.key", Value = "Old Z" },
                    new StringEntry { Key = "a.key", Value = "Old A" });

                var metadata = ScriptableObject.CreateInstance<StringTableMetadata>();
                var metadataSerialized = new SerializedObject(metadata);
                metadataSerialized.FindProperty("tableId").stringValue = tableId;
                metadataSerialized.FindProperty("tableType").enumValueIndex = (int)TableType.String;
                metadataSerialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(metadata, folder + "/Metadata.asset");

                window = ScriptableObject.CreateInstance<MultiLanguageStringTableEditor>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(MultiLanguageStringTableEditor).GetField("localizationSettings", flags)
                    ?.SetValue(window, settings);
                typeof(MultiLanguageStringTableEditor).GetField("_requestedTableId", flags)
                    ?.SetValue(window, tableId);
                typeof(MultiLanguageStringTableEditor).GetMethod("DiscoverTables", flags)
                    ?.Invoke(window, null);
                typeof(MultiLanguageStringTableEditor).GetMethod("RebuildKeys", flags)
                    ?.Invoke(window, null);

                var orderedKeys = (List<string>)typeof(MultiLanguageStringTableEditor)
                    .GetField("_allKeys", flags)?.GetValue(window);
                Assert.That(orderedKeys, Is.EqualTo(new[] { "z.key", "a.key", "item.10", "item.2" }));

                MethodInfo countExportableKeys = typeof(MultiLanguageStringTableEditor)
                    .GetMethod("CountExportableAuthoringKeys", flags);
                Assert.That(countExportableKeys?.Invoke(window, new object[] { false }), Is.EqualTo(4));
                typeof(MultiLanguageStringTableEditor).GetField("_searchFilter", flags)
                    ?.SetValue(window, "item.");
                Assert.That(countExportableKeys?.Invoke(window, new object[] { true }), Is.EqualTo(2));
                typeof(MultiLanguageStringTableEditor).GetField("_searchFilter", flags)
                    ?.SetValue(window, string.Empty);

                Type sortModeType = typeof(MultiLanguageStringTableEditor).GetNestedType("KeySortMode", flags);
                object naturalSort = Enum.Parse(sortModeType, "NaturalAscending");
                bool sorted = (bool)typeof(MultiLanguageStringTableEditor)
                    .GetMethod("SortAuthoringEntries", flags)
                    ?.Invoke(window, new[] { naturalSort, false });
                Assert.That(sorted, Is.True);
                typeof(MultiLanguageStringTableEditor).GetMethod("RebuildKeys", flags)
                    ?.Invoke(window, null);
                orderedKeys = (List<string>)typeof(MultiLanguageStringTableEditor)
                    .GetField("_allKeys", flags)?.GetValue(window);
                Assert.That(orderedKeys, Is.EqualTo(new[] { "a.key", "item.2", "item.10", "z.key" }));

                Undo.PerformUndo();
                typeof(MultiLanguageStringTableEditor).GetMethod("RefreshColumns", flags)
                    ?.Invoke(window, new object[] { tableId });
                typeof(MultiLanguageStringTableEditor).GetMethod("RebuildKeys", flags)
                    ?.Invoke(window, null);
                orderedKeys = (List<string>)typeof(MultiLanguageStringTableEditor)
                    .GetField("_allKeys", flags)?.GetValue(window);
                Assert.That(orderedKeys, Is.EqualTo(new[] { "z.key", "a.key", "item.10", "item.2" }));

                var document = new LocalizationCsvDocument(new List<string[]>
                {
                    new[] { "Key", "SourceRevision", "en", "fr", "fr.Status", "fr.TranslatedSourceRevision" },
                    new[] { "z.key", "0", "Source Z", "New Z", "Draft", "0" },
                });
                MethodInfo buildPlan = typeof(MultiLanguageStringTableEditor)
                    .GetMethod("TryBuildImportPlan", flags);
                object[] planArguments = { document, null, null };
                bool valid = (bool)buildPlan.Invoke(window, planArguments);
                Assert.That(valid, Is.True, planArguments[2] as string);

                typeof(MultiLanguageStringTableEditor).GetMethod("ApplyImportPlan", flags)
                    ?.Invoke(window, new[] { planArguments[1] });

                Assert.That(GetStringTableValue(target, "z.key"), Is.EqualTo("New Z"));
                Assert.That(GetStringTableValue(target, "a.key"), Is.EqualTo("Old A"));
            }
            finally
            {
                if (window != null)
                    Object.DestroyImmediate(window);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void UndoTransactionRevertsEveryMutationWhenCommitThrows()
        {
            var probe = ScriptableObject.CreateInstance<UndoProbe>();
            probe.Value = 7;

            try
            {
                bool committed = LocalizationUndoTransaction.TryExecute(
                    "Localization Transaction Test",
                    new Object[] { probe },
                    () =>
                    {
                        probe.Value = 42;
                        EditorUtility.SetDirty(probe);
                        throw new InvalidOperationException("Injected failure");
                    },
                    out string error);

                Assert.That(committed, Is.False);
                Assert.That(error, Does.Contain("Injected failure"));
                Assert.That(probe.Value, Is.EqualTo(7));
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        private static void SetLocaleCode(Locale locale, string code)
        {
            var serialized = new SerializedObject(locale);
            serialized.FindProperty("localeCode").stringValue = code;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static StringTable CreateStringTableAsset(
            string path,
            string tableId,
            string localeCode,
            string key,
            string value)
        {
            return CreateStringTableAsset(
                path,
                tableId,
                localeCode,
                new StringEntry { Key = key, Value = value });
        }

        private static StringTable CreateStringTableAsset(
            string path,
            string tableId,
            string localeCode,
            params StringEntry[] values)
        {
            var table = ScriptableObject.CreateInstance<StringTable>();
            var serialized = new SerializedObject(table);
            serialized.FindProperty("tableId").stringValue = tableId;
            serialized.FindProperty("localeCode").stringValue = localeCode;
            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("Key").stringValue = values[index].Key;
                entry.FindPropertyRelative("Value").stringValue = values[index].Value;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(table, path);
            return table;
        }

        private static string GetStringTableValue(StringTable table, string key)
        {
            var serialized = new SerializedObject(table);
            SerializedProperty entries = serialized.FindProperty("entries");
            for (int index = 0; index < entries.arraySize; index++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                if (string.Equals(
                        entry.FindPropertyRelative("Key").stringValue,
                        key,
                        StringComparison.Ordinal))
                {
                    return entry.FindPropertyRelative("Value").stringValue;
                }
            }
            return null;
        }

        private sealed class UndoProbe : ScriptableObject
        {
            public int Value;
        }
    }
}
