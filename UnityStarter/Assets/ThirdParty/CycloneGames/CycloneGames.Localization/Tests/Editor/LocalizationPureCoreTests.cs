using CycloneGames.Localization.Core;
using NUnit.Framework;

namespace CycloneGames.Localization.Tests.Editor
{
    public sealed class LocalizationPureCoreTests
    {
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
            var transformed = PseudoLocalizer.Transform("Score {0}", PseudoLocaleMode.Full);

            Assert.That(transformed, Does.Contain("{0}"));
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
