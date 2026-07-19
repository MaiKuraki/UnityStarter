using System;
using System.Globalization;
using CycloneGames.Localization.Core;
using NUnit.Framework;

namespace CycloneGames.Localization.Tests.Editor
{
    public sealed class LocalizationPureCoreTests
    {
        [Test]
        public void PluralRuleSetVersionIsAuditable()
        {
            Assert.That(PluralRules.RuleSetVersion, Is.EqualTo("CLDR-48-integer-cardinal-subset"));
        }

        [Test]
        public void PseudoLocalizerDisabledReturnsOriginalReference()
        {
            const string text = "Start Game";

            var transformed = PseudoLocalizer.Transform(text, PseudoLocaleMode.None);

            Assert.That(ReferenceEquals(text, transformed), Is.True);
        }

        [Test]
        public void PseudoLocalizerFullPreservesFormatPlaceholders()
        {
            var transformed = PseudoLocalizer.Transform(
                "<b>Score {0:N2}</b>",
                PseudoLocaleMode.Full);

            Assert.That(transformed, Does.Contain("<b>"));
            Assert.That(transformed, Does.Contain("</b>"));
            Assert.That(transformed, Does.Contain("{0:N2}"));
            Assert.That(transformed[0], Is.EqualTo('\u27E6'));
            Assert.DoesNotThrow(() => string.Format(CultureInfo.InvariantCulture, transformed, 12.5));
        }

        [Test]
        public void PseudoLocalizerMirrorPreservesUnicodeTextElements()
        {
            string transformed = PseudoLocalizer.Transform(
                "A\U0001F469\u200D\U0001F680e\u0301 {0}",
                PseudoLocaleMode.Mirror);

            Assert.That(transformed, Does.Contain("{0}"));
            Assert.That(transformed, Does.Contain("e\u0301"));
            Assert.That(HasUnpairedSurrogate(transformed), Is.False);
        }

        [Test]
        public void LocaleIdValidatesAndCanonicalizesBoundedCodes()
        {
            Assert.That(LocaleId.TryCreate("ZH-hans-cn", out LocaleId locale), Is.True);
            Assert.That(locale.Code, Is.EqualTo("zh-Hans-CN"));
            Assert.That(locale.Language.Code, Is.EqualTo("zh"));
            Assert.That(ReferenceEquals(locale.Language.Code, locale.Language.Code), Is.True);

            Assert.That(LocaleId.TryCreate("en_US", out _), Is.False);
            Assert.That(LocaleId.TryCreate("x-private", out _), Is.False);
            Assert.That(LocaleId.TryCreate(new string('a', LocaleId.MaxCodeLength + 1), out _), Is.False);
            Assert.That(new LocaleId(" en ").IsValid, Is.False);
        }

        [TestCase("en", 1, PluralCategory.One)]
        [TestCase("en", 2, PluralCategory.Other)]
        [TestCase("zh-CN", 1, PluralCategory.Other)]
        [TestCase("pt", 0, PluralCategory.One)]
        [TestCase("pt-PT", 0, PluralCategory.Other)]
        [TestCase("ru", 1, PluralCategory.One)]
        [TestCase("ru", 2, PluralCategory.Few)]
        [TestCase("ru", 5, PluralCategory.Many)]
        [TestCase("ar", 0, PluralCategory.Zero)]
        [TestCase("ar", 2, PluralCategory.Two)]
        [TestCase("ar", int.MinValue, PluralCategory.Many)]
        [TestCase("he", 1, PluralCategory.One)]
        [TestCase("he", 2, PluralCategory.Two)]
        [TestCase("he", 10, PluralCategory.Other)]
        [TestCase("is", 11, PluralCategory.Other)]
        [TestCase("is", 21, PluralCategory.One)]
        [TestCase("is", 111, PluralCategory.Other)]
        [TestCase("mk", 11, PluralCategory.Other)]
        [TestCase("mk", 21, PluralCategory.One)]
        [TestCase("mk", 111, PluralCategory.Other)]
        [TestCase("lv", 10, PluralCategory.Zero)]
        [TestCase("lv", 11, PluralCategory.Zero)]
        [TestCase("lv", 19, PluralCategory.Zero)]
        [TestCase("lv", 21, PluralCategory.One)]
        [TestCase("hi", 0, PluralCategory.One)]
        [TestCase("hi", 1, PluralCategory.One)]
        [TestCase("hi", 2, PluralCategory.Other)]
        [TestCase("bn", 0, PluralCategory.One)]
        [TestCase("bn", 1, PluralCategory.One)]
        [TestCase("bn", 2, PluralCategory.Other)]
        [TestCase("gu", 0, PluralCategory.One)]
        [TestCase("gu", 1, PluralCategory.One)]
        [TestCase("gu", 2, PluralCategory.Other)]
        [TestCase("kn", 0, PluralCategory.One)]
        [TestCase("kn", 1, PluralCategory.One)]
        [TestCase("kn", 2, PluralCategory.Other)]
        [TestCase("si", 0, PluralCategory.One)]
        [TestCase("si", 1, PluralCategory.One)]
        [TestCase("si", 2, PluralCategory.Other)]
        [TestCase("hy", 0, PluralCategory.One)]
        [TestCase("hy", 1, PluralCategory.One)]
        [TestCase("hy", 2, PluralCategory.Other)]
        [TestCase("zu", 0, PluralCategory.One)]
        [TestCase("zu", 1, PluralCategory.One)]
        [TestCase("zu", 2, PluralCategory.Other)]
        public void PluralRulesResolveExpectedCategory(string localeCode, int count, PluralCategory expected)
        {
            Assert.That(PluralRules.Resolve(new LocaleId(localeCode), count), Is.EqualTo(expected));
        }

        [Test]
        public void FallbackChainBuilderKeepsBreadthFirstOrder()
        {
            var en = new TestLocaleNode("en");
            var ja = new TestLocaleNode("ja");
            var zh = new TestLocaleNode("zh-CN", en, ja);

            var chain = LocaleFallbackChainBuilder.Build(zh);

            Assert.That(chain, Is.EqualTo(new[] { new LocaleId("zh-CN"), new LocaleId("en"), new LocaleId("ja") }));
        }

        [Test]
        public void FallbackChainBuilderDeduplicatesCycles()
        {
            var en = new TestLocaleNode("en");
            var zh = new TestLocaleNode("zh-CN", en);
            en.SetFallbacks(zh);

            var chain = LocaleFallbackChainBuilder.Build(zh);

            Assert.That(chain, Is.EqualTo(new[] { new LocaleId("zh-CN"), new LocaleId("en") }));
        }

        [Test]
        public void FallbackChainBuilderRejectsConfiguredCapacityOverflow()
        {
            var fr = new TestLocaleNode("fr");
            var en = new TestLocaleNode("en", fr);
            var zh = new TestLocaleNode("zh-CN", en);

            Assert.Throws<InvalidOperationException>(() => LocaleFallbackChainBuilder.Build(zh, 2));
        }

        private static bool HasUnpairedSurrogate(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) return true;
                    i++;
                }
                else if (char.IsLowSurrogate(c))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class TestLocaleNode : ILocaleFallbackNode<TestLocaleNode>
        {
            private TestLocaleNode[] _fallbacks;

            public TestLocaleNode(string code, params TestLocaleNode[] fallbacks)
            {
                Id = new LocaleId(code);
                _fallbacks = fallbacks ?? System.Array.Empty<TestLocaleNode>();
            }

            public LocaleId Id { get; }
            public int FallbackCount => _fallbacks.Length;

            public TestLocaleNode GetFallback(int index)
            {
                return _fallbacks[index];
            }

            public void SetFallbacks(params TestLocaleNode[] fallbacks)
            {
                _fallbacks = fallbacks ?? System.Array.Empty<TestLocaleNode>();
            }
        }
    }
}
