using System;
using CycloneGames.Spatial.Core;
using NUnit.Framework;

namespace CycloneGames.Spatial.Tests.Editor
{
    /// <summary>
    /// Partition semantics. The floor rule and the exact-boundary rule are the two places where a spatial grid
    /// silently disagrees with the rest of the game, so both are pinned here.
    /// </summary>
    public sealed class GridSettingsTests
    {
        [Test]
        public void WorldToCell_UsesFloorNotTruncation()
        {
            var settings = new GridSettings(1f);

            Assert.That(settings.WorldToCell(-0.5f, -0.5f, -0.5f), Is.EqualTo(new CellCoordinate(-1, -1, -1)));
            Assert.That(settings.WorldToCell(-0.0001f, 0f, 0f).X, Is.EqualTo(-1));
            Assert.That(settings.WorldToCell(-1f, -1f, -1f), Is.EqualTo(new CellCoordinate(-1, -1, -1)));
            Assert.That(settings.WorldToCell(-1.0001f, 0f, 0f).X, Is.EqualTo(-2));
            Assert.That(settings.WorldToCell(0.9999f, 0f, 0f).X, Is.EqualTo(0));
        }

        [Test]
        public void WorldToCell_ExactBoundaryBelongsToUpperCell()
        {
            var settings = new GridSettings(1f);

            Assert.That(settings.WorldToCell(0f, 0f, 0f), Is.EqualTo(new CellCoordinate(0, 0, 0)));
            Assert.That(settings.WorldToCell(1f, 1f, 1f), Is.EqualTo(new CellCoordinate(1, 1, 1)));
            Assert.That(settings.WorldToCell(2f, 4f, 8f), Is.EqualTo(new CellCoordinate(2, 4, 8)));
        }

        [Test]
        public void WorldToCell_SurvivesDivisionThatIsNotRepresentable()
        {
            // 0.3 / 0.1 is 2.9999999999999996 in binary floating point when computed naively. Dividing instead of
            // multiplying by a reciprocal keeps the answer on the correct side of the boundary.
            var settings = new GridSettings(0.1f);
            Assert.That(settings.WorldToCell(0.3f, 0f, 0f).X, Is.EqualTo(3));
        }

        [Test]
        public void WorldToCell_AppliesOrigin()
        {
            var settings = new GridSettings(2f, 100f, -50f, 0f);

            Assert.That(settings.WorldToCell(100f, -50f, 0f), Is.EqualTo(new CellCoordinate(0, 0, 0)));
            Assert.That(settings.WorldToCell(104f, -50f, 0f), Is.EqualTo(new CellCoordinate(2, 0, 0)));
        }

        [Test]
        public void CellMin_ReturnsWorldCorner()
        {
            var settings = new GridSettings(2f, 10f, 0f, 0f);

            Assert.That(settings.CellMinX(0), Is.EqualTo(10f).Within(1e-4f));
            Assert.That(settings.CellMinX(3), Is.EqualTo(16f).Within(1e-4f));
            Assert.That(settings.CellMinX(-1), Is.EqualTo(8f).Within(1e-4f));
        }

        [Test]
        public void Constructor_RejectsInvalidCellSize()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridSettings(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridSettings(-1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridSettings(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridSettings(float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridSettings(1f, float.NaN, 0f, 0f));
        }

        [Test]
        public void CellRangeForSphere_CoversEveryCandidate()
        {
            var settings = new GridSettings(1f);

            Assert.That(settings.CellRangeForSphere(10.5f, 10.5f, 10.5f, 1.2f, out CellCoordinate min, out CellCoordinate max), Is.True);
            Assert.That(min, Is.EqualTo(new CellCoordinate(9, 9, 9)));
            Assert.That(max, Is.EqualTo(new CellCoordinate(11, 11, 11)));
        }

        [Test]
        public void CellRangeForSphere_UsesFloorForNegativeCenters()
        {
            var settings = new GridSettings(1f);

            Assert.That(settings.CellRangeForSphere(-10.5f, -10.5f, -10.5f, 1.2f, out CellCoordinate min, out CellCoordinate max), Is.True);
            Assert.That(min, Is.EqualTo(new CellCoordinate(-12, -12, -12)));
            Assert.That(max, Is.EqualTo(new CellCoordinate(-10, -10, -10)));
        }

        [Test]
        public void CellRangeForSphere_ZeroRadiusStaysInOneCell()
        {
            var settings = new GridSettings(1f);

            Assert.That(settings.CellRangeForSphere(0.5f, 0.5f, 0.5f, 0f, out CellCoordinate min, out CellCoordinate max), Is.True);
            Assert.That(min, Is.EqualTo(new CellCoordinate(0, 0, 0)));
            Assert.That(max, Is.EqualTo(new CellCoordinate(0, 0, 0)));
        }

        [Test]
        public void CellRangeForSphere_RejectsUnusableInput()
        {
            var settings = new GridSettings(1f);

            Assert.That(settings.CellRangeForSphere(0f, 0f, 0f, -1f, out _, out _), Is.False);
            Assert.That(settings.CellRangeForSphere(float.NaN, 0f, 0f, 1f, out _, out _), Is.False);
            Assert.That(settings.CellRangeForSphere(0f, 0f, 0f, float.PositiveInfinity, out _, out _), Is.False);
        }

        [Test]
        public void CellRangeForAabb_IsExactForTheBox()
        {
            var settings = new GridSettings(1f);

            Assert.That(settings.CellRangeForAabb(0.2f, 0.2f, 0.2f, 2.8f, 2.8f, 2.8f, out CellCoordinate min, out CellCoordinate max), Is.True);
            Assert.That(min, Is.EqualTo(new CellCoordinate(0, 0, 0)));
            Assert.That(max, Is.EqualTo(new CellCoordinate(2, 2, 2)));
        }

        [Test]
        public void CellRangeForAabb_RejectsInvertedBox()
        {
            var settings = new GridSettings(1f);

            Assert.That(settings.CellRangeForAabb(5f, 0f, 0f, 1f, 0f, 0f, out _, out _), Is.False);
            Assert.That(settings.CellRangeForAabb(0f, 0f, 0f, float.NaN, 1f, 1f, out _, out _), Is.False);
        }
    }
}
