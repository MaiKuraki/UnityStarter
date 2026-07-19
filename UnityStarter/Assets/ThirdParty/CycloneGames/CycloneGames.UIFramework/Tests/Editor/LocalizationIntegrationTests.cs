using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Localization.Core;
using CycloneGames.Localization.Runtime;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.UIFramework.Runtime.Integrations.Localization;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class LocalizationIntegrationTests
    {
        private static readonly Action<UILocaleLayout, LocaleId, bool> ApplyLocaleDirect =
            CreateApplyLocaleDelegate();

        private readonly List<UnityEngine.Object> _ownedObjects = new List<UnityEngine.Object>(16);
        private readonly List<LocalizationService> _ownedServices = new List<LocalizationService>(4);

        private sealed class BindingProbe : ScriptableObject
        {
            private readonly List<string> _events = new List<string>(16);

            public IReadOnlyList<string> Events => _events;

            public void Record(string value)
            {
                _events.Add(value);
            }

            public void Clear()
            {
                _events.Clear();
            }
        }

        private sealed class RecordingLocalizationTarget : MonoBehaviour, ILocalizationBindingTarget
        {
            [SerializeField] private BindingProbe Probe;
            [SerializeField] private string TargetName;
            [SerializeField] private bool ThrowOnBind;
            [SerializeField] private string ReentrantTrigger;
            [SerializeField] private string ReentrantLocale;

            private ILocalizationService _service;
            private bool _isBound;
            private bool _hasReentered;

            public void Initialize(
                BindingProbe probe,
                string targetName,
                bool throwOnBind = false,
                string reentrantTrigger = null,
                string reentrantLocale = null)
            {
                Probe = probe;
                TargetName = targetName;
                ThrowOnBind = throwOnBind;
                ReentrantTrigger = reentrantTrigger;
                ReentrantLocale = reentrantLocale;
            }

            public void Bind(in LocalizationBindingContext context)
            {
                Probe.Record(TargetName + ":Bind");
                _service = context.Localization;
                _service.Changed += HandleChange;
                _isBound = true;
                Probe.Record(TargetName + ":Locale:" + _service.CurrentLocale.Code);
                if (ThrowOnBind)
                {
                    throw new InvalidOperationException(TargetName + " binding failed.");
                }
            }

            public void Unbind()
            {
                Probe.Record(TargetName + ":Unbind");
                if (_isBound && _service != null)
                {
                    _service.Changed -= HandleChange;
                }

                _isBound = false;
                _service = null;
            }

            private void HandleChange(LocalizationChange change)
            {
                Probe.Record(TargetName + ":Locale:" + change.CurrentLocale.Code);
                if (_hasReentered ||
                    string.IsNullOrEmpty(ReentrantTrigger) ||
                    !string.Equals(
                        change.CurrentLocale.Code,
                        ReentrantTrigger,
                        StringComparison.Ordinal))
                {
                    return;
                }

                _hasReentered = true;
                _service.TrySetLocale(new LocaleId(ReentrantLocale));
            }
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _ownedServices.Count - 1; i >= 0; i--)
            {
                _ownedServices[i].Dispose();
            }

            _ownedServices.Clear();
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

        [Test]
        public void LocaleChange_AppliesCompleteLayoutSnapshot()
        {
            UILocaleLayout layout = CreateLayout(out TrackedElement tracked);
            ElementSnapshot expected = CreateSnapshot(2f, true);
            ConfigureLayout(
                layout,
                "en",
                new[] { tracked },
                new[]
                {
                    new LocaleSnapshot
                    {
                        LocaleCode = "ja-JP",
                        SchemaVersion = LocaleSnapshot.CurrentSchemaVersion,
                        Elements = new[] { expected }
                    }
                });

            ApplyLocale(layout, new LocaleId("ja-JP"));

            AssertSnapshotApplied(expected, tracked);
        }

        [Test]
        public void LocaleFallback_UsesLanguageOverrideThenRestoresBase()
        {
            UILocaleLayout layout = CreateLayout(out TrackedElement tracked);
            ElementSnapshot baseSnapshot = ElementSnapshot.Capture(tracked);
            ElementSnapshot languageSnapshot = CreateSnapshot(3f, false);
            ConfigureLayout(
                layout,
                "en",
                new[] { tracked },
                new[]
                {
                    new LocaleSnapshot
                    {
                        LocaleCode = "zh",
                        SchemaVersion = LocaleSnapshot.CurrentSchemaVersion,
                        Elements = new[] { languageSnapshot }
                    }
                });

            ApplyLocale(layout, new LocaleId("zh-CN"));
            AssertSnapshotApplied(languageSnapshot, tracked);

            ApplyLocale(layout, new LocaleId("de-DE"));
            AssertSnapshotApplied(baseSnapshot, tracked);
        }

        [Test]
        public void LegacySnapshot_RestoresBaseForNewFieldsBeforeApplyingLegacyValues()
        {
            UILocaleLayout layout = CreateLayout(out TrackedElement tracked);
            ElementSnapshot baseSnapshot = ElementSnapshot.Capture(tracked);
            ElementSnapshot modern = CreateSnapshot(4f, true);
            ElementSnapshot legacy = new ElementSnapshot
            {
                AnchoredPosition = new Vector2(31f, 42f),
                SizeDelta = new Vector2(510f, 260f),
                FontSize = 27f,
                LineSpacing = 1.5f,
                CharacterSpacing = 2.5f
            };
            ConfigureLayout(
                layout,
                "en",
                new[] { tracked },
                new[]
                {
                    new LocaleSnapshot
                    {
                        LocaleCode = "ja",
                        SchemaVersion = LocaleSnapshot.CurrentSchemaVersion,
                        Elements = new[] { modern }
                    },
                    new LocaleSnapshot
                    {
                        LocaleCode = "fr",
                        SchemaVersion = 0,
                        Elements = new[] { legacy }
                    }
                });

            ApplyLocale(layout, new LocaleId("ja"));
            ApplyLocale(layout, new LocaleId("fr"));

            Assert.That(tracked.Target.anchorMin, Is.EqualTo(baseSnapshot.AnchorMin));
            Assert.That(tracked.Target.anchorMax, Is.EqualTo(baseSnapshot.AnchorMax));
            Assert.That(tracked.Target.pivot, Is.EqualTo(baseSnapshot.Pivot));
            Assert.That(tracked.Target.localScale, Is.EqualTo(baseSnapshot.LocalScale));
            Assert.That(tracked.Target.anchoredPosition, Is.EqualTo(legacy.AnchoredPosition));
            Assert.That(tracked.Target.sizeDelta, Is.EqualTo(legacy.SizeDelta));
            Assert.That(tracked.Text.fontSize, Is.EqualTo(legacy.FontSize));
        }

        [Test]
        public void MissingSchemaValue_LeavesTrackedElementAtBaseLayout()
        {
            UILocaleLayout layout = CreateLayout(out TrackedElement tracked);
            ElementSnapshot baseSnapshot = ElementSnapshot.Capture(tracked);
            ConfigureLayout(
                layout,
                "en",
                new[] { tracked },
                new[]
                {
                    new LocaleSnapshot
                    {
                        LocaleCode = "ko",
                        SchemaVersion = LocaleSnapshot.CurrentSchemaVersion,
                        Elements = new[] { default(ElementSnapshot) }
                    }
                });

            ApplyLocale(layout, new LocaleId("ko"));

            AssertSnapshotApplied(baseSnapshot, tracked);
        }

        [Test]
        public void FutureSchema_RestoresBaseInsteadOfInterpretingUnknownData()
        {
            UILocaleLayout layout = CreateLayout(out TrackedElement tracked);
            ElementSnapshot baseSnapshot = ElementSnapshot.Capture(tracked);
            ConfigureLayout(
                layout,
                "en",
                new[] { tracked },
                new[]
                {
                    new LocaleSnapshot
                    {
                        LocaleCode = "ja",
                        SchemaVersion = LocaleSnapshot.CurrentSchemaVersion + 1,
                        Elements = new[] { CreateSnapshot(9f, true) }
                    }
                });

            ApplyLocale(layout, new LocaleId("ja"));

            AssertSnapshotApplied(baseSnapshot, tracked);
        }

        [Test]
        public void PureRectImageSnapshot_AppliesWithoutTextOrLayoutAlignmentData()
        {
            GameObject root = new GameObject("LocaleLayout", typeof(RectTransform));
            GameObject child = new GameObject("Image", typeof(RectTransform), typeof(Image));
            child.transform.SetParent(root.transform, false);
            _ownedObjects.Add(root);

            UILocaleLayout layout = root.AddComponent<UILocaleLayout>();
            RectTransform rect = child.GetComponent<RectTransform>();
            TrackedElement tracked = new TrackedElement { Target = rect };
            ElementSnapshot snapshot = ElementSnapshot.Capture(in tracked);
            snapshot.AnchoredPosition = new Vector2(144f, 64f);
            ConfigureLayout(
                layout,
                "en",
                new[] { tracked },
                new[]
                {
                    new LocaleSnapshot
                    {
                        LocaleCode = "ja",
                        SchemaVersion = LocaleSnapshot.CurrentSchemaVersion,
                        Elements = new[] { snapshot }
                    }
                });

            ApplyLocale(layout, new LocaleId("ja"));

            Assert.That(rect.anchoredPosition, Is.EqualTo(snapshot.AnchoredPosition));
        }

        [Test]
        public void AlternatingLayoutApply_DoesNotAllocateManagedMemoryAfterWarmup()
        {
            GameObject root = new GameObject("LocaleLayout", typeof(RectTransform));
            _ownedObjects.Add(root);
            UILocaleLayout layout = root.AddComponent<UILocaleLayout>();
            TrackedElement tracked = new TrackedElement
            {
                Target = root.GetComponent<RectTransform>()
            };
            ConfigureLayout(
                layout,
                "en",
                new[] { tracked },
                new[]
                {
                    new LocaleSnapshot
                    {
                        LocaleCode = "ja",
                        SchemaVersion = LocaleSnapshot.CurrentSchemaVersion,
                        Elements = new[] { CreateSnapshot(2f, false) }
                    }
                });

            LocaleId baseLocale = new LocaleId("en");
            LocaleId overrideLocale = new LocaleId("ja");
            ApplyLocaleDirect(layout, overrideLocale, true);
            ApplyLocaleDirect(layout, baseLocale, true);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 512; i++)
            {
                ApplyLocaleDirect(
                    layout,
                    (i & 1) == 0 ? overrideLocale : baseLocale,
                    true);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.Zero);
        }

        [Test]
        public void ExplicitBinding_ReceivesLocaleEventsAndUnbindStopsDelivery()
        {
            Locale english = CreateLocale("en");
            Locale japanese = CreateLocale("ja");
            LocalizationService service = CreateService(english, english, japanese);

            UILocaleLayout layout = CreateLayout(out TrackedElement tracked);
            ElementSnapshot japaneseSnapshot = CreateSnapshot(5f, true);
            ConfigureLayout(
                layout,
                "en",
                new[] { tracked },
                new[]
                {
                    new LocaleSnapshot
                    {
                        LocaleCode = "ja",
                        SchemaVersion = LocaleSnapshot.CurrentSchemaVersion,
                        Elements = new[] { japaneseSnapshot }
                    }
                });

            LocalizationBindingContext bindingContext = new LocalizationBindingContext(service);
            layout.Bind(in bindingContext);
            Assert.That(service.TrySetLocale(new LocaleId("ja")), Is.True);
            AssertSnapshotApplied(japaneseSnapshot, tracked);

            layout.Unbind();
            tracked.Target.anchoredPosition = new Vector2(999f, 999f);
            Assert.That(service.TrySetLocale(new LocaleId("en")), Is.True);
            Assert.That(tracked.Target.anchoredPosition, Is.EqualTo(new Vector2(999f, 999f)));
        }

        [Test]
        public void ServiceShutdown_RestoresBaseLayout()
        {
            Locale english = CreateLocale("en");
            Locale japanese = CreateLocale("ja");
            LocalizationService service = CreateService(english, english, japanese);
            UILocaleLayout layout = CreateLayout(out TrackedElement tracked);
            ElementSnapshot baseSnapshot = ElementSnapshot.Capture(in tracked);
            ElementSnapshot japaneseSnapshot = CreateSnapshot(5f, true);
            ConfigureLayout(
                layout,
                "en",
                new[] { tracked },
                new[]
                {
                    new LocaleSnapshot
                    {
                        LocaleCode = "ja",
                        SchemaVersion = LocaleSnapshot.CurrentSchemaVersion,
                        Elements = new[] { japaneseSnapshot }
                    }
                });
            LocalizationBindingContext bindingContext = new LocalizationBindingContext(service);
            layout.Bind(in bindingContext);
            service.TrySetLocale(new LocaleId("ja"));
            AssertSnapshotApplied(japaneseSnapshot, tracked);

            service.Shutdown();

            Assert.That(service.IsInitialized, Is.False);
            AssertSnapshotApplied(baseSnapshot, tracked);
        }

        [Test]
        public void ExplicitBinding_RejectsUninitializedServiceWithoutReplacingAValidBinding()
        {
            Locale english = CreateLocale("en");
            Locale japanese = CreateLocale("ja");
            LocalizationService initialized = CreateService(english, english, japanese);
            LocalizationService uninitialized = OwnService();
            UILocaleLayout layout = CreateLayout(out TrackedElement tracked);
            ElementSnapshot japaneseSnapshot = CreateSnapshot(5f, true);
            ConfigureLayout(
                layout,
                "en",
                new[] { tracked },
                new[]
                {
                    new LocaleSnapshot
                    {
                        LocaleCode = "ja",
                        SchemaVersion = LocaleSnapshot.CurrentSchemaVersion,
                        Elements = new[] { japaneseSnapshot }
                    }
                });
            LocalizationBindingContext initializedContext = new LocalizationBindingContext(initialized);
            LocalizationBindingContext uninitializedContext = new LocalizationBindingContext(uninitialized);
            layout.Bind(in initializedContext);

            Assert.Throws<InvalidOperationException>(() => layout.Bind(uninitializedContext));
            Assert.That(initialized.TrySetLocale(new LocaleId("ja")), Is.True);

            AssertSnapshotApplied(japaneseSnapshot, tracked);
        }

        [Test]
        public void LocaleMutation_FromNonOwnerThreadFailsWithoutChangingState()
        {
            Locale english = CreateLocale("en");
            Locale japanese = CreateLocale("ja");
            LocalizationService service = CreateService(english, english, japanese);
            Exception failure = null;
            Thread worker = new Thread(() =>
            {
                try
                {
                    service.TrySetLocale(new LocaleId("ja"));
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            })
            {
                IsBackground = true
            };

            worker.Start();
            Assert.That(worker.Join(5000), Is.True, "Worker did not complete in time.");

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(service.CurrentLocale, Is.EqualTo(new LocaleId("en")));
        }

        [Test]
        public void DisabledLayout_CatchesUpToLatestLocaleWhenReenabled()
        {
            Locale english = CreateLocale("en");
            Locale japanese = CreateLocale("ja");
            LocalizationService service = CreateService(english, english, japanese);
            UILocaleLayout layout = CreateLayout(out TrackedElement tracked);
            ElementSnapshot baseSnapshot = ElementSnapshot.Capture(tracked);
            ElementSnapshot japaneseSnapshot = CreateSnapshot(6f, true);
            ConfigureLayout(
                layout,
                "en",
                new[] { tracked },
                new[]
                {
                    new LocaleSnapshot
                    {
                        LocaleCode = "ja",
                        SchemaVersion = LocaleSnapshot.CurrentSchemaVersion,
                        Elements = new[] { japaneseSnapshot }
                    }
                });
            LocalizationBindingContext bindingContext = new LocalizationBindingContext(service);
            layout.Bind(in bindingContext);

            layout.enabled = false;
            TestReflection.Invoke(layout, "OnDisable");
            Assert.That(service.TrySetLocale(new LocaleId("ja")), Is.True);
            AssertSnapshotApplied(baseSnapshot, tracked);
            layout.enabled = true;
            TestReflection.Invoke(layout, "OnEnable");

            AssertSnapshotApplied(japaneseSnapshot, tracked);
        }

        [Test]
        public async Task WindowBinder_BindsTargetsInHierarchyOrderAndUnbindsInReverse()
        {
            Locale english = CreateLocale("en");
            Locale japanese = CreateLocale("ja");
            LocalizationService localization = CreateService(english, english, japanese);

            BindingProbe probe = ScriptableObject.CreateInstance<BindingProbe>();
            _ownedObjects.Add(probe);
            using (UIRuntimeTestFixture fixture = new UIRuntimeTestFixture())
            {
                UIWindowConfiguration configuration = fixture.CreateDirectConfiguration("LocalizedWindow");
                RecordingLocalizationTarget first = configuration.WindowPrefab.gameObject
                    .AddComponent<RecordingLocalizationTarget>();
                first.Initialize(probe, "A");
                GameObject child = new GameObject("LocalizedChild", typeof(RectTransform));
                child.transform.SetParent(configuration.WindowPrefab.transform, false);
                RecordingLocalizationTarget second = child.AddComponent<RecordingLocalizationTarget>();
                second.Initialize(probe, "B");

                UIService service = new UIService(
                    fixture.Root,
                    binders: new IUIWindowBinder[]
                    {
                        new LocalizationWindowBinder(localization)
                    });
                try
                {
                    await service.OpenAsync(configuration);
                    CollectionAssert.AreEqual(
                        new[] { "A:Bind", "A:Locale:en", "B:Bind", "B:Locale:en" },
                        probe.Events);

                    probe.Clear();
                    Assert.That(localization.TrySetLocale(new LocaleId("ja")), Is.True);
                    CollectionAssert.AreEqual(
                        new[] { "A:Locale:ja", "B:Locale:ja" },
                        probe.Events);

                    probe.Clear();
                    await service.CloseAsync("LocalizedWindow");
                    CollectionAssert.AreEqual(new[] { "B:Unbind", "A:Unbind" }, probe.Events);

                    probe.Clear();
                    localization.TrySetLocale(new LocaleId("en"));
                    Assert.That(probe.Events, Is.Empty);
                }
                finally
                {
                    service.Dispose();
                }
            }
        }

        [Test]
        public async Task WindowBinder_TargetFailureRollsBackAttemptedTargetsInReverse()
        {
            Locale english = CreateLocale("en");
            LocalizationService localization = CreateService(english, english);
            BindingProbe probe = ScriptableObject.CreateInstance<BindingProbe>();
            _ownedObjects.Add(probe);

            using (UIRuntimeTestFixture fixture = new UIRuntimeTestFixture())
            {
                UIWindowConfiguration configuration = fixture.CreateDirectConfiguration("FailingLocalizedWindow");
                RecordingLocalizationTarget first = configuration.WindowPrefab.gameObject
                    .AddComponent<RecordingLocalizationTarget>();
                first.Initialize(probe, "A");
                RecordingLocalizationTarget failing = configuration.WindowPrefab.gameObject
                    .AddComponent<RecordingLocalizationTarget>();
                failing.Initialize(probe, "B", throwOnBind: true);
                RecordingLocalizationTarget unattempted = configuration.WindowPrefab.gameObject
                    .AddComponent<RecordingLocalizationTarget>();
                unattempted.Initialize(probe, "C");
                UIService service = new UIService(
                    fixture.Root,
                    binders: new IUIWindowBinder[]
                    {
                        new LocalizationWindowBinder(localization)
                    });
                try
                {
                    InvalidOperationException failure = null;
                    try
                    {
                        await service.OpenAsync(configuration);
                    }
                    catch (InvalidOperationException exception)
                    {
                        failure = exception;
                    }

                    Assert.That(failure, Is.Not.Null);
                    Assert.That(service.ActiveWindowCount, Is.Zero);
                    Assert.That(
                        service.TryGetWindow("FailingLocalizedWindow", out UIWindow failedWindow),
                        Is.False);
                    Assert.That(failedWindow, Is.Null);
                    CollectionAssert.AreEqual(
                        new[]
                        {
                            "A:Bind",
                            "A:Locale:en",
                            "B:Bind",
                            "B:Locale:en",
                            "B:Unbind",
                            "A:Unbind"
                        },
                        probe.Events);
                }
                finally
                {
                    service.Dispose();
                }
            }
        }

        [Test]
        public async Task WindowBinder_UninitializedServiceFailsBeforeSubscriptionAndCanRetryAfterInitialization()
        {
            Locale english = CreateLocale("en");
            LocalizationService localization = OwnService();
            BindingProbe probe = ScriptableObject.CreateInstance<BindingProbe>();
            _ownedObjects.Add(probe);

            using (UIRuntimeTestFixture fixture = new UIRuntimeTestFixture())
            {
                UIWindowConfiguration configuration = fixture.CreateDirectConfiguration("LateLocalization");
                RecordingLocalizationTarget target = configuration.WindowPrefab.gameObject
                    .AddComponent<RecordingLocalizationTarget>();
                target.Initialize(probe, "Target");
                UIService service = new UIService(
                    fixture.Root,
                    binders: new IUIWindowBinder[]
                    {
                        new LocalizationWindowBinder(localization)
                    });
                try
                {
                    Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        await service.OpenAsync(configuration));
                    Assert.That(probe.Events, Is.Empty);

                    localization.Initialize(
                        new LocalizationOptions(
                            english,
                            new[] { english },
                            detectSystemLanguage: false));
                    await service.OpenAsync(configuration);

                    CollectionAssert.AreEqual(
                        new[] { "Target:Bind", "Target:Locale:en" },
                        probe.Events);
                }
                finally
                {
                    service.Dispose();
                }
            }
        }

        [Test]
        public async Task WindowBinder_ReentrantLocaleChangePreservesSubscriberOrder()
        {
            Locale english = CreateLocale("en");
            Locale japanese = CreateLocale("ja");
            Locale korean = CreateLocale("ko");
            LocalizationService localization = CreateService(
                english,
                english,
                japanese,
                korean);
            BindingProbe probe = ScriptableObject.CreateInstance<BindingProbe>();
            _ownedObjects.Add(probe);

            using (UIRuntimeTestFixture fixture = new UIRuntimeTestFixture())
            {
                UIWindowConfiguration configuration = fixture.CreateDirectConfiguration("ReentrantLocale");
                RecordingLocalizationTarget reentrant = configuration.WindowPrefab.gameObject
                    .AddComponent<RecordingLocalizationTarget>();
                reentrant.Initialize(probe, "A", reentrantTrigger: "ja", reentrantLocale: "ko");
                RecordingLocalizationTarget recorder = configuration.WindowPrefab.gameObject
                    .AddComponent<RecordingLocalizationTarget>();
                recorder.Initialize(probe, "B");
                UIService service = new UIService(
                    fixture.Root,
                    binders: new IUIWindowBinder[]
                    {
                        new LocalizationWindowBinder(localization)
                    });
                try
                {
                    await service.OpenAsync(configuration);
                    probe.Clear();
                    Assert.That(localization.TrySetLocale(new LocaleId("ja")), Is.True);

                    Assert.That(localization.CurrentLocale, Is.EqualTo(new LocaleId("ko")));
                    CollectionAssert.AreEqual(
                        new[]
                        {
                            "A:Locale:ja",
                            "B:Locale:ja",
                            "A:Locale:ko",
                            "B:Locale:ko"
                        },
                        probe.Events);
                }
                finally
                {
                    service.Dispose();
                }
            }
        }

        private UILocaleLayout CreateLayout(out TrackedElement tracked)
        {
            GameObject root = new GameObject("LocaleLayoutRoot", typeof(RectTransform));
            GameObject child = new GameObject(
                "TrackedElement",
                typeof(RectTransform),
                typeof(TextMeshProUGUI),
                typeof(VerticalLayoutGroup));
            child.transform.SetParent(root.transform, false);
            _ownedObjects.Add(root);

            RectTransform rect = child.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.15f, 0.2f);
            rect.anchorMax = new Vector2(0.75f, 0.8f);
            rect.pivot = new Vector2(0.3f, 0.7f);
            rect.anchoredPosition = new Vector2(10f, 20f);
            rect.sizeDelta = new Vector2(240f, 120f);
            rect.localScale = new Vector3(1.1f, 1.2f, 1f);

            TextMeshProUGUI text = child.GetComponent<TextMeshProUGUI>();
            text.fontSize = 18f;
            text.lineSpacing = 1f;
            text.characterSpacing = 2f;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.isRightToLeftText = false;

            VerticalLayoutGroup layoutGroup = child.GetComponent<VerticalLayoutGroup>();
            layoutGroup.childAlignment = TextAnchor.UpperLeft;

            tracked = new TrackedElement
            {
                Target = rect,
                Text = text,
                LayoutGroup = layoutGroup
            };
            return root.AddComponent<UILocaleLayout>();
        }

        private Locale CreateLocale(string code)
        {
            Locale locale = ScriptableObject.CreateInstance<Locale>();
            TestReflection.SetField(locale, "localeCode", code);
            TestReflection.Invoke(locale, "OnValidate");
            _ownedObjects.Add(locale);
            return locale;
        }

        private LocalizationService OwnService()
        {
            LocalizationService service = new LocalizationService();
            _ownedServices.Add(service);
            return service;
        }

        private LocalizationService CreateService(
            Locale defaultLocale,
            params Locale[] availableLocales)
        {
            LocalizationService service = OwnService();
            service.Initialize(
                new LocalizationOptions(
                    defaultLocale,
                    availableLocales,
                    detectSystemLanguage: false));
            return service;
        }

        private static void ConfigureLayout(
            UILocaleLayout layout,
            string baseLocale,
            TrackedElement[] elements,
            LocaleSnapshot[] snapshots)
        {
            TestReflection.SetField(layout, "_baseLocale", baseLocale);
            TestReflection.SetField(layout, "_elements", elements);
            TestReflection.SetField(layout, "_snapshots", snapshots);
            TestReflection.Invoke(layout, "OnValidate");
        }

        private static void ApplyLocale(UILocaleLayout layout, LocaleId locale)
        {
            ApplyLocaleDirect(layout, locale, true);
        }

        private static Action<UILocaleLayout, LocaleId, bool> CreateApplyLocaleDelegate()
        {
            MethodInfo method = typeof(UILocaleLayout).GetMethod(
                "Apply",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(typeof(UILocaleLayout).FullName, "Apply");
            }

            return (Action<UILocaleLayout, LocaleId, bool>)Delegate.CreateDelegate(
                typeof(Action<UILocaleLayout, LocaleId, bool>),
                method);
        }

        private static ElementSnapshot CreateSnapshot(float multiplier, bool rightToLeft)
        {
            return new ElementSnapshot
            {
                FontSize = 20f * multiplier,
                LineSpacing = 2f * multiplier,
                CharacterSpacing = 3f * multiplier,
                AnchoredPosition = new Vector2(11f, 13f) * multiplier,
                SizeDelta = new Vector2(170f, 90f) * multiplier,
                AnchorMin = new Vector2(0.05f, 0.1f) * multiplier,
                AnchorMax = new Vector2(0.35f, 0.4f) * multiplier,
                Pivot = new Vector2(0.2f, 0.25f) * multiplier,
                LocalScale = new Vector3(multiplier, multiplier + 0.1f, 1f),
                TextAlignment = rightToLeft
                    ? TextAlignmentOptions.TopRight
                    : TextAlignmentOptions.BottomLeft,
                IsRightToLeftText = rightToLeft,
                ChildAlignment = rightToLeft ? TextAnchor.UpperRight : TextAnchor.LowerLeft,
                HasValue = true
            };
        }

        private static void AssertSnapshotApplied(
            in ElementSnapshot expected,
            in TrackedElement tracked)
        {
            Assert.That(tracked.Target.anchorMin, Is.EqualTo(expected.AnchorMin));
            Assert.That(tracked.Target.anchorMax, Is.EqualTo(expected.AnchorMax));
            Assert.That(tracked.Target.pivot, Is.EqualTo(expected.Pivot));
            Assert.That(tracked.Target.anchoredPosition, Is.EqualTo(expected.AnchoredPosition));
            Assert.That(tracked.Target.sizeDelta, Is.EqualTo(expected.SizeDelta));
            Assert.That(tracked.Target.localScale, Is.EqualTo(expected.LocalScale));
            Assert.That(tracked.Text.fontSize, Is.EqualTo(expected.FontSize));
            Assert.That(tracked.Text.lineSpacing, Is.EqualTo(expected.LineSpacing));
            Assert.That(tracked.Text.characterSpacing, Is.EqualTo(expected.CharacterSpacing));
            Assert.That(tracked.Text.alignment, Is.EqualTo(expected.TextAlignment));
            Assert.That(tracked.Text.isRightToLeftText, Is.EqualTo(expected.IsRightToLeftText));
            Assert.That(tracked.LayoutGroup.childAlignment, Is.EqualTo(expected.ChildAlignment));
        }
    }
}
