using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class BuildInspectorUiTests
    {
        [TestCase(0f, 0, 0)]
        [TestCase(0f, 6, 1)]
        [TestCase(288f, 6, 1)]
        [TestCase(289f, 6, 2)]
        [TestCase(435f, 6, 2)]
        [TestCase(436f, 6, 3)]
        [TestCase(1000f, 1, 1)]
        [TestCase(1000f, 2, 2)]
        public void ResolveColumnCount_UsesBoundedResponsiveGrid(
            float availableWidth,
            int itemCount,
            int expected)
        {
            int actual = BuildInspectorUi.ResolveColumnCount(
                availableWidth,
                itemCount,
                minimumCellWidth: 142f,
                gap: 5f,
                maximumColumns: 3);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void ResolveColumnCount_ClampsInvalidLayoutInputs()
        {
            int actual = BuildInspectorUi.ResolveColumnCount(
                availableWidth: -100f,
                itemCount: 4,
                minimumCellWidth: -10f,
                gap: -5f,
                maximumColumns: 0);

            Assert.That(actual, Is.EqualTo(1));
        }

        [TestCase(228f, 1)]
        [TestCase(229f, 2)]
        [TestCase(345f, 2)]
        [TestCase(346f, 3)]
        public void ResolveColumnCount_CompactGridKeepsNarrowPresetsUsable(
            float availableWidth,
            int expected)
        {
            int actual = BuildInspectorUi.ResolveColumnCount(
                availableWidth,
                itemCount: 6,
                minimumCellWidth: BuildInspectorUi.CompactGridCellWidth,
                gap: 5f,
                maximumColumns: 3);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCase(300f, 2, 1, false, 147.5f)]
        [TestCase(300f, 2, 1, true, 300f)]
        [TestCase(300f, 3, 2, true, 147.5f)]
        [TestCase(-1f, 0, 0, true, 1f)]
        public void ResolveGridCellWidth_CanExpandIncompleteRows(
            float rowWidth,
            int columns,
            int rowItemCount,
            bool expandIncompleteRow,
            float expected)
        {
            float actual = BuildInspectorUi.ResolveGridCellWidth(
                rowWidth,
                columns,
                rowItemCount,
                gap: 5f,
                expandIncompleteRow: expandIncompleteRow);

            Assert.That(actual, Is.EqualTo(expected).Within(0.001f));
        }

        [TestCase(200f, 150f, 86f)]
        [TestCase(280f, 150f, 86f)]
        [TestCase(360f, 150f, 118f)]
        [TestCase(440f, 150f, 150f)]
        [TestCase(720f, 90f, 90f)]
        public void ResolveLabelWidth_UsesBoundedResponsiveWidth(
            float availableWidth,
            float defaultLabelWidth,
            float expected)
        {
            float actual = BuildInspectorUi.ResolveLabelWidth(
                availableWidth,
                defaultLabelWidth);

            Assert.That(actual, Is.EqualTo(expected).Within(0.001f));
        }

        [TestCase(291f, 1, 102f, 0)]
        [TestCase(290f, 1, 102f, 1)]
        [TestCase(177f, 1, 102f, 1)]
        [TestCase(176f, 1, 102f, 2)]
        [TestCase(370f, 2, 104f, 0)]
        [TestCase(242f, 2, 104f, 1)]
        [TestCase(241f, 2, 104f, 2)]
        public void ResolveFieldLayout_PreservesMinimumInteractiveWidths(
            float availableWidth,
            int actionCount,
            float labelWidth,
            int expected)
        {
            BuildInspectorFieldLayoutMode actual =
                BuildInspectorUi.ResolveFieldLayout(
                    availableWidth,
                    actionCount,
                    labelWidth);

            Assert.That((int)actual, Is.EqualTo(expected));
        }

        [TestCase(272f, 80f, 130f, false)]
        [TestCase(272f, 80f, 170f, true)]
        [TestCase(-1f, -1f, -1f, true)]
        public void ShouldStackStatusRow_UsesContentWidth(
            float availableWidth,
            float labelWidth,
            float valueWidth,
            bool expected)
        {
            Assert.That(
                BuildInspectorUi.ShouldStackStatusRow(
                    availableWidth,
                    labelWidth,
                    valueWidth),
                Is.EqualTo(expected));
        }

        [Test]
        public void SemanticTones_HaveDistinctBlockingAndReadyColors()
        {
            Assert.That(
                BuildInspectorUi.GetToneColor(BuildInspectorTone.Ready),
                Is.Not.EqualTo(BuildInspectorUi.GetToneColor(BuildInspectorTone.Error)));
            Assert.That(
                BuildInspectorUi.GetToneColor(BuildInspectorTone.Warning),
                Is.Not.EqualTo(BuildInspectorUi.GetToneColor(BuildInspectorTone.Disabled)));
        }

        [Test]
        public void PrimaryActionTint_IsBrighterThanSectionAccentInBothSkins()
        {
            float accentLuminance = RelativeLuminance(BuildInspectorUi.ActionColor);
            Color pro = BuildInspectorUi.GetPrimaryActionTint(proSkin: true);
            Color personal = BuildInspectorUi.GetPrimaryActionTint(proSkin: false);

            Assert.That(RelativeLuminance(pro), Is.GreaterThan(accentLuminance));
            Assert.That(RelativeLuminance(personal), Is.GreaterThan(accentLuminance));
            Assert.That(pro, Is.Not.EqualTo(personal));
        }

        [Test]
        public void OuterContentGutterReduction_IsSmallAndBounded()
        {
            Assert.That(
                BuildInspectorUi.OuterLeftGutterReduction,
                Is.InRange(6f, 8f));
        }

        [TestCase(0f, 0f, 320f, 22f)]
        [TestCase(12f, 8f, 13f, 16f)]
        [TestCase(-40f, -20f, 1f, 1f)]
        [TestCase(100f, 50f, 6f, 4f)]
        [TestCase(100f, 50f, 0f, 0f)]
        public void NestedFoldoutArrowRect_RemainsInsideHeader(
            float x,
            float y,
            float width,
            float height)
        {
            var header = new Rect(x, y, width, height);

            Rect arrow = BuildInspectorUi.GetNestedFoldoutArrowRect(header);

            Assert.That(arrow.width, Is.GreaterThanOrEqualTo(0f));
            Assert.That(arrow.height, Is.GreaterThanOrEqualTo(0f));
            Assert.That(arrow.xMin, Is.GreaterThanOrEqualTo(header.xMin));
            Assert.That(arrow.xMax, Is.LessThanOrEqualTo(header.xMax));
            Assert.That(arrow.yMin, Is.GreaterThanOrEqualTo(header.yMin));
            Assert.That(arrow.yMax, Is.LessThanOrEqualTo(header.yMax));
        }

        [Test]
        public void NestedFoldoutArrowRect_NormalizesInvertedHeaderBounds()
        {
            var inverted = new Rect(40f, 30f, -20f, -10f);

            Rect arrow = BuildInspectorUi.GetNestedFoldoutArrowRect(inverted);

            Assert.That(arrow.xMin, Is.GreaterThanOrEqualTo(20f));
            Assert.That(arrow.xMax, Is.LessThanOrEqualTo(40f));
            Assert.That(arrow.yMin, Is.GreaterThanOrEqualTo(20f));
            Assert.That(arrow.yMax, Is.LessThanOrEqualTo(30f));
        }

        private static float RelativeLuminance(Color color)
        {
            return color.r * 0.2126f
                + color.g * 0.7152f
                + color.b * 0.0722f;
        }
    }
}
