// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CycloneGames.Audio.Runtime;
using CycloneGames.Audio.Runtime.Integrations.Localization;
using CycloneGames.Localization.Core;
using CycloneGames.Localization.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Audio.Localization.Tests.Editor
{
    public sealed class AudioLocalizationBridgeTests
    {
        private readonly List<UnityEngine.Object> ownedObjects =
            new List<UnityEngine.Object>(16);
        private readonly List<LocalizationService> ownedServices =
            new List<LocalizationService>(4);

        private readonly struct MapEntryData
        {
            public MapEntryData(
                string localizationLocaleCode,
                string voiceLocaleCode,
                params string[] voiceFallbackLocaleCodes)
            {
                LocalizationLocaleCode = localizationLocaleCode;
                VoiceLocaleCode = voiceLocaleCode;
                VoiceFallbackLocaleCodes = voiceFallbackLocaleCodes ?? Array.Empty<string>();
            }

            public string LocalizationLocaleCode { get; }
            public string VoiceLocaleCode { get; }
            public string[] VoiceFallbackLocaleCodes { get; }
        }

        private sealed class FakeAudioVoiceLocaleControl : IAudioVoiceLocaleControl
        {
            private AudioVoiceLocaleSnapshot currentVoiceLocale;
            private long revision;

            public FakeAudioVoiceLocaleControl(AudioVoiceLocaleSnapshot initial = default)
            {
                currentVoiceLocale = initial;
            }

            public AudioVoiceLocaleSnapshot CurrentVoiceLocale => currentVoiceLocale;
            public long VoiceLocaleRevision => revision;
            public int SetCallCount { get; private set; }
            public bool AcceptSet { get; set; } = true;
            public int RejectAfterMutationOnCall { get; set; } = -1;
            public int RejectWithoutMutationOnCall { get; set; } = -1;
            public List<AudioVoiceLocaleSnapshot> SetCalls { get; } =
                new List<AudioVoiceLocaleSnapshot>(8);
            public Action<AudioVoiceLocaleSnapshot> AfterSet { get; set; }

            public event Action<AudioVoiceLocaleChange> VoiceLocaleChanged;

            public bool TrySetVoiceLocale(in AudioVoiceLocaleSnapshot locale)
            {
                SetCallCount++;
                int callNumber = SetCallCount;
                SetCalls.Add(locale);
                if (!locale.IsValid ||
                    !AcceptSet ||
                    callNumber == RejectWithoutMutationOnCall)
                {
                    return false;
                }
                if (locale == currentVoiceLocale)
                    return true;

                AudioVoiceLocaleSnapshot previous = currentVoiceLocale;
                currentVoiceLocale = locale;
                revision++;
                VoiceLocaleChanged?.Invoke(
                    new AudioVoiceLocaleChange(previous, currentVoiceLocale, revision));
                AfterSet?.Invoke(locale);
                if (callNumber == RejectAfterMutationOnCall)
                    return false;
                return true;
            }

            public bool ClearVoiceLocale()
            {
                if (!currentVoiceLocale.IsValid)
                    return false;

                AudioVoiceLocaleSnapshot previous = currentVoiceLocale;
                currentVoiceLocale = default;
                revision++;
                VoiceLocaleChanged?.Invoke(
                    new AudioVoiceLocaleChange(previous, currentVoiceLocale, revision));
                return true;
            }
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = ownedServices.Count - 1; i >= 0; i--)
                ownedServices[i].Dispose();
            ownedServices.Clear();

            for (int i = ownedObjects.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object owned = ownedObjects[i];
                if (owned != null)
                    UnityEngine.Object.DestroyImmediate(owned);
            }

            ownedObjects.Clear();
        }

        [Test]
        public void IdentityMapper_UsesCanonicalLocaleWithoutFallbacks()
        {
            bool mapped = IdentityAudioLocalizationMapper.Instance.TryMap(
                new LocaleId("ZH-hans-cn"),
                out AudioVoiceLocaleSnapshot voiceLocale);

            Assert.That(mapped, Is.True);
            Assert.That(voiceLocale.Primary.Code, Is.EqualTo("zh-Hans-CN"));
            Assert.That(voiceLocale.FallbackCount, Is.Zero);
        }

        [Test]
        public void ExplicitMap_UsesExactMappingAndPreservesOrderedFallbacks()
        {
            AudioLocalizationMap map = CreateMap(
                new MapEntryData("ja-JP", "ja-JP", "ja", "en-US"));

            Assert.That(
                map.TryMap(new LocaleId("ja-JP"), out AudioVoiceLocaleSnapshot voiceLocale),
                Is.True);
            Assert.That(voiceLocale.Primary.Code, Is.EqualTo("ja-JP"));
            Assert.That(voiceLocale.FallbackCount, Is.EqualTo(2));
            Assert.That(voiceLocale.GetFallback(0).Code, Is.EqualTo("ja"));
            Assert.That(voiceLocale.GetFallback(1).Code, Is.EqualTo("en-US"));
            Assert.That(map.TryMap(new LocaleId("ja"), out _), Is.False);
        }

        [Test]
        public void ExplicitMap_InvalidEntryRejectsCompleteMap()
        {
            AudioLocalizationMap map = CreateMap(
                new MapEntryData("en", "en-US"),
                new MapEntryData("ja", "x"));

            Assert.That(map.TryValidate(out string error), Is.False);
            Assert.That(error, Does.Contain("invalid voice locale code"));
            Assert.That(map.TryMap(new LocaleId("en"), out _), Is.False);
        }

        [Test]
        public void ExplicitMap_CanonicalDuplicateRejectsCompleteMap()
        {
            AudioLocalizationMap map = CreateMap(
                new MapEntryData("en-US", "en-US"),
                new MapEntryData("en-US", "en-GB"));

            Assert.That(map.TryValidate(out string error), Is.False);
            Assert.That(error, Does.Contain("duplicate locale"));
            Assert.That(map.TryMap(new LocaleId("en-US"), out _), Is.False);
        }

        [Test]
        public void ExplicitMap_NonCanonicalCodesAreRejected()
        {
            AudioLocalizationMap sourceMap = CreateMap(
                new MapEntryData("EN-us", "en-US"));
            Assert.That(sourceMap.TryValidate(out string sourceError), Is.False);
            Assert.That(sourceError, Does.Contain("must use canonical"));

            AudioLocalizationMap voiceMap = CreateMap(
                new MapEntryData("en-US", "EN-us"));
            Assert.That(voiceMap.TryValidate(out string voiceError), Is.False);
            Assert.That(voiceError, Does.Contain("must use canonical"));

            AudioLocalizationMap fallbackMap = CreateMap(
                new MapEntryData("en-US", "en-US", "EN-us"));
            Assert.That(fallbackMap.TryValidate(out string fallbackError), Is.False);
            Assert.That(fallbackError, Does.Contain("must use canonical"));
        }

        [Test]
        public void ExplicitMap_DuplicateVoiceFallbacksAreRejected()
        {
            AudioLocalizationMap primaryDuplicate = CreateMap(
                new MapEntryData("en-US", "en-US", "en-US"));
            Assert.That(primaryDuplicate.TryValidate(out string primaryError), Is.False);
            Assert.That(primaryError, Does.Contain("duplicates the primary"));

            AudioLocalizationMap fallbackDuplicate = CreateMap(
                new MapEntryData("en-US", "en-US", "en", "en"));
            Assert.That(fallbackDuplicate.TryValidate(out string fallbackError), Is.False);
            Assert.That(fallbackError, Does.Contain("duplicate voice fallback"));
        }

        [Test]
        public void Bind_PerformsInitialIdentitySynchronization()
        {
            Locale english = CreateLocale("en-US");
            LocalizationService localization = CreateService(english, english);
            var audio = new FakeAudioVoiceLocaleControl();

            using var bridge = new AudioLocalizationBridge(localization, audio);
            bridge.Bind();

            Assert.That(bridge.IsBound, Is.True);
            Assert.That(audio.SetCallCount, Is.EqualTo(1));
            Assert.That(audio.CurrentVoiceLocale.Primary.Code, Is.EqualTo("en-US"));
            Assert.That(
                bridge.LastProcessedLocalizationRevision,
                Is.EqualTo(localization.Revision));
        }

        [Test]
        public void LocaleChanged_UpdatesVoiceLocale()
        {
            Locale english = CreateLocale("en");
            Locale japanese = CreateLocale("ja");
            LocalizationService localization = CreateService(english, english, japanese);
            var audio = new FakeAudioVoiceLocaleControl();

            using var bridge = new AudioLocalizationBridge(localization, audio);
            bridge.Bind();
            Assert.That(localization.TrySetLocale(japanese.Id), Is.True);

            Assert.That(audio.SetCallCount, Is.EqualTo(2));
            Assert.That(audio.CurrentVoiceLocale.Primary.Code, Is.EqualTo("ja"));
            Assert.That(bridge.LastKnownGoodVoiceLocale.Primary.Code, Is.EqualTo("ja"));
        }

        [Test]
        public void ContentAndPseudoChanges_DoNotInvokeAudioSetter()
        {
            Locale english = CreateLocale("en");
            LocalizationService localization = CreateService(english, english);
            var audio = new FakeAudioVoiceLocaleControl();

            using var bridge = new AudioLocalizationBridge(localization, audio);
            bridge.Bind();
            int callsAfterBind = audio.SetCallCount;

            Assert.That(
                localization.RegisterStringTable(CreateStringTable("ui", "en", "title", "Start")),
                Is.True);
            localization.PseudoMode = PseudoLocaleMode.Accents;

            Assert.That(audio.SetCallCount, Is.EqualTo(callsAfterBind));
            Assert.That(audio.CurrentVoiceLocale.Primary.Code, Is.EqualTo("en"));
        }

        [Test]
        public void MissingMapping_PreservesLastKnownGoodAndReportsDiagnostic()
        {
            Locale english = CreateLocale("en");
            Locale japanese = CreateLocale("ja");
            LocalizationService localization = CreateService(english, english, japanese);
            AudioLocalizationMap map = CreateMap(new MapEntryData("en", "en-US", "en"));
            var audio = new FakeAudioVoiceLocaleControl();
            var diagnostics = new List<AudioLocalizationDiagnostic>();

            using var bridge = new AudioLocalizationBridge(
                localization,
                audio,
                map,
                diagnostics.Add);
            bridge.Bind();
            Assert.That(localization.TrySetLocale(japanese.Id), Is.True);

            Assert.That(audio.SetCallCount, Is.EqualTo(1));
            Assert.That(audio.CurrentVoiceLocale.Primary.Code, Is.EqualTo("en-US"));
            Assert.That(bridge.LastKnownGoodVoiceLocale.Primary.Code, Is.EqualTo("en-US"));
            Assert.That(diagnostics.Count, Is.EqualTo(1));
            Assert.That(
                diagnostics[0].Code,
                Is.EqualTo(AudioLocalizationDiagnosticCode.MappingUnavailable));
            Assert.That(diagnostics[0].LocalizationLocale, Is.EqualTo(japanese.Id));
        }

        [Test]
        public void AudioRejection_PreservesLastKnownGoodAndReportsDiagnostic()
        {
            Locale english = CreateLocale("en");
            Locale japanese = CreateLocale("ja");
            LocalizationService localization = CreateService(english, english, japanese);
            var audio = new FakeAudioVoiceLocaleControl();
            var diagnostics = new List<AudioLocalizationDiagnostic>();

            using var bridge = new AudioLocalizationBridge(
                localization,
                audio,
                diagnosticSink: diagnostics.Add);
            bridge.Bind();
            audio.AcceptSet = false;

            Assert.That(localization.TrySetLocale(japanese.Id), Is.True);

            Assert.That(audio.CurrentVoiceLocale.Primary.Code, Is.EqualTo("en"));
            Assert.That(bridge.LastKnownGoodVoiceLocale.Primary.Code, Is.EqualTo("en"));
            Assert.That(diagnostics.Count, Is.EqualTo(1));
            Assert.That(
                diagnostics[0].Code,
                Is.EqualTo(AudioLocalizationDiagnosticCode.VoiceLocaleRejected));
        }

        [Test]
        public void PartialAudioRejection_RestoresPreviousVoiceLocale()
        {
            Locale english = CreateLocale("en");
            Locale japanese = CreateLocale("ja");
            LocalizationService localization = CreateService(english, english, japanese);
            var audio = new FakeAudioVoiceLocaleControl
            {
                RejectAfterMutationOnCall = 2,
            };
            var diagnostics = new List<AudioLocalizationDiagnostic>();

            using var bridge = new AudioLocalizationBridge(
                localization,
                audio,
                diagnosticSink: diagnostics.Add);
            bridge.Bind();

            Assert.That(localization.TrySetLocale(japanese.Id), Is.True);

            CollectionAssert.AreEqual(
                new[] { "en", "ja", "en" },
                GetPrimaryCodes(audio.SetCalls));
            Assert.That(audio.CurrentVoiceLocale.Primary.Code, Is.EqualTo("en"));
            Assert.That(bridge.LastKnownGoodVoiceLocale.Primary.Code, Is.EqualTo("en"));
            Assert.That(diagnostics.Count, Is.EqualTo(1));
            Assert.That(
                diagnostics[0].Code,
                Is.EqualTo(AudioLocalizationDiagnosticCode.VoiceLocaleRejected));
        }

        [Test]
        public void FailedLastKnownGoodRestore_ReportsDedicatedDiagnostic()
        {
            Locale english = CreateLocale("en");
            Locale japanese = CreateLocale("ja");
            LocalizationService localization = CreateService(english, english, japanese);
            var audio = new FakeAudioVoiceLocaleControl
            {
                RejectAfterMutationOnCall = 2,
                RejectWithoutMutationOnCall = 3,
            };
            var diagnostics = new List<AudioLocalizationDiagnostic>();

            using var bridge = new AudioLocalizationBridge(
                localization,
                audio,
                diagnosticSink: diagnostics.Add);
            bridge.Bind();

            Assert.That(localization.TrySetLocale(japanese.Id), Is.True);

            Assert.That(audio.CurrentVoiceLocale.Primary.Code, Is.EqualTo("ja"));
            Assert.That(bridge.LastKnownGoodVoiceLocale.Primary.Code, Is.EqualTo("en"));
            Assert.That(
                ContainsDiagnostic(
                    diagnostics,
                    AudioLocalizationDiagnosticCode.LastKnownGoodRestoreFailed),
                Is.True);
            Assert.That(
                ContainsDiagnostic(
                    diagnostics,
                    AudioLocalizationDiagnosticCode.VoiceLocaleRejected),
                Is.True);
        }

        [Test]
        public void Shutdown_UnbindsAndPreservesVoiceLocale()
        {
            Locale english = CreateLocale("en");
            LocalizationService localization = CreateService(english, english);
            var audio = new FakeAudioVoiceLocaleControl();

            using var bridge = new AudioLocalizationBridge(localization, audio);
            bridge.Bind();
            AudioVoiceLocaleSnapshot beforeShutdown = audio.CurrentVoiceLocale;
            int callsBeforeShutdown = audio.SetCallCount;

            localization.Shutdown();

            Assert.That(bridge.IsBound, Is.False);
            Assert.That(audio.SetCallCount, Is.EqualTo(callsBeforeShutdown));
            Assert.That(audio.CurrentVoiceLocale, Is.EqualTo(beforeShutdown));
        }

        [Test]
        public void Dispose_IsIdempotentAndStopsSynchronization()
        {
            Locale english = CreateLocale("en");
            Locale japanese = CreateLocale("ja");
            LocalizationService localization = CreateService(english, english, japanese);
            var audio = new FakeAudioVoiceLocaleControl();
            var bridge = new AudioLocalizationBridge(localization, audio);
            bridge.Bind();
            int callsAfterBind = audio.SetCallCount;

            Assert.DoesNotThrow(bridge.Dispose);
            Assert.DoesNotThrow(bridge.Dispose);
            Assert.That(localization.TrySetLocale(japanese.Id), Is.True);

            Assert.That(bridge.IsBound, Is.False);
            Assert.That(audio.SetCallCount, Is.EqualTo(callsAfterBind));
            Assert.That(audio.CurrentVoiceLocale.Primary.Code, Is.EqualTo("en"));
        }

        [Test]
        public void PublicStateReads_FromWorkerThread_ThrowOwnerThreadContractError()
        {
            Locale english = CreateLocale("en");
            LocalizationService localization = CreateService(english, english);
            var audio = new FakeAudioVoiceLocaleControl();

            using var bridge = new AudioLocalizationBridge(localization, audio);

            Assert.Throws<InvalidOperationException>(() =>
                Task.Run(() => bridge.IsBound).GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() =>
                Task.Run(() => bridge.LastKnownGoodVoiceLocale).GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() =>
                Task.Run(() => bridge.LastProcessedLocalizationRevision).GetAwaiter().GetResult());
        }

        [Test]
        public void ReentrantNewerLocale_FinalRevisionWins()
        {
            Locale english = CreateLocale("en");
            Locale japanese = CreateLocale("ja");
            Locale korean = CreateLocale("ko");
            LocalizationService localization = CreateService(
                english,
                english,
                japanese,
                korean);
            var audio = new FakeAudioVoiceLocaleControl();
            bool requestedKorean = false;
            audio.AfterSet = locale =>
            {
                if (requestedKorean || locale.Primary != new VoiceLocaleId("ja"))
                    return;

                requestedKorean = true;
                Assert.That(localization.TrySetLocale(korean.Id), Is.True);
            };

            using var bridge = new AudioLocalizationBridge(localization, audio);
            bridge.Bind();
            Assert.That(localization.TrySetLocale(japanese.Id), Is.True);

            Assert.That(localization.CurrentLocale, Is.EqualTo(korean.Id));
            Assert.That(audio.CurrentVoiceLocale.Primary.Code, Is.EqualTo("ko"));
            Assert.That(bridge.LastKnownGoodVoiceLocale.Primary.Code, Is.EqualTo("ko"));
            Assert.That(
                bridge.LastProcessedLocalizationRevision,
                Is.EqualTo(localization.Revision));
            CollectionAssert.AreEqual(
                new[] { "en", "ja", "ko" },
                GetPrimaryCodes(audio.SetCalls));
        }

        private LocalizationService CreateService(Locale defaultLocale, params Locale[] locales)
        {
            var service = new LocalizationService();
            ownedServices.Add(service);
            service.Initialize(new LocalizationOptions(
                defaultLocale,
                locales,
                detectSystemLanguage: false));
            return service;
        }

        private Locale CreateLocale(string code)
        {
            var locale = ScriptableObject.CreateInstance<Locale>();
            ownedObjects.Add(locale);
            var serialized = new SerializedObject(locale);
            serialized.FindProperty("localeCode").stringValue = code;
            serialized.FindProperty("displayName").stringValue = code;
            serialized.FindProperty("nativeName").stringValue = code;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return locale;
        }

        private AudioLocalizationMap CreateMap(params MapEntryData[] data)
        {
            var map = ScriptableObject.CreateInstance<AudioLocalizationMap>();
            ownedObjects.Add(map);
            var serialized = new SerializedObject(map);
            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = data != null ? data.Length : 0;

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("localizationLocaleCode").stringValue =
                    data[i].LocalizationLocaleCode;
                entry.FindPropertyRelative("voiceLocaleCode").stringValue =
                    data[i].VoiceLocaleCode;

                SerializedProperty fallbacks =
                    entry.FindPropertyRelative("voiceFallbackLocaleCodes");
                string[] fallbackCodes = data[i].VoiceFallbackLocaleCodes;
                fallbacks.arraySize = fallbackCodes.Length;
                for (int fallbackIndex = 0; fallbackIndex < fallbacks.arraySize; fallbackIndex++)
                {
                    fallbacks.GetArrayElementAtIndex(fallbackIndex).stringValue =
                        fallbackCodes[fallbackIndex];
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return map;
        }

        private StringTable CreateStringTable(
            string tableId,
            string localeCode,
            string key,
            string value)
        {
            var table = ScriptableObject.CreateInstance<StringTable>();
            ownedObjects.Add(table);
            var serialized = new SerializedObject(table);
            serialized.FindProperty("tableId").stringValue = tableId;
            serialized.FindProperty("localeCode").stringValue = localeCode;

            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = 1;
            SerializedProperty entry = entries.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("Key").stringValue = key;
            entry.FindPropertyRelative("Value").stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return table;
        }

        private static string[] GetPrimaryCodes(IReadOnlyList<AudioVoiceLocaleSnapshot> snapshots)
        {
            var result = new string[snapshots.Count];
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i].Primary.Code;
            return result;
        }

        private static bool ContainsDiagnostic(
            IReadOnlyList<AudioLocalizationDiagnostic> diagnostics,
            AudioLocalizationDiagnosticCode code)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Code == code)
                    return true;
            }

            return false;
        }
    }
}
