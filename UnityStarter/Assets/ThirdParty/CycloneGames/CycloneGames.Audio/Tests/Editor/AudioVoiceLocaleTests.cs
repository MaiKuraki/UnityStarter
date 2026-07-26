using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;

namespace CycloneGames.Audio.Tests.Editor
{
    public sealed class AudioVoiceLocaleTests
    {
        [TestCase("en", "en")]
        [TestCase("EN-us", "en-US")]
        [TestCase("ZH-hANS-cn", "zh-Hans-CN")]
        [TestCase("sr-lATN-rs-posIX", "sr-Latn-RS-posix")]
        public void VoiceLocaleId_Canonicalizes_ValidCodes(
            string source,
            string expectedCode)
        {
            Assert.IsTrue(VoiceLocaleId.TryCreate(source, out VoiceLocaleId locale));

            Assert.IsTrue(locale.IsValid);
            Assert.AreEqual(expectedCode, locale.Code);
            Assert.AreEqual(expectedCode, locale.ToString());
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("e")]
        [TestCase("1n")]
        [TestCase("-en")]
        [TestCase("en-")]
        [TestCase("en--US")]
        [TestCase("en_US")]
        [TestCase("en US")]
        [TestCase("en-123456789")]
        [TestCase("en-a-b-c-d-e-f-g-h")]
        [TestCase("\u4E2D\u6587")]
        public void VoiceLocaleId_Rejects_InvalidCodes(string source)
        {
            Assert.IsFalse(VoiceLocaleId.TryCreate(source, out VoiceLocaleId locale));
            Assert.IsFalse(locale.IsValid);
            Assert.AreEqual(VoiceLocaleId.Invalid, locale);
            Assert.AreEqual(string.Empty, locale.ToString());
        }

        [Test]
        public void VoiceLocaleId_Rejects_CodeBeyondMaximumLength()
        {
            const string source =
                "abcdefgh-abcdefgh-abcdefgh-abcdefgh-abcdefgh-abcdefgh-abcdefgh-abcdefgh";

            Assert.Greater(source.Length, VoiceLocaleId.MaxCodeLength);

            Assert.IsFalse(VoiceLocaleId.TryCreate(source, out VoiceLocaleId locale));
            Assert.AreEqual(VoiceLocaleId.Invalid, locale);
        }

        [Test]
        public void Snapshot_CopiesFallbacks_AndPreservesOrder()
        {
            VoiceLocaleId primary = Locale("fr-CA");
            VoiceLocaleId[] fallbacks =
            {
                Locale("fr"),
                Locale("en-US"),
                Locale("en")
            };

            Assert.IsTrue(AudioVoiceLocaleSnapshot.TryCreate(primary, fallbacks, out AudioVoiceLocaleSnapshot snapshot));

            fallbacks[0] = Locale("ja");

            Assert.IsTrue(snapshot.IsValid);
            Assert.AreEqual(4, snapshot.Count);
            Assert.AreEqual(3, snapshot.FallbackCount);
            Assert.AreEqual(Locale("fr-CA"), snapshot.Primary);
            Assert.AreEqual(Locale("fr"), snapshot.GetFallback(0));
            Assert.AreEqual(Locale("en-US"), snapshot.GetFallback(1));
            Assert.AreEqual(Locale("en"), snapshot.GetFallback(2));
            Assert.AreEqual(Locale("fr"), snapshot[1]);
        }

        [Test]
        public void Snapshot_RejectsDuplicatePrimaryOrFallback()
        {
            Assert.IsFalse(AudioVoiceLocaleSnapshot.TryCreate(
                Locale("en"),
                new[] { Locale("en") },
                out AudioVoiceLocaleSnapshot duplicatePrimary));
            Assert.IsFalse(duplicatePrimary.IsValid);

            Assert.IsFalse(AudioVoiceLocaleSnapshot.TryCreate(
                Locale("en"),
                new[] { Locale("fr"), Locale("fr") },
                out AudioVoiceLocaleSnapshot duplicateFallback));
            Assert.IsFalse(duplicateFallback.IsValid);
        }

        [Test]
        public void Snapshot_Accepts_MaximumBoundedLocaleCount()
        {
            VoiceLocaleId[] fallbacks = CreateDistinctFallbacks(AudioVoiceLocaleSnapshot.MaxLocaleCount - 1);

            Assert.IsTrue(AudioVoiceLocaleSnapshot.TryCreate(
                Locale("en"),
                fallbacks,
                out AudioVoiceLocaleSnapshot snapshot));
            Assert.AreEqual(AudioVoiceLocaleSnapshot.MaxLocaleCount, snapshot.Count);
        }

        [Test]
        public void Snapshot_Rejects_FallbackCountBeyondBound()
        {
            VoiceLocaleId[] fallbacks = CreateDistinctFallbacks(AudioVoiceLocaleSnapshot.MaxLocaleCount);

            Assert.IsFalse(AudioVoiceLocaleSnapshot.TryCreate(
                Locale("en"),
                fallbacks,
                out AudioVoiceLocaleSnapshot snapshot));
            Assert.IsFalse(snapshot.IsValid);
        }

        [Test]
        public void Snapshot_Rejects_InvalidPrimaryOrFallback()
        {
            Assert.IsFalse(AudioVoiceLocaleSnapshot.TryCreate(
                VoiceLocaleId.Invalid,
                Array.Empty<VoiceLocaleId>(),
                out AudioVoiceLocaleSnapshot invalidPrimary));
            Assert.IsFalse(invalidPrimary.IsValid);

            VoiceLocaleId[] invalidFallback = { Locale("fr"), VoiceLocaleId.Invalid };
            Assert.IsFalse(AudioVoiceLocaleSnapshot.TryCreate(
                Locale("en"),
                invalidFallback,
                out AudioVoiceLocaleSnapshot invalidChain));
            Assert.IsFalse(invalidChain.IsValid);
        }

        [Test]
        public void Snapshot_GetFallback_Rejects_OutOfRangeIndex()
        {
            AudioVoiceLocaleSnapshot snapshot = Snapshot("en", "fr");

            Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.GetFallback(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.GetFallback(snapshot.FallbackCount));
        }

        [Test]
        public void LocaleControl_ChangingValue_IncrementsRevisionAndPublishesSnapshot()
        {
            var control = new AudioVoiceLocaleControl();
            AudioVoiceLocaleSnapshot first = Snapshot("en-US", "en");
            AudioVoiceLocaleSnapshot second = Snapshot("ja-JP", "ja", "en");
            var changes = new List<AudioVoiceLocaleChange>();
            control.VoiceLocaleChanged += changes.Add;

            Assert.IsTrue(control.TrySetVoiceLocale(first));
            Assert.IsTrue(control.TrySetVoiceLocale(second));

            Assert.AreEqual(2L, control.VoiceLocaleRevision);
            Assert.AreEqual(second, control.CurrentVoiceLocale);
            Assert.AreEqual(2, changes.Count);
            Assert.IsFalse(changes[0].Previous.IsValid);
            Assert.AreEqual(first, changes[0].Current);
            Assert.AreEqual(1L, changes[0].Revision);
            Assert.AreEqual(first, changes[1].Previous);
            Assert.AreEqual(second, changes[1].Current);
            Assert.AreEqual(2L, changes[1].Revision);
        }

        [Test]
        public void LocaleControl_SameValue_DoesNotIncrementRevisionOrPublishAgain()
        {
            var control = new AudioVoiceLocaleControl();
            AudioVoiceLocaleSnapshot first = Snapshot("en-US", "en", "fr");
            AudioVoiceLocaleSnapshot equivalentCopy = Snapshot("en-US", "en", "fr");
            int eventCount = 0;
            control.VoiceLocaleChanged += _ => eventCount++;

            Assert.IsTrue(control.TrySetVoiceLocale(first));
            Assert.IsTrue(control.TrySetVoiceLocale(equivalentCopy));

            Assert.AreEqual(1L, control.VoiceLocaleRevision);
            Assert.AreEqual(1, eventCount);
            Assert.AreEqual(first, control.CurrentVoiceLocale);
        }

        [Test]
        public void LocaleControl_InvalidValue_DoesNotChangeState()
        {
            var control = new AudioVoiceLocaleControl();
            int eventCount = 0;
            control.VoiceLocaleChanged += _ => eventCount++;

            Assert.IsFalse(control.TrySetVoiceLocale(default));

            Assert.IsFalse(control.CurrentVoiceLocale.IsValid);
            Assert.AreEqual(0L, control.VoiceLocaleRevision);
            Assert.AreEqual(0, eventCount);
        }

        [Test]
        public void LocaleControl_Clear_ChangesOnceAndPublishesPreviousValue()
        {
            var control = new AudioVoiceLocaleControl();
            AudioVoiceLocaleSnapshot first = Snapshot("en-US", "en");
            var changes = new List<AudioVoiceLocaleChange>();
            control.VoiceLocaleChanged += changes.Add;
            Assert.IsTrue(control.TrySetVoiceLocale(first));

            Assert.IsTrue(control.ClearVoiceLocale());
            Assert.IsFalse(control.ClearVoiceLocale());

            Assert.IsFalse(control.CurrentVoiceLocale.IsValid);
            Assert.AreEqual(2L, control.VoiceLocaleRevision);
            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual(first, changes[1].Previous);
            Assert.IsFalse(changes[1].Current.IsValid);
            Assert.AreEqual(2L, changes[1].Revision);
        }

        [Test]
        public void LocaleControl_ReentrantMutation_IsDeliveredInFifoOrder()
        {
            var control = new AudioVoiceLocaleControl();
            AudioVoiceLocaleSnapshot english = Snapshot("en");
            AudioVoiceLocaleSnapshot japanese = Snapshot("ja");
            var deliveries = new List<string>();

            control.VoiceLocaleChanged += change =>
            {
                deliveries.Add(
                    $"first:{change.Revision}:{control.CurrentVoiceLocale.Primary.Code}");
                if (change.Revision == 1)
                    Assert.IsTrue(control.TrySetVoiceLocale(japanese));
            };
            control.VoiceLocaleChanged += change => deliveries.Add(
                $"second:{change.Revision}:{control.CurrentVoiceLocale.Primary.Code}");

            Assert.IsTrue(control.TrySetVoiceLocale(english));

            CollectionAssert.AreEqual(
                new[]
                {
                    "first:1:en",
                    "second:1:en",
                    "first:2:ja",
                    "second:2:ja",
                },
                deliveries);
            Assert.AreEqual(japanese, control.CurrentVoiceLocale);
            Assert.AreEqual(2L, control.VoiceLocaleRevision);
        }

        [Test]
        public void LocaleControl_ReentrantBudget_RejectsOnlyMutationThatCannotBeDelivered()
        {
            const int dispatchLimit = 64;
            var reported = new List<Exception>();
            var accepted = new List<bool>(dispatchLimit);
            var deliveredRevisions = new List<long>(dispatchLimit);
            var control = new AudioVoiceLocaleControl(reported.Add);

            control.VoiceLocaleChanged += change =>
            {
                deliveredRevisions.Add(change.Revision);
                if (change.Revision != 1)
                    return;

                for (int i = 0; i < dispatchLimit; i++)
                {
                    accepted.Add(control.TrySetVoiceLocale(
                        Snapshot(CreateTestLocaleCode(i))));
                }
            };

            Assert.IsTrue(control.TrySetVoiceLocale(Snapshot("en")));

            Assert.AreEqual(dispatchLimit, accepted.Count);
            for (int i = 0; i < dispatchLimit - 1; i++)
                Assert.IsTrue(accepted[i], $"Reentrant mutation {i} should be accepted.");
            Assert.IsFalse(accepted[dispatchLimit - 1]);
            Assert.AreEqual(dispatchLimit, deliveredRevisions.Count);
            Assert.AreEqual(dispatchLimit, control.VoiceLocaleRevision);
            Assert.AreEqual(
                CreateTestLocaleCode(dispatchLimit - 2),
                control.CurrentVoiceLocale.Primary.Code);
            Assert.AreEqual(1, reported.Count);
            Assert.IsInstanceOf<InvalidOperationException>(reported[0]);
        }

        [Test]
        public void LocaleControl_SubscriberFailure_IsolatedFromLaterSubscribers()
        {
            var reported = new List<Exception>();
            var control = new AudioVoiceLocaleControl(reported.Add);
            int laterSubscriberCalls = 0;
            control.VoiceLocaleChanged += _ =>
                throw new InvalidOperationException("Expected subscriber failure.");
            control.VoiceLocaleChanged += _ => laterSubscriberCalls++;

            Assert.DoesNotThrow(() => control.TrySetVoiceLocale(Snapshot("en")));

            Assert.AreEqual(1, laterSubscriberCalls);
            Assert.AreEqual(1, reported.Count);
            Assert.IsInstanceOf<InvalidOperationException>(reported[0]);
        }

        [Test]
        public void LocaleControl_ReadFromWorkerThread_ThrowsMainThreadContractError()
        {
            var control = new AudioVoiceLocaleControl();
            Assert.IsTrue(control.TrySetVoiceLocale(Snapshot("en")));

            Assert.Throws<InvalidOperationException>(() =>
                Task.Run(() => control.CurrentVoiceLocale).GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() =>
                Task.Run(() => control.VoiceLocaleRevision).GetAwaiter().GetResult());
        }

        private static VoiceLocaleId Locale(string code)
        {
            Assert.IsTrue(VoiceLocaleId.TryCreate(code, out VoiceLocaleId locale), code);
            return locale;
        }

        private static AudioVoiceLocaleSnapshot Snapshot(string primary, params string[] fallbacks)
        {
            VoiceLocaleId[] fallbackLocales = new VoiceLocaleId[fallbacks.Length];
            for (int i = 0; i < fallbacks.Length; i++)
                fallbackLocales[i] = Locale(fallbacks[i]);

            Assert.IsTrue(AudioVoiceLocaleSnapshot.TryCreate(
                Locale(primary),
                fallbackLocales,
                out AudioVoiceLocaleSnapshot snapshot));
            return snapshot;
        }

        private static VoiceLocaleId[] CreateDistinctFallbacks(int count)
        {
            var fallbacks = new VoiceLocaleId[count];
            for (int i = 0; i < count; i++)
                fallbacks[i] = Locale("q" + (char)('a' + i));
            return fallbacks;
        }

        private static string CreateTestLocaleCode(int index)
        {
            return string.Concat(
                "q",
                (char)('a' + index / 26),
                (char)('a' + index % 26));
        }

    }
}
