using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Localization.Core;
using CycloneGames.Localization.Editor;
using CycloneGames.Localization.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

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

            service.Initialize(new LocalizationOptions(zh, new[] { zh, en }, false));
            service.RegisterStringTable(CreateStringTable("ui", "en", Entry("title", "Start")));
            service.RegisterStringTable(CreateStringTable("ui", "zh-CN", Entry("subtitle", "Ni Hao")));

            Assert.That(service.GetString("ui", "title"), Is.EqualTo("Start"));
            Assert.That(service.GetString("ui", "subtitle"), Is.EqualTo("Ni Hao"));
        }

        [Test]
        public void BlankDirectTranslationUsesFallback()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            var fr = CreateLocale("fr", en);

            service.Initialize(new LocalizationOptions(fr, new[] { fr, en }, false));
            service.RegisterStringTable(CreateStringTable("ui", "en", Entry("title", "Start")));
            service.RegisterStringTable(CreateStringTable("ui", "fr", Entry("title", "   ")));

            Assert.That(service.GetString("ui", "title"), Is.EqualTo("Start"));
        }

        [Test]
        public void BlankCatalogTranslationUsesFallback()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            var fr = CreateLocale("fr", en);
            service.Initialize(new LocalizationOptions(fr, new[] { fr, en }, false));

            LocalizationCatalog catalog = CreateCatalog(
                new List<CatalogStringTable>
                {
                    new CatalogStringTable("ui", "en", new List<CatalogStringEntry>
                    {
                        new CatalogStringEntry("title", "Start"),
                    }),
                    new CatalogStringTable("ui", "fr", new List<CatalogStringEntry>
                    {
                        new CatalogStringEntry("title", "\t"),
                    }),
                },
                new List<CatalogAssetTable>());

            Assert.That(service.TryRegisterCatalog("base", catalog), Is.True);
            Assert.That(service.GetString("ui", "title"), Is.EqualTo("Start"));
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

            service.Initialize(new LocalizationOptions(en, new[] { en }, false));
            service.RegisterStringTable(CreateStringTable("ui", "en", Entry("items.other", "{0} items")));

            Assert.That(service.GetPluralString("ui", "items", 1), Is.EqualTo("1 items"));
            Assert.That(service.GetPluralString("ui", "items", 3), Is.EqualTo("3 items"));
        }

        [Test]
        public void StringTableCompileRejectsDuplicateKey()
        {
            var table = CreateStringTable(
                "ui",
                "en",
                Entry("title", "Old"),
                Entry("title", "New"));

            Assert.Throws<System.InvalidOperationException>(() => table.Compile());
        }

        [Test]
        public void AssetTableCompileRejectsDuplicateKey()
        {
            var table = CreateAssetTable(
                "icons",
                "en",
                AssetEntry("flag", "Assets/Old.png"),
                AssetEntry("flag", "Assets/New.png"));

            Assert.Throws<System.InvalidOperationException>(() => table.Compile());
        }

        [Test]
        public void ServiceResolvesAssetThroughFallbackChain()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            var zh = CreateLocale("zh-CN", en);

            service.Initialize(new LocalizationOptions(zh, new[] { zh, en }, false));
            service.RegisterAssetTable(CreateAssetTable("icons", "en", AssetEntry("flag", "Assets/Flag_en.png")));
            service.RegisterAssetTable(CreateAssetTable("icons", "zh-CN", AssetEntry("confirm", "Assets/Confirm_zh.png")));

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

            var stringTables = new List<CatalogStringTable>
                {
                    new CatalogStringTable("ui", "en", new List<CatalogStringEntry>
                    {
                        new CatalogStringEntry("title", "Start"),
                    }),
                    new CatalogStringTable("ui", "zh-CN", new List<CatalogStringEntry>
                    {
                        new CatalogStringEntry("subtitle", "Ni Hao"),
                    }),
                };
            var assetTables = new List<CatalogAssetTable>
                {
                    new CatalogAssetTable("icons", "en", new List<CatalogAssetEntry>
                    {
                        new CatalogAssetEntry("flag", new AssetRef("Assets/Flag_en.png", "flag-guid")),
                    }),
                };
            catalog.SetData(
                "1.0.0",
                LocalizationCatalog.ComputeContentHash(stringTables, assetTables),
                stringTables,
                assetTables);

            service.Initialize(new LocalizationOptions(zh, new[] { zh, en }, false));
            Assert.That(service.TryRegisterCatalog("base", catalog), Is.True);

            Assert.That(service.GetString("ui", "title"), Is.EqualTo("Start"));
            Assert.That(service.GetString("ui", "subtitle"), Is.EqualTo("Ni Hao"));
            Assert.That(service.ResolveAsset("icons", "flag").Guid, Is.EqualTo("flag-guid"));
        }

        [UnityTest]
        public IEnumerator AssetPackageCatalogInstallAlwaysReleasesTemporaryHandle()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            service.Initialize(new LocalizationOptions(en, new[] { en }, false));
            var package = new ControlledAssetPackage();
            LocalizationCatalog catalog = CreateCatalog(
                new List<CatalogStringTable>
                {
                    new CatalogStringTable("ui", "en", new List<CatalogStringEntry>
                    {
                        new CatalogStringEntry("title", "Start"),
                    }),
                },
                new List<CatalogAssetTable>());

            UniTask<bool> pending = service.LoadAndRegisterCatalogAsync(
                package,
                "base-content",
                "Localization/Base");
            var handle = (ControlledAssetHandle<LocalizationCatalog>)package.Handles[0];
            handle.Complete(catalog);

            bool installed = false;
            yield return pending.ToCoroutine(value => installed = value);

            Assert.That(installed, Is.True);
            Assert.That(handle.DisposeCount, Is.EqualTo(1));
            Assert.That(service.GetString("ui", "title"), Is.EqualTo("Start"));
        }

        [Test]
        public void DefaultLimitsInitializeWithoutExplicitOverrides()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");

            Assert.DoesNotThrow(() => service.Initialize(new LocalizationOptions(en, new[] { en }, false)));
            Assert.That(LocalizationLimits.Default.MaxAvailableLocales,
                Is.EqualTo(LocalizationLimits.DefaultMaxAvailableLocales));
            Assert.That(service.AvailableLocales, Is.EqualTo(new[] { new LocaleId("en") }));
        }

        [Test]
        public void AvailableLocalesIsAnImmutableLocaleIdSnapshot()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            var ja = CreateLocale("ja");
            service.Initialize(new LocalizationOptions(en, new[] { en, ja }, false));

            Assert.That(service.AvailableLocales, Is.EqualTo(new[] { en.Id, ja.Id }));
            var list = (IList<LocaleId>)service.AvailableLocales;
            Assert.Throws<NotSupportedException>(() => list[0] = ja.Id);
            Assert.Throws<InvalidOperationException>(
                () => service.Initialize(new LocalizationOptions(en, new[] { en }, false)));
        }

        [Test]
        public void ExplicitSelectorsRunWhenSystemDetectionIsDisabled()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            var ja = CreateLocale("ja");

            service.Initialize(new LocalizationOptions(
                en,
                new[] { en, ja },
                false,
                new ILocaleSelector[] { new FixedLocaleSelector("JA") }));

            Assert.That(service.CurrentLocale, Is.EqualTo(ja.Id));
        }

        [Test]
        public void InitializationRejectsFallbackAssetsOutsideConfiguredClosure()
        {
            var en = CreateLocale("en");
            var zh = CreateLocale("zh-CN", en);
            var service = new LocalizationService();

            Assert.Throws<InvalidOperationException>(
                () => service.Initialize(new LocalizationOptions(zh, new[] { zh }, false)));
        }

        [Test]
        public void LocaleChangesAreFifoAndSubscriberExceptionsAreIsolated()
        {
            var diagnostics = new List<LocalizationDiagnostic>();
            var service = new LocalizationService();
            var en = CreateLocale("en");
            var ja = CreateLocale("ja");
            var fr = CreateLocale("fr");
            service.Initialize(new LocalizationOptions(
                en,
                new[] { en, ja, fr },
                false,
                diagnosticSink: diagnostics.Add));

            var deliveries = new List<string>();
            service.Changed += change =>
            {
                deliveries.Add("first:" + change.CurrentLocale.Code);
                if (change.Reason == LocalizationChangeReason.LocaleChanged && change.CurrentLocale == ja.Id)
                    Assert.That(service.TrySetLocale(fr.Id), Is.True);
                throw new InvalidOperationException("subscriber failure");
            };
            service.Changed += change => deliveries.Add("second:" + change.CurrentLocale.Code);

            Assert.That(service.TrySetLocale(ja.Id), Is.True);

            Assert.That(deliveries, Is.EqualTo(new[]
            {
                "first:ja",
                "second:ja",
                "first:fr",
                "second:fr",
            }));
            Assert.That(service.CurrentLocale, Is.EqualTo(fr.Id));
            Assert.That(diagnostics.FindAll(
                diagnostic => diagnostic.Code == LocalizationDiagnosticCode.SubscriberException).Count,
                Is.EqualTo(2));
        }

        [Test]
        public void MutationFromNonOwnerThreadThrowsButQueriesRemainAvailable()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            var ja = CreateLocale("ja");
            service.Initialize(new LocalizationOptions(en, new[] { en, ja }, false));
            service.RegisterStringTable(CreateStringTable("ui", "en", Entry("title", "Start")));

            Assert.That(Task.Run(() => service.GetString("ui", "title")).GetAwaiter().GetResult(),
                Is.EqualTo("Start"));
            Assert.Throws<InvalidOperationException>(() =>
                Task.Run(() => service.TrySetLocale(ja.Id)).GetAwaiter().GetResult());
        }

        [Test]
        public void InitializationFromWorkerThreadIsRejectedBeforeReadingAuthoringAssets()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            var options = new LocalizationOptions(en, new[] { en }, false);

            Assert.Throws<InvalidOperationException>(() =>
                Task.Run(() => service.Initialize(options)).GetAwaiter().GetResult());
            Assert.That(service.IsInitialized, Is.False);
        }

        [Test]
        public void LocaleFallbackChainsAreFrozenDuringInitialization()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            var fr = CreateLocale("fr");
            var zh = CreateLocale("zh-CN", en);
            service.Initialize(new LocalizationOptions(en, new[] { en, fr, zh }, false));
            service.RegisterStringTable(CreateStringTable("ui", "en", Entry("title", "English")));
            service.RegisterStringTable(CreateStringTable("ui", "fr", Entry("title", "French")));

            var serialized = new SerializedObject(zh);
            SerializedProperty fallbacks = serialized.FindProperty("fallbacks");
            fallbacks.arraySize = 1;
            fallbacks.GetArrayElementAtIndex(0).objectReferenceValue = fr;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(service.TrySetLocale(zh.Id), Is.True);
            Assert.That(service.GetString("ui", "title"), Is.EqualTo("English"));
        }

        [Test]
        public void MissingDiagnosticsAreBoundedPerInstance()
        {
            var diagnostics = new List<LocalizationDiagnostic>();
            var service = new LocalizationService();
            var en = CreateLocale("en");
            var limits = new LocalizationLimits(maxMissingDiagnostics: 2);
            service.Initialize(new LocalizationOptions(
                en,
                new[] { en },
                false,
                diagnosticSink: diagnostics.Add,
                limits: limits));

            service.GetString("ui", "one");
            service.GetString("ui", "two");
            service.GetString("ui", "three");
            service.GetString("ui", "one");

            Assert.That(diagnostics.FindAll(
                diagnostic => diagnostic.Code == LocalizationDiagnosticCode.MissingKey).Count,
                Is.EqualTo(2));
        }

        [Test]
        public void LocaleAndPseudoChangesReusePublishedContentMaps()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            var ja = CreateLocale("ja");
            service.Initialize(new LocalizationOptions(en, new[] { en, ja }, false));
            service.RegisterStringTable(CreateStringTable("ui", "en", Entry("title", "Start")));

            object initialMap = GetSnapshotMember(service, "StringTables");
            service.TrySetLocale(ja.Id);
            object localeMap = GetSnapshotMember(service, "StringTables");
            service.PseudoMode = PseudoLocaleMode.Accents;
            object pseudoMap = GetSnapshotMember(service, "StringTables");

            Assert.That(localeMap, Is.SameAs(initialMap));
            Assert.That(pseudoMap, Is.SameAs(initialMap));
        }

        [Test]
        public void CompiledTablesCopyCallerOwnedDictionaries()
        {
            var source = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["title"] = "Start",
            };
            var compiled = new CompiledStringTable("ui", new LocaleId("en"), source);

            source["title"] = "Mutated";
            source["other"] = "Other";

            Assert.That(compiled.Count, Is.EqualTo(1));
            Assert.That(compiled.TryGetValue("title", out string value), Is.True);
            Assert.That(value, Is.EqualTo("Start"));
        }

        [Test]
        public void CatalogReplacementIsAtomicAndOwnerRemovalReleasesContent()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            service.Initialize(new LocalizationOptions(en, new[] { en }, false));

            LocalizationCatalog valid = CreateCatalog(
                new List<CatalogStringTable>
                {
                    new CatalogStringTable("ui", "en", new List<CatalogStringEntry>
                    {
                        new CatalogStringEntry("title", "Start"),
                    }),
                },
                new List<CatalogAssetTable>());
            Assert.That(service.TryRegisterCatalog("base", valid), Is.True);
            Assert.That(service.GetString("ui", "title"), Is.EqualTo("Start"));

            LocalizationCatalog corruptReplacement = CreateCatalog(
                new List<CatalogStringTable>
                {
                    new CatalogStringTable("ui", "en", new List<CatalogStringEntry>
                    {
                        new CatalogStringEntry("title", "Changed"),
                    }),
                },
                new List<CatalogAssetTable>(),
                new string('0', 64));
            Assert.That(service.TryRegisterCatalog("base", corruptReplacement), Is.False);
            Assert.That(service.GetString("ui", "title"), Is.EqualTo("Start"));

            LocalizationCatalog duplicateReplacement = CreateCatalog(
                new List<CatalogStringTable>
                {
                    new CatalogStringTable("ui", "en", new List<CatalogStringEntry>
                    {
                        new CatalogStringEntry("title", "First"),
                        new CatalogStringEntry("title", "Second"),
                    }),
                },
                new List<CatalogAssetTable>());
            Assert.That(service.TryRegisterCatalog("base", duplicateReplacement), Is.False);
            Assert.That(service.GetString("ui", "title"), Is.EqualTo("Start"));

            Assert.That(service.RemoveCatalog("base"), Is.True);
            Assert.That(service.GetString("ui", "title"), Is.Null);
        }

        [Test]
        public void CatalogLimitsRejectOversizedTablesBeforeCommit()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            service.Initialize(new LocalizationOptions(
                en,
                new[] { en },
                false,
                limits: new LocalizationLimits(maxEntriesPerTable: 1)));
            LocalizationCatalog catalog = CreateCatalog(
                new List<CatalogStringTable>
                {
                    new CatalogStringTable("ui", "en", new List<CatalogStringEntry>
                    {
                        new CatalogStringEntry("one", "One"),
                        new CatalogStringEntry("two", "Two"),
                    }),
                },
                new List<CatalogAssetTable>());

            Assert.That(service.TryRegisterCatalog("base", catalog), Is.False);
            Assert.That(service.GetString("ui", "one"), Is.Null);
        }

        [Test]
        public void CatalogAggregateTextBudgetIsCheckedBeforeCommit()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            service.Initialize(new LocalizationOptions(
                en,
                new[] { en },
                false,
                limits: new LocalizationLimits(maxCatalogTextCharacters: 12)));
            LocalizationCatalog catalog = CreateCatalog(
                new List<CatalogStringTable>
                {
                    new CatalogStringTable("ui", "en", new List<CatalogStringEntry>
                    {
                        new CatalogStringEntry("title", "A value beyond the aggregate budget"),
                    }),
                },
                new List<CatalogAssetTable>());

            Assert.That(service.TryRegisterCatalog("base", catalog), Is.False);
            Assert.That(service.GetString("ui", "title"), Is.Null);
        }

        [Test]
        public void DirectTablesRejectUnconfiguredLocalesAndUnsafeIdentifiers()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            service.Initialize(new LocalizationOptions(en, new[] { en }, false));

            Assert.That(service.RegisterStringTable(
                CreateStringTable("ui", "ja", Entry("title", "Start"))), Is.False);
            Assert.That(service.RegisterStringTable(
                CreateStringTable(" ui", "en", Entry("title", "Start"))), Is.False);
            Assert.That(service.RegisterStringTable(
                CreateStringTable("ui", "en", Entry("bad\nkey", "Start"))), Is.False);
        }

        [Test]
        public void FormatProviderIsExplicitAndInvalidTemplatesDoNotThrow()
        {
            var diagnostics = new List<LocalizationDiagnostic>();
            var service = new LocalizationService();
            var en = CreateLocale("en");
            service.Initialize(new LocalizationOptions(
                en,
                new[] { en },
                false,
                diagnosticSink: diagnostics.Add,
                formatProvider: CultureInfo.GetCultureInfo("fr-FR")));
            service.RegisterStringTable(CreateStringTable(
                "ui",
                "en",
                Entry("number", "{0:N1}"),
                Entry("broken", "{0")));

            Assert.That(service.GetFormattedString("ui", "number", 12.5), Does.Contain("12,5"));
            Assert.DoesNotThrow(() => service.GetFormattedString("ui", "broken", 1));
            Assert.That(diagnostics.Exists(
                diagnostic => diagnostic.Code == LocalizationDiagnosticCode.FormatError), Is.True);
        }

        [Test]
        public void ShutdownPublishesTerminalChangeAndDisposeIsIdempotent()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            service.Initialize(new LocalizationOptions(en, new[] { en }, false));
            LocalizationChange terminal = default;
            service.Changed += change => terminal = change;

            service.Shutdown();

            Assert.That(service.IsInitialized, Is.False);
            Assert.That(service.CurrentLocale.IsValid, Is.False);
            Assert.That(terminal.Reason, Is.EqualTo(LocalizationChangeReason.Shutdown));
            Assert.Throws<InvalidOperationException>(() => service.TrySetLocale(en.Id));
            Assert.DoesNotThrow(() => service.Dispose());
            Assert.DoesNotThrow(() => service.Dispose());
            Assert.Throws<ObjectDisposedException>(() => service.Changed += _ => { });
            Assert.That(GetSnapshotMember(service, "DiagnosticSink"), Is.Null);
            Assert.That(GetSnapshotMember(service, "FormatProvider"), Is.SameAs(CultureInfo.InvariantCulture));
        }

        [Test]
        public void LocalizationSettingsAuthoringLocaleFallsBackToDefault()
        {
            var settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            var en = CreateLocale("en");
            var ja = CreateLocale("ja");
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("defaultLocale").objectReferenceValue = en;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(settings.AuthoringLocale, Is.SameAs(en));

            serialized.Update();
            serialized.FindProperty("authoringLocale").objectReferenceValue = ja;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(settings.AuthoringLocale, Is.SameAs(ja));
        }

        [Test]
        public void BindingContextRequiresOnlyLocalizationForTextTargets()
        {
            var service = new LocalizationService();

            Assert.DoesNotThrow(() => new LocalizationBindingContext(service));
            Assert.Throws<ArgumentNullException>(() => new LocalizationBindingContext(null));
        }

        [UnityTest]
        public IEnumerator LocalizeImageRejectsStaleCompletionAndRestoresDesignerSpriteOnMissingAsset()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            var ja = CreateLocale("ja");
            var fr = CreateLocale("fr");
            service.Initialize(new LocalizationOptions(en, new[] { en, ja, fr }, false));
            service.RegisterAssetTable(CreateAssetTable(
                "icons", "en", AssetEntry("flag", "Flags/en")));
            service.RegisterAssetTable(CreateAssetTable(
                "icons", "ja", AssetEntry("flag", "Flags/ja")));

            var package = new ControlledAssetPackage();
            GameObject gameObject = null;
            Texture2D designerTexture = null;
            Texture2D enTexture = null;
            Texture2D jaTexture = null;
            Sprite designer = null;
            Sprite enSprite = null;
            Sprite jaSprite = null;
            try
            {
                designer = CreateSprite(out designerTexture);
                enSprite = CreateSprite(out enTexture);
                jaSprite = CreateSprite(out jaTexture);
                CreateImageBinding(
                    designer,
                    out gameObject,
                    out Component image,
                    out ILocalizationBindingTarget binding,
                    out PropertyInfo spriteProperty);

                SetLocalizedImageKey(binding, "icons", "flag");
                binding.Bind(new LocalizationBindingContext(service, package));
                Assert.That(package.Handles.Count, Is.EqualTo(1));

                Assert.That(service.TrySetLocale(ja.Id), Is.True);
                Assert.That(package.Handles.Count, Is.EqualTo(2));
                var enHandle = (ControlledAssetHandle<Sprite>)package.Handles[0];
                var jaHandle = (ControlledAssetHandle<Sprite>)package.Handles[1];

                jaHandle.Complete(jaSprite);
                yield return null;
                Assert.That(spriteProperty.GetValue(image), Is.SameAs(jaSprite));

                enHandle.Complete(enSprite);
                yield return null;
                Assert.That(spriteProperty.GetValue(image), Is.SameAs(jaSprite));
                Assert.That(enHandle.DisposeCount, Is.EqualTo(1));

                Assert.That(service.TrySetLocale(fr.Id), Is.True);
                yield return null;
                Assert.That(spriteProperty.GetValue(image), Is.SameAs(designer));
                Assert.That(jaHandle.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject);
                if (designer != null) UnityEngine.Object.DestroyImmediate(designer);
                if (enSprite != null) UnityEngine.Object.DestroyImmediate(enSprite);
                if (jaSprite != null) UnityEngine.Object.DestroyImmediate(jaSprite);
                if (designerTexture != null) UnityEngine.Object.DestroyImmediate(designerTexture);
                if (enTexture != null) UnityEngine.Object.DestroyImmediate(enTexture);
                if (jaTexture != null) UnityEngine.Object.DestroyImmediate(jaTexture);
            }
        }

        [UnityTest]
        public IEnumerator LocalizeImageProviderFailureKeepsLastGoodAndUnbindPreservesExternalSprite()
        {
            var service = new LocalizationService();
            var en = CreateLocale("en");
            var ja = CreateLocale("ja");
            service.Initialize(new LocalizationOptions(en, new[] { en, ja }, false));
            service.RegisterAssetTable(CreateAssetTable(
                "icons", "en", AssetEntry("flag", "Flags/en")));
            service.RegisterAssetTable(CreateAssetTable(
                "icons", "ja", AssetEntry("flag", "Flags/ja")));

            var package = new ControlledAssetPackage();
            GameObject gameObject = null;
            Texture2D designerTexture = null;
            Texture2D enTexture = null;
            Texture2D externalTexture = null;
            Sprite designer = null;
            Sprite enSprite = null;
            Sprite externalSprite = null;
            try
            {
                designer = CreateSprite(out designerTexture);
                enSprite = CreateSprite(out enTexture);
                externalSprite = CreateSprite(out externalTexture);
                CreateImageBinding(
                    designer,
                    out gameObject,
                    out Component image,
                    out ILocalizationBindingTarget binding,
                    out PropertyInfo spriteProperty);

                SetLocalizedImageKey(binding, "icons", "flag");
                binding.Bind(new LocalizationBindingContext(service, package));
                var enHandle = (ControlledAssetHandle<Sprite>)package.Handles[0];
                enHandle.Complete(enSprite);
                yield return null;
                Assert.That(spriteProperty.GetValue(image), Is.SameAs(enSprite));

                Assert.That(service.TrySetLocale(ja.Id), Is.True);
                var failedHandle = (ControlledAssetHandle<Sprite>)package.Handles[1];
                LogAssert.Expect(LogType.Exception, new Regex("provider failure"));
                failedHandle.Fail(new InvalidOperationException("provider failure"));
                yield return null;

                Assert.That(spriteProperty.GetValue(image), Is.SameAs(enSprite));
                Assert.That(enHandle.DisposeCount, Is.EqualTo(0));
                Assert.That(failedHandle.DisposeCount, Is.EqualTo(1));

                spriteProperty.SetValue(image, externalSprite);
                binding.Unbind();
                Assert.That(spriteProperty.GetValue(image), Is.SameAs(externalSprite));
                Assert.That(enHandle.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject);
                if (designer != null) UnityEngine.Object.DestroyImmediate(designer);
                if (enSprite != null) UnityEngine.Object.DestroyImmediate(enSprite);
                if (externalSprite != null) UnityEngine.Object.DestroyImmediate(externalSprite);
                if (designerTexture != null) UnityEngine.Object.DestroyImmediate(designerTexture);
                if (enTexture != null) UnityEngine.Object.DestroyImmediate(enTexture);
                if (externalTexture != null) UnityEngine.Object.DestroyImmediate(externalTexture);
            }
        }

        [Test]
        public void CatalogContentHashIsDeterministic()
        {
            Assert.That(LocalizationCatalog.CurrentSchemaVersion, Is.EqualTo(2));
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
            Assert.That(first, Is.EqualTo(LocalizationCatalog.ComputeContentHash(stringTables, assetTables)));
        }

        [Test]
        public void CatalogHashFramesRecordStructureToPreventTokenStreamCollisions()
        {
            var oneTableWithThreeEntries = new List<CatalogStringTable>
            {
                new CatalogStringTable("a", "en", new List<CatalogStringEntry>
                {
                    new CatalogStringEntry("S", "c"),
                    new CatalogStringEntry("fr", "S"),
                    new CatalogStringEntry("e", "de"),
                }),
            };
            var threeEmptyTables = new List<CatalogStringTable>
            {
                new CatalogStringTable("a", "en", new List<CatalogStringEntry>()),
                new CatalogStringTable("c", "fr", new List<CatalogStringEntry>()),
                new CatalogStringTable("e", "de", new List<CatalogStringEntry>()),
            };

            string first = LocalizationCatalog.ComputeContentHash(
                oneTableWithThreeEntries,
                new List<CatalogAssetTable>());
            string second = LocalizationCatalog.ComputeContentHash(
                threeEmptyTables,
                new List<CatalogAssetTable>());

            Assert.That(first, Is.Not.EqualTo(second));
        }

        [Test]
        public void CatalogHashAndRegistrationRejectMalformedUtf16()
        {
            var stringTables = new List<CatalogStringTable>
            {
                new CatalogStringTable("ui", "en", new List<CatalogStringEntry>
                {
                    new CatalogStringEntry("title", "bad\uD800"),
                }),
            };
            var assetTables = new List<CatalogAssetTable>();

            Assert.Throws<EncoderFallbackException>(
                () => LocalizationCatalog.ComputeContentHash(stringTables, assetTables));

            var catalog = ScriptableObject.CreateInstance<LocalizationCatalog>();
            catalog.SetData("1.0.0", new string('0', 64), stringTables, assetTables);
            var service = new LocalizationService();
            var en = CreateLocale("en");
            service.Initialize(new LocalizationOptions(en, new[] { en }, false));
            Assert.That(service.TryRegisterCatalog("base", catalog), Is.False);
        }

        private static LocalizationCatalog CreateCatalog(
            List<CatalogStringTable> stringTables,
            List<CatalogAssetTable> assetTables,
            string hashOverride = null)
        {
            var catalog = ScriptableObject.CreateInstance<LocalizationCatalog>();
            string hash = hashOverride ?? LocalizationCatalog.ComputeContentHash(stringTables, assetTables);
            catalog.SetData("1.0.0", hash, stringTables, assetTables);
            return catalog;
        }

        private static void CreateImageBinding(
            Sprite designerSprite,
            out GameObject gameObject,
            out Component image,
            out ILocalizationBindingTarget binding,
            out PropertyInfo spriteProperty)
        {
            Type imageType = Assembly.Load("UnityEngine.UI").GetType("UnityEngine.UI.Image", true);
            Type bindingType = Assembly.Load("CycloneGames.Localization.Components").GetType(
                "CycloneGames.Localization.Runtime.LocalizeImage",
                true);

            gameObject = new GameObject("Localization Image Test");
            image = gameObject.AddComponent(imageType);
            spriteProperty = imageType.GetProperty("sprite", BindingFlags.Instance | BindingFlags.Public);
            spriteProperty.SetValue(image, designerSprite);
            binding = (ILocalizationBindingTarget)gameObject.AddComponent(bindingType);
            bindingType.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(binding, null);
        }

        private static void SetLocalizedImageKey(
            ILocalizationBindingTarget binding,
            string tableId,
            string entryKey)
        {
            PropertyInfo property = binding.GetType().GetProperty(
                "LocalizedAsset",
                BindingFlags.Instance | BindingFlags.Public);
            property.SetValue(binding, new LocalizedAsset<Sprite>(tableId, entryKey));
        }

        private static Sprite CreateSprite(out Texture2D texture)
        {
            texture = new Texture2D(2, 2);
            return Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
        }

        private static object GetSnapshotMember(LocalizationService service, string propertyName)
        {
            FieldInfo snapshotField = typeof(LocalizationService).GetField(
                "_snapshot",
                BindingFlags.Instance | BindingFlags.NonPublic);
            object snapshot = snapshotField.GetValue(service);
            PropertyInfo property = snapshot.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public);
            return property.GetValue(snapshot);
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

        private sealed class FixedLocaleSelector : ILocaleSelector
        {
            private readonly string _code;

            public FixedLocaleSelector(string code)
            {
                _code = code;
            }

            public string GetPreferredLocaleCode() => _code;
        }

        private interface IControlledAssetHandle
        {
            int DisposeCount { get; }
        }

        private sealed class ControlledAssetHandle<T> : IAssetHandle<T>, IControlledAssetHandle
            where T : UnityEngine.Object
        {
            private readonly UniTaskCompletionSource _completion = new UniTaskCompletionSource();

            public T Asset { get; private set; }
            public UnityEngine.Object AssetObject => Asset;
            public bool IsDone { get; private set; }
            public float Progress => IsDone ? 1f : 0f;
            public string Error { get; private set; }
            public UniTask Task => _completion.Task;
            public int DisposeCount { get; private set; }

            public void Complete(T asset)
            {
                Asset = asset;
                IsDone = true;
                _completion.TrySetResult();
            }

            public void Fail(Exception exception)
            {
                Error = exception.Message;
                IsDone = true;
                _completion.TrySetException(exception);
            }

            public void Dispose()
            {
                DisposeCount++;
            }

            public void WaitForAsyncComplete()
            {
            }
        }

        private sealed class ControlledAssetPackage : IAssetPackage
        {
            public readonly List<IControlledAssetHandle> Handles = new List<IControlledAssetHandle>();

            public string Name => "LocalizationTests";

            public UniTask<bool> InitializeAsync(
                AssetPackageInitOptions options,
                CancellationToken cancellationToken = default)
            {
                return UniTask.FromResult(true);
            }

            public UniTask DestroyAsync() => UniTask.CompletedTask;

            public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(
                string location,
                string bucket = null,
                string tag = null,
                string owner = null,
                CancellationToken cancellationToken = default)
                where TAsset : UnityEngine.Object
            {
                var handle = new ControlledAssetHandle<TAsset>();
                Handles.Add(handle);
                return handle;
            }

            public IInstantiateHandle InstantiateAsync(
                IAssetHandle<GameObject> handle,
                Transform parent = null,
                bool worldPositionStays = false,
                bool setActive = true)
            {
                throw new NotSupportedException();
            }

            public bool IsAssetCached<TAsset>(string location) where TAsset : UnityEngine.Object => false;
            public UniTask UnloadUnusedAssetsAsync() => UniTask.CompletedTask;
            public void SetCacheIdleMemoryBudget(long maxIdleBytes) { }
            public int TrimIdleCache(AssetCacheRetentionPolicy policy) => 0;
            public void ClearBucket(string bucket) { }
            public void ClearBucketsByPrefix(string bucketPrefix) { }
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
