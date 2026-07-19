using System;
using System.Collections.Generic;
using System.Globalization;

using CycloneGames.Utility.Runtime;

using NUnit.Framework;

using UnityEngine;

namespace CycloneGames.Utility.Tests.Editor
{
    public sealed class UtilityCoreTests
    {
        [Test]
        public void ListSwapRemove_UsesExplicitBoundsAndDoesNotPreserveOrder()
        {
            var values = new List<string> { "A", "B", "C" };

            values.SwapRemoveAt(1);

            Assert.That(values, Is.EqualTo(new[] { "A", "C" }));
            Assert.That(values.TrySwapRemoveAt(2), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => values.SwapRemoveAt(-1));
        }

        [Test]
        public void ArraySwapRemove_ClearsReleasedReference()
        {
            string[] values = { "A", "B", "C" };

            int count = values.SwapRemoveAt(0, 3);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(values[0], Is.EqualTo("C"));
            Assert.That(values[2], Is.Null);
            Assert.That(values.TrySwapRemoveAt(2, count, out int unchangedCount), Is.False);
            Assert.That(unchangedCount, Is.EqualTo(count));
        }

        [Test]
        public void Shuffle_WithCallerOwnedRandom_IsRepeatableWithinCurrentRuntime()
        {
            int[] first = { 0, 1, 2, 3, 4, 5 };
            int[] second = { 0, 1, 2, 3, 4, 5 };

            first.Shuffle(new System.Random(12345));
            second.Shuffle(new System.Random(12345));

            Assert.That(first, Is.EqualTo(second));
        }

        [Test]
        public void Formatters_UseInvariantCulture()
        {
            CultureInfo previousCulture = CultureInfo.CurrentCulture;
            CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

                Assert.That(FormatUtil.FormatBytes(1536, 2), Is.EqualTo("1.5 KB"));
                Assert.That(FormatUtil.FormatNumber(1500, 2), Is.EqualTo("1.5K"));
                Assert.That(FormatUtil.FormatDuration(3661.5d, true), Is.EqualTo("1:01:01.500"));
                Assert.That(FormatUtil.FormatPercent(0.753f, 1), Is.EqualTo("75.3%"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                CultureInfo.CurrentUICulture = previousUiCulture;
            }
        }

        [Test]
        public void TryFormatters_ReportInvalidInputAndSmallDestinations()
        {
            Span<char> tiny = stackalloc char[1];

            Assert.That(FormatUtil.TryFormatNumber(1500, tiny, out int numberWritten), Is.False);
            Assert.That(numberWritten, Is.Zero);
            Assert.That(FormatUtil.TryFormatDuration(double.NaN, tiny, out int durationWritten), Is.False);
            Assert.That(durationWritten, Is.Zero);
            Assert.That(FormatUtil.TryFormatPercent(2f, tiny, out int percentWritten), Is.False);
            Assert.That(percentWritten, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => FormatUtil.FormatBytes(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => FormatUtil.FormatDuration(double.PositiveInfinity));
        }

        [Test]
        public void ColorHex_RoundTripsWithoutIntermediateContractChanges()
        {
            Color original = new Color32(0x12, 0x34, 0xAB, 0xCD);

            string hex = original.ToHexString();
            bool parsed = Colors.TryParseHex(hex.AsSpan(), out Color restored);
            Color32 restoredBytes = restored;

            Assert.That(hex, Is.EqualTo("#1234ABCD"));
            Assert.That(parsed, Is.True);
            Assert.That(restoredBytes.r, Is.EqualTo(0x12));
            Assert.That(restoredBytes.g, Is.EqualTo(0x34));
            Assert.That(restoredBytes.b, Is.EqualTo(0xAB));
            Assert.That(restoredBytes.a, Is.EqualTo(0xCD));
            Assert.That(original.ToUInt32(), Is.EqualTo(0x1234ABCDu));
        }

        [Test]
        public void ColorContrast_UsesWcagRelativeLuminance()
        {
            Assert.That(Color.black.GetRelativeLuminance(), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(Color.white.GetRelativeLuminance(), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(Color.black.GetContrastRatio(Color.white), Is.EqualTo(21f).Within(0.001f));
        }

        [Test]
        public void RandomColor_WithCallerOwnedRandom_DoesNotUseUnityGlobalState()
        {
            Color first = Colors.RandomColor(new System.Random(77));
            Color second = Colors.RandomColor(new System.Random(77));

            Assert.That(first, Is.EqualTo(second));
        }

        [Test]
        public void VectorOperations_RejectInvalidRangesAndHandleLargeFiniteInputs()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Vector3(1f, 0f, 0f).ClampMagnitude(-1f));
            Vector3 projected = new Vector3(1f, 2f, 3f).ProjectOnPlane(Vector3.up * 5f);
            Assert.That(projected.x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(projected.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(projected.z, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(
                new Vector3(float.MaxValue, 0f, 0f).Angle(new Vector3(0f, float.MaxValue, 0f)),
                Is.EqualTo(90f).Within(0.01f));
            Vector3 signs = new Vector3(0f, -5f, 2f).Sign();
            Assert.That(signs.x, Is.Zero);
            Assert.That(signs.y, Is.EqualTo(-1f));
            Assert.That(signs.z, Is.EqualTo(1f));
            Assert.That(
                Vector3.zero.TryRemap(1f, 1f, 0f, 10f, out _),
                Is.False);
            Assert.That(
                Vector3.zero.TryRemap(-float.MaxValue, float.MaxValue, -1f, 1f, out Vector3 extremeRemap),
                Is.True);
            Assert.That(extremeRemap.sqrMagnitude, Is.LessThan(0.000001f));
            Assert.That(
                new Vector3(float.NaN, 0f, 0f).TryRemap(0f, 1f, 0f, 1f, out _),
                Is.False);
        }

        [Test]
        public void SafeAreaCalculation_BoundsPaddingAndConvertsGuiCoordinates()
        {
            SafeAreaPolicy policy = new SafeAreaPolicy(false, false, false);
            Rect pixelRect = SafeAreaUtility.CalculatePixelRect(
                new Rect(10f, 20f, 80f, 170f),
                100,
                200,
                in policy);

            Assert.That(pixelRect, Is.EqualTo(new Rect(10f, 20f, 80f, 170f)));
            Assert.That(SafeAreaUtility.ToGuiRect(pixelRect, 200), Is.EqualTo(new Rect(10f, 10f, 80f, 170f)));

            SafeAreaPolicy extremePadding = new SafeAreaPolicy(
                false,
                false,
                false,
                new Vector4(10_000f, 10_000f, 10_000f, 10_000f));
            Assert.That(
                SafeAreaUtility.TryCalculateAnchors(
                    new Rect(0f, 0f, 100f, 200f),
                    100,
                    200,
                    in extremePadding,
                    out Vector2 anchorMin,
                    out Vector2 anchorMax),
                Is.True);
            Assert.That(anchorMin.x, Is.LessThanOrEqualTo(anchorMax.x));
            Assert.That(anchorMin.y, Is.LessThanOrEqualTo(anchorMax.y));

            SafeAreaPolicy negativePadding = new SafeAreaPolicy(
                false,
                false,
                false,
                new Vector4(-50f, -50f, -50f, -50f));
            Assert.That(
                SafeAreaUtility.CalculatePixelRect(
                    new Rect(10f, 20f, 80f, 170f),
                    100,
                    200,
                    in negativePadding),
                Is.EqualTo(pixelRect));

            SafeAreaPolicy bottomExtensionWithTopBalance = new SafeAreaPolicy(true, true, false);
            Rect balanced = SafeAreaUtility.CalculatePixelRect(
                new Rect(0f, 30f, 100f, 150f),
                100,
                200,
                in bottomExtensionWithTopBalance);
            Assert.That(balanced.yMin, Is.EqualTo(20f));
            Assert.That(balanced.yMax, Is.EqualTo(180f));
        }

        [Test]
        public void StringSelectorAttribute_RequiresAConstantsTypeAndPreservesCustomMode()
        {
            Assert.Throws<ArgumentNullException>(() => new StringAsConstSelectorAttribute(null));

            var selector = new StringAsConstSelectorAttribute(typeof(TestConstants))
            {
                AllowCustom = true,
                UseMenu = true,
                Separator = '.'
            };

            Assert.That(selector.ConstantsType, Is.EqualTo(typeof(TestConstants)));
            Assert.That(selector.AllowCustom, Is.True);
            Assert.That(selector.UseMenu, Is.True);
            Assert.That(selector.Separator, Is.EqualTo('.'));
        }

        private static class TestConstants
        {
            public const string Alpha = "alpha";
        }
    }
}
