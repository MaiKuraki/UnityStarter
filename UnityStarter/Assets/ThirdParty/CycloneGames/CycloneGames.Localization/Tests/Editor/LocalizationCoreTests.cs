using System.Collections.Generic;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Localization.Core;
using CycloneGames.Localization.Editor;
using CycloneGames.Localization.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Tests.Editor
{
    public sealed class LocalizationCoreTests
    {
        [Test]
        public void LocalizedStringRequiresTableAndEntry()
        {
            Assert.That(new LocalizedString(null, "title").IsValid, Is.False);
            Assert.That(new LocalizedString("ui", null).IsValid, Is.False);
            Assert.That(new LocalizedString("ui", string.Empty).IsValid, Is.False);
            Assert.That(new LocalizedString("ui", "title").IsValid, Is.True);
        }

        [Test]
        public void LocalizedStringHashIncludesTableId()
        {
            var uiTitle = new LocalizedString("ui", "title");
            var dialogTitle = new LocalizedString("dialog", "title");

            Assert.That(uiTitle.Equals(dialogTitle), Is.False);
            Assert.That(uiTitle.GetHashCode(), Is.Not.EqualTo(dialogTitle.GetHashCode()));
        }

        [Test]
        public void LocalizedAssetRequiresTableAndEntry()
        {
            Assert.That(new LocalizedAsset<Texture2D>(null, "icon").IsValid, Is.False);
            Assert.That(new LocalizedAsset<Texture2D>("ui", null).IsValid, Is.False);
            Assert.That(new LocalizedAsset<Texture2D>("ui", "icon").IsValid, Is.True);
        }

        [Test]
        public void LocaleIdUsesOrdinalValueEquality()
        {
            var dynamicCode = new string(new[] { 'e', 'n' });

            Assert.That(new LocaleId("en"), Is.EqualTo(new LocaleId(dynamicCode)));
            Assert.That(new LocaleId(string.Empty).IsValid, Is.False);
        }

        [Test]
        public void ServiceResolvesStringThroughFallbackChain()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            var zh = CreateLocale("zh-CN", en);

            service.RegisterStringTable(CreateStringTable("ui", "en", Entry("title", "Start")));
            service.RegisterStringTable(CreateStringTable("ui", "zh-CN", Entry("subtitle", "Ni Hao")));
            service.InitializeAsync(new LocalizationOptions(zh, new[] { zh, en }, false)).GetAwaiter().GetResult();

            Assert.That(service.GetString("ui", "title"), Is.EqualTo("Start"));
            Assert.That(service.GetString("ui", "subtitle"), Is.EqualTo("Ni Hao"));
        }

        [Test]
        public void ServiceTryGetStringRejectsInvalidKeys()
        {
            var service = new LocalizationService();

            Assert.That(service.TryGetString(string.Empty, "title", out var value), Is.False);
            Assert.That(value, Is.Null);
            Assert.That(service.TryGetString("ui", string.Empty, out value), Is.False);
            Assert.That(value, Is.Null);
        }

        [Test]
        public void ServiceUsesOtherPluralFallback()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");

            service.RegisterStringTable(CreateStringTable("ui", "en", Entry("items.other", "{0} items")));
            service.InitializeAsync(new LocalizationOptions(en, new[] { en }, false)).GetAwaiter().GetResult();

            Assert.That(service.GetPluralString("ui", "items", 1), Is.EqualTo("1 items"));
            Assert.That(service.GetPluralString("ui", "items", 3), Is.EqualTo("3 items"));
        }

        [Test]
        public void StringTableCompileUsesLastDuplicateKey()
        {
            var table = CreateStringTable(
                "ui",
                "en",
                Entry("title", "Old"),
                Entry("title", "New"));

            var compiled = table.Compile();

            Assert.That(compiled.TryGetValue("title", out var value), Is.True);
            Assert.That(value, Is.EqualTo("New"));
            Assert.That(table.Compile(), Is.SameAs(compiled));
        }

        [Test]
        public void AssetTableCompileUsesLastDuplicateKey()
        {
            var table = CreateAssetTable(
                "icons",
                "en",
                AssetEntry("flag", "Assets/Old.png"),
                AssetEntry("flag", "Assets/New.png"));

            var compiled = table.Compile();

            Assert.That(compiled.TryGetValue("flag", out var value), Is.True);
            Assert.That(value.Location, Is.EqualTo("Assets/New.png"));
            Assert.That(table.Compile(), Is.SameAs(compiled));
        }

        [Test]
        public void ServiceResolvesAssetThroughFallbackChain()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            var zh = CreateLocale("zh-CN", en);

            service.RegisterAssetTable(CreateAssetTable("icons", "en", AssetEntry("flag", "Assets/Flag_en.png")));
            service.RegisterAssetTable(CreateAssetTable("icons", "zh-CN", AssetEntry("confirm", "Assets/Confirm_zh.png")));
            service.InitializeAsync(new LocalizationOptions(zh, new[] { zh, en }, false)).GetAwaiter().GetResult();

            Assert.That(service.ResolveAsset("icons", "flag").Location, Is.EqualTo("Assets/Flag_en.png"));
            Assert.That(service.ResolveAsset("icons", "confirm").Location, Is.EqualTo("Assets/Confirm_zh.png"));
        }

        [Test]
        public void ServiceRegistersCatalogTables()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            var zh = CreateLocale("zh-CN", en);
            var catalog = ScriptableObject.CreateInstance<LocalizationCatalog>();

            catalog.SetData(
                "1.0.0",
                "test",
                new List<CatalogStringTable>
                {
                    new CatalogStringTable("ui", "en", new List<CatalogStringEntry>
                    {
                        new CatalogStringEntry("title", "Start"),
                    }),
                    new CatalogStringTable("ui", "zh-CN", new List<CatalogStringEntry>
                    {
                        new CatalogStringEntry("subtitle", "Ni Hao"),
                    }),
                },
                new List<CatalogAssetTable>
                {
                    new CatalogAssetTable("icons", "en", new List<CatalogAssetEntry>
                    {
                        new CatalogAssetEntry("flag", new AssetRef("Assets/Flag_en.png", "flag-guid")),
                    }),
                });

            service.RegisterCatalog(catalog);
            service.InitializeAsync(new LocalizationOptions(zh, new[] { zh, en }, false)).GetAwaiter().GetResult();

            Assert.That(service.GetString("ui", "title"), Is.EqualTo("Start"));
            Assert.That(service.GetString("ui", "subtitle"), Is.EqualTo("Ni Hao"));
            Assert.That(service.ResolveAsset("icons", "flag").Guid, Is.EqualTo("flag-guid"));
        }

        [Test]
        public void CatalogContentHashIsDeterministic()
        {
            var stringTables = new List<CatalogStringTable>
            {
                new CatalogStringTable("ui", "en", new List<CatalogStringEntry>
                {
                    new CatalogStringEntry("title", "Start"),
                    new CatalogStringEntry("subtitle", "Continue"),
                }),
            };
            var assetTables = new List<CatalogAssetTable>
            {
                new CatalogAssetTable("icons", "en", new List<CatalogAssetEntry>
                {
                    new CatalogAssetEntry("flag", new AssetRef("Assets/Flag.png", "flag-guid")),
                }),
            };

            string first = LocalizationCatalogBuilder.ComputeContentHash(stringTables, assetTables);
            string second = LocalizationCatalogBuilder.ComputeContentHash(stringTables, assetTables);

            Assert.That(first, Is.EqualTo(second));
        }

        private static TestStringEntry Entry(string key, string value)
        {
            return new TestStringEntry(key, value);
        }

        private static Locale CreateLocale(string code, params Locale[] fallbacks)
        {
            var locale = ScriptableObject.CreateInstance<Locale>();
            var serialized = new SerializedObject(locale);

            serialized.FindProperty("localeCode").stringValue = code;
            serialized.FindProperty("displayName").stringValue = code;
            serialized.FindProperty("nativeName").stringValue = code;

            var fallbackProperty = serialized.FindProperty("fallbacks");
            fallbackProperty.arraySize = fallbacks != null ? fallbacks.Length : 0;

            for (int i = 0; i < fallbackProperty.arraySize; i++)
            {
                fallbackProperty.GetArrayElementAtIndex(i).objectReferenceValue = fallbacks[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return locale;
        }

        private static StringTable CreateStringTable(string tableId, string localeCode, params TestStringEntry[] entries)
        {
            var table = ScriptableObject.CreateInstance<StringTable>();
            var serialized = new SerializedObject(table);

            serialized.FindProperty("tableId").stringValue = tableId;
            serialized.FindProperty("localeCode").stringValue = localeCode;

            var entriesProperty = serialized.FindProperty("entries");
            entriesProperty.arraySize = entries != null ? entries.Length : 0;

            for (int i = 0; i < entriesProperty.arraySize; i++)
            {
                var entryProperty = entriesProperty.GetArrayElementAtIndex(i);
                entryProperty.FindPropertyRelative("Key").stringValue = entries[i].Key;
                entryProperty.FindPropertyRelative("Value").stringValue = entries[i].Value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return table;
        }

        private static TestAssetEntry AssetEntry(string key, string location)
        {
            return new TestAssetEntry(key, location);
        }

        private static AssetTable CreateAssetTable(string tableId, string localeCode, params TestAssetEntry[] entries)
        {
            var table = ScriptableObject.CreateInstance<AssetTable>();
            var serialized = new SerializedObject(table);

            serialized.FindProperty("tableId").stringValue = tableId;
            serialized.FindProperty("localeCode").stringValue = localeCode;

            var entriesProperty = serialized.FindProperty("entries");
            entriesProperty.arraySize = entries != null ? entries.Length : 0;

            for (int i = 0; i < entriesProperty.arraySize; i++)
            {
                var entryProperty = entriesProperty.GetArrayElementAtIndex(i);
                entryProperty.FindPropertyRelative("Key").stringValue = entries[i].Key;
                var assetProperty = entryProperty.FindPropertyRelative("Asset");
                assetProperty.FindPropertyRelative("m_Location").stringValue = entries[i].Location;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return table;
        }

        private readonly struct TestStringEntry
        {
            public readonly string Key;
            public readonly string Value;

            public TestStringEntry(string key, string value)
            {
                Key = key;
                Value = value;
            }
        }

        private readonly struct TestAssetEntry
        {
            public readonly string Key;
            public readonly string Location;

            public TestAssetEntry(string key, string location)
            {
                Key = key;
                Location = location;
            }
        }
    }
}
