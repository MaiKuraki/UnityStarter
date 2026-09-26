using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CycloneGames.Localization.Core;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.UIFramework.Runtime.Integrations.Localization;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor.Integrations.Localization
{
    /// <summary>
    /// Proves the narrow localization contract is actually usable on its own.
    /// </summary>
    /// <remarks>
    /// <see cref="ILocalizationProvider"/> exists so a project can bring I2 Localization, the Unity
    /// Localization package, or its own backend without pulling in the CycloneGames runtime. These
    /// tests drive the binder with a provider that implements nothing else.
    /// </remarks>
    public sealed class NarrowLocalizationProviderTests
    {
        /// <summary>
        /// A provider with no dependency on the CycloneGames localization runtime. It is not an
        /// <c>ILocalizationService</c>.
        /// </summary>
        private sealed class NarrowProvider : ILocalizationProvider
        {
            private readonly List<LocaleId> _locales = new List<LocaleId>(4);

            public NarrowProvider(params string[] localeCodes)
            {
                foreach (string code in localeCodes)
                {
                    _locales.Add(new LocaleId(code));
                }

                CurrentLocale = _locales.Count > 0 ? _locales[0] : LocaleId.Invalid;
            }

            public LocaleId CurrentLocale { get; private set; }
            public IReadOnlyList<LocaleId> AvailableLocales => _locales;
            public bool IsInitialized { get; private set; }
            public long Revision { get; private set; }

            public event Action<LocalizationChange> Changed;

            public void CompleteInitialization()
            {
                if (IsInitialized)
                {
                    return;
                }

                IsInitialized = true;
                Raise(CurrentLocale, CurrentLocale, LocalizationChangeReason.Initialized);
            }

            public void SwitchTo(LocaleId localeId)
            {
                if (!_locales.Contains(localeId) || localeId == CurrentLocale)
                {
                    return;
                }

                LocaleId previous = CurrentLocale;
                CurrentLocale = localeId;
                Raise(previous, localeId, LocalizationChangeReason.LocaleChanged);
            }

            private void Raise(LocaleId previous, LocaleId current, LocalizationChangeReason reason)
            {
                Revision++;
                Changed?.Invoke(new LocalizationChange(previous, current, reason, Revision));
            }

            public string GetString(string tableId, string entryKey)
            {
                return tableId + "/" + entryKey + "@" + CurrentLocale.Code;
            }

            public bool TryGetString(string tableId, string entryKey, out string value)
            {
                value = GetString(tableId, entryKey);
                return true;
            }

            public string GetFormattedString(string tableId, string entryKey, params object[] args)
            {
                return GetString(tableId, entryKey)
                    + (args == null || args.Length == 0 ? string.Empty : "|" + string.Join(",", args));
            }

            public string GetPluralString(string tableId, string entryKey, int count)
            {
                return GetString(tableId, entryKey) + "#" + count;
            }
        }

        /// <summary>
        /// Shared recording surface. The window is instantiated from the prefab, so the instance that
        /// binds holds its own copy of every value-type field on the component; a ScriptableObject
        /// reference survives the clone and is shared by both.
        /// </summary>
        private sealed class NarrowProbe : ScriptableObject
        {
            private readonly List<string> _texts = new List<string>(8);

            public IReadOnlyList<string> Texts => _texts;
            public int BindCount { get; private set; }
            public int UnbindCount { get; private set; }

            public void RecordBind(string text)
            {
                BindCount++;
                _texts.Add(text);
            }

            public void RecordText(string text)
            {
                _texts.Add(text);
            }

            public void RecordUnbind()
            {
                UnbindCount++;
            }
        }

        /// <summary>
        /// A binding target that only ever touches the narrow contract. If the binder or the context
        /// ever started requiring the wide service, this target would fail.
        /// </summary>
        private sealed class NarrowTarget : MonoBehaviour, ILocalizationBindingTarget
        {
            [SerializeField] private NarrowProbe probe;

            private ILocalizationProvider _provider;

            public void Initialize(NarrowProbe value)
            {
                probe = value;
            }

            public void Bind(in LocalizationBindingContext context)
            {
                _provider = context.Localization;
                probe.RecordBind(_provider.GetString("ui", "title"));
                _provider.Changed += OnLocalizationChanged;
            }

            public void Unbind()
            {
                if (_provider != null)
                {
                    _provider.Changed -= OnLocalizationChanged;
                    _provider = null;
                }

                if (probe != null)
                {
                    probe.RecordUnbind();
                }
            }

            private void OnLocalizationChanged(LocalizationChange change)
            {
                if (_provider == null)
                {
                    return;
                }

                probe.RecordText(_provider.GetString("ui", "title"));
            }
        }

        private readonly List<UnityEngine.Object> _ownedObjects = new List<UnityEngine.Object>(4);

        [TearDown]
        public void TearDown()
        {
            for (int i = _ownedObjects.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object owned = _ownedObjects[i];
                if (owned != null)
                {
                    UnityEngine.Object.DestroyImmediate(owned);
                }
            }

            _ownedObjects.Clear();
        }

        private NarrowProbe CreateProbe()
        {
            NarrowProbe probe = ScriptableObject.CreateInstance<NarrowProbe>();
            _ownedObjects.Add(probe);
            return probe;
        }

        [Test]
        public void NarrowProvider_IsNotTheWideService()
        {
            Assert.That(
                new NarrowProvider("en"),
                Is.Not.InstanceOf<CycloneGames.Localization.Runtime.ILocalizationService>());
        }

        [Test]
        public async Task WindowBinder_BindsFromAnInitializedNarrowProvider()
        {
            NarrowProvider provider = new NarrowProvider("en", "ja");
            provider.CompleteInitialization();
            NarrowProbe probe = CreateProbe();

            using (UIRuntimeTestFixture fixture = new UIRuntimeTestFixture())
            {
                UIWindowConfiguration configuration = fixture.CreateDirectConfiguration("NarrowBound");
                configuration.WindowPrefab.gameObject.AddComponent<NarrowTarget>().Initialize(probe);
                UIService service = new UIService(
                    fixture.Root,
                    binders: new IUIWindowBinder[] { new LocalizationWindowBinder(provider) });
                try
                {
                    await service.OpenAsync(configuration);

                    Assert.That(probe.BindCount, Is.EqualTo(1));
                    CollectionAssert.AreEqual(new[] { "ui/title@en" }, probe.Texts);

                    provider.SwitchTo(new LocaleId("ja"));
                    CollectionAssert.AreEqual(new[] { "ui/title@en", "ui/title@ja" }, probe.Texts);
                }
                finally
                {
                    service.Dispose();
                }
            }
        }

        [Test]
        public async Task WindowBinder_DefersBindingUntilNarrowProviderInitializes()
        {
            NarrowProvider provider = new NarrowProvider("en", "ja");
            NarrowProbe probe = CreateProbe();
            Assert.That(provider.IsInitialized, Is.False);

            using (UIRuntimeTestFixture fixture = new UIRuntimeTestFixture())
            {
                UIWindowConfiguration configuration = fixture.CreateDirectConfiguration("NarrowDeferred");
                configuration.WindowPrefab.gameObject.AddComponent<NarrowTarget>().Initialize(probe);
                UIService service = new UIService(
                    fixture.Root,
                    binders: new IUIWindowBinder[] { new LocalizationWindowBinder(provider) });
                try
                {
                    await service.OpenAsync(configuration);

                    // The window still opens, and nothing is bound yet.
                    Assert.That(service.ActiveWindowCount, Is.EqualTo(1));
                    Assert.That(probe.BindCount, Is.EqualTo(0));

                    provider.CompleteInitialization();

                    Assert.That(probe.BindCount, Is.EqualTo(1));
                    CollectionAssert.AreEqual(new[] { "ui/title@en" }, probe.Texts);
                }
                finally
                {
                    service.Dispose();
                }
            }
        }

        [Test]
        public async Task WindowBinder_UnsubscribesFromNarrowProviderOnClose()
        {
            NarrowProvider provider = new NarrowProvider("en", "ja");
            provider.CompleteInitialization();
            NarrowProbe probe = CreateProbe();

            using (UIRuntimeTestFixture fixture = new UIRuntimeTestFixture())
            {
                UIWindowConfiguration configuration = fixture.CreateDirectConfiguration("NarrowClosed");
                configuration.WindowPrefab.gameObject.AddComponent<NarrowTarget>().Initialize(probe);
                UIService service = new UIService(
                    fixture.Root,
                    binders: new IUIWindowBinder[] { new LocalizationWindowBinder(provider) });
                try
                {
                    await service.OpenAsync(configuration);
                    await service.CloseAsync("NarrowClosed");

                    Assert.That(probe.UnbindCount, Is.EqualTo(1));

                    provider.SwitchTo(new LocaleId("ja"));

                    // The subscription went away with the window, so no event leaked to the target.
                    CollectionAssert.AreEqual(new[] { "ui/title@en" }, probe.Texts);
                }
                finally
                {
                    service.Dispose();
                }
            }
        }
    }
}
